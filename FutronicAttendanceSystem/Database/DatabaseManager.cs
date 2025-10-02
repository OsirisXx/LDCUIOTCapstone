using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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

        public User GetUserById(int id)
        {
            try
            {
                var cmd = new MySqlCommand("SELECT * FROM users WHERE id = @id", connection);
                cmd.Parameters.AddWithValue("@id", id);
                
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new User
                        {
                            Id = reader.GetInt32("id"),
                            Username = reader.GetString("username"),
                            FingerprintTemplate = (byte[])reader["fingerprint_template"],
                            EmployeeId = reader.IsDBNull(3) ? null : reader.GetString(3),
                            Department = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Email = reader.IsDBNull(5) ? null : reader.GetString(5),
                            Phone = reader.IsDBNull(6) ? null : reader.GetString(6),
                            IsActive = reader.GetBoolean("is_active"),
                            CreatedAt = reader.GetDateTime("created_at"),
                            UpdatedAt = reader.GetDateTime("updated_at")
                        };
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get user: {ex.Message}", ex);
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

        // New method that accepts the user GUID directly (non-throwing variant returns result)
        public AttendanceAttemptResult TryRecordAttendanceByGuid(string userGuid, string action, string notes = null)
        {
            try
            {
                var result = InternalTryRecordAttendance(userGuid, action, notes);
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

        private AttendanceAttemptResult InternalTryRecordAttendance(string userGuid, string action, string notes = null)
        {
            try
            {
                LogMessage("INFO", $"Recording attendance for user {userGuid}, action {action}, current room: {CurrentRoomId}");
                
                // First, validate if there's a scheduled class for the current time and room
                var scheduleValidation = ValidateScheduleForCurrentTime(userGuid);
                if (!scheduleValidation.IsValid)
                {
                    LogMessage("WARNING", $"Schedule validation failed: {scheduleValidation.Reason}");
                    return new AttendanceAttemptResult { Success = false, Reason = scheduleValidation.Reason, SubjectName = scheduleValidation.SubjectName };
                }

                // Try to find an active session for today in the current device's room
                string sessionId = null;
                string scheduleId = null;
                using (var cmdFindSession = new MySqlCommand(@"
                    SELECT SESSIONID, SCHEDULEID 
                    FROM SESSIONS 
                    WHERE SESSIONDATE = CURRENT_DATE 
                      AND STATUS IN ('active','waiting')
                      AND ROOMID = @currentRoomId
                    ORDER BY COALESCE(STARTTIME, TIMESTAMP(CURRENT_DATE,'00:00:00')) DESC 
                    LIMIT 1", connection))
                {
                    cmdFindSession.Parameters.AddWithValue("@currentRoomId", CurrentRoomId ?? "");
                    using (var r = cmdFindSession.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            sessionId = r.GetString(0);
                            scheduleId = r.GetString(1);
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

                if (string.IsNullOrWhiteSpace(scheduleId))
                {
                    // Cannot record without a valid schedule - this should not happen if validation passed
                    LogMessage("ERROR", "RecordAttendance: No valid schedule available to attach attendance.");
                    return new AttendanceAttemptResult { Success = false, Reason = "No valid schedule available" };
                }

                // Map action to scan type and status
                string scanType = string.Equals(action, "Check In", StringComparison.OrdinalIgnoreCase) ? "time_in" : "time_out";
                string status = "Present";
                string authMethod = "Fingerprint";
                string location = CurrentLocation; // Use current location setting

                // Compose insert
                var insertCmd = new MySqlCommand(@"
                    INSERT INTO ATTENDANCERECORDS
                    (ATTENDANCEID, USERID, SCHEDULEID, SESSIONID, SCANTYPE, SCANDATETIME, DATE, TIMEIN, AUTHMETHOD, LOCATION, STATUS, ACADEMICYEAR, SEMESTER)
                    VALUES (UUID(), @USERID, @SCHEDULEID, @SESSIONID, @SCANTYPE, NOW(), CURRENT_DATE,
                            CASE WHEN @SCANTYPE = 'time_in' THEN CURRENT_TIME ELSE NULL END,
                            @AUTHMETHOD, @LOCATION, @STATUS,
                            (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY='current_academic_year' LIMIT 1),
                            (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY='current_semester' LIMIT 1))
                ", connection);

                insertCmd.Parameters.AddWithValue("@USERID", userGuid);
                insertCmd.Parameters.AddWithValue("@SCHEDULEID", scheduleId);
                insertCmd.Parameters.AddWithValue("@SESSIONID", (object)sessionId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@SCANTYPE", scanType);
                insertCmd.Parameters.AddWithValue("@AUTHMETHOD", authMethod);
                insertCmd.Parameters.AddWithValue("@LOCATION", location);
                insertCmd.Parameters.AddWithValue("@STATUS", status);

                insertCmd.ExecuteNonQuery();
                
                LogMessage("INFO", $"Attendance recorded successfully for user GUID: {userGuid}, action: {action}");
                return new AttendanceAttemptResult { Success = true, ScheduleId = scheduleId, SubjectName = scheduleValidation.SubjectName };
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to record attendance (web schema): {ex.Message}", ex);
            }
        }

        private void RecordAttendanceInternal(string userGuid, string action, string notes = null)
        {
            var res = InternalTryRecordAttendance(userGuid, action, notes);
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
        private ScheduleValidationResult ValidateScheduleForCurrentTime(string userGuid)
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
                using (var cmdGetUserType = new MySqlCommand(@"
                    SELECT USERTYPE FROM USERS WHERE USERID = @userGuid", connection))
                {
                    cmdGetUserType.Parameters.AddWithValue("@userGuid", userGuid);
                    var userTypeObj = cmdGetUserType.ExecuteScalar();
                    if (userTypeObj != null)
                    {
                        userType = userTypeObj.ToString();
                    }
                }

                if (string.IsNullOrEmpty(userType))
                {
                    result.Reason = "User not found";
                    LogMessage("ERROR", $"User {userGuid} not found in database");
                    return result;
                }

                // For instructors: Check if they have a scheduled class at this time
                if (userType != null && userType.Equals("instructor", StringComparison.OrdinalIgnoreCase))
                {
                    LogMessage("DEBUG", $"Searching for instructor schedule with: userGuid={userGuid}, roomId={CurrentRoomId}, day={currentDay}, time={currentTime}");
                    Console.WriteLine($"DEBUG: Searching for instructor schedule with: userGuid={userGuid}, roomId={CurrentRoomId}, day={currentDay}, time={currentTime}");
                    
                    // Use the same logic as the web system: TIME(NOW()) BETWEEN cs.STARTTIME AND cs.ENDTIME
                    using (var cmdCheckInstructorSchedule = new MySqlCommand(@"
                        SELECT cs.SCHEDULEID, s.SUBJECTNAME, cs.STARTTIME, cs.ENDTIME
                        FROM CLASSSCHEDULES cs
                        JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
                        WHERE s.INSTRUCTORID = @userGuid
                          AND cs.ROOMID = @roomId
                          AND cs.DAYOFWEEK = @currentDay
                          AND TIME(NOW()) BETWEEN cs.STARTTIME AND cs.ENDTIME
                          AND cs.ACADEMICYEAR = '2025-2026'
                          AND cs.SEMESTER = 'First Semester'", connection))
                    {
                        cmdCheckInstructorSchedule.Parameters.AddWithValue("@userGuid", userGuid);
                        cmdCheckInstructorSchedule.Parameters.AddWithValue("@roomId", CurrentRoomId);
                        cmdCheckInstructorSchedule.Parameters.AddWithValue("@currentDay", currentDay);

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
                    // First check if student is enrolled in a scheduled class
                    string scheduleId = null;
                    string subjectName = null;
                    
                    using (var cmdCheckStudentSchedule = new MySqlCommand(@"
                        SELECT cs.SCHEDULEID, s.SUBJECTNAME, cs.STARTTIME, cs.ENDTIME
                        FROM CLASSSCHEDULES cs
                        JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
                        JOIN SUBJECTENROLLMENT se ON s.SUBJECTID = se.SUBJECTID
                        WHERE se.USERID = @userGuid
                          AND se.STATUS = 'enrolled'
                          AND cs.ROOMID = @roomId
                          AND cs.DAYOFWEEK = @currentDay
                          AND cs.STARTTIME <= @currentTime
                          AND cs.ENDTIME >= @currentTime
                          AND cs.ACADEMICYEAR = '2025-2026'
                          AND cs.SEMESTER = 'First Semester'", connection))
                    {
                        cmdCheckStudentSchedule.Parameters.AddWithValue("@userGuid", userGuid);
                        cmdCheckStudentSchedule.Parameters.AddWithValue("@roomId", CurrentRoomId);
                        cmdCheckStudentSchedule.Parameters.AddWithValue("@currentDay", currentDay);
                        cmdCheckStudentSchedule.Parameters.AddWithValue("@currentTime", currentTime);

                        using (var reader = cmdCheckStudentSchedule.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                scheduleId = reader.GetString("SCHEDULEID");
                                subjectName = reader.GetString("SUBJECTNAME");
                            }
                            else
                            {
                                result.Reason = "Student is not enrolled in any class scheduled at this time";
                                return result;
                            }
                        }
                    }

                    // If student is enrolled, check if there's an active session for this schedule
                    if (!string.IsNullOrEmpty(scheduleId))
                    {
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
                                }
                                else
                                {
                                    // No active session - instructor must start session first
                                    result.Reason = "No active class session. Instructor must start the session first.";
                                }
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
                      AND cs.ACADEMICYEAR = '2025-2026'
                      AND cs.SEMESTER = 'First Semester'", connection))
                {
                    cmd.Parameters.AddWithValue("@today", today);
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
