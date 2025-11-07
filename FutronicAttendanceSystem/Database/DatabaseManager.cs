using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.IO;
using MySql.Data.MySqlClient;
using FutronicAttendanceSystem.Database.Models;
using FutronicAttendanceSystem.Database.Config;

namespace FutronicAttendanceSystem.Database
{
    public class DatabaseManager : IDisposable
    {
        private readonly DatabaseConfig config;
        private readonly string deviceId;
        private readonly string deviceLocation;
        private MySqlConnection connection;
        
        // Current device and location settings
        public string CurrentDeviceId { get; set; }
        public string CurrentRoomId { get; set; }
        public string CurrentLocation { get; set; } = "inside";
        
        // Current academic settings
        public string CurrentAcademicYear { get; private set; } = "2024-2025";
        public string CurrentSemester { get; private set; } = "First Semester";
        
        // Device information
        public Models.Device CurrentDevice { get; private set; }

        public DatabaseManager(DatabaseConfig config, string deviceId, string deviceLocation)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config), "Database configuration cannot be null");
                
            this.config = config;
            this.deviceId = deviceId ?? "Unknown";
            this.deviceLocation = deviceLocation ?? "Unknown";
            
            System.Diagnostics.Debug.WriteLine($"DatabaseManager: Server={config.Server}, Database={config.Database}, User={config.Username}");
            Console.WriteLine($"DatabaseManager: Server={config.Server}, Database={config.Database}, User={config.Username}");
            
            InitializeDatabase();
            LoadAcademicSettings();
            try { RegisterDevice(); } catch { /* Non-fatal for new schema without DEVICES */ }
        }

        private void InitializeDatabase()
        {
            try
            {
                connection = new MySqlConnection(config.GetConnectionString());
                connection.Open();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to connect to database: {ex.Message}", ex);
            }
        }

        private void LoadAcademicSettings()
        {
            try
            {
                // Load current academic year
                var academicYearCmd = new MySqlCommand("SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year'", connection);
                var academicYearResult = academicYearCmd.ExecuteScalar();
                if (academicYearResult != null && !string.IsNullOrWhiteSpace(academicYearResult.ToString()))
                {
                    CurrentAcademicYear = academicYearResult.ToString();
                }
                else
                {
                    LogMessage("WARNING", "No academic year setting found, using default: 2024-2025");
                }

                // Load current semester
                var semesterCmd = new MySqlCommand("SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester'", connection);
                var semesterResult = semesterCmd.ExecuteScalar();
                if (semesterResult != null && !string.IsNullOrWhiteSpace(semesterResult.ToString()))
                {
                    CurrentSemester = semesterResult.ToString();
                }
                else
                {
                    LogMessage("WARNING", "No semester setting found, using default: First Semester");
                }

                LogMessage("INFO", $"Loaded academic settings - Year: {CurrentAcademicYear}, Semester: {CurrentSemester}");
                Console.WriteLine($"Academic Settings Loaded - Year: {CurrentAcademicYear}, Semester: {CurrentSemester}");
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to load academic settings: {ex.Message}");
                LogMessage("WARNING", "Using default academic settings - Year: 2024-2025, Semester: First Semester");
                Console.WriteLine($"Warning: Failed to load academic settings, using defaults: {ex.Message}");
            }
        }

        private int LoadInstructorEarlyWindow()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "attendance_scenarios.json");
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var config = JsonDocument.Parse(json);
                    if (config.RootElement.TryGetProperty("InstructorEarlyWindow", out var value))
                    {
                        return value.GetInt32();
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("WARNING", $"Failed to load InstructorEarlyWindow: {ex.Message}");
            }
            return 15; // Default fallback
        }

        private int LoadStudentEarlyArrivalWindow()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "attendance_scenarios.json");
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var config = JsonDocument.Parse(json);
                    if (config.RootElement.TryGetProperty("StudentEarlyArrivalWindow", out var value))
                    {
                        return value.GetInt32();
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("WARNING", $"Failed to load StudentEarlyArrivalWindow: {ex.Message}");
            }
            return 15; // Default fallback
        }

        private void RegisterDevice()
        {
            try
            {
                var ipAddress = GetLocalIPAddress();
                
                // First, try to find existing device by name
                var findCmd = new MySqlCommand(@"
                    SELECT DEVICEID FROM DEVICES WHERE DEVICENAME = @deviceName LIMIT 1", connection);
                findCmd.Parameters.AddWithValue("@deviceName", deviceId);
                
                var existingDeviceId = findCmd.ExecuteScalar()?.ToString();
                
                if (string.IsNullOrEmpty(existingDeviceId))
                {
                    // Create new device - we need a room to associate with
                    // Try to find a room that matches our location or use the first available room
                    var roomCmd = new MySqlCommand(@"
                        SELECT ROOMID FROM ROOMS 
                        WHERE BUILDING LIKE @location OR ROOMNAME LIKE @location 
                        ORDER BY CREATED_AT DESC LIMIT 1", connection);
                    roomCmd.Parameters.AddWithValue("@location", $"%{deviceLocation}%");
                    
                    var roomId = roomCmd.ExecuteScalar()?.ToString();
                    
                    if (string.IsNullOrEmpty(roomId))
                    {
                        // Get any available room as fallback
                        roomCmd = new MySqlCommand("SELECT ROOMID FROM ROOMS WHERE STATUS = 'Available' LIMIT 1", connection);
                        roomId = roomCmd.ExecuteScalar()?.ToString();
                    }
                    
                    if (!string.IsNullOrEmpty(roomId))
                    {
                        // Insert new device
                        var insertCmd = new MySqlCommand(@"
                            INSERT INTO DEVICES (DEVICEID, DEVICETYPE, DEVICENAME, LOCATION, ROOMID, IPADDRESS, LASTSEEN, STATUS) 
                            VALUES (UUID(), 'Fingerprint_Scanner', @deviceName, @location, @roomId, @ipAddress, NOW(), 'Active')", connection);

                        insertCmd.Parameters.AddWithValue("@deviceName", deviceId);
                        insertCmd.Parameters.AddWithValue("@location", deviceLocation);
                        insertCmd.Parameters.AddWithValue("@roomId", roomId);
                        insertCmd.Parameters.AddWithValue("@ipAddress", ipAddress);
                        
                        insertCmd.ExecuteNonQuery();
                        
                        // Get the newly created device ID
                        var getNewDeviceCmd = new MySqlCommand(@"
                            SELECT DEVICEID FROM DEVICES WHERE DEVICENAME = @deviceName LIMIT 1", connection);
                        getNewDeviceCmd.Parameters.AddWithValue("@deviceName", deviceId);
                        CurrentDeviceId = getNewDeviceCmd.ExecuteScalar()?.ToString();
                        CurrentRoomId = roomId;
                    }
                }
                else
                {
                    // Update existing device
                    var updateCmd = new MySqlCommand(@"
                        UPDATE DEVICES SET 
                            IPADDRESS = @ipAddress,
                            LASTSEEN = NOW(),
                            STATUS = 'Active'
                        WHERE DEVICEID = @deviceId", connection);

                    updateCmd.Parameters.AddWithValue("@ipAddress", ipAddress);
                    updateCmd.Parameters.AddWithValue("@deviceId", existingDeviceId);
                    updateCmd.ExecuteNonQuery();
                    
                    CurrentDeviceId = existingDeviceId;
                    
                    // Get the room ID for this device
                    var roomCmd = new MySqlCommand("SELECT ROOMID FROM DEVICES WHERE DEVICEID = @deviceId", connection);
                    roomCmd.Parameters.AddWithValue("@deviceId", existingDeviceId);
                    CurrentRoomId = roomCmd.ExecuteScalar()?.ToString();
                }
                
                // Load current device information
                LoadCurrentDevice();
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to register device: {ex.Message}");
                // Don't throw - device registration is not critical for fingerprint operations
            }
        }
        
        // Load current device information
        private void LoadCurrentDevice()
        {
            try
            {
                if (string.IsNullOrEmpty(CurrentDeviceId)) return;
                
                var cmd = new MySqlCommand(@"
                    SELECT 
                        d.DEVICEID, d.DEVICETYPE, d.DEVICENAME, d.LOCATION, d.ROOMID,
                        d.IPADDRESS, d.MACADDRESS, d.FIRMWAREVERSION, d.LASTMAINTENANCE,
                        d.LASTSEEN, d.STATUS, d.CREATED_AT, d.UPDATED_AT,
                        r.ROOMNUMBER, r.ROOMNAME, r.BUILDING
                    FROM DEVICES d
                    LEFT JOIN ROOMS r ON d.ROOMID = r.ROOMID
                    WHERE d.DEVICEID = @deviceId", connection);
                
                cmd.Parameters.AddWithValue("@deviceId", CurrentDeviceId);
                
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        CurrentDevice = new Models.Device
                        {
                            DeviceId = reader.GetString("DEVICEID"),
                            DeviceType = reader.GetString("DEVICETYPE"),
                            DeviceName = reader.GetString("DEVICENAME"),
                            Location = reader.GetString("LOCATION"),
                            RoomId = reader.GetString("ROOMID"),
                            IpAddress = reader.IsDBNull(reader.GetOrdinal("IPADDRESS")) ? null : reader.GetString("IPADDRESS"),
                            MacAddress = reader.IsDBNull(reader.GetOrdinal("MACADDRESS")) ? null : reader.GetString("MACADDRESS"),
                            FirmwareVersion = reader.IsDBNull(reader.GetOrdinal("FIRMWAREVERSION")) ? null : reader.GetString("FIRMWAREVERSION"),
                            LastMaintenance = reader.IsDBNull(reader.GetOrdinal("LASTMAINTENANCE")) ? null : (DateTime?)reader.GetDateTime("LASTMAINTENANCE"),
                            LastSeen = reader.IsDBNull(reader.GetOrdinal("LASTSEEN")) ? null : (DateTime?)reader.GetDateTime("LASTSEEN"),
                            Status = reader.GetString("STATUS"),
                            CreatedAt = reader.GetDateTime("CREATED_AT"),
                            UpdatedAt = reader.GetDateTime("UPDATED_AT"),
                            Room = new Models.Room
                            {
                                RoomId = reader.GetString("ROOMID"),
                                RoomNumber = reader.IsDBNull(reader.GetOrdinal("ROOMNUMBER")) ? "" : reader.GetString("ROOMNUMBER"),
                                RoomName = reader.IsDBNull(reader.GetOrdinal("ROOMNAME")) ? "" : reader.GetString("ROOMNAME"),
                                Building = reader.IsDBNull(reader.GetOrdinal("BUILDING")) ? "" : reader.GetString("BUILDING")
                            }
                        };
                        
                        CurrentRoomId = CurrentDevice.RoomId;
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to load current device: {ex.Message}");
            }
        }

        // Load all active users from iot_attendance schema (including those without fingerprints for enrollment)
        public List<User> LoadAllUsers()
        {
            var users = new List<User>();
            
            try
            {
                // FIXED: Load ALL active users with proper fingerprint status check
                // Use LEFT JOIN to include users without fingerprints so they can be enrolled
                // Check if user has active fingerprint in AUTHENTICATIONMETHODS table
                var cmd = new MySqlCommand(@"
                    SELECT 
                        U.USERID,
                        U.FIRSTNAME,
                        U.LASTNAME,
                        U.EMAIL,
                        U.USERTYPE,
                        U.DEPARTMENT,
                        U.STATUS,
                        U.STUDENTID,
                        U.FACULTYID,
                        U.YEARLEVEL,
                        U.RFIDTAG,
                        A.FINGERPRINTTEMPLATE,
                        CASE 
                            WHEN A.USERID IS NOT NULL AND A.METHODTYPE = 'Fingerprint' AND A.ISACTIVE = 1 AND A.STATUS = 'Active'
                            THEN TRUE 
                            ELSE FALSE 
                        END as HAS_FINGERPRINT
                    FROM USERS U
                    LEFT JOIN AUTHENTICATIONMETHODS A 
                        ON A.USERID = U.USERID 
                       AND A.METHODTYPE = 'Fingerprint' 
                       AND A.ISACTIVE = 1
                       AND A.STATUS = 'Active'
                    WHERE U.STATUS = 'Active'
                    ORDER BY U.LASTNAME, U.FIRSTNAME", connection);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // Create a unique identifier from the USERID for the Id field
                        var userIdGuid = reader.GetString("USERID");
                        var userEmail = reader.IsDBNull(reader.GetOrdinal("EMAIL")) ? "" : reader.GetString("EMAIL");
                        var firstName = reader.IsDBNull(reader.GetOrdinal("FIRSTNAME")) ? "" : reader.GetString("FIRSTNAME");
                        var lastName = reader.IsDBNull(reader.GetOrdinal("LASTNAME")) ? "" : reader.GetString("LASTNAME");
                        
                        // FIXED: Handle users without fingerprints (null from LEFT JOIN)
                        byte[] fingerprintTemplate = null;
                        if (!reader.IsDBNull(reader.GetOrdinal("FINGERPRINTTEMPLATE")))
                        {
                            fingerprintTemplate = (byte[])reader["FINGERPRINTTEMPLATE"];
                        }
                        
                        // Get the HAS_FINGERPRINT status from the query
                        bool hasFingerprint = reader.GetBoolean("HAS_FINGERPRINT");
                        
                        // Get RFID tag
                        string rfidTag = reader.IsDBNull(reader.GetOrdinal("RFIDTAG")) ? null : reader.GetString("RFIDTAG");

                        users.Add(new User
                        {
                            // Use a hash of the USERID GUID as the Id for identification
                            Id = Math.Abs(userIdGuid.GetHashCode()),
                            Username = !string.IsNullOrEmpty(userEmail) ? userEmail : $"{firstName} {lastName}".Trim(),
                            FingerprintTemplate = hasFingerprint ? (fingerprintTemplate ?? new byte[0]) : new byte[0], // Use query result for enrollment status
                            Department = reader.IsDBNull(reader.GetOrdinal("DEPARTMENT")) ? null : reader.GetString("DEPARTMENT"),
                            Email = userEmail,
                            // Store the original USERID GUID in EmployeeId field for later reference
                            EmployeeId = userIdGuid,
                            IsActive = reader.GetString("STATUS").Equals("Active", StringComparison.OrdinalIgnoreCase),
                            FirstName = firstName,
                            LastName = lastName,
                            UserType = reader.IsDBNull(reader.GetOrdinal("USERTYPE")) ? null : reader.GetString("USERTYPE"),
                            RfidTag = rfidTag,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load users: {ex.Message}", ex);
            }
            
            return users;
        }

        // Delete fingerprint authentication for a user by USERID GUID
        public bool DeleteUserFingerprintByGuid(string userGuid)
        {
            try
            {
                var cmd = new MySqlCommand(@"
                    DELETE FROM AUTHENTICATIONMETHODS
                    WHERE USERID = @USERID AND METHODTYPE = 'Fingerprint'", connection);
                cmd.Parameters.AddWithValue("@USERID", userGuid);
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to delete fingerprint: {ex.Message}", ex);
            }
        }

        // Load all available rooms from the database
        public List<Models.Room> LoadAllRooms()
        {
            var rooms = new List<Models.Room>();
            
            try
            {
                var cmd = new MySqlCommand(@"
                    SELECT 
                        ROOMID,
                        ROOMNUMBER,
                        ROOMNAME,
                        BUILDING,
                        CAPACITY,
                        DEVICEID,
                        DOORSTATUS,
                        STATUS,
                        CREATED_AT,
                        UPDATED_AT
                    FROM ROOMS 
                    WHERE STATUS = 'Available'
                    ORDER BY BUILDING, ROOMNUMBER", connection);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rooms.Add(new Models.Room
                        {
                            RoomId = reader.GetString("ROOMID"),
                            RoomNumber = reader.GetString("ROOMNUMBER"),
                            RoomName = reader.IsDBNull(reader.GetOrdinal("ROOMNAME")) ? "" : reader.GetString("ROOMNAME"),
                            Building = reader.GetString("BUILDING"),
                            Capacity = reader.IsDBNull(reader.GetOrdinal("CAPACITY")) ? null : (int?)reader.GetInt32("CAPACITY"),
                            DeviceId = reader.IsDBNull(reader.GetOrdinal("DEVICEID")) ? null : reader.GetString("DEVICEID"),
                            DoorStatus = reader.IsDBNull(reader.GetOrdinal("DOORSTATUS")) ? "locked" : reader.GetString("DOORSTATUS"),
                            Status = reader.GetString("STATUS"),
                            CreatedAt = reader.GetDateTime("CREATED_AT"),
                            UpdatedAt = reader.GetDateTime("UPDATED_AT")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load rooms: {ex.Message}", ex);
            }
            
            return rooms;
        }

        // Get distinct buildings for location selection
        public List<string> GetBuildings()
        {
            var buildings = new List<string>();
            
            try
            {
                var cmd = new MySqlCommand(@"
                    SELECT DISTINCT BUILDING 
                    FROM ROOMS 
                    WHERE STATUS = 'Available' 
                    ORDER BY BUILDING", connection);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        buildings.Add(reader.GetString("BUILDING"));
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to load buildings: {ex.Message}");
            }
            
            return buildings;
        }

        // Get rooms for a specific building
        public List<Models.Room> GetRoomsByBuilding(string building)
        {
            var rooms = new List<Models.Room>();
            
            try
            {
                var cmd = new MySqlCommand(@"
                    SELECT 
                        ROOMID,
                        ROOMNUMBER,
                        ROOMNAME,
                        BUILDING,
                        CAPACITY,
                        DEVICEID,
                        DOORSTATUS,
                        STATUS,
                        CREATED_AT,
                        UPDATED_AT
                    FROM ROOMS 
                    WHERE BUILDING = @building AND STATUS = 'Available'
                    ORDER BY ROOMNUMBER", connection);

                cmd.Parameters.AddWithValue("@building", building);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rooms.Add(new Models.Room
                        {
                            RoomId = reader.GetString("ROOMID"),
                            RoomNumber = reader.GetString("ROOMNUMBER"),
                            RoomName = reader.IsDBNull(reader.GetOrdinal("ROOMNAME")) ? "" : reader.GetString("ROOMNAME"),
                            Building = reader.GetString("BUILDING"),
                            Capacity = reader.IsDBNull(reader.GetOrdinal("CAPACITY")) ? null : (int?)reader.GetInt32("CAPACITY"),
                            DeviceId = reader.IsDBNull(reader.GetOrdinal("DEVICEID")) ? null : reader.GetString("DEVICEID"),
                            DoorStatus = reader.IsDBNull(reader.GetOrdinal("DOORSTATUS")) ? "locked" : reader.GetString("DOORSTATUS"),
                            Status = reader.GetString("STATUS"),
                            CreatedAt = reader.GetDateTime("CREATED_AT"),
                            UpdatedAt = reader.GetDateTime("UPDATED_AT")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to load rooms for building {building}: {ex.Message}");
            }
            
            return rooms;
        }

        // Change the current room for this device
        public bool ChangeCurrentRoom(string roomId)
        {
            try
            {
                if (string.IsNullOrEmpty(CurrentDeviceId))
                {
                    LogMessage("WARNING", "No current device ID available for room change");
                    return false;
                }

                var cmd = new MySqlCommand(@"
                    UPDATE DEVICES SET 
                        ROOMID = @roomId,
                        UPDATED_AT = NOW()
                    WHERE DEVICEID = @deviceId", connection);

                cmd.Parameters.AddWithValue("@roomId", roomId);
                cmd.Parameters.AddWithValue("@deviceId", CurrentDeviceId);
                
                var result = cmd.ExecuteNonQuery() > 0;
                
                if (result)
                {
                    CurrentRoomId = roomId;
                    LoadCurrentDevice(); // Refresh device info
                    LogMessage("INFO", $"Device room changed to: {roomId}");
                    LogMessage("INFO", $"Current device room after change: {CurrentDevice?.Room?.DisplayName ?? "Unknown"}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to change room: {ex.Message}");
                return false;
            }
        }

        // Change the current location (inside/outside)
        public void ChangeCurrentLocation(string location)
        {
            if (location == "inside" || location == "outside")
            {
                CurrentLocation = location;
                LogMessage("INFO", $"Location changed to: {location}");
            }
            else
            {
                LogMessage("WARNING", $"Invalid location: {location}. Must be 'inside' or 'outside'");
            }
        }

        // Get current room information
        public Models.Room GetCurrentRoom()
        {
            return CurrentDevice?.Room;
        }

        // Debug method to log current device and room status
        public void LogCurrentDeviceStatus()
        {
            LogMessage("DEBUG", $"Current Device ID: {CurrentDeviceId}");
            LogMessage("DEBUG", $"Current Room ID: {CurrentRoomId}");
            LogMessage("DEBUG", $"Current Device Name: {CurrentDevice?.DeviceName ?? "Unknown"}");
            LogMessage("DEBUG", $"Current Room: {CurrentDevice?.Room?.DisplayName ?? "Unknown"}");
            LogMessage("DEBUG", $"Current Location: {CurrentLocation}");
        }

        // Initialize device with a specific room (for multi-device support)
        public bool InitializeDeviceInRoom(string roomId, string deviceName = null)
        {
            try
            {
                if (string.IsNullOrEmpty(roomId))
                {
                    LogMessage("ERROR", "Cannot initialize device: roomId is required");
                    return false;
                }

                var actualDeviceName = deviceName ?? deviceId;
                var ipAddress = GetLocalIPAddress();

                // Check if device already exists
                var findCmd = new MySqlCommand(@"
                    SELECT DEVICEID FROM DEVICES WHERE DEVICENAME = @deviceName LIMIT 1", connection);
                findCmd.Parameters.AddWithValue("@deviceName", actualDeviceName);
                
                var existingDeviceId = findCmd.ExecuteScalar()?.ToString();
                
                if (string.IsNullOrEmpty(existingDeviceId))
                {
                    // Create new device in specified room
                    var insertCmd = new MySqlCommand(@"
                        INSERT INTO DEVICES (DEVICEID, DEVICETYPE, DEVICENAME, LOCATION, ROOMID, IPADDRESS, LASTSEEN, STATUS) 
                        VALUES (UUID(), 'Fingerprint_Scanner', @deviceName, @location, @roomId, @ipAddress, NOW(), 'Active')", connection);

                    insertCmd.Parameters.AddWithValue("@deviceName", actualDeviceName);
                    insertCmd.Parameters.AddWithValue("@location", deviceLocation);
                    insertCmd.Parameters.AddWithValue("@roomId", roomId);
                    insertCmd.Parameters.AddWithValue("@ipAddress", ipAddress);
                    
                    insertCmd.ExecuteNonQuery();
                    
                    // Get the newly created device ID
                    CurrentDeviceId = findCmd.ExecuteScalar()?.ToString();
                }
                else
                {
                    // Update existing device to new room
                    var updateCmd = new MySqlCommand(@"
                        UPDATE DEVICES SET 
                            ROOMID = @roomId,
                            IPADDRESS = @ipAddress,
                            LASTSEEN = NOW(),
                            STATUS = 'Active'
                        WHERE DEVICEID = @deviceId", connection);

                    updateCmd.Parameters.AddWithValue("@roomId", roomId);
                    updateCmd.Parameters.AddWithValue("@ipAddress", ipAddress);
                    updateCmd.Parameters.AddWithValue("@deviceId", existingDeviceId);
                    updateCmd.ExecuteNonQuery();
                    
                    CurrentDeviceId = existingDeviceId;
                }
                
                CurrentRoomId = roomId;
                LoadCurrentDevice();
                
                LogMessage("INFO", $"Device {actualDeviceName} initialized in room {roomId}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to initialize device in room: {ex.Message}");
                return false;
            }
        }

        // Get all devices for management
        public List<Models.Device> GetAllDevices()
        {
            var devices = new List<Models.Device>();
            
            try
            {
                var cmd = new MySqlCommand(@"
                    SELECT 
                        d.DEVICEID, d.DEVICETYPE, d.DEVICENAME, d.LOCATION, d.ROOMID,
                        d.IPADDRESS, d.MACADDRESS, d.FIRMWAREVERSION, d.LASTMAINTENANCE,
                        d.LASTSEEN, d.STATUS, d.CREATED_AT, d.UPDATED_AT,
                        r.ROOMNUMBER, r.ROOMNAME, r.BUILDING
                    FROM DEVICES d
                    LEFT JOIN ROOMS r ON d.ROOMID = r.ROOMID
                    ORDER BY d.DEVICENAME", connection);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        devices.Add(new Models.Device
                        {
                            DeviceId = reader.GetString("DEVICEID"),
                            DeviceType = reader.GetString("DEVICETYPE"),
                            DeviceName = reader.GetString("DEVICENAME"),
                            Location = reader.GetString("LOCATION"),
                            RoomId = reader.GetString("ROOMID"),
                            IpAddress = reader.IsDBNull(reader.GetOrdinal("IPADDRESS")) ? null : reader.GetString("IPADDRESS"),
                            MacAddress = reader.IsDBNull(reader.GetOrdinal("MACADDRESS")) ? null : reader.GetString("MACADDRESS"),
                            FirmwareVersion = reader.IsDBNull(reader.GetOrdinal("FIRMWAREVERSION")) ? null : reader.GetString("FIRMWAREVERSION"),
                            LastMaintenance = reader.IsDBNull(reader.GetOrdinal("LASTMAINTENANCE")) ? null : (DateTime?)reader.GetDateTime("LASTMAINTENANCE"),
                            LastSeen = reader.IsDBNull(reader.GetOrdinal("LASTSEEN")) ? null : (DateTime?)reader.GetDateTime("LASTSEEN"),
                            Status = reader.GetString("STATUS"),
                            CreatedAt = reader.GetDateTime("CREATED_AT"),
                            UpdatedAt = reader.GetDateTime("UPDATED_AT"),
                            Room = new Models.Room
                            {
                                RoomId = reader.GetString("ROOMID"),
                                RoomNumber = reader.IsDBNull(reader.GetOrdinal("ROOMNUMBER")) ? "" : reader.GetString("ROOMNUMBER"),
                                RoomName = reader.IsDBNull(reader.GetOrdinal("ROOMNAME")) ? "" : reader.GetString("ROOMNAME"),
                                Building = reader.IsDBNull(reader.GetOrdinal("BUILDING")) ? "" : reader.GetString("BUILDING")
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to load devices: {ex.Message}");
            }
            
            return devices;
        }

        // Create or update a user in USERS and upsert fingerprint in AUTHENTICATIONMETHODS
        public void CreateOrUpdateWebUserWithFingerprint(
            string firstName,
            string lastName,
            string email,
            string passwordHash,
            string userType,
            string status,
            string studentId,
            string facultyId,
            string yearLevel,
            string department,
            byte[] fingerprintTemplate)
        {
            try
            {
                // Ensure USERS row exists (by EMAIL as unique key)
                // Upsert USERS
                var upsertUser = new MySqlCommand(@"
                    INSERT INTO USERS (USERID, FIRSTNAME, LASTNAME, EMAIL, PASSWORD_HASH, USERTYPE, STATUS, STUDENTID, FACULTYID, YEARLEVEL, DEPARTMENT)
                    VALUES (UUID(), @FIRSTNAME, @LASTNAME, @EMAIL, @PASSWORD_HASH, @USERTYPE, @STATUS, @STUDENTID, @FACULTYID, @YEARLEVEL, @DEPARTMENT)
                    ON DUPLICATE KEY UPDATE
                        FIRSTNAME = VALUES(FIRSTNAME),
                        LASTNAME = VALUES(LASTNAME),
                        PASSWORD_HASH = COALESCE(VALUES(PASSWORD_HASH), PASSWORD_HASH),
                        USERTYPE = VALUES(USERTYPE),
                        STATUS = VALUES(STATUS),
                        STUDENTID = COALESCE(VALUES(STUDENTID), STUDENTID),
                        FACULTYID = COALESCE(VALUES(FACULTYID), FACULTYID),
                        YEARLEVEL = VALUES(YEARLEVEL),
                        DEPARTMENT = VALUES(DEPARTMENT)
                ", connection);

                upsertUser.Parameters.AddWithValue("@FIRSTNAME", firstName);
                upsertUser.Parameters.AddWithValue("@LASTNAME", lastName);
                upsertUser.Parameters.AddWithValue("@EMAIL", email);
                upsertUser.Parameters.AddWithValue("@PASSWORD_HASH", (object)passwordHash ?? DBNull.Value);
                upsertUser.Parameters.AddWithValue("@USERTYPE", userType);
                upsertUser.Parameters.AddWithValue("@STATUS", status);
                upsertUser.Parameters.AddWithValue("@STUDENTID", (object)studentId ?? DBNull.Value);
                upsertUser.Parameters.AddWithValue("@FACULTYID", (object)facultyId ?? DBNull.Value);
                upsertUser.Parameters.AddWithValue("@YEARLEVEL", (object)yearLevel ?? DBNull.Value);
                upsertUser.Parameters.AddWithValue("@DEPARTMENT", (object)department ?? DBNull.Value);
                upsertUser.ExecuteNonQuery();

                // Get USERID by EMAIL
                var getUserId = new MySqlCommand("SELECT USERID FROM USERS WHERE EMAIL = @EMAIL", connection);
                getUserId.Parameters.AddWithValue("@EMAIL", email);
                var userIdObj = getUserId.ExecuteScalar();
                if (userIdObj == null)
                    throw new Exception("Failed to resolve USERID after upsert.");
                var userId = Convert.ToString(userIdObj);

                // Upsert AUTHENTICATIONMETHODS row for Fingerprint
                var upsertAuth = new MySqlCommand(@"
                    INSERT INTO AUTHENTICATIONMETHODS (AUTHID, USERID, METHODTYPE, IDENTIFIER, FINGERPRINTTEMPLATE, ISACTIVE, STATUS)
                    VALUES (UUID(), @USERID, 'Fingerprint', @IDENTIFIER, @TEMPLATE, TRUE, 'Active')
                    ON DUPLICATE KEY UPDATE
                        FINGERPRINTTEMPLATE = VALUES(FINGERPRINTTEMPLATE),
                        ISACTIVE = TRUE,
                        STATUS = 'Active'
                ", connection);

                // Use a stable identifier; prefer STUDENTID/FACULTYID, otherwise EMAIL
                string identifier = !string.IsNullOrWhiteSpace(studentId) ? $"FP_{studentId}" :
                                    !string.IsNullOrWhiteSpace(facultyId) ? $"FP_{facultyId}" : $"FP_{email}";

                upsertAuth.Parameters.AddWithValue("@USERID", userId);
                upsertAuth.Parameters.AddWithValue("@IDENTIFIER", identifier);
                upsertAuth.Parameters.AddWithValue("@TEMPLATE", fingerprintTemplate ?? new byte[0]);
                upsertAuth.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create/update user with fingerprint: {ex.Message}", ex);
            }
        }

        // Add fingerprint template to existing user by USERID
        public bool AddFingerprintToExistingUser(string userGuid, byte[] fingerprintTemplate)
        {
            try
            {
                // First, verify the user exists
                var checkUserCmd = new MySqlCommand("SELECT USERID FROM USERS WHERE USERID = @USERID AND STATUS = 'Active'", connection);
                checkUserCmd.Parameters.AddWithValue("@USERID", userGuid);
                var userExists = checkUserCmd.ExecuteScalar() != null;
                
                if (!userExists)
                {
                    throw new Exception($"User with GUID {userGuid} not found or not active");
                }

                // Delete any existing fingerprint records for this user
                var deleteExistingCmd = new MySqlCommand(@"
                    DELETE FROM AUTHENTICATIONMETHODS 
                    WHERE USERID = @USERID AND METHODTYPE = 'Fingerprint'", connection);
                deleteExistingCmd.Parameters.AddWithValue("@USERID", userGuid);
                deleteExistingCmd.ExecuteNonQuery();

                // Insert new fingerprint record
                var insertFingerprintCmd = new MySqlCommand(@"
                    INSERT INTO AUTHENTICATIONMETHODS (AUTHID, USERID, METHODTYPE, IDENTIFIER, FINGERPRINTTEMPLATE, ISACTIVE, STATUS)
                    VALUES (UUID(), @USERID, 'Fingerprint', @IDENTIFIER, @TEMPLATE, TRUE, 'Active')", connection);
                
                insertFingerprintCmd.Parameters.AddWithValue("@USERID", userGuid);
                insertFingerprintCmd.Parameters.AddWithValue("@IDENTIFIER", $"FP_{userGuid}");
                insertFingerprintCmd.Parameters.AddWithValue("@TEMPLATE", fingerprintTemplate ?? new byte[0]);
                
                var result = insertFingerprintCmd.ExecuteNonQuery() > 0;
                
                if (result)
                {
                    LogMessage("INFO", $"Successfully added fingerprint for user {userGuid}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to add fingerprint to existing user: {ex.Message}");
                throw new Exception($"Failed to add fingerprint to existing user: {ex.Message}", ex);
            }
        }

        public User GetUserByRfidTag(string rfidTag)
        {
            try
            {
                Console.WriteLine($"====== DATABASE RFID LOOKUP ======");
                Console.WriteLine($"Searching for RFIDTAG = '{rfidTag}'");
                
                var cmd = new MySqlCommand("SELECT * FROM USERS WHERE RFIDTAG = @rfidTag", connection);
                cmd.Parameters.AddWithValue("@rfidTag", rfidTag);
                
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        string firstName = reader.GetString("FIRSTNAME");
                        string lastName = reader.GetString("LASTNAME");
                        string userType = reader.IsDBNull(reader.GetOrdinal("USERTYPE")) ? "NULL" : reader.GetString("USERTYPE");
                        string status = reader.GetString("STATUS");
                        
                        Console.WriteLine($"✅ RFID Match Found!");
                        Console.WriteLine($"   Name: {firstName} {lastName}");
                        Console.WriteLine($"   UserType: {userType}");
                        Console.WriteLine($"   Status: {status}");
                        
                        return new User
                        {
                            Id = 0, // USERID is a GUID string, not an integer
                            Username = firstName + " " + lastName,
                            FirstName = firstName,
                            LastName = lastName,
                            FingerprintTemplate = new byte[0], // No fingerprint template in this table
                            EmployeeId = reader.GetString("USERID"), // Use USERID as the GUID
                            Department = reader.IsDBNull(reader.GetOrdinal("DEPARTMENT")) ? null : reader.GetString("DEPARTMENT"),
                            Email = reader.IsDBNull(reader.GetOrdinal("EMAIL")) ? null : reader.GetString("EMAIL"),
                            Phone = reader.IsDBNull(reader.GetOrdinal("PHONENUMBER")) ? null : reader.GetString("PHONENUMBER"),
                            IsActive = status == "Active",
                            UserType = reader.IsDBNull(reader.GetOrdinal("USERTYPE")) ? null : reader.GetString("USERTYPE"),
                            RfidTag = reader.IsDBNull(reader.GetOrdinal("RFIDTAG")) ? null : reader.GetString("RFIDTAG"),
                            CreatedAt = reader.GetDateTime("CREATED_AT"),
                            UpdatedAt = reader.GetDateTime("UPDATED_AT")
                        };
                    }
                    else
                    {
                        Console.WriteLine($"❌ No match found in database");
                        Console.WriteLine($"   Query: SELECT * FROM USERS WHERE RFIDTAG = '{rfidTag}'");
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get user: {ex.Message}", ex);
            }
        }

        // Update or delete RFID tag for a user
        public bool UpdateUserRfidTag(string userGuid, string rfidTag)
        {
            try
            {
                var cmd = new MySqlCommand("UPDATE USERS SET RFIDTAG = @rfidTag, UPDATED_AT = CURRENT_TIMESTAMP WHERE USERID = @userGuid", connection);
                cmd.Parameters.AddWithValue("@rfidTag", string.IsNullOrEmpty(rfidTag) ? (object)DBNull.Value : rfidTag);
                cmd.Parameters.AddWithValue("@userGuid", userGuid);
                
                int rowsAffected = cmd.ExecuteNonQuery();
                Console.WriteLine($"Updated RFID tag for user {userGuid}: rows affected = {rowsAffected}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update RFID tag: {ex.Message}");
                throw new Exception($"Failed to update RFID tag: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Log an access attempt to ACCESSLOGS table (supports NULL USERID for unknown users)
        /// </summary>
        public void LogAccessAttempt(string userId, string roomId, string authMethod, 
            string location, string accessType, string result, string reason = null)
        {
            try
            {
                if (connection == null || connection.State != System.Data.ConnectionState.Open)
                {
                    Console.WriteLine("Database connection not available for logging access attempt");
                    return;
                }

                // Use CurrentRoomId if roomId is not provided
                string effectiveRoomId = string.IsNullOrEmpty(roomId) ? CurrentRoomId : roomId;
                
                if (string.IsNullOrEmpty(effectiveRoomId))
                {
                    Console.WriteLine("⚠️ Cannot log access attempt - no room ID available");
                    LogMessage("WARNING", "Cannot log access attempt - no room ID available");
                    return;
                }

                string logId = Guid.NewGuid().ToString();
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                var cmd = new MySqlCommand(@"
                    INSERT INTO ACCESSLOGS (
                        LOGID, USERID, ROOMID, TIMESTAMP, ACCESSTYPE, AUTHMETHOD,
                        LOCATION, RESULT, REASON, CREATED_AT
                    ) VALUES (
                        @LOGID, @USERID, @ROOMID, @TIMESTAMP, @ACCESSTYPE, @AUTHMETHOD,
                        @LOCATION, @RESULT, @REASON, NOW()
                    )", connection);

                cmd.Parameters.AddWithValue("@LOGID", logId);
                cmd.Parameters.AddWithValue("@USERID", string.IsNullOrEmpty(userId) ? (object)DBNull.Value : userId);
                cmd.Parameters.AddWithValue("@ROOMID", effectiveRoomId);
                cmd.Parameters.AddWithValue("@TIMESTAMP", timestamp);
                cmd.Parameters.AddWithValue("@ACCESSTYPE", accessType);
                cmd.Parameters.AddWithValue("@AUTHMETHOD", authMethod);
                cmd.Parameters.AddWithValue("@LOCATION", location);
                cmd.Parameters.AddWithValue("@RESULT", result);
                cmd.Parameters.AddWithValue("@REASON", string.IsNullOrEmpty(reason) ? (object)DBNull.Value : reason);

                cmd.ExecuteNonQuery();
                Console.WriteLine($"✅ Access attempt logged: {accessType} - {result} for room {effectiveRoomId}");
                LogMessage("INFO", $"Access attempt logged: {accessType} - {result} - {(string.IsNullOrEmpty(userId) ? "Unknown User" : userId)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to log access attempt: {ex.Message}");
                LogMessage("ERROR", $"Failed to log access attempt: {ex.Message}");
                // Don't throw - logging failures shouldn't break the application
            }
        }

        public bool DeleteUser(int userId)
        {
            try
            {
                var cmd = new MySqlCommand("DELETE FROM users WHERE id = @id", connection);
                cmd.Parameters.AddWithValue("@id", userId);
                
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to delete user: {ex.Message}", ex);
            }
        }

        // Record attendance into iot_attendance.ATTENDANCERECORDS compatible with frontend /attendance-logs
        public void RecordAttendance(int userId, string action, string notes = null)
        {
            // Legacy method - try to resolve by hash
            RecordAttendanceByHash(userId, action, notes);
        }

        // Result object for attendance attempts
        public class AttendanceAttemptResult
        {
            public bool Success { get; set; }
            public string Reason { get; set; }
            public string ScheduleId { get; set; }
            public string SubjectName { get; set; }
        }

        // New method that accepts the user GUID directly with optional location (non-throwing variant returns result)
        public AttendanceAttemptResult TryRecordAttendanceByGuid(string userGuid, string action, string location = null)
        {
            try
            {
                var result = InternalTryRecordAttendance(userGuid, action, location);
                return result;
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"TryRecordAttendanceByGuid failed: {ex.Message}");
                return new AttendanceAttemptResult { Success = false, Reason = ex.Message };
            }
        }

        // Old method preserved for compatibility
        public void RecordAttendanceByGuid(string userGuid, string action, string notes = null)
        {
            try
            {
                RecordAttendanceInternal(userGuid, action, notes);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to record attendance by GUID: {ex.Message}", ex);
            }
        }

        private void RecordAttendanceByHash(int userId, string action, string notes = null)
        {
            try
            {
                // The userId parameter is actually a hash of the USERID GUID
                // We need to find the actual USERID GUID from our loaded users
                string userGuid = null;

                // Try to find the user GUID by matching the hash ID
                var findGuidCmd = new MySqlCommand(@"
                    SELECT USERID 
                    FROM USERS 
                    WHERE STATUS = 'Active'", connection);
                
                try
                {
                    using (var reader = findGuidCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var guid = reader.GetString("USERID");
                            if (Math.Abs(guid.GetHashCode()) == userId)
                            {
                                userGuid = guid;
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogMessage("ERROR", $"Error finding user GUID: {ex.Message}");
                }

                // If we cannot resolve a GUID, skip to avoid corrupt data
                if (string.IsNullOrWhiteSpace(userGuid))
                {
                    LogMessage("ERROR", $"RecordAttendance: Failed to resolve USERID GUID for hash id {userId}");
                    return;
                }

                RecordAttendanceInternal(userGuid, action, notes);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to record attendance by hash: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets or creates an "Administrative Access" schedule for custodians and deans without specific class schedules.
        /// This ensures all attendance records have a valid SCHEDULEID without requiring schema changes.
        /// </summary>
        private string GetOrCreateAdministrativeSchedule(string roomId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(roomId))
                {
                    LogMessage("WARNING", "Room ID is null or empty, cannot create administrative schedule");
                    return null;
                }

                // Get current academic year and semester from settings
                string academicYear = CurrentAcademicYear;
                string semester = CurrentSemester;

                // First, get or create the "Administrative Access" subject
                string adminSubjectId = null;
                using (var cmdGetSubject = new MySqlCommand(@"
                    SELECT SUBJECTID FROM SUBJECTS 
                    WHERE SUBJECTCODE = 'ADMIN-ACCESS' 
                      AND ARCHIVED_AT IS NULL 
                    LIMIT 1", connection))
                {
                    var subjectObj = cmdGetSubject.ExecuteScalar();
                    if (subjectObj != null)
                    {
                        adminSubjectId = subjectObj.ToString();
                        LogMessage("INFO", $"Found existing Administrative Access subject: {adminSubjectId}");
                    }
                }

                // If subject doesn't exist, create it
                if (string.IsNullOrWhiteSpace(adminSubjectId))
                {
                    // Get a system admin user ID (or use first admin user)
                    string adminUserId = null;
                    using (var cmdGetAdmin = new MySqlCommand(@"
                        SELECT USERID FROM USERS 
                        WHERE USERTYPE = 'admin' AND ARCHIVED_AT IS NULL 
                        LIMIT 1", connection))
                    {
                        var adminObj = cmdGetAdmin.ExecuteScalar();
                        if (adminObj != null)
                        {
                            adminUserId = adminObj.ToString();
                        }
                    }

                    if (string.IsNullOrWhiteSpace(adminUserId))
                    {
                        LogMessage("ERROR", "No admin user found to assign Administrative Access subject");
                        return null;
                    }

                    // Create the subject
                    adminSubjectId = Guid.NewGuid().ToString();
                    using (var cmdCreateSubject = new MySqlCommand(@"
                        INSERT INTO SUBJECTS (SUBJECTID, SUBJECTCODE, SUBJECTNAME, INSTRUCTORID, SEMESTER, YEAR, ACADEMICYEAR)
                        VALUES (@subjectId, 'ADMIN-ACCESS', 'Administrative Door Access', @instructorId, @semester, YEAR(CURDATE()), @academicYear)", connection))
                    {
                        cmdCreateSubject.Parameters.AddWithValue("@subjectId", adminSubjectId);
                        cmdCreateSubject.Parameters.AddWithValue("@instructorId", adminUserId);
                        cmdCreateSubject.Parameters.AddWithValue("@semester", semester);
                        cmdCreateSubject.Parameters.AddWithValue("@academicYear", academicYear);
                        cmdCreateSubject.ExecuteNonQuery();
                        LogMessage("INFO", $"Created Administrative Access subject: {adminSubjectId}");
                    }
                }

                // Now, get or create a schedule for this subject in the specified room
                string scheduleId = null;
                using (var cmdGetSchedule = new MySqlCommand(@"
                    SELECT SCHEDULEID FROM CLASSSCHEDULES 
                    WHERE SUBJECTID = @subjectId 
                      AND ROOMID = @roomId 
                      AND ACADEMICYEAR = @academicYear
                      AND SEMESTER = @semester
                      AND ARCHIVED_AT IS NULL
                    LIMIT 1", connection))
                {
                    cmdGetSchedule.Parameters.AddWithValue("@subjectId", adminSubjectId);
                    cmdGetSchedule.Parameters.AddWithValue("@roomId", roomId);
                    cmdGetSchedule.Parameters.AddWithValue("@academicYear", academicYear);
                    cmdGetSchedule.Parameters.AddWithValue("@semester", semester);
                    var scheduleObj = cmdGetSchedule.ExecuteScalar();
                    if (scheduleObj != null)
                    {
                        scheduleId = scheduleObj.ToString();
                        LogMessage("INFO", $"Found existing Administrative Access schedule: {scheduleId} for room {roomId}");
                        return scheduleId;
                    }
                }

                // If schedule doesn't exist, create it (Monday-Friday, all day access)
                scheduleId = Guid.NewGuid().ToString();
                using (var cmdCreateSchedule = new MySqlCommand(@"
                    INSERT INTO CLASSSCHEDULES (SCHEDULEID, SUBJECTID, ROOMID, DAYOFWEEK, STARTTIME, ENDTIME, ACADEMICYEAR, SEMESTER)
                    VALUES (@scheduleId, @subjectId, @roomId, 'Monday', '00:00:00', '23:59:59', @academicYear, @semester)", connection))
                {
                    cmdCreateSchedule.Parameters.AddWithValue("@scheduleId", scheduleId);
                    cmdCreateSchedule.Parameters.AddWithValue("@subjectId", adminSubjectId);
                    cmdCreateSchedule.Parameters.AddWithValue("@roomId", roomId);
                    cmdCreateSchedule.Parameters.AddWithValue("@academicYear", academicYear);
                    cmdCreateSchedule.Parameters.AddWithValue("@semester", semester);
                    cmdCreateSchedule.ExecuteNonQuery();
                    LogMessage("INFO", $"Created Administrative Access schedule: {scheduleId} for room {roomId}");
                }

                // Create schedules for other weekdays too (Tuesday-Friday) for completeness
                string[] weekdays = { "Tuesday", "Wednesday", "Thursday", "Friday" };
                foreach (string day in weekdays)
                {
                    string additionalScheduleId = Guid.NewGuid().ToString();
                    try
                    {
                        using (var cmdCreateAdditional = new MySqlCommand(@"
                            INSERT INTO CLASSSCHEDULES (SCHEDULEID, SUBJECTID, ROOMID, DAYOFWEEK, STARTTIME, ENDTIME, ACADEMICYEAR, SEMESTER)
                            VALUES (@scheduleId, @subjectId, @roomId, @dayOfWeek, '00:00:00', '23:59:59', @academicYear, @semester)", connection))
                        {
                            cmdCreateAdditional.Parameters.AddWithValue("@scheduleId", additionalScheduleId);
                            cmdCreateAdditional.Parameters.AddWithValue("@subjectId", adminSubjectId);
                            cmdCreateAdditional.Parameters.AddWithValue("@roomId", roomId);
                            cmdCreateAdditional.Parameters.AddWithValue("@dayOfWeek", day);
                            cmdCreateAdditional.Parameters.AddWithValue("@academicYear", academicYear);
                            cmdCreateAdditional.Parameters.AddWithValue("@semester", semester);
                            cmdCreateAdditional.ExecuteNonQuery();
                        }
                    }
                    catch (Exception ex)
                    {
                        // Ignore duplicate key errors, log others
                        LogMessage("WARNING", $"Could not create additional administrative schedule for {day}: {ex.Message}");
                    }
                }

                return scheduleId;
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to get/create administrative schedule: {ex.Message}");
                return null;
            }
        }

        private AttendanceAttemptResult InternalTryRecordAttendance(string userGuid, string action, string location = null)
        {
            try
            {
                LogMessage("INFO", $"Recording attendance for user {userGuid}, action {action}, current room: {CurrentRoomId}");                                

                // Check if this is a sign-out action - if so, use more lenient validation
                bool isSignOut = !string.IsNullOrEmpty(action) && 
                    (action.IndexOf("sign-out", StringComparison.OrdinalIgnoreCase) >= 0 || 
                     action.IndexOf("sign out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     action.IndexOf("check-out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     action.IndexOf("check out", StringComparison.OrdinalIgnoreCase) >= 0);

                if (isSignOut)
                {
                    LogMessage("INFO", $"🔍 SIGN-OUT DETECTED: Processing sign-out for user {userGuid}, action: {action}");
                    Console.WriteLine($"🔍 SIGN-OUT DETECTED: Processing sign-out for user {userGuid}, action: {action}");
                }

                // First, validate if there's a scheduled class for the current time and room                                                                   
                var scheduleValidation = ValidateScheduleForCurrentTime(userGuid);                                                                              
                
                // For sign-out, allow if validation fails but there's an active session or prior sign-in today
                if (!scheduleValidation.IsValid && !isSignOut)
                {
                    LogMessage("WARNING", $"Schedule validation failed: {scheduleValidation.Reason}");                                                          
                    return new AttendanceAttemptResult { Success = false, Reason = scheduleValidation.Reason, SubjectName = scheduleValidation.SubjectName };   
                }
                
                // For sign-out with failed validation, try to find active session or prior sign-in
                if (!scheduleValidation.IsValid && isSignOut)
                {
                    LogMessage("INFO", $"Sign-out action with failed schedule validation - checking for active session or prior sign-in...");
                    
                    // Check for active session in this room (any status)
                    using (var cmdFindSession = new MySqlCommand(@"
                        SELECT SESSIONID, SCHEDULEID, STATUS
                        FROM SESSIONS
                        WHERE SESSIONDATE = CURRENT_DATE
                          AND ROOMID = @currentRoomId
                          AND STATUS IN ('active', 'waiting', 'ended')
                        ORDER BY COALESCE(STARTTIME, TIMESTAMP(CURRENT_DATE,'00:00:00')) DESC
                        LIMIT 1", connection))
                    {
                        cmdFindSession.Parameters.AddWithValue("@currentRoomId", CurrentRoomId ?? "");
                        using (var r = cmdFindSession.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                string foundSessionId = r.GetString(0);
                                string foundScheduleId = r.IsDBNull(1) ? null : r.GetString(1);
                                string sessionStatus = r.GetString(2);
                                
                                LogMessage("INFO", $"Found active session {foundSessionId} (status: {sessionStatus}) for sign-out");
                                
                                // Use the schedule from the session
                                if (!string.IsNullOrEmpty(foundScheduleId))
                                {
                                    scheduleValidation.IsValid = true;
                                    scheduleValidation.ScheduleId = foundScheduleId;
                                    scheduleValidation.Reason = "Sign-out allowed - active session found";
                                }
                            }
                        }
                    }
                    
                    // If still invalid, check for prior sign-in today in this room
                    if (!scheduleValidation.IsValid)
                    {
                        using (var cmdFindSignIn = new MySqlCommand(@"
                            SELECT DISTINCT ar.SCHEDULEID, cs.SUBJECTID, s.SUBJECTNAME
                            FROM ATTENDANCERECORDS ar
                            LEFT JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
                            LEFT JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
                            WHERE ar.USERID = @userGuid
                              AND DATE(ar.SCANDATETIME) = CURRENT_DATE
                              AND ar.SCANTYPE IN ('time_in', 'time_in_confirmation', 'early_arrival', 'early_arrival_upgraded')
                              AND EXISTS (
                                  SELECT 1 FROM CLASSSCHEDULES cs2
                                  WHERE cs2.SCHEDULEID = ar.SCHEDULEID
                                    AND cs2.ROOMID = @currentRoomId
                              )
                            ORDER BY ar.SCANDATETIME DESC
                            LIMIT 1", connection))
                        {
                            cmdFindSignIn.Parameters.AddWithValue("@userGuid", userGuid);
                            cmdFindSignIn.Parameters.AddWithValue("@currentRoomId", CurrentRoomId ?? "");
                            using (var r = cmdFindSignIn.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    string foundScheduleId = r.IsDBNull(0) ? null : r.GetString(0);
                                    if (!string.IsNullOrEmpty(foundScheduleId))
                                    {
                                        scheduleValidation.IsValid = true;
                                        scheduleValidation.ScheduleId = foundScheduleId;
                                        scheduleValidation.SubjectName = r.IsDBNull(2) ? "Unknown" : r.GetString(2);
                                        scheduleValidation.Reason = "Sign-out allowed - prior sign-in found";
                                        LogMessage("INFO", $"Sign-out allowed - found prior sign-in with schedule {foundScheduleId}");
                                    }
                                }
                            }
                        }
                    }
                    
                    // If still invalid, deny sign-out
                    if (!scheduleValidation.IsValid)
                    {
                        LogMessage("WARNING", $"Sign-out denied - no active session or prior sign-in found: {scheduleValidation.Reason}");
                        return new AttendanceAttemptResult { Success = false, Reason = "Sign-out denied: No active session or prior sign-in found", SubjectName = scheduleValidation.SubjectName };
                    }
                }

                // Try to find an active session for today in the current device's room
                // For sign-out, also include 'ended' sessions since sign-out can happen after session ends
                string sessionId = null;
                string scheduleId = null;
                string sessionQuery = isSignOut 
                    ? @"SELECT SESSIONID, SCHEDULEID
                        FROM SESSIONS
                        WHERE SESSIONDATE = CURRENT_DATE
                          AND STATUS IN ('active','waiting','ended')
                          AND ROOMID = @currentRoomId
                        ORDER BY COALESCE(STARTTIME, TIMESTAMP(CURRENT_DATE,'00:00:00')) DESC                                                                       
                        LIMIT 1"
                    : @"SELECT SESSIONID, SCHEDULEID
                        FROM SESSIONS
                        WHERE SESSIONDATE = CURRENT_DATE
                          AND STATUS IN ('active','waiting')
                          AND ROOMID = @currentRoomId
                        ORDER BY COALESCE(STARTTIME, TIMESTAMP(CURRENT_DATE,'00:00:00')) DESC                                                                       
                        LIMIT 1";
                    
                using (var cmdFindSession = new MySqlCommand(sessionQuery, connection))
                {
                    cmdFindSession.Parameters.AddWithValue("@currentRoomId", CurrentRoomId ?? "");                                                              
                    using (var r = cmdFindSession.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            sessionId = r.GetString(0);
                            scheduleId = r.IsDBNull(1) ? null : r.GetString(1);
                            LogMessage("INFO", $"Found session {sessionId} for room {CurrentRoomId}");                                                          
                        }
                        else
                        {
                            LogMessage("INFO", $"No active session found for room {CurrentRoomId} today");                                                      
                        }
                    }
                }

                // Use the schedule from validation if no active session found  
                if (sessionId == null && !string.IsNullOrWhiteSpace(scheduleValidation.ScheduleId))                                                             
                {
                    scheduleId = scheduleValidation.ScheduleId;
                    LogMessage("INFO", $"Using validated schedule {scheduleId} for attendance recording");                                                      
                }

                // Get user type first to determine if we should use administrative schedule
                string userType = null;
                using (var cmdGetUserType = new MySqlCommand("SELECT USERTYPE FROM USERS WHERE USERID = @userGuid AND ARCHIVED_AT IS NULL", connection))
                {
                    cmdGetUserType.Parameters.AddWithValue("@userGuid", userGuid);
                    var userTypeObj = cmdGetUserType.ExecuteScalar();
                    if (userTypeObj != null)
                    {
                        userType = userTypeObj.ToString();
                    }
                }

                bool isCustodian = userType != null && userType.Equals("custodian", StringComparison.OrdinalIgnoreCase);
                bool isDean = userType != null && userType.Equals("dean", StringComparison.OrdinalIgnoreCase);

                // For custodians or deans without a specific schedule, use administrative schedule
                if (string.IsNullOrWhiteSpace(scheduleId) && (isCustodian || (isDean && string.IsNullOrWhiteSpace(scheduleValidation.ScheduleId))))
                {
                    scheduleId = GetOrCreateAdministrativeSchedule(CurrentRoomId ?? "");
                    if (string.IsNullOrWhiteSpace(scheduleId))
                    {
                        LogMessage("ERROR", $"RecordAttendance: Failed to get administrative schedule for {userType}.");
                        return new AttendanceAttemptResult { Success = false, Reason = "Failed to get administrative schedule" };
                    }
                    sessionId = null; // Don't associate with a session
                    LogMessage("INFO", $"Using administrative schedule {scheduleId} for {userType} access");
                }
                                // Other roles require a schedule (except sign-out, which can use NULL if no schedule found)
                else if (string.IsNullOrWhiteSpace(scheduleId) && !isSignOut)
                {
                    LogMessage("ERROR", "RecordAttendance: No valid schedule available to attach attendance.");
                    return new AttendanceAttemptResult { Success = false, Reason = "No valid schedule available" };
                }
                
                // For sign-out without schedule, allow NULL schedule (will be matched by sign-in schedule in reports)
                if (string.IsNullOrWhiteSpace(scheduleId) && isSignOut)
                {
                    LogMessage("INFO", $"Sign-out record will be created with NULL scheduleId - will be matched by sign-in schedule in reports");
                }

                // Map action to scan type and status (robust parsing)
                string scanType;
                if (!string.IsNullOrEmpty(action))
                {
                    var a = action.ToLowerInvariant();
                    if (a.Contains("check-in") || a.Contains("check in") || a.Contains("sign-in") || a.Contains("sign in"))
                    {
                        scanType = "time_in";
                    }
                    else if (a.Contains("check-out") || a.Contains("check out") || a.Contains("sign-out") || a.Contains("sign out"))
                    {
                        scanType = "time_out";
                    }
                    else
                    {
                        scanType = "time_in"; // sensible default
                    }
                }
                else
                {
                    scanType = "time_in";
                }
                string status = "Present";
                bool isDoorAccessAction = !string.IsNullOrEmpty(action) && action.IndexOf("Door Access", StringComparison.OrdinalIgnoreCase) >= 0;
                
                // Determine auth method based on action string
                string authMethod = "Fingerprint"; // Default
                if (action != null && action.IndexOf("RFID", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    authMethod = "RFID";
                }
                
                // Determine effective location: prefer explicit parameter, fallback to current setting
                string effectiveLocation = (location == "inside" || location == "outside") ? location : CurrentLocation;

                // Check for prior early arrival before recording new attendance
                string priorAttendanceId = null;
                DateTime? earlyArrivalTime = null;
                bool isEarlyArrival = action != null && action.IndexOf("Early Arrival", StringComparison.OrdinalIgnoreCase) >= 0;
                
                if (!isEarlyArrival && !string.IsNullOrEmpty(scheduleId))
                {
                    // Check if there's a prior early arrival for this user/schedule today
                    string priorEarlyArrivalQuery = @"
                        SELECT ATTENDANCEID, TIMEIN, SCANDATETIME
                        FROM ATTENDANCERECORDS
                        WHERE USERID = @userGuid
                          AND DATE = CURRENT_DATE
                          AND ACTIONTYPE = 'Early Arrival'
                          AND SCHEDULEID = @scheduleId
                        ORDER BY SCANDATETIME DESC
                        LIMIT 1";

                    using (var cmdCheckEarly = new MySqlCommand(priorEarlyArrivalQuery, connection))
                    {
                        cmdCheckEarly.Parameters.AddWithValue("@userGuid", userGuid);
                        cmdCheckEarly.Parameters.AddWithValue("@scheduleId", scheduleId);
                        
                        using (var reader = cmdCheckEarly.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                priorAttendanceId = reader.GetString("ATTENDANCEID");
                                if (!reader.IsDBNull(reader.GetOrdinal("TIMEIN")))
                                {
                                    var timeInStr = reader.GetString("TIMEIN");
                                    if (TimeSpan.TryParse(timeInStr, out var timeIn))
                                    {
                                        earlyArrivalTime = DateTime.Today.Add(timeIn);
                                    }
                                }
                            }
                        }
                    }
                }

                                // Determine action type and status based on timing
                string actionType = "Sign In"; // Default

                // Check if this schedule is an administrative schedule (for custodians/deans)
                bool isAdministrativeSchedule = false;
                if (!string.IsNullOrEmpty(scheduleId))
                {
                    using (var cmdCheckAdmin = new MySqlCommand(
                        "SELECT COUNT(*) FROM CLASSSCHEDULES cs JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID WHERE cs.SCHEDULEID = @scheduleId AND s.SUBJECTCODE = 'ADMIN-ACCESS'", connection))
                    {
                        cmdCheckAdmin.Parameters.AddWithValue("@scheduleId", scheduleId);
                        var adminCount = Convert.ToInt32(cmdCheckAdmin.ExecuteScalar());
                        isAdministrativeSchedule = adminCount > 0;
                    }
                }

                // Get class start time for late marking calculation (skip for custodians/deans with admin schedule)
                TimeSpan classStartTime = TimeSpan.Zero;
                var currentTime = DateTime.Now.TimeOfDay;
                var minutesAfterStart = 0.0;

                if (!isAdministrativeSchedule && !string.IsNullOrEmpty(scheduleId))
                {
                    using (var cmdGetStart = new MySqlCommand(
                        "SELECT STARTTIME FROM CLASSSCHEDULES WHERE SCHEDULEID = @scheduleId", connection))                                                     
                    {
                        cmdGetStart.Parameters.AddWithValue("@scheduleId", scheduleId);                                                                         
                        var startObj = cmdGetStart.ExecuteScalar();
                        if (startObj != null && TimeSpan.TryParse(startObj.ToString(), out var st))                                                             
                        {
                            classStartTime = st;
                            minutesAfterStart = (currentTime - classStartTime).TotalMinutes;
                        }
                    }
                }

                // Determine status and action type
                if (isCustodian || (isDean && isAdministrativeSchedule) || isDoorAccessAction)
                {
                    // Custodians and deans with admin schedules: Always "Present", never "Late", action is "Door Access"
                    status = "Present";
                    actionType = "Door Access";
                }
                else
                {
                    // Regular users: Calculate status based on timing
                    // Check if this is an early arrival
                    if (isEarlyArrival || minutesAfterStart < 0)
                    {
                        actionType = "Early Arrival";
                        status = "Present";
                    }
                    // Check if scan is within grace period (15 minutes after start)                                                                        
                    else if (minutesAfterStart > 15)
                    {
                        // Deans with scheduled classes: treat as Present even if beyond grace period
                        status = userType != null && userType.Equals("dean", StringComparison.OrdinalIgnoreCase) ? "Present" : "Late";
                        actionType = "Sign In";
                    }
                    else
                    {
                        status = "Present";
                        actionType = "Sign In";
                    }

                    // Override action type based on user type and location 
                    if (!isEarlyArrival && minutesAfterStart >= 0 && userType != null)
                    {
                        if (userType.Equals("instructor", StringComparison.OrdinalIgnoreCase))                                                              
                        {
                            if (effectiveLocation == "outside")
                                actionType = scanType == "time_in" ? "Session Start" : "Session End";                                                       
                            else
                                actionType = scanType == "time_in" ? "Sign In" : "Sign Out";                                                                
                        }
                        else if (userType.Equals("student", StringComparison.OrdinalIgnoreCase))                                                            
                        {
                            if (effectiveLocation == "outside")
                                actionType = "Door Access";
                            else
                                actionType = scanType == "time_in" ? "Sign In" : "Sign Out";                                                                
                        }
                        else if (userType.Equals("dean", StringComparison.OrdinalIgnoreCase) && !isAdministrativeSchedule)
                        {
                            // Dean with their own class schedule - normal attendance
                            actionType = scanType == "time_in" ? "Sign In" : "Sign Out";
                        }
                    }
                }

                // Handle early arrival confirmation - update existing record instead of inserting new
                // BUT: Skip this for sign-out actions - sign-out should always create a new record
                if (priorAttendanceId != null && earlyArrivalTime.HasValue && !isSignOut)
                {
                    // Update the existing early arrival record to reflect confirmation
                    var updateCmd = new MySqlCommand(@"
                        UPDATE ATTENDANCERECORDS
                        SET ACTIONTYPE = @ACTIONTYPE,
                            AUTHMETHOD = @AUTHMETHOD,
                            LOCATION = @LOCATION,
                            STATUS = @STATUS,
                            SESSIONID = @SESSIONID
                        WHERE ATTENDANCEID = @ATTENDANCEID
                    ", connection);
                    
                    updateCmd.Parameters.AddWithValue("@ATTENDANCEID", priorAttendanceId);
                    updateCmd.Parameters.AddWithValue("@ACTIONTYPE", "Sign In"); // Change from "Early Arrival" to "Sign In"
                    updateCmd.Parameters.AddWithValue("@AUTHMETHOD", "RFID + Fingerprint");
                    updateCmd.Parameters.AddWithValue("@LOCATION", effectiveLocation);
                    updateCmd.Parameters.AddWithValue("@STATUS", "Present");
                    updateCmd.Parameters.AddWithValue("@SESSIONID", (object)sessionId ?? DBNull.Value);
                    updateCmd.ExecuteNonQuery();
                    
                    LogMessage("INFO", $"Early arrival confirmed for user GUID: {userGuid}, preserved early time: {earlyArrivalTime.Value:HH:mm:ss}");
                    Console.WriteLine($"✅ Early arrival confirmed - preserved early time: {earlyArrivalTime.Value:HH:mm:ss}");
                    Console.WriteLine($"   UserGUID: {userGuid}");
                    Console.WriteLine($"   ActionType: Sign In (was Early Arrival)");
                    Console.WriteLine($"   AuthMethod: RFID + Fingerprint");
                    Console.WriteLine($"   ScheduleID: {scheduleId}");
                    Console.WriteLine($"   SessionID: {sessionId ?? "NULL"}");
                    
                    return new AttendanceAttemptResult { Success = true, ScheduleId = scheduleId, SubjectName = scheduleValidation.SubjectName };
                }
                
                // For sign-out, ensure we use the correct action type and scan type
                if (isSignOut)
                {
                    actionType = "Sign Out";
                    scanType = "time_out";
                    status = "Present"; // Sign-out is always considered present
                    LogMessage("INFO", $"Sign-out action: Setting ActionType=Sign Out, SCANTYPE=time_out");
                    Console.WriteLine($"🔍 SIGN-OUT: Setting ActionType=Sign Out, SCANTYPE=time_out");
                }

                // For new records, capture the scan time explicitly
                DateTime scanTime = DateTime.Now;

                // Compose insert
                var insertCmd = new MySqlCommand(@"
                    INSERT INTO ATTENDANCERECORDS
                    (ATTENDANCEID, USERID, SCHEDULEID, SESSIONID, SCANTYPE, SCANDATETIME, DATE, TIMEIN, AUTHMETHOD, LOCATION, STATUS, ACTIONTYPE, ACADEMICYEAR, SEMESTER)
                    VALUES (UUID(), @USERID, @SCHEDULEID, @SESSIONID, @SCANTYPE, @SCANDATETIME, CURRENT_DATE,
                            @TIMEIN,
                            @AUTHMETHOD, @LOCATION, @STATUS, @ACTIONTYPE,
                            (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY='current_academic_year' LIMIT 1),
                            (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY='current_semester' LIMIT 1))
                ", connection);

                insertCmd.Parameters.AddWithValue("@USERID", userGuid);
                insertCmd.Parameters.AddWithValue("@SCHEDULEID", (object)scheduleId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@SESSIONID", (object)sessionId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@SCANTYPE", scanType);
                insertCmd.Parameters.AddWithValue("@SCANDATETIME", scanTime);
                insertCmd.Parameters.AddWithValue("@TIMEIN", scanTime.TimeOfDay);
                insertCmd.Parameters.AddWithValue("@AUTHMETHOD", authMethod);
                insertCmd.Parameters.AddWithValue("@LOCATION", effectiveLocation);
                insertCmd.Parameters.AddWithValue("@STATUS", status);
                insertCmd.Parameters.AddWithValue("@ACTIONTYPE", actionType);

                insertCmd.ExecuteNonQuery();
                
                LogMessage("INFO", $"Attendance recorded successfully for user GUID: {userGuid}, action: {action}, authMethod: {authMethod}, actionType: {actionType}");
                Console.WriteLine($"✅ Attendance saved to ATTENDANCERECORDS:");
                Console.WriteLine($"   UserGUID: {userGuid}");
                Console.WriteLine($"   Action: {action}");
                Console.WriteLine($"   ActionType: {actionType}");
                Console.WriteLine($"   AuthMethod: {authMethod}");
                Console.WriteLine($"   ScheduleID: {scheduleId}");
                Console.WriteLine($"   SessionID: {sessionId ?? "NULL"}");
                
                return new AttendanceAttemptResult { Success = true, ScheduleId = scheduleId, SubjectName = scheduleValidation.SubjectName };
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to record attendance (web schema): {ex.Message}", ex);
            }
        }

        private void RecordAttendanceInternal(string userGuid, string action, string notes = null)
        {
            // Legacy path: ignore notes and use current location setting
            var res = InternalTryRecordAttendance(userGuid, action, null);
            if (!res.Success)
            {
                throw new Exception(res.Reason ?? "Unknown attendance failure");
            }
        }

        // Schedule validation result class
        public class ScheduleValidationResult
        {
            public bool IsValid { get; set; }
            public string Reason { get; set; }
            public string ScheduleId { get; set; }
            public string SubjectName { get; set; }
        }

        // Validate if there's a scheduled class for the current time and user
        public ScheduleValidationResult ValidateScheduleForCurrentTime(string userGuid)
        {
            try
            {
                var result = new ScheduleValidationResult { IsValid = false };

                // Get current time and day - use proper day format matching database ENUM
                var now = DateTime.Now;
                var currentDay = now.DayOfWeek.ToString(); // This should match database ENUM values
                var currentTime = now.ToString("HH:mm:ss");
                
                LogMessage("DEBUG", $"Schedule validation for user {userGuid}: Day={currentDay}, Time={currentTime}, Room={CurrentRoomId}");
                Console.WriteLine($"DEBUG: Schedule validation for user {userGuid}: Day={currentDay}, Time={currentTime}, Room={CurrentRoomId}");

                // Check if CurrentRoomId is null
                if (string.IsNullOrEmpty(CurrentRoomId))
                {
                    LogMessage("ERROR", "CurrentRoomId is NULL - device may not be properly initialized in a room");
                    result.Reason = "Device not properly initialized in a room. Please check device room assignment.";
                    return result;
                }

                // Get user type
                string userType = null;
                
                Console.WriteLine($"====== SCHEDULE VALIDATION DEBUG ======");
                Console.WriteLine($"UserGUID: {userGuid}");
                Console.WriteLine($"Room: {CurrentRoomId}");
                Console.WriteLine($"Day: {currentDay}");
                Console.WriteLine($"Time: {currentTime}");
                
                using (var cmdGetUserType = new MySqlCommand(@"
                    SELECT USERTYPE, FIRSTNAME, LASTNAME, EMAIL FROM USERS WHERE USERID = @userGuid", connection))
                {
                    cmdGetUserType.Parameters.AddWithValue("@userGuid", userGuid);
                    
                    using (var reader = cmdGetUserType.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            userType = reader.GetString("USERTYPE");
                            string firstName = reader.IsDBNull(reader.GetOrdinal("FIRSTNAME")) ? "" : reader.GetString("FIRSTNAME");
                            string lastName = reader.IsDBNull(reader.GetOrdinal("LASTNAME")) ? "" : reader.GetString("LASTNAME");
                            string email = reader.IsDBNull(reader.GetOrdinal("EMAIL")) ? "" : reader.GetString("EMAIL");
                            
                            Console.WriteLine($"User Found: {firstName} {lastName} ({email}) - Type: {userType}");
                        }
                        else
                        {
                            Console.WriteLine($"❌ No user found with USERID = {userGuid}");
                        }
                    }
                }

                if (string.IsNullOrEmpty(userType))
                {
                    result.Reason = "User not found";
                    LogMessage("ERROR", $"User {userGuid} not found in database");
                    return result;
                }

                // Custodian: Always allow access, no schedule validation needed
                if (userType != null && userType.Equals("custodian", StringComparison.OrdinalIgnoreCase))
                {
                    result.IsValid = true;
                    result.Reason = "Custodian access - no schedule required";
                    LogMessage("INFO", $"Custodian access granted for user {userGuid}");
                    return result;
                }

                // Dean: Check if they have scheduled classes, but allow access regardless
                if (userType != null && userType.Equals("dean", StringComparison.OrdinalIgnoreCase))
                {
                    LogMessage("DEBUG", $"Dean access requested for user {userGuid}");
                    
                    // Fetch current academic settings fresh from database (not cached)
                    // This ensures schedule changes are immediately recognized without app restart
                    string academicYear = CurrentAcademicYear; // Fallback to cached value
                    string semester = CurrentSemester; // Fallback to cached value
                    try
                    {
                        using (var cmdGetSettings = new MySqlCommand(@"
                            SELECT 
                                MAX(CASE WHEN SETTINGKEY = 'current_academic_year' THEN SETTINGVALUE END) as academic_year,
                                MAX(CASE WHEN SETTINGKEY = 'current_semester' THEN SETTINGVALUE END) as semester
                            FROM SETTINGS
                            WHERE SETTINGKEY IN ('current_academic_year', 'current_semester')", connection))
                        {
                            using (var reader = cmdGetSettings.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    if (!reader.IsDBNull(0) && !string.IsNullOrWhiteSpace(reader.GetString(0)))
                                        academicYear = reader.GetString(0);
                                    if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
                                        semester = reader.GetString(1);
                                }
                            }
                        }
                        LogMessage("DEBUG", $"Fetched fresh academic settings for dean - Year: {academicYear}, Semester: {semester}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage("WARNING", $"Failed to fetch fresh academic settings, using cached values: {ex.Message}");
                    }
                    
                    // Try to find a scheduled class (same query as instructor)
                    using (var cmdCheckDeanSchedule = new MySqlCommand(@"
                        SELECT cs.SCHEDULEID, s.SUBJECTNAME, cs.STARTTIME, cs.ENDTIME
                        FROM CLASSSCHEDULES cs
                        JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
                        WHERE s.INSTRUCTORID = @userGuid
                          AND cs.ROOMID = @roomId
                          AND cs.DAYOFWEEK = @currentDay
                          AND TIME(NOW()) BETWEEN cs.STARTTIME AND cs.ENDTIME
                          AND cs.ACADEMICYEAR = @academicYear
                          AND cs.SEMESTER = @semester", connection))
                    {
                        cmdCheckDeanSchedule.Parameters.AddWithValue("@userGuid", userGuid);
                        cmdCheckDeanSchedule.Parameters.AddWithValue("@roomId", CurrentRoomId);
                        cmdCheckDeanSchedule.Parameters.AddWithValue("@currentDay", currentDay);
                        cmdCheckDeanSchedule.Parameters.AddWithValue("@academicYear", academicYear);
                        cmdCheckDeanSchedule.Parameters.AddWithValue("@semester", semester);

                        using (var reader = cmdCheckDeanSchedule.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                result.IsValid = true;
                                result.ScheduleId = reader.GetString("SCHEDULEID");
                                result.SubjectName = reader.GetString("SUBJECTNAME");
                                result.Reason = "Dean has scheduled class";
                                LogMessage("INFO", $"Dean has scheduled class: {result.SubjectName} (ID: {result.ScheduleId})");
                            }
                            else
                            {
                                result.IsValid = true;
                                result.Reason = "Dean access granted - no scheduled class";
                                LogMessage("INFO", $"Dean access granted without scheduled class");
                            }
                        }
                    }
                    
                    return result;
                }

                // For instructors: Check if they have a scheduled class at this time
                if (userType != null && userType.Equals("instructor", StringComparison.OrdinalIgnoreCase))
                {
                    LogMessage("DEBUG", $"Searching for instructor schedule with: userGuid={userGuid}, roomId={CurrentRoomId}, day={currentDay}, time={currentTime}");
                    Console.WriteLine($"DEBUG: Searching for instructor schedule with: userGuid={userGuid}, roomId={CurrentRoomId}, day={currentDay}, time={currentTime}");
                    
                    // Load instructor early window from configuration  
                    int instructorEarlyMinutes = LoadInstructorEarlyWindow();
                    string earlyWindowTime = $"-00:{instructorEarlyMinutes:D2}:00";
                    Console.WriteLine($"DEBUG: Instructor early arrival window: {instructorEarlyMinutes} minutes");
                    
                    // Fetch current academic settings fresh from database (not cached)
                    // This ensures schedule changes are immediately recognized without app restart
                    string academicYear = CurrentAcademicYear; // Fallback to cached value
                    string semester = CurrentSemester; // Fallback to cached value
                    try
                    {
                        using (var cmdGetSettings = new MySqlCommand(@"
                            SELECT 
                                MAX(CASE WHEN SETTINGKEY = 'current_academic_year' THEN SETTINGVALUE END) as academic_year,
                                MAX(CASE WHEN SETTINGKEY = 'current_semester' THEN SETTINGVALUE END) as semester
                            FROM SETTINGS
                            WHERE SETTINGKEY IN ('current_academic_year', 'current_semester')", connection))
                        {
                            using (var reader = cmdGetSettings.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    if (!reader.IsDBNull(0) && !string.IsNullOrWhiteSpace(reader.GetString(0)))
                                        academicYear = reader.GetString(0);
                                    if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
                                        semester = reader.GetString(1);
                                }
                            }
                        }
                        LogMessage("DEBUG", $"Fetched fresh academic settings - Year: {academicYear}, Semester: {semester}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage("WARNING", $"Failed to fetch fresh academic settings, using cached values: {ex.Message}");
                    }
                    
                    // Use ADDTIME to allow early arrival within configured window
                    using (var cmdCheckInstructorSchedule = new MySqlCommand(@"
                        SELECT cs.SCHEDULEID, s.SUBJECTNAME, cs.STARTTIME, cs.ENDTIME
                        FROM CLASSSCHEDULES cs
                        JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
                        WHERE s.INSTRUCTORID = @userGuid
                          AND cs.ROOMID = @roomId
                          AND cs.DAYOFWEEK = @currentDay
                          AND TIME(NOW()) BETWEEN ADDTIME(cs.STARTTIME, @earlyWindow) AND cs.ENDTIME
                          AND cs.ACADEMICYEAR = @academicYear
                          AND cs.SEMESTER = @semester", connection))
                    {
                        cmdCheckInstructorSchedule.Parameters.AddWithValue("@userGuid", userGuid);
                        cmdCheckInstructorSchedule.Parameters.AddWithValue("@roomId", CurrentRoomId);
                        cmdCheckInstructorSchedule.Parameters.AddWithValue("@currentDay", currentDay);
                        cmdCheckInstructorSchedule.Parameters.AddWithValue("@earlyWindow", earlyWindowTime);
                        cmdCheckInstructorSchedule.Parameters.AddWithValue("@academicYear", academicYear);
                        cmdCheckInstructorSchedule.Parameters.AddWithValue("@semester", semester);

                        using (var reader = cmdCheckInstructorSchedule.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                result.IsValid = true;
                                result.ScheduleId = reader.GetString("SCHEDULEID");
                                result.SubjectName = reader.GetString("SUBJECTNAME");
                                result.Reason = "Instructor has scheduled class";
                                
                                LogMessage("INFO", $"Found instructor schedule: {result.SubjectName} (ID: {result.ScheduleId})");
                                LogMessage("DEBUG", $"Schedule details - Start: {reader.GetString("STARTTIME")}, End: {reader.GetString("ENDTIME")}");
                                Console.WriteLine($"✅ Found instructor schedule: {result.SubjectName} (ScheduleID: {result.ScheduleId})");
                            }
                            else
                            {
                                result.Reason = "No scheduled class for instructor at this time";
                                LogMessage("WARNING", $"No instructor schedule found for user {userGuid} at {currentDay} {currentTime} in room {CurrentRoomId}");
                            }
                        }
                    }
                    
                    // Check for active session AFTER closing the main reader
                    if (result.IsValid)
                    {
                        var scheduleId = result.ScheduleId;
                        using (var cmdCheckSession = new MySqlCommand(@"
                            SELECT SESSIONID FROM SESSIONS 
                            WHERE SCHEDULEID = @scheduleId 
                              AND SESSIONDATE = CURRENT_DATE 
                              AND STATUS = 'active'", connection))
                        {
                            cmdCheckSession.Parameters.AddWithValue("@scheduleId", scheduleId);
                            var existingSession = cmdCheckSession.ExecuteScalar();
                            
                            LogMessage("DEBUG", $"Checking for active session with scheduleId: {scheduleId}");
                            LogMessage("DEBUG", $"Session check result: {existingSession?.ToString() ?? "NULL"}");
                            
                            if (existingSession == null)
                            {
                                // No active session - start one for the instructor
                                LogMessage("INFO", $"Starting new session for instructor {userGuid} with schedule {scheduleId}");
                                StartClassSession(userGuid, scheduleId);
                                result.Reason = "Instructor has scheduled class - session started";
                            }
                            else
                            {
                                LogMessage("INFO", $"Active session already exists for schedule {scheduleId}");
                                result.Reason = "Instructor has scheduled class - session already active";
                            }
                        }
                    }
                }
                // For students: Check if they are enrolled in a subject that has a scheduled class at this time
                // AND check if there's an active session (instructor must start session first)
                else if (userType != null && userType.Equals("student", StringComparison.OrdinalIgnoreCase))
                {
                    LogMessage("DEBUG", $"Student validation for user {userGuid}: Day={currentDay}, Time={currentTime}, Room={CurrentRoomId}");
                    Console.WriteLine($"====== STUDENT VALIDATION ======");
                    Console.WriteLine($"UserGUID: {userGuid}");
                    Console.WriteLine($"Room: {CurrentRoomId}");
                    Console.WriteLine($"Day: {currentDay}");
                    Console.WriteLine($"Current SQL Time: TIME(NOW())");
                    
                    // First check if student is enrolled in a scheduled class
                    string scheduleId = null;
                    string subjectName = null;
                    
                    // First, check if student has ANY enrollments
                    using (var cmdCheckEnrollment = new MySqlCommand(@"
                        SELECT se.ENROLLMENTID, s.SUBJECTNAME, s.SUBJECTID
                        FROM SUBJECTENROLLMENT se
                        JOIN SUBJECTS s ON se.SUBJECTID = s.SUBJECTID
                        WHERE se.USERID = @userGuid AND se.STATUS = 'enrolled'", connection))
                    {
                        cmdCheckEnrollment.Parameters.AddWithValue("@userGuid", userGuid);
                        
                        int enrollmentCount = 0;
                        using (var enrollReader = cmdCheckEnrollment.ExecuteReader())
                        {
                            while (enrollReader.Read())
                            {
                                enrollmentCount++;
                                string enrolledSubjectName = enrollReader.GetString("SUBJECTNAME");
                                string enrolledSubjectId = enrollReader.GetString("SUBJECTID");
                                Console.WriteLine($"  - Enrolled in: {enrolledSubjectName} (ID: {enrolledSubjectId})");
                            }
                        }
                        
                        Console.WriteLine($"Student has {enrollmentCount} active enrollment(s)");
                        
                        if (enrollmentCount == 0)
                        {
                            Console.WriteLine($"❌ Student {userGuid} has NO enrollments in database!");
                            result.Reason = "Student is not enrolled in any subjects";
                            return result;
                        }
                    }
                    
                    // Load student early window from configuration
                    int studentEarlyMinutes = LoadStudentEarlyArrivalWindow();
                    string earlyWindowTime = $"-00:{studentEarlyMinutes:D2}:00";
                    Console.WriteLine($"DEBUG: Student early arrival window: {studentEarlyMinutes} minutes");
                    
                    // Fetch current academic settings fresh from database (not cached)
                    // This ensures schedule changes are immediately recognized without app restart
                    string academicYear = CurrentAcademicYear; // Fallback to cached value
                    string semester = CurrentSemester; // Fallback to cached value
                    try
                    {
                        using (var cmdGetSettings = new MySqlCommand(@"
                            SELECT 
                                MAX(CASE WHEN SETTINGKEY = 'current_academic_year' THEN SETTINGVALUE END) as academic_year,
                                MAX(CASE WHEN SETTINGKEY = 'current_semester' THEN SETTINGVALUE END) as semester
                            FROM SETTINGS
                            WHERE SETTINGKEY IN ('current_academic_year', 'current_semester')", connection))
                        {
                            using (var reader = cmdGetSettings.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    if (!reader.IsDBNull(0) && !string.IsNullOrWhiteSpace(reader.GetString(0)))
                                        academicYear = reader.GetString(0);
                                    if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
                                        semester = reader.GetString(1);
                                }
                            }
                        }
                        LogMessage("DEBUG", $"Fetched fresh academic settings for student - Year: {academicYear}, Semester: {semester}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage("WARNING", $"Failed to fetch fresh academic settings, using cached values: {ex.Message}");
                    }
                    
                    // Use ADDTIME to allow early arrival within configured window
                    bool scheduleFound = false;
                    using (var cmdCheckStudentSchedule = new MySqlCommand(@"
                        SELECT cs.SCHEDULEID, s.SUBJECTNAME, cs.STARTTIME, cs.ENDTIME
                        FROM CLASSSCHEDULES cs
                        JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
                        JOIN SUBJECTENROLLMENT se ON s.SUBJECTID = se.SUBJECTID
                        WHERE se.USERID = @userGuid
                          AND se.STATUS = 'enrolled'
                          AND cs.ROOMID = @roomId
                          AND cs.DAYOFWEEK = @currentDay
                          AND TIME(NOW()) BETWEEN ADDTIME(cs.STARTTIME, @earlyWindow) AND cs.STARTTIME
                          AND cs.ACADEMICYEAR = @academicYear
                          AND cs.SEMESTER = @semester", connection))
                    {
                        cmdCheckStudentSchedule.Parameters.AddWithValue("@userGuid", userGuid);
                        cmdCheckStudentSchedule.Parameters.AddWithValue("@roomId", CurrentRoomId);
                        cmdCheckStudentSchedule.Parameters.AddWithValue("@currentDay", currentDay);
                        cmdCheckStudentSchedule.Parameters.AddWithValue("@earlyWindow", earlyWindowTime);
                        cmdCheckStudentSchedule.Parameters.AddWithValue("@academicYear", academicYear);
                        cmdCheckStudentSchedule.Parameters.AddWithValue("@semester", semester);

                        using (var reader = cmdCheckStudentSchedule.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                scheduleId = reader.GetString("SCHEDULEID");
                                subjectName = reader.GetString("SUBJECTNAME");
                                scheduleFound = true;
                                
                                LogMessage("INFO", $"Found student schedule: {subjectName} (ID: {scheduleId})");
                                LogMessage("DEBUG", $"Schedule details - Start: {reader.GetString("STARTTIME")}, End: {reader.GetString("ENDTIME")}");
                                Console.WriteLine($"✅ Found student schedule: {subjectName} (ScheduleID: {scheduleId})");
                                Console.WriteLine($"   Start: {reader.GetString("STARTTIME")}, End: {reader.GetString("ENDTIME")}");
                            }
                        } // Reader fully disposed
                    } // Command fully disposed - connection is now completely free

                    // Fallback check: if no schedule found, try active session validation
                    // This is OUTSIDE the previous using block to ensure connection is free
                    if (!scheduleFound)
                    {
                        LogMessage("WARNING", $"No schedule found for student {userGuid} at {currentDay} {currentTime} in room {CurrentRoomId}");
                        Console.WriteLine($"❌ No schedule found for student {userGuid} at {currentDay} {currentTime} in room {CurrentRoomId}");

                        // Fallback: if there's an active session in this room, validate by subject enrollment
                        string activeScheduleId = null;
                        string activeSubjectId = null;
                        string activeSubjectName = null;
                        
                        using (var cmdFindActiveSession = new MySqlCommand(@"
                            SELECT s.SESSIONID, s.STATUS, s.STARTTIME, cs.SCHEDULEID, subj.SUBJECTID, subj.SUBJECTNAME
                            FROM SESSIONS s
                            JOIN CLASSSCHEDULES cs ON s.SCHEDULEID = cs.SCHEDULEID
                            JOIN SUBJECTS subj ON cs.SUBJECTID = subj.SUBJECTID
                            WHERE s.ROOMID = @roomId
                              AND s.SESSIONDATE = CURRENT_DATE
                              AND s.STATUS = 'active'
                            LIMIT 1", connection))
                        {
                            cmdFindActiveSession.Parameters.AddWithValue("@roomId", CurrentRoomId);

                            using (var sessionReader = cmdFindActiveSession.ExecuteReader())
                            {
                                if (sessionReader.Read())
                                {
                                    // Extract all values while reader is open
                                    activeScheduleId = sessionReader.GetString("SCHEDULEID");
                                    activeSubjectId = sessionReader.GetString("SUBJECTID");
                                    activeSubjectName = sessionReader.GetString("SUBJECTNAME");
                                }
                            } // Reader is now fully disposed
                        } // Command is now fully disposed

                        // Now check enrollment (connection is free, no DataReader conflicts)
                        if (!string.IsNullOrEmpty(activeSubjectId))
                        {
                            using (var cmdCheckEnrollForActive = new MySqlCommand(@"
                                SELECT COUNT(*)
                                FROM SUBJECTENROLLMENT
                                WHERE USERID = @userGuid
                                  AND SUBJECTID = @subjectId
                                  AND STATUS = 'enrolled'
                                  AND ACADEMICYEAR = @academicYear
                                  AND SEMESTER = @semester", connection))
                            {
                                cmdCheckEnrollForActive.Parameters.AddWithValue("@userGuid", userGuid);
                                cmdCheckEnrollForActive.Parameters.AddWithValue("@subjectId", activeSubjectId);
                                cmdCheckEnrollForActive.Parameters.AddWithValue("@academicYear", CurrentAcademicYear);
                                cmdCheckEnrollForActive.Parameters.AddWithValue("@semester", CurrentSemester);

                                var enrolledCount = Convert.ToInt32(cmdCheckEnrollForActive.ExecuteScalar());
                                if (enrolledCount > 0)
                                {
                                    // Allow based on active session + valid enrollment
                                    result.IsValid = true;
                                    result.ScheduleId = activeScheduleId;
                                    result.SubjectName = activeSubjectName;
                                    result.Reason = "Student is enrolled in the active session subject";
                                    Console.WriteLine($"✅ Student validated via active session subject: {activeSubjectName} (ScheduleID: {activeScheduleId})");
                                    return result;
                                }
                            }
                        }

                        // Still no match after active-session fallback
                        result.Reason = "Student is not enrolled in any class scheduled at this time";
                        return result;
                    }

                    // If student is enrolled, check if there's an active session for this schedule
                    if (!string.IsNullOrEmpty(scheduleId))
                    {
                        LogMessage("DEBUG", $"Checking for active session with scheduleId: {scheduleId}");
                        Console.WriteLine($"DEBUG: Checking for active session with scheduleId: {scheduleId}");
                        
                        // FIRST: Try exact schedule match
                        using (var cmdCheckActiveSession = new MySqlCommand(@"
                            SELECT SESSIONID, STATUS, STARTTIME
                            FROM SESSIONS 
                            WHERE SCHEDULEID = @scheduleId 
                              AND SESSIONDATE = CURRENT_DATE 
                              AND STATUS = 'active'", connection))
                        {
                            cmdCheckActiveSession.Parameters.AddWithValue("@scheduleId", scheduleId);
                            
                            using (var reader = cmdCheckActiveSession.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    // Active session exists - student can record attendance
                                    result.IsValid = true;
                                    result.ScheduleId = scheduleId;
                                    result.SubjectName = subjectName;
                                    result.Reason = "Student is enrolled and active session exists";
                                    
                                    var sessionId = reader.GetString("SESSIONID");
                                    LogMessage("INFO", $"Active session found: {sessionId} for student {userGuid}");
                                    Console.WriteLine($"✅ Active session found: {sessionId} for student {userGuid}");
                                    return result;
                                }
                            }
                        }
                        
                        // FALLBACK: If exact match fails, try matching by room + subject + date
                        // This handles cases where instructor and student schedules have different IDs but same subject
                        Console.WriteLine($"⚠️ No exact schedule match, trying subject-based lookup...");
                        using (var cmdCheckSessionBySubject = new MySqlCommand(@"
                            SELECT s.SESSIONID, s.STATUS, s.STARTTIME, cs.SCHEDULEID, subj.SUBJECTNAME
                            FROM SESSIONS s
                            JOIN CLASSSCHEDULES cs ON s.SCHEDULEID = cs.SCHEDULEID
                            JOIN SUBJECTS subj ON cs.SUBJECTID = subj.SUBJECTID
                            WHERE subj.SUBJECTNAME = @subjectName
                              AND s.ROOMID = @roomId
                              AND s.SESSIONDATE = CURRENT_DATE
                              AND s.STATUS = 'active'", connection))
                        {
                            cmdCheckSessionBySubject.Parameters.AddWithValue("@subjectName", subjectName);
                            cmdCheckSessionBySubject.Parameters.AddWithValue("@roomId", CurrentRoomId);
                            
                            bool sessionFound = false;
                            using (var reader = cmdCheckSessionBySubject.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    // Found session by subject name match
                                    sessionFound = true;
                                    result.IsValid = true;
                                    result.ScheduleId = scheduleId;
                                    result.SubjectName = subjectName;
                                    result.Reason = "Student is enrolled and active session exists (matched by subject)";
                                    
                                    var sessionId = reader.GetString("SESSIONID");
                                    var sessionScheduleId = reader.GetString("SCHEDULEID");
                                    LogMessage("INFO", $"Active session found by subject: {sessionId} (scheduleId: {sessionScheduleId}) for student {userGuid}");
                                    Console.WriteLine($"✅ Active session found by subject match: {sessionId}");
                                    Console.WriteLine($"   Session ScheduleID: {sessionScheduleId} vs Student ScheduleID: {scheduleId}");
                                }
                            }
                            
                            // Check early arrival only if no session was found (reader is now closed)
                            if (!sessionFound)
                            {
                                // Check if within early arrival window using configured value
                                int earlyWindowMinutes = LoadStudentEarlyArrivalWindow();
                                Console.WriteLine($"DEBUG: Checking early arrival for student - window: {earlyWindowMinutes} minutes");
                                
                                using (var cmdGetStartTime = new MySqlCommand(
                                    "SELECT STARTTIME FROM CLASSSCHEDULES WHERE SCHEDULEID = @scheduleId", connection))
                                {
                                    cmdGetStartTime.Parameters.AddWithValue("@scheduleId", scheduleId);
                                    var startTimeObj = cmdGetStartTime.ExecuteScalar();
                                    
                                    if (startTimeObj != null && TimeSpan.TryParse(startTimeObj.ToString(), out var startTime))
                                    {
                                        var nowTime = DateTime.Now.TimeOfDay;
                                        var timeUntilClass = (startTime - nowTime).TotalMinutes;
                                        Console.WriteLine($"DEBUG: Current time: {nowTime}, Class starts: {startTime}, Minutes until class: {timeUntilClass:F0}");
                                        
                                        if (timeUntilClass > 0 && timeUntilClass <= earlyWindowMinutes)
                                        {
                                            // Allow early arrival within configured window
                                            result.IsValid = true;
                                            result.ScheduleId = scheduleId;
                                            result.SubjectName = subjectName;
                                            result.Reason = $"Early arrival allowed. Class starts in {timeUntilClass:F0} minutes.";
                                            Console.WriteLine($"✅ Early arrival allowed (window: {earlyWindowMinutes} min, actual: {timeUntilClass:F0} min)");
                                            return result;
                                        }
                                        else
                                        {
                                            Console.WriteLine($"❌ Too early or too late. Window: {earlyWindowMinutes} min, Time until class: {timeUntilClass:F0} min");
                                        }
                                    }
                                }
                                
                                // Not in early arrival window - instructor must start session first
                                result.Reason = "No active class session. Instructor must start the session first.";
                                LogMessage("WARNING", $"No active session found for scheduleId: {scheduleId} or subject: {subjectName}");
                                Console.WriteLine($"❌ No active session found for scheduleId: {scheduleId}");
                                Console.WriteLine($"❌ Also no session found for subject: {subjectName} in room: {CurrentRoomId}");
                            }
                        }
                    }
                }
                else
                {
                    result.Reason = "Invalid user type for attendance";
                }

                return result;
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Schedule validation error: {ex.Message}");
                LogMessage("ERROR", $"Stack trace: {ex.StackTrace}");
                return new ScheduleValidationResult 
                { 
                    IsValid = false, 
                    Reason = $"Validation error: {ex.Message}" 
                };
            }
        }

        public List<AttendanceRecord> GetAttendanceRecords(int? userId = null, DateTime? fromDate = null, DateTime? toDate = null, int limit = 100)
        {
            var records = new List<AttendanceRecord>();
            
            try
            {
                var query = @"
                    SELECT ar.*, u.username, d.device_name 
                    FROM attendance_records ar
                    JOIN users u ON ar.user_id = u.id
                    JOIN devices d ON ar.device_id = d.id
                    WHERE 1=1";
                
                var cmd = new MySqlCommand(query, connection);
                
                if (userId.HasValue)
                {
                    query += " AND ar.user_id = @userId";
                    cmd.Parameters.AddWithValue("@userId", userId.Value);
                }
                
                if (fromDate.HasValue)
                {
                    query += " AND ar.timestamp >= @fromDate";
                    cmd.Parameters.AddWithValue("@fromDate", fromDate.Value);
                }
                
                if (toDate.HasValue)
                {
                    query += " AND ar.timestamp <= @toDate";
                    cmd.Parameters.AddWithValue("@toDate", toDate.Value);
                }
                
                query += " ORDER BY ar.timestamp DESC LIMIT @limit";
                cmd.Parameters.AddWithValue("@limit", limit);
                
                cmd.CommandText = query;
                
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(new AttendanceRecord
                        {
                            Id = reader.GetInt32("id"),
                            UserId = reader.GetInt32("user_id"),
                            DeviceId = reader.GetInt32("device_id"),
                            Action = reader.GetString("action"),
                            Timestamp = reader.GetDateTime("timestamp"),
                            Location = reader.IsDBNull(5) ? null : reader.GetString(5),
                            Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
                            Username = reader.GetString("username"),
                            DeviceName = reader.GetString("device_name")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get attendance records: {ex.Message}", ex);
            }
            
            return records;
        }

        public void UpdateHeartbeat()
        {
            try
            {
                var cmd = new MySqlCommand("UPDATE devices SET last_heartbeat = NOW() WHERE device_name = @deviceName", connection);
                cmd.Parameters.AddWithValue("@deviceName", deviceId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // Don't throw exception for heartbeat failures
                System.Diagnostics.Debug.WriteLine($"Heartbeat failed: {ex.Message}");
            }
        }

        // Start a class session (for instructors using C# system)
        public bool StartClassSession(string instructorGuid, string scheduleId)
        {
            try
            {
                // Check if there's already an active session for this schedule today
                using (var cmdCheckExisting = new MySqlCommand(@"
                    SELECT SESSIONID FROM SESSIONS 
                    WHERE SCHEDULEID = @scheduleId 
                      AND SESSIONDATE = CURRENT_DATE 
                      AND STATUS = 'active'", connection))
                {
                    cmdCheckExisting.Parameters.AddWithValue("@scheduleId", scheduleId);
                    var existingSession = cmdCheckExisting.ExecuteScalar();
                    
                    if (existingSession != null)
                    {
                        LogMessage("WARNING", $"Active session already exists for schedule {scheduleId}");
                        return false;
                    }
                }

                // Create new session
                Console.WriteLine($"====== CREATING SESSION ======");
                Console.WriteLine($"ScheduleID: {scheduleId}");
                Console.WriteLine($"InstructorID: {instructorGuid}");
                Console.WriteLine($"RoomID: {CurrentRoomId}");
                
                using (var cmdCreateSession = new MySqlCommand(@"
                    INSERT INTO SESSIONS (SESSIONID, SCHEDULEID, INSTRUCTORID, ROOMID, SESSIONDATE, STARTTIME, STATUS, DOORUNLOCKEDAT, ACADEMICYEAR, SEMESTER)
                    VALUES (UUID(), @scheduleId, @instructorId, @roomId, CURRENT_DATE, CURRENT_TIMESTAMP, 'active', CURRENT_TIMESTAMP,
                            (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year'),
                            (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester'))", connection))
                {
                    cmdCreateSession.Parameters.AddWithValue("@scheduleId", scheduleId);
                    cmdCreateSession.Parameters.AddWithValue("@instructorId", instructorGuid);
                    cmdCreateSession.Parameters.AddWithValue("@roomId", CurrentRoomId);
                    
                    cmdCreateSession.ExecuteNonQuery();
                    Console.WriteLine($"✅ Session created successfully");
                }

                // Unlock door
                using (var cmdUnlockDoor = new MySqlCommand(@"
                    UPDATE ROOMS SET DOORSTATUS = 'unlocked', UPDATED_AT = CURRENT_TIMESTAMP 
                    WHERE ROOMID = @roomId", connection))
                {
                    cmdUnlockDoor.Parameters.AddWithValue("@roomId", CurrentRoomId);
                    cmdUnlockDoor.ExecuteNonQuery();
                }

                LogMessage("INFO", $"Class session started successfully for schedule {scheduleId} by instructor {instructorGuid}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to start class session: {ex.Message}");
                return false;
            }
        }

        // End a class session (for instructors using C# system)
        public bool EndClassSession(string instructorGuid, string scheduleId)
        {
            try
            {
                // Find active session for this schedule
                using (var cmdFindSession = new MySqlCommand(@"
                    SELECT SESSIONID FROM SESSIONS 
                    WHERE SCHEDULEID = @scheduleId 
                      AND SESSIONDATE = CURRENT_DATE 
                      AND STATUS = 'active'", connection))
                {
                    cmdFindSession.Parameters.AddWithValue("@scheduleId", scheduleId);
                    var sessionId = cmdFindSession.ExecuteScalar()?.ToString();
                    
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        LogMessage("WARNING", $"No active session found for schedule {scheduleId}");
                        return false;
                    }

                    // End the session
                    using (var cmdEndSession = new MySqlCommand(@"
                        UPDATE SESSIONS SET 
                            STATUS = 'ended', 
                            ENDTIME = CURRENT_TIMESTAMP,
                            DOORLOCKEDAT = CURRENT_TIMESTAMP,
                            UPDATED_AT = CURRENT_TIMESTAMP
                        WHERE SESSIONID = @sessionId", connection))
                    {
                        cmdEndSession.Parameters.AddWithValue("@sessionId", sessionId);
                        cmdEndSession.ExecuteNonQuery();
                    }

                    // Lock door
                    using (var cmdLockDoor = new MySqlCommand(@"
                        UPDATE ROOMS SET DOORSTATUS = 'locked', UPDATED_AT = CURRENT_TIMESTAMP 
                        WHERE ROOMID = @roomId", connection))
                    {
                        cmdLockDoor.Parameters.AddWithValue("@roomId", CurrentRoomId);
                        cmdLockDoor.ExecuteNonQuery();
                    }

                    LogMessage("INFO", $"Class session ended successfully for schedule {scheduleId} by instructor {instructorGuid}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to end class session: {ex.Message}");
                return false;
            }
        }

        public void LogMessage(string level, string message)
        {
            try
            {
                var deviceId = GetDeviceId();
                
                var cmd = new MySqlCommand(@"
                    INSERT INTO system_logs (device_id, log_level, message, timestamp) 
                    VALUES (@deviceId, @level, @message, NOW())", connection);

                cmd.Parameters.AddWithValue("@deviceId", deviceId);
                cmd.Parameters.AddWithValue("@level", level);
                cmd.Parameters.AddWithValue("@message", message);
                
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // Don't throw exception for logging failures
                System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}");
            }
        }

        // Test validation for specific instructor
        public void TestInstructorValidation(string instructorEmail)
        {
            try
            {
                LogMessage("DEBUG", $"=== Testing validation for instructor: {instructorEmail} ===");
                
                // Find the instructor
                string instructorGuid = null;
                using (var cmd = new MySqlCommand("SELECT USERID FROM USERS WHERE EMAIL = @email", connection))
                {
                    cmd.Parameters.AddWithValue("@email", instructorEmail);
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        instructorGuid = result.ToString();
                        LogMessage("DEBUG", $"Found instructor GUID: {instructorGuid}");
                    }
                    else
                    {
                        LogMessage("ERROR", $"Instructor {instructorEmail} not found in database");
                        return;
                    }
                }
                
                // Test the validation
                var validation = ValidateScheduleForCurrentTime(instructorGuid);
                LogMessage("DEBUG", $"Validation result: IsValid={validation.IsValid}, Reason={validation.Reason}");
                LogMessage("DEBUG", $"Schedule ID: {validation.ScheduleId ?? "NULL"}, Subject: {validation.SubjectName ?? "NULL"}");
                
                LogMessage("DEBUG", "=== End instructor validation test ===");
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Instructor validation test failed: {ex.Message}");
                LogMessage("ERROR", $"Stack trace: {ex.StackTrace}");
            }
        }

        // Auto-assign device to a room if not already assigned
        public void AutoAssignRoomIfNeeded()
        {
            try
            {
                if (string.IsNullOrEmpty(CurrentRoomId))
                {
                    LogMessage("INFO", "CurrentRoomId is NULL, attempting to auto-assign to a room...");
                    
                    // Try to find any available room
                    using (var cmd = new MySqlCommand("SELECT ROOMID FROM ROOMS WHERE STATUS = 'Available' LIMIT 1", connection))
                    {
                        var roomId = cmd.ExecuteScalar()?.ToString();
                        if (!string.IsNullOrEmpty(roomId))
                        {
                            LogMessage("INFO", $"Auto-assigning device to room: {roomId}");
                            ChangeCurrentRoom(roomId);
                        }
                        else
                        {
                            LogMessage("ERROR", "No available rooms found in database");
                        }
                    }
                }
                else
                {
                    LogMessage("DEBUG", $"Device already assigned to room: {CurrentRoomId}");
                }
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Auto-assign room failed: {ex.Message}");
            }
        }

        // Debug method to check database connectivity and data
        public void DebugDatabaseStatus()
        {
            try
            {
                LogMessage("DEBUG", "=== Database Debug Status ===");
                
                // Check if we can connect to the database
                LogMessage("DEBUG", $"Database connection: {(connection?.State == System.Data.ConnectionState.Open ? "Connected" : "Disconnected")}");
                LogMessage("DEBUG", $"Current Room ID: {CurrentRoomId ?? "NULL"}");
                LogMessage("DEBUG", $"Current Device ID: {CurrentDeviceId ?? "NULL"}");
                
                // Show available rooms
                LogMessage("DEBUG", "Available rooms:");
                using (var cmd = new MySqlCommand("SELECT ROOMID, ROOMNAME, STATUS FROM ROOMS", connection))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            LogMessage("DEBUG", $"  Room: {reader.GetString("ROOMID")} - {reader.GetString("ROOMNAME")} (Status: {reader.GetString("STATUS")})");
                        }
                    }
                }
                
                // Check if we have any users
                var userCount = 0;
                using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM USERS WHERE STATUS = 'Active'", connection))
                {
                    userCount = Convert.ToInt32(cmd.ExecuteScalar());
                }
                LogMessage("DEBUG", $"Active users count: {userCount}");
                
                // Check if we have any instructors
                var instructorCount = 0;
                using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM USERS WHERE USERTYPE = 'instructor' AND STATUS = 'Active'", connection))
                {
                    instructorCount = Convert.ToInt32(cmd.ExecuteScalar());
                }
                LogMessage("DEBUG", $"Active instructors count: {instructorCount}");
                
                // Check specific instructor
                using (var cmd = new MySqlCommand("SELECT USERID, EMAIL, USERTYPE, STATUS FROM USERS WHERE EMAIL = 'harleyinstructor@gmail.com'", connection))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            LogMessage("DEBUG", $"Found instructor: ID={reader.GetString("USERID")}, Email={reader.GetString("EMAIL")}, Type={reader.GetString("USERTYPE")}, Status={reader.GetString("STATUS")}");
                        }
                        else
                        {
                            LogMessage("WARNING", "harleyinstructor@gmail.com not found in database");
                        }
                    }
                }
                
                // Check if we have any class schedules
                var scheduleCount = 0;
                using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM CLASSSCHEDULES", connection))
                {
                    scheduleCount = Convert.ToInt32(cmd.ExecuteScalar());
                }
                LogMessage("DEBUG", $"Class schedules count: {scheduleCount}");
                
                // Check schedules for today
                var today = DateTime.Now.DayOfWeek.ToString();
                using (var cmd = new MySqlCommand(@"
                    SELECT cs.SCHEDULEID, s.SUBJECTNAME, s.INSTRUCTORID, u.EMAIL, cs.DAYOFWEEK, cs.STARTTIME, cs.ENDTIME, cs.ROOMID
                    FROM CLASSSCHEDULES cs
                    JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
                    JOIN USERS u ON s.INSTRUCTORID = u.USERID
                    WHERE cs.DAYOFWEEK = @today
                      AND cs.ACADEMICYEAR = @academicYear
                      AND cs.SEMESTER = @semester", connection))
                {
                    cmd.Parameters.AddWithValue("@today", today);
                    cmd.Parameters.AddWithValue("@academicYear", CurrentAcademicYear);
                    cmd.Parameters.AddWithValue("@semester", CurrentSemester);
                    using (var reader = cmd.ExecuteReader())
                    {
                        LogMessage("DEBUG", $"Schedules for {today}:");
                        while (reader.Read())
                        {
                            LogMessage("DEBUG", $"  - {reader.GetString("SUBJECTNAME")} by {reader.GetString("EMAIL")} at {reader.GetString("STARTTIME")}-{reader.GetString("ENDTIME")} in room {reader.GetString("ROOMID")}");
                        }
                    }
                }
                
                // Check current academic settings
                string academicYear = null, semester = null;
                using (var cmd = new MySqlCommand("SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year'", connection))
                {
                    academicYear = cmd.ExecuteScalar()?.ToString();
                }
                using (var cmd = new MySqlCommand("SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester'", connection))
                {
                    semester = cmd.ExecuteScalar()?.ToString();
                }
                LogMessage("DEBUG", $"Academic Year: {academicYear ?? "NULL"}, Semester: {semester ?? "NULL"}");
                
                LogMessage("DEBUG", "=== End Database Debug Status ===");
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Database debug failed: {ex.Message}");
            }
        }

        private int GetDeviceId()
        {
            try
            {
                var cmd = new MySqlCommand("SELECT id FROM devices WHERE device_name = @deviceName", connection);
                cmd.Parameters.AddWithValue("@deviceName", deviceId);
                
                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 1; // Default to 1 if not found
            }
            catch
            {
                return 1; // Default fallback
            }
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch
            {
                // Fallback
            }
            return "Unknown";
        }

        // NEW METHODS FOR DUAL SENSOR SUPPORT

        /// <summary>
        /// Get all available rooms (for room selection in startup dialog)
        /// </summary>
        public List<Models.Room> GetAllAvailableRooms()
        {
            return LoadAllRooms(); // Reuse existing method
        }

        /// <summary>
        /// Register or update a dual sensor device (inside or outside)
        /// </summary>
        public bool RegisterDualSensorDevice(string deviceName, string roomId, string position, int sensorIndex)
        {
            try
            {
                var ipAddress = GetLocalIPAddress();
                string location = position.ToLower(); // "inside" or "outside"
                string fullDeviceName = $"{deviceName}_{position}";

                // Check if device already exists
                var findCmd = new MySqlCommand(@"
                    SELECT DEVICEID FROM DEVICES WHERE DEVICENAME = @deviceName LIMIT 1", connection);
                findCmd.Parameters.AddWithValue("@deviceName", fullDeviceName);
                
                var existingDeviceId = findCmd.ExecuteScalar()?.ToString();
                
                if (string.IsNullOrEmpty(existingDeviceId))
                {
                    // Create new device
                    var insertCmd = new MySqlCommand(@"
                        INSERT INTO DEVICES (DEVICEID, DEVICETYPE, DEVICENAME, LOCATION, ROOMID, IPADDRESS, LASTSEEN, STATUS) 
                        VALUES (UUID(), 'Fingerprint_Scanner', @deviceName, @location, @roomId, @ipAddress, NOW(), 'Active')", connection);

                    insertCmd.Parameters.AddWithValue("@deviceName", fullDeviceName);
                    insertCmd.Parameters.AddWithValue("@location", location);
                    insertCmd.Parameters.AddWithValue("@roomId", roomId);
                    insertCmd.Parameters.AddWithValue("@ipAddress", ipAddress);
                    
                    insertCmd.ExecuteNonQuery();
                    
                    LogMessage("INFO", $"Registered new dual sensor device: {fullDeviceName} in room {roomId} as {position}");
                    return true;
                }
                else
                {
                    // Update existing device
                    var updateCmd = new MySqlCommand(@"
                        UPDATE DEVICES SET 
                            ROOMID = @roomId,
                            LOCATION = @location,
                            IPADDRESS = @ipAddress,
                            LASTSEEN = NOW(),
                            STATUS = 'Active'
                        WHERE DEVICEID = @deviceId", connection);

                    updateCmd.Parameters.AddWithValue("@roomId", roomId);
                    updateCmd.Parameters.AddWithValue("@location", location);
                    updateCmd.Parameters.AddWithValue("@ipAddress", ipAddress);
                    updateCmd.Parameters.AddWithValue("@deviceId", existingDeviceId);
                    updateCmd.ExecuteNonQuery();
                    
                    LogMessage("INFO", $"Updated dual sensor device: {fullDeviceName} in room {roomId} as {position}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to register dual sensor device: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Record attendance with specific device ID (for dual sensor tracking)
        /// </summary>
        public AttendanceAttemptResult RecordAttendanceWithDeviceId(string userGuid, string deviceId, string location, string notes = null)
        {
            try
            {
                // Determine action based on location
                string action = location.ToLower() == "inside" ? "Check In" : "Check Out";
                
                // Use existing attendance recording logic, passing explicit location
                var result = InternalTryRecordAttendance(userGuid, action, location);
                
                if (result.Success)
                {
                    LogMessage("INFO", $"Attendance recorded via device {deviceId} - User: {userGuid}, Location: {location}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to record attendance with device ID: {ex.Message}");
                return new AttendanceAttemptResult { Success = false, Reason = ex.Message };
            }
        }

        /// <summary>
        /// Update device heartbeat for dual sensor
        /// </summary>
        public bool UpdateDeviceHeartbeat(string deviceName, string position)
        {
            try
            {
                string fullDeviceName = $"{deviceName}_{position}";
                
                var cmd = new MySqlCommand(@"
                    UPDATE DEVICES SET 
                        LASTSEEN = NOW(),
                        STATUS = 'Active'
                    WHERE DEVICENAME = @deviceName", connection);
                    
                cmd.Parameters.AddWithValue("@deviceName", fullDeviceName);
                
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                LogMessage("ERROR", $"Failed to update device heartbeat: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                // Mark device as offline
                var cmd = new MySqlCommand("UPDATE devices SET is_online = FALSE WHERE device_name = @deviceName", connection);
                cmd.Parameters.AddWithValue("@deviceName", deviceId);
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Ignore errors during disposal
            }
            finally
            {
                connection?.Close();
                connection?.Dispose();
            }
        }
    }
}
