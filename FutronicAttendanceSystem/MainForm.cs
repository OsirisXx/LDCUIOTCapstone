using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Futronic.SDKHelper;
using FutronicAttendanceSystem.Database;
using FutronicAttendanceSystem.Database.Models;
using FutronicAttendanceSystem.Utils;
using FutronicAttendanceSystem.UI;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace FutronicAttendanceSystem
{
    public partial class MainForm : Form
    {
        // Constants
        const string kCompanyName = "FutronicAttendance";
        const string kProductName = "AttendanceSystem";
        const string kDbName = "Users";

        // Current operation
        private FutronicSdkBase m_Operation;
        private FutronicIdentification m_AttendanceOperation; // Separate operation for attendance
        private bool m_bExit = false;
        private Object m_OperationObj; // For enrollment operations
        private List<UserRecord> m_IdentificationUsers; // For identification operations
        private bool m_bInitializationSuccess = false;
        
        // DUAL SENSOR MODE: New fields for dual sensor support
        private bool isDualSensorMode = false;
        private DeviceConfiguration deviceConfig;
        private FutronicIdentification m_InsideSensorOperation;
        private FutronicIdentification m_OutsideSensorOperation;
        private bool m_InsideSensorEnabled = true;
        private bool m_OutsideSensorEnabled = true;
        private DualSensorPanel dualSensorPanel;
        private TabPage dualSensorTab;
        
        // Operation state tracking
        private bool m_bEnrollmentInProgress = false;
        private bool m_bAttendanceActive = false;
        private bool m_bRfidAttendanceActive = false; // NEW: RFID attendance state
        
        // NEW: Attendance session state tracking
        private enum AttendanceSessionState
        {
            Inactive,           // No session active
            WaitingForInstructor, // Waiting for instructor to start session
            ActiveForStudents,   // Session active, students can sign in
            WaitingForInstructorSignOut, // Waiting for instructor to open sign-out
            ActiveForSignOut,   // Students can sign out
            WaitingForInstructorClose // Waiting for instructor to close session
        }
        
        private AttendanceSessionState currentSessionState = AttendanceSessionState.Inactive;
        private string currentInstructorId = null;
        private string currentScheduleId = null;
        
        // RFID Session state tracking
        private AttendanceSessionState currentRfidSessionState = AttendanceSessionState.Inactive;
        private string currentRfidInstructorId = null;
        private string currentRfidScheduleId = null;
        
        // False positive detection prevention
        private DateTime m_lastPutOnTime = DateTime.MinValue;
        private bool m_bInitialStartup = true;
        private const int MIN_PUTON_INTERVAL_MS = 1000; // Minimum 1 second between put-on events
        
        // Database and cloud storage
        private DatabaseManager dbManager;
        private List<User> cloudUsers;
        private List<Database.Models.Room> availableRooms;
        private ConfigManager config;
        
        // PERFORMANCE: Cached dictionaries for O(1) user lookups
        private Dictionary<string, User> userLookupByUsername;
        private Dictionary<string, User> userLookupByGuid;
        
        // NEW: User selection and filtering
        private List<User> filteredUsers;
        private User selectedUser;
        private bool isUserSelected = false;
        
        // Device availability check
        private bool IsFingerprintDeviceAvailable()
        {
            try
            {
                // Try to create a temporary FutronicIdentification object to test device availability
                using (var testOperation = new FutronicIdentification())
                {
                    // Set basic properties to ensure proper initialization
                    testOperation.FakeDetection = false;
                    testOperation.FFDControl = false;
                    testOperation.FastMode = true;
                    testOperation.FARN = 100;
                    testOperation.Version = VersionCompatible.ftr_version_compatible;
                    
                    // If we can create the object without exception, device is likely available
                    return true;
                }
            }
            catch (Exception ex)
            {
                SetStatusText($"Device check failed: {ex.Message}");
                return false;
            }
        }
        
        // Watchdog timer to detect stuck operations
        private System.Windows.Forms.Timer watchdogTimer;
        private DateTime lastSuccessfulOperation = DateTime.Now;
        
        // Debouncing to prevent duplicate processing
        private string lastProcessedUser = "";
        private DateTime lastProcessedTime = DateTime.MinValue;
        private const int DEBOUNCE_INTERVAL_MS = 1000; // 1 second between same user processing
        
        // TWO-SCAN VERIFICATION: For security and accuracy
        private bool awaitingVerificationScan = false;
        private string pendingVerificationUser = "";
        private DateTime verificationScanStartTime = DateTime.MinValue;
        private const int VERIFICATION_TIMEOUT_SECONDS = 15; // 15 seconds to complete verification
        
        // Track student sign-in status to prevent duplicates
        // Track signed-in students by GUID to avoid name collisions and case issues
        private HashSet<string> signedInStudentGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Track students who already signed out within the current session
        private HashSet<string> signedOutStudentGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // RFID Native C# Implementation
        private System.Windows.Forms.Timer rfidInputTimer;
        private string rfidBuffer = "";
        private bool rfidCapturing = false;
        private DateTime lastRfidInput = DateTime.MinValue;
        private const int RFID_TIMEOUT_MS = 200; // 200ms timeout for RFID input completion
        
        // Global keyboard hook for RFID input capture
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        
        [DllImport("user32.dll")]
        private static extern int GetKeyNameText(int lParam, StringBuilder lpString, int nSize);
        
        [DllImport("user32.dll")]
        private static extern int MapVirtualKey(int uCode, int uMapType);
        
        private void StartWatchdogTimer()
        {
            watchdogTimer = new System.Windows.Forms.Timer();
            watchdogTimer.Interval = 60000; // Check every 60 seconds (less aggressive)
            watchdogTimer.Tick += (s, e) => {
                try
                {
                    var timeSinceLastOperation = DateTime.Now - lastSuccessfulOperation;
                    // Only restart if truly stuck (5 minutes of no activity) - much less aggressive
                    if (timeSinceLastOperation.TotalSeconds > 300 && m_bAttendanceActive)
                    {
                        Console.WriteLine("Watchdog: System appears stuck (5+ min no activity), attempting restart...");
                        SetStatusText("System restarting due to extended inactivity...");
                        
                        // Restart the fingerprint operation
                        System.Threading.Tasks.Task.Run(() => {
                            try
                            {
                                SafeRestartFingerprintOperation();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Watchdog restart failed: {ex.Message}");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Watchdog error: {ex.Message}");
                }
            };
            watchdogTimer.Start();
        }

        // UI Controls
        private TabControl tabControl;
        private TabPage enrollmentTab;
        private TabPage attendanceTab;
        private TabPage rfidAttendanceTab; // NEW: RFID Attendance tab
        private TabPage deviceManagementTab;
        private TabPage fingerprintUsersTab;
        private TabPage scenariosTab; // NEW: Attendance Scenarios Configuration tab
        private PictureBox pictureFingerprint;
		private Button btnEnroll;
        private Button btnStop;
        private Button btnIdentify;
        private ProgressBar enrollProgressBar;
        private Label lblEnrollStep;
        
        // Live clock controls
        private Label lblLiveTime;
        private Label lblLiveDay;
        private System.Windows.Forms.Timer clockTimer;
		
		// NEW: Table-based user selection interface
		private DataGridView dgvUsers;
		private TextBox txtSearchUsers;
		private ComboBox cmbSearchType;
		private Button btnSearchUsers;
		private Button btnRefreshUserList;
		private Label lblSelectedUser;
		private Button btnClearSelection;
		
		// Hidden enrollment fields (populated from selected user)
		private TextBox txtFirstName;
		private TextBox txtLastName;
		private TextBox txtEmail;
		private TextBox txtPassword;
		private ComboBox cmbUserType;
		private ComboBox cmbStatus;
		private TextBox txtStudentId;
		private TextBox txtFacultyId;
		private ComboBox cmbYearLevel;
		private TextBox txtDepartment;
		
        private TextBox txtStatus;
        private DataGridView dgvAttendance;
        private Button btnExportAttendance;
        
        // NEW: Session state UI controls
        private Label lblSessionState;
        private Label lblSessionInfo;
        
        // RFID Attendance Controls
        private Label lblRfidSessionState;
        private Label lblRfidSessionInfo;
        private Button btnStartRfidAttendance;
        private Button btnStopRfidAttendance;
        private Button btnForceEndRfidSession;
        private DataGridView dgvRfidAttendance;
        private Button btnExportRfidAttendance;
        private TextBox txtRfidStatus;
        private Label lblRfidCurrentRoom;
        private ComboBox cmbRfidLocation;
        private ComboBox cmbRfidRoom;
        private Button btnRfidChangeRoom;
        private Button btnForceEndSession;
        // Removed unused fields: lblStatus, userListBox, btnDeleteUser, btnRefreshUsers
        
        // Location and Room controls
        private ComboBox cmbLocation;
        private ComboBox cmbRoom;
        private Button btnChangeRoom;
        private Label lblCurrentRoom;
        
        // Device Management controls
        private ListView deviceListView;
        private Button btnInitializeInRoom;
        private ComboBox cmbDeviceRoom;
        private TextBox txtDeviceName;
        private Button btnRefreshDevices;

        // Fingerprint users controls
        private ListView fingerprintUsersListView;
        private Button btnExportUsersCsv;
        private Button btnDeleteFingerprint;
        private Button btnRefreshFingerprintUsers;
        private bool fingerprintUsersAccessGranted = false;
        private List<User> fingerprintUsers = new List<User>();

        // Attendance Scenarios Configuration controls
        private NumericUpDown numInstructorEarlyWindow;
        private NumericUpDown numStudentGracePeriod;
        private NumericUpDown numInstructorLateTolerance;
        private NumericUpDown numAutoCloseDelay;
        private NumericUpDown numStudentEarlyArrivalWindow;
        private NumericUpDown numInstructorEndTolerance;
        private Button btnResetToDefaults;
        private Button btnSaveScenarios;
        private Button btnLoadScenarios;
        private Label lblScenariosTitle;
        private Label lblInstructorEarlyWindow;
        private Label lblStudentGracePeriod;
        private Label lblInstructorLateTolerance;
        private Label lblAutoCloseDelay;
        private Label lblStudentEarlyArrivalWindow;
        private Label lblInstructorEndTolerance;

        // Attendance records
        private List<Database.Models.AttendanceRecord> attendanceRecords = new List<Database.Models.AttendanceRecord>();
        private System.Windows.Forms.Timer identifyRetryTimer;
        private int nextRestartDelayMs = 3000;
        private bool alwaysOnAttendance = true;
        private bool isIdentifying = false;
        private DateTime lastActivityTime = DateTime.Now;
        private bool awaitingSecondScan = false;
        private string pendingUserName = "";
        private DateTime firstScanTime = DateTime.MinValue;
        private DateTime lastScanTime = DateTime.MinValue;
        private System.Windows.Forms.Timer countdownTimer;
        private bool noUsersAlertShown = false;
        
        // POWER MANAGEMENT: Smart idle detection
        private bool isInIdleMode = false;
        private DateTime lastScanAttemptTime = DateTime.Now;
        private const int IDLE_TIMEOUT_SECONDS = 30; // Go to idle mode after 30 seconds of no activity
        private const int SCAN_INTERVAL_ACTIVE_MS = 300; // Fast scanning when active (3.3 scans/sec) - SDK-safe
        private const int SCAN_INTERVAL_IDLE_MS = 2000; // Slow scanning when idle (0.5 scans/sec)
        
        // THREAD SAFETY: Prevent concurrent SDK calls
        private bool isGetBaseTemplateInProgress = false;
        private readonly object getBaseTemplateLock = new object();
        
        // Background timers
        private System.Windows.Forms.Timer heartbeatTimer;
        private System.Windows.Forms.Timer syncTimer;

        // HTTP client for backend communication
        private static readonly HttpClient http = new HttpClient();

        // Backend API base URL
        private readonly string backendBaseUrl = "http://localhost:5000";

        // Enrollment progress smoothing
        private System.Windows.Forms.Timer enrollProgressTimer;
        private int enrollProgressTarget = 0;
        private bool isEnrollmentActive = false;

        public MainForm()
        {
            Console.WriteLine("MainForm constructor starting...");
            InitializeComponent();
            // Reduce flicker on WinForms by enabling optimized double buffering
            try
            {
                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                this.UpdateStyles();
            }
            catch { }
            
            try
            {
                Console.WriteLine("Loading configuration...");
                // Load configuration
                config = ConfigManager.Instance;
                alwaysOnAttendance = config.Application.AlwaysOnAttendance;
                
                // Check fingerprint device availability on startup
                Console.WriteLine("Checking fingerprint device availability...");
                if (!IsFingerprintDeviceAvailable())
                {
                    Console.WriteLine("WARNING: Fingerprint device not available. Application will start but attendance features may not work.");
                }
                else
                {
                    Console.WriteLine("Fingerprint device is available.");
                }
                
            // Configure HttpClient header for device heartbeat authentication
            try
            {
                http.DefaultRequestHeaders.Remove("x-device-api-key");
                http.DefaultRequestHeaders.Add("x-device-api-key", "0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567");
            }
            catch { }

                Console.WriteLine("Initializing database connection...");
                // Initialize database connection
                InitializeDatabase();
                
                // Debug database status
                dbManager?.DebugDatabaseStatus();
                
                // Auto-assign room if needed
                dbManager?.AutoAssignRoomIfNeeded();
                
                // Test specific instructor validation
                dbManager?.TestInstructorValidation("harleyinstructor@gmail.com");
                
                m_bInitializationSuccess = true;
                RefreshUserList();
                // Attendance records are now loaded from database when needed
                SetStatusText("System is booting up. Please wait...");

                // Initialize auto-retry timer for always-on attendance
                identifyRetryTimer = new System.Windows.Forms.Timer();
                identifyRetryTimer.Interval = nextRestartDelayMs;
                identifyRetryTimer.Tick += (s, e) =>
                {
                    if (!m_bExit && alwaysOnAttendance)
                    {
                        identifyRetryTimer.Stop();
                        StartIdentification();
                    }
                };
                
                // Set attendance as active if always-on is enabled
                if (alwaysOnAttendance)
                {
                    m_bAttendanceActive = true;
                    currentSessionState = AttendanceSessionState.WaitingForInstructor;
                    UpdateSessionStateDisplay();
                }
                
                // Initialize background timers
                StartBackgroundTasks();
                
                // Start watchdog timer
                StartWatchdogTimer();
                
                // Initialize watchdog timer to detect stuck states
                var watchdogTimer = new System.Windows.Forms.Timer();
                watchdogTimer.Interval = 10000; // Check every 10 seconds
                watchdogTimer.Tick += (s, e) =>
                {
                    if (!m_bExit && alwaysOnAttendance)
                    {
                        var timeSinceLastActivity = DateTime.Now - lastActivityTime;
                        if (timeSinceLastActivity.TotalSeconds > 15 && !isIdentifying)
                        {
                            SetStatusText("System restarting...");
                            RestartIdentification();
                        }
                        
                        // Check for confirmation timeout
                        if (awaitingSecondScan && (DateTime.Now - firstScanTime).TotalSeconds > 30)
                        {
                            awaitingSecondScan = false;
                            SetStatusText("Confirmation timeout. Please start over with your first scan.");
                            nextRestartDelayMs = 2000;
                            ScheduleNextGetBaseTemplate(nextRestartDelayMs);
                        }
                    }
                };
                watchdogTimer.Start();

                // Enrollment progress timer
                enrollProgressTimer = new System.Windows.Forms.Timer();
                enrollProgressTimer.Interval = 100; // 10 updates per second
                enrollProgressTimer.Tick += (s, e) =>
                {
                    try
                    {
                        if (!isEnrollmentActive || enrollProgressBar == null) return;
                        var current = enrollProgressBar.Value;
                        if (current < enrollProgressTarget)
                        {
                            // Ease-in increment
                            int delta = Math.Max(1, (enrollProgressTarget - current) / 10);
                            SetEnrollProgress(Math.Min(enrollProgressTarget, current + delta));
                        }
                    }
                    catch { }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Initialization failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Initialization failed: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        // DUAL SENSOR MODE: New constructor for dual sensor support
        public MainForm(DatabaseManager db, DeviceConfiguration deviceConfiguration)
        {
            Console.WriteLine("=== MainForm Dual Sensor Mode Constructor ===");
            
            // Set dual sensor mode
            isDualSensorMode = true;
            deviceConfig = deviceConfiguration;
            dbManager = db;
            
            // CRITICAL FIX: Set the database manager's current room ID from device configuration
            // This is needed for schedule validation to work correctly
            if (dbManager != null && !string.IsNullOrEmpty(deviceConfig.RoomId))
            {
                dbManager.CurrentRoomId = deviceConfig.RoomId;
                Console.WriteLine($"  ✅ Set DatabaseManager.CurrentRoomId = {deviceConfig.RoomId}");
            }
            else
            {
                Console.WriteLine($"  ⚠️ Warning: Could not set CurrentRoomId (dbManager={dbManager != null}, RoomId={deviceConfig?.RoomId})");
            }
            
            Console.WriteLine($"Dual Sensor Mode Enabled:");
            Console.WriteLine($"  Room: {deviceConfig.RoomName} (ID: {deviceConfig.RoomId})");
            Console.WriteLine($"  Inside Sensor: {deviceConfig.InsideSensor?.DeviceId}");
            Console.WriteLine($"  Outside Sensor: {deviceConfig.OutsideSensor?.DeviceId}");
            Console.WriteLine($"  Test Mode: {deviceConfig.TestMode}");
            
            InitializeComponent();
            
            // Reduce flicker
            try
            {
                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                this.UpdateStyles();
            }
            catch { }
            
            try
            {
                // Load configuration
                config = ConfigManager.Instance;
                
                // Configure HttpClient
                try
                {
                    http.DefaultRequestHeaders.Remove("x-device-api-key");
                    http.DefaultRequestHeaders.Add("x-device-api-key", "0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567");
                }
                catch { }
                
                // Load users from database
                Console.WriteLine("Loading users for dual sensor mode...");
                m_bInitializationSuccess = true;
                RefreshUserList();
                
                // Initialize dual sensor tab
                InitializeDualSensorTab();
                InitializeDeviceConfigTab();  // Add config tab to main tab control
                
                // Start dual sensor operations
                StartDualSensorOperations();
                
                // Initialize background timers for device heartbeat
                StartBackgroundTasks();
                
                Console.WriteLine("✅ Dual Sensor Mode initialized successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Dual Sensor Initialization failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Dual Sensor initialization failed:\n\n{ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitializeDatabase()
        {
            try
            {
                SetStatusText("Preparing database. Please wait...");
                
                dbManager = new DatabaseManager(
                    config.Database,
                    config.Device.DeviceId,
                    config.Device.Location
                );
                
                SetStatusText("Loading users. Please wait...");
                
                // Load users from database with timeout
                var syncTask = System.Threading.Tasks.Task.Run(() => SyncUsersFromCloud());
                if (!syncTask.Wait(10000)) // 10 second timeout
                {
                    SetStatusText("Database sync timeout. Please check your connection.");
                    cloudUsers = new List<Database.Models.User>(); // Initialize empty list
                    RebuildUserLookupCaches(); // Initialize empty caches
                }

                // Load rooms for selection
                LoadAvailableRooms();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect to database: {ex.Message}\n\nPlease check your database configuration in appsettings.json", 
                    "Database Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                cloudUsers = new List<Database.Models.User>(); // Initialize empty list to prevent null reference
                RebuildUserLookupCaches(); // Initialize empty caches
                throw;
            }
        }

        private void StartBackgroundTasks()
        {
            // Heartbeat timer
            heartbeatTimer = new System.Windows.Forms.Timer();
            heartbeatTimer.Interval = config.Application.HeartbeatInterval;
            heartbeatTimer.Tick += (s, e) => 
            {
                try
                {
                    dbManager?.UpdateHeartbeat();
                    // Also send heartbeat to web backend for dashboard device presence
                    _ = SendApiHeartbeatAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Heartbeat failed: {ex.Message}");
                }
            };
            heartbeatTimer.Start();

            // Sync timer
            syncTimer = new System.Windows.Forms.Timer();
            syncTimer.Interval = config.Application.SyncInterval;
            syncTimer.Tick += (s, e) => SyncUsersFromCloud();
            syncTimer.Start();
        }

        private async Task SendApiHeartbeatAsync()
        {
            try
            {
                var payload = new
                {
                    deviceType = "Fingerprint_Scanner",
                    deviceId = config?.Device?.DeviceId ?? Environment.MachineName,
                    location = config?.Device?.Location ?? null,
                    roomNumber = GetCurrentRoomNumberSafe(),
                    hostname = Environment.MachineName,
                    ipAddress = GetLocalIPv4Safe(),
                    appVersion = Application.ProductVersion,
                    capabilities = new[] { "fingerprint", "futronic" }
                };

                var json = JsonSerializer.Serialize(payload);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    var url = $"{backendBaseUrl}/api/devices/heartbeat";
                    using (var response = await http.PostAsync(url, content))
                    {
                        // No throw; best-effort
                        _ = response.StatusCode;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendApiHeartbeatAsync error: {ex.Message}");
            }
        }

        private string GetCurrentRoomNumberSafe()
        {
            try
            {
                if (cmbRoom != null && cmbRoom.SelectedItem is Database.Models.Room room && !string.IsNullOrEmpty(room.RoomNumber))
                {
                    return room.RoomNumber;
                }
                // No config fallback available; return null when not selected
                return null;
            }
            catch { return null; }
        }

        private string GetLocalIPv4Safe()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        private void SyncUsersFromCloud()
        {
            try
            {
                SetStatusText("Syncing users. Please wait...");
                
                cloudUsers = dbManager.LoadAllUsers();
                
                // PERFORMANCE: Build lookup caches for O(1) access
                RebuildUserLookupCaches();
                
                SetStatusText($"Users synced. Ready soon...");
                
                // Refresh UI if on enrollment tab
                if (tabControl?.SelectedTab == enrollmentTab)
                {
                    this.Invoke(new Action(() => RefreshUserList()));
                }
            }
            catch (Exception ex)
            {
                SetStatusText("Sync failed. The system will keep trying in the background.");
                dbManager?.LogMessage("ERROR", $"User sync failed: {ex.Message}");
                cloudUsers = new List<Database.Models.User>(); // Initialize empty list to prevent null reference
                RebuildUserLookupCaches(); // Rebuild even if empty
            }
        }
        
        // PERFORMANCE: Build cached dictionaries for instant user lookups
        private void RebuildUserLookupCaches()
        {
            try
            {
                if (cloudUsers == null)
                {
                    userLookupByUsername = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
                    userLookupByGuid = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
                    return;
                }
                
                // Build username lookup (case-insensitive)
                userLookupByUsername = new Dictionary<string, User>(cloudUsers.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var user in cloudUsers)
                {
                    if (!string.IsNullOrEmpty(user.Username))
                    {
                        // Store by username (already formatted as "FirstName LastName")
                        userLookupByUsername[user.Username] = user;
                    }
                }
                
                // Build GUID lookup (case-insensitive)
                userLookupByGuid = new Dictionary<string, User>(cloudUsers.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var user in cloudUsers)
                {
                    if (!string.IsNullOrEmpty(user.EmployeeId))
                    {
                        userLookupByGuid[user.EmployeeId] = user;
                    }
                }
                
                Console.WriteLine($"User lookup caches built: {userLookupByUsername.Count} usernames, {userLookupByGuid.Count} GUIDs");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error building user lookup caches: {ex.Message}");
                // Initialize empty dictionaries on error
                userLookupByUsername = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
                userLookupByGuid = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void InitializeComponent()
        {
            this.Text = "Futronic Attendance System";
            this.Size = new Size(1200, 750); // Increased width by 20% for better layout
            this.MinimumSize = new Size(1000, 600); // Prevent form from becoming too small
            this.WindowState = FormWindowState.Maximized; // Set window to fullscreen/maximized by default
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += MainForm_FormClosing;
            this.Load += MainForm_Load;

            // Create tab control
            tabControl = new TabControl();
            tabControl.Dock = DockStyle.Fill;
            tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
            this.Controls.Add(tabControl);

            // Create enrollment tab
            enrollmentTab = new TabPage("User Enrollment");
            enrollmentTab.Padding = new Padding(12);
            tabControl.TabPages.Add(enrollmentTab);

            // Create attendance tab
            attendanceTab = new TabPage("Fingerprint Attendance");
            tabControl.TabPages.Add(attendanceTab);

            // Create RFID attendance tab
            rfidAttendanceTab = new TabPage("RFID Attendance");
            tabControl.TabPages.Add(rfidAttendanceTab);

            // Create device management tab
            deviceManagementTab = new TabPage("Device Management");
            tabControl.TabPages.Add(deviceManagementTab);

            // Create fingerprint users tab
            fingerprintUsersTab = new TabPage("Fingerprint Users");
            tabControl.TabPages.Add(fingerprintUsersTab);

            // Create attendance scenarios configuration tab
            scenariosTab = new TabPage("Attendance Scenarios");
            tabControl.TabPages.Add(scenariosTab);

            InitializeEnrollmentTab();
            InitializeAttendanceTab();
            InitializeRfidAttendanceTab();
            InitializeDeviceManagementTab();
            InitializeFingerprintUsersTab();
            InitializeScenariosTab();
        }

        private void InitializeEnrollmentTab()
        {
            enrollmentTab.Controls.Clear();
            
            // Main layout: Split between fingerprint preview and user selection
            var mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 2;
            mainLayout.RowCount = 1;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350)); // Fingerprint area
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // User selection area
            enrollmentTab.Controls.Add(mainLayout);

            // LEFT PANEL: Fingerprint Preview and Controls
            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            mainLayout.Controls.Add(leftPanel, 0, 0);

            // Fingerprint preview
            pictureFingerprint = new PictureBox();
            pictureFingerprint.Dock = DockStyle.Top;
            pictureFingerprint.Height = 250;
            pictureFingerprint.BorderStyle = BorderStyle.FixedSingle;
            pictureFingerprint.SizeMode = PictureBoxSizeMode.Zoom;
            pictureFingerprint.BackColor = Color.LightGray;
            leftPanel.Controls.Add(pictureFingerprint);

            // Enrollment instruction area
            var instructionPanel = new Panel { Dock = DockStyle.Top, Height = 120, Padding = new Padding(4) };
            instructionPanel.BackColor = Color.FromArgb(245, 248, 255);
            
            lblEnrollStep = new Label { Dock = DockStyle.Top, Height = 40, TextAlign = ContentAlignment.MiddleLeft };
            lblEnrollStep.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            lblEnrollStep.ForeColor = Color.FromArgb(30, 60, 120);
            lblEnrollStep.Text = "Select a user from the table, then click 'Start Enrollment'";
            
            enrollProgressBar = new ProgressBar { Dock = DockStyle.Top, Height = 20, Minimum = 0, Maximum = 100, Value = 0, Style = ProgressBarStyle.Continuous };
            
            var tip = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            tip.Font = new Font("Segoe UI", 8, FontStyle.Regular);
            tip.ForeColor = Color.FromArgb(60, 90, 140);
            tip.Text = "Tips: Clean finger and sensor. Press firmly but not too hard.";
            
            instructionPanel.Controls.Add(tip);
            instructionPanel.Controls.Add(lblEnrollStep);
            instructionPanel.Controls.Add(enrollProgressBar);
            leftPanel.Controls.Add(instructionPanel);

            // Control buttons
            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.LeftToRight };
            
            btnEnroll = new Button();
            btnEnroll.Size = new Size(120, 35);
            btnEnroll.Text = "Start Enrollment";
            btnEnroll.BackColor = Color.LightBlue;
            btnEnroll.Enabled = false; // Disabled until user is selected
            btnEnroll.Click += BtnEnroll_Click;
            buttonPanel.Controls.Add(btnEnroll);

            btnStop = new Button();
            btnStop.Size = new Size(80, 35);
            btnStop.Text = "Stop";
            btnStop.BackColor = Color.LightCoral;
            btnStop.Enabled = false;
            btnStop.Click += BtnStop_Click;
            buttonPanel.Controls.Add(btnStop);

            leftPanel.Controls.Add(buttonPanel);

            // RIGHT PANEL: User Selection Table with proper spacing using TableLayoutPanel
            var rightPanel = new TableLayoutPanel();
            rightPanel.Dock = DockStyle.Fill;
            rightPanel.Padding = new Padding(8, 8, 8, 8);
            rightPanel.RowCount = 3;
            rightPanel.ColumnCount = 1;
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); // Search panel
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); // Selected user panel  
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // DataGridView
            mainLayout.Controls.Add(rightPanel, 1, 0);

            // Enhanced search controls with modern design
            var searchPanel = new Panel { Dock = DockStyle.Top, Height = 55, Padding = new Padding(8, 8, 8, 8) };
            searchPanel.BackColor = Color.FromArgb(248, 249, 250);
            
            var searchLabel = new Label { 
                Text = "🔍 Search:", 
                Location = new Point(0, 15), 
                Size = new Size(70, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 58, 64)
            };
            searchPanel.Controls.Add(searchLabel);
            
            txtSearchUsers = new TextBox { 
                Location = new Point(75, 12), 
                Size = new Size(250, 25),
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
            // Note: PlaceholderText not available in .NET Framework, using tooltip instead
            txtSearchUsers.TextChanged += TxtSearchUsers_TextChanged;
            var toolTip = new ToolTip();
            toolTip.SetToolTip(txtSearchUsers, "Type any keyword (name, ID, department, type...)");
            searchPanel.Controls.Add(txtSearchUsers);
            
            cmbSearchType = new ComboBox { 
                Location = new Point(335, 12), 
                Size = new Size(120, 25), 
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F)
            };
            cmbSearchType.Items.AddRange(new object[] { "All Fields", "Name Only", "User ID Only", "Department Only", "Type Only" });
            cmbSearchType.SelectedIndex = 0; // Default to "All Fields"
            cmbSearchType.SelectedIndexChanged += (s, e) => FilterAndRefreshTable();
            searchPanel.Controls.Add(cmbSearchType);
            
            btnSearchUsers = new Button { 
                Location = new Point(465, 12), 
                Size = new Size(70, 25), 
                Text = "🔍 Search",
                BackColor = Color.FromArgb(0, 123, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F)
            };
            btnSearchUsers.FlatAppearance.BorderSize = 0;
            btnSearchUsers.Click += BtnSearchUsers_Click;
            searchPanel.Controls.Add(btnSearchUsers);
            
            btnRefreshUserList = new Button { 
                Location = new Point(545, 12), 
                Size = new Size(70, 25), 
                Text = "🔄 Refresh",
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F)
            };
            btnRefreshUserList.FlatAppearance.BorderSize = 0;
            btnRefreshUserList.Click += BtnRefreshUserList_Click;
            searchPanel.Controls.Add(btnRefreshUserList);
            
            // Clear search button
            var btnClearSearch = new Button { 
                Location = new Point(625, 12), 
                Size = new Size(25, 25), 
                Text = "✕",
                BackColor = Color.FromArgb(220, 53, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold)
            };
            btnClearSearch.FlatAppearance.BorderSize = 0;
            btnClearSearch.Click += (s, e) => {
                txtSearchUsers.Text = "";
                cmbSearchType.SelectedIndex = 0;
            };
            searchPanel.Controls.Add(btnClearSearch);
            
            rightPanel.Controls.Add(searchPanel, 0, 0);

            // Selected user info with proper spacing
            var selectedUserPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 8) };
            selectedUserPanel.BackColor = Color.FromArgb(240, 248, 255);
            selectedUserPanel.BorderStyle = BorderStyle.FixedSingle;
            
            lblSelectedUser = new Label { Text = "No user selected", Location = new Point(10, 10), Size = new Size(400, 20), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            selectedUserPanel.Controls.Add(lblSelectedUser);
            
            btnClearSelection = new Button { Location = new Point(420, 8), Size = new Size(80, 25), Text = "Clear" };
            btnClearSelection.Click += BtnClearSelection_Click;
            selectedUserPanel.Controls.Add(btnClearSelection);
            
            rightPanel.Controls.Add(selectedUserPanel, 0, 1);

            // Create a container panel for the DataGridView to prevent overlap
            var tableContainerPanel = new Panel();
            tableContainerPanel.Dock = DockStyle.Fill;
            tableContainerPanel.Padding = new Padding(0, 5, 0, 0); // Top padding to prevent overlap
            
            // Users DataGridView with improved design and proper spacing
            dgvUsers = new DataGridView();
            dgvUsers.Dock = DockStyle.Fill;
            dgvUsers.AllowUserToAddRows = false;
            dgvUsers.AllowUserToDeleteRows = false;
            dgvUsers.ReadOnly = true;
            dgvUsers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvUsers.MultiSelect = false;
            dgvUsers.AutoGenerateColumns = false;
            dgvUsers.SelectionChanged += DgvUsers_SelectionChanged;
            dgvUsers.CellDoubleClick += DgvUsers_CellDoubleClick;
            
            // Enhanced styling for professional look
            dgvUsers.BackgroundColor = Color.White;
            dgvUsers.BorderStyle = BorderStyle.None;
            dgvUsers.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvUsers.GridColor = Color.FromArgb(230, 230, 230);
            dgvUsers.RowHeadersVisible = false;
            dgvUsers.EnableHeadersVisualStyles = false;
            
            // Row and column styling
            dgvUsers.DefaultCellStyle.SelectionBackColor = Color.FromArgb(51, 122, 183);
            dgvUsers.DefaultCellStyle.SelectionForeColor = Color.White;
            dgvUsers.DefaultCellStyle.BackColor = Color.White;
            dgvUsers.DefaultCellStyle.ForeColor = Color.Black;
            dgvUsers.DefaultCellStyle.Font = new Font("Segoe UI", 9F);
            dgvUsers.DefaultCellStyle.Padding = new Padding(5, 3, 5, 3);
            
            // Header styling with proper height and spacing
            dgvUsers.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(52, 58, 64);
            dgvUsers.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvUsers.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            dgvUsers.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 8, 5, 8);
            dgvUsers.ColumnHeadersHeight = 40; // Increased height for better visibility
            dgvUsers.RowTemplate.Height = 32;
            
            // Alternating row colors
            dgvUsers.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 250);
            
            // Add improved columns with better layout
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { 
                Name = "FirstName", HeaderText = "First Name", Width = 110,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleLeft }
            });
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { 
                Name = "LastName", HeaderText = "Last Name", Width = 110,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleLeft }
            });
            // Email column removed - not available in PDF parse data
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { 
                Name = "UserType", HeaderText = "Type", Width = 80,
                DefaultCellStyle = new DataGridViewCellStyle { 
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(240, 248, 255)
                }
            });
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { 
                Name = "UserID", HeaderText = "User ID", Width = 250,
                DefaultCellStyle = new DataGridViewCellStyle { 
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Font = new Font("Consolas", 8F),
                    ForeColor = Color.FromArgb(108, 117, 125)
                }
            });
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { 
                Name = "Department", HeaderText = "Department", Width = 130,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleLeft }
            });
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { 
                Name = "Status", HeaderText = "Status", Width = 70,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { 
                Name = "HasFingerprint", HeaderText = "Enrolled", Width = 80,
                DefaultCellStyle = new DataGridViewCellStyle { 
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 8F, FontStyle.Bold)
                }
            });
            
            // Add DataGridView to container and container to right panel
            tableContainerPanel.Controls.Add(dgvUsers);
            rightPanel.Controls.Add(tableContainerPanel, 0, 2);

            // Initialize hidden form fields (for compatibility with existing enrollment logic)
            InitializeHiddenFormFields();
            
            // Initialize filtered users list
            filteredUsers = new List<User>();
            
            // Load initial user data
            LoadUsersIntoTable();
        }

        // NEW: Initialize hidden form fields for compatibility with existing enrollment logic
        private void InitializeHiddenFormFields()
        {
            // Create hidden form fields that will be populated when a user is selected
            txtFirstName = new TextBox { Visible = false };
            txtLastName = new TextBox { Visible = false };
            txtEmail = new TextBox { Visible = false };
            txtPassword = new TextBox { Visible = false, UseSystemPasswordChar = true };
            cmbUserType = new ComboBox { Visible = false, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbUserType.Items.AddRange(new object[] { "student", "instructor", "admin" });
            cmbStatus = new ComboBox { Visible = false, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbStatus.Items.AddRange(new object[] { "Active", "Inactive" });
            txtStudentId = new TextBox { Visible = false };
            txtFacultyId = new TextBox { Visible = false };
            cmbYearLevel = new ComboBox { Visible = false, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbYearLevel.Items.AddRange(new object[] { "", "1", "2", "3", "4", "5" });
            txtDepartment = new TextBox { Visible = false };
        }

        // NEW: Load users into the DataGridView
        private void LoadUsersIntoTable()
        {
            try
            {
                if (cloudUsers == null || cloudUsers.Count == 0)
                {
                    SetStatusText("No users available. Please check database connection.");
                    return;
                }

                // Filter users based on search criteria
                filteredUsers = FilterUsers(cloudUsers, txtSearchUsers?.Text ?? "", cmbSearchType?.SelectedItem?.ToString() ?? "All");
                
                // Clear existing rows
                dgvUsers.Rows.Clear();
                
                // Add users to DataGridView with improved data display
                foreach (var user in filteredUsers)
                {
                    var row = dgvUsers.Rows.Add();
                    dgvUsers.Rows[row].Cells["FirstName"].Value = user.FirstName ?? "";
                    dgvUsers.Rows[row].Cells["LastName"].Value = user.LastName ?? "";
                    // Email column removed - not available in PDF parse data
                    
                    // Format user type with proper casing and colors
                    var userType = user.UserType ?? "";
                    dgvUsers.Rows[row].Cells["UserType"].Value = userType.ToUpper();
                    if (userType.ToLower() == "instructor")
                    {
                        dgvUsers.Rows[row].Cells["UserType"].Style.BackColor = Color.FromArgb(255, 243, 205);
                        dgvUsers.Rows[row].Cells["UserType"].Style.ForeColor = Color.FromArgb(133, 100, 4);
                    }
                    else if (userType.ToLower() == "student")
                    {
                        dgvUsers.Rows[row].Cells["UserType"].Style.BackColor = Color.FromArgb(208, 244, 234);
                        dgvUsers.Rows[row].Cells["UserType"].Style.ForeColor = Color.FromArgb(22, 101, 52);
                    }
                    
                    // Show the User ID (GUID) - truncated for display
                    var userId = user.EmployeeId ?? "";
                    dgvUsers.Rows[row].Cells["UserID"].Value = userId.Length > 8 ? userId.Substring(0, 8) + "..." : userId;
                    
                    dgvUsers.Rows[row].Cells["Department"].Value = user.Department ?? "";
                    
                    // Status with color coding
                    var status = user.IsActive ? "Active" : "Inactive";
                    dgvUsers.Rows[row].Cells["Status"].Value = status;
                    if (user.IsActive)
                    {
                        dgvUsers.Rows[row].Cells["Status"].Style.BackColor = Color.FromArgb(212, 237, 218);
                        dgvUsers.Rows[row].Cells["Status"].Style.ForeColor = Color.FromArgb(21, 87, 36);
                    }
                    else
                    {
                        dgvUsers.Rows[row].Cells["Status"].Style.BackColor = Color.FromArgb(248, 215, 218);
                        dgvUsers.Rows[row].Cells["Status"].Style.ForeColor = Color.FromArgb(114, 28, 36);
                    }
                    
                    // Fingerprint enrollment status
                    var hasFingerprint = user.FingerprintTemplate != null && user.FingerprintTemplate.Length > 0;
                    dgvUsers.Rows[row].Cells["HasFingerprint"].Value = hasFingerprint ? "✓ YES" : "✗ NO";
                    if (hasFingerprint)
                    {
                        dgvUsers.Rows[row].Cells["HasFingerprint"].Style.BackColor = Color.FromArgb(212, 237, 218);
                        dgvUsers.Rows[row].Cells["HasFingerprint"].Style.ForeColor = Color.FromArgb(21, 87, 36);
                    }
                    else
                    {
                        dgvUsers.Rows[row].Cells["HasFingerprint"].Style.BackColor = Color.FromArgb(255, 243, 205);
                        dgvUsers.Rows[row].Cells["HasFingerprint"].Style.ForeColor = Color.FromArgb(133, 100, 4);
                    }
                    
                    // Store the user object in the row tag for easy access
                    dgvUsers.Rows[row].Tag = user;
                }
                
                SetStatusText($"Loaded {filteredUsers.Count} users from database");
            }
            catch (Exception ex)
            {
                SetStatusText($"Error loading users: {ex.Message}");
                Console.WriteLine($"Error loading users: {ex}");
            }
        }

        // Enhanced filter with comprehensive keyword search across all fields
        private List<User> FilterUsers(List<User> users, string searchText, string searchType)
        {
            if (string.IsNullOrEmpty(searchText))
                return users;

            // Convert search text to lowercase for case-insensitive matching
            var searchLower = searchText.ToLower();

            var filtered = users.Where(user => 
            {
                switch (searchType)
                {
                    case "Name Only":
                        return (user.FirstName?.ToLower().Contains(searchLower) ?? false) ||
                               (user.LastName?.ToLower().Contains(searchLower) ?? false) ||
                               ($"{user.FirstName} {user.LastName}".ToLower().Contains(searchLower));

                    // Email search removed - not available in PDF parse data

                    case "User ID Only":
                        return user.EmployeeId?.ToLower().Contains(searchLower) ?? false;

                    case "Department Only":
                        return user.Department?.ToLower().Contains(searchLower) ?? false;

                    case "Type Only":
                        return user.UserType?.ToLower().Contains(searchLower) ?? false;

                    default: // "All Fields" - search across everything (excluding email)
                        return (user.FirstName?.ToLower().Contains(searchLower) ?? false) ||
                               (user.LastName?.ToLower().Contains(searchLower) ?? false) ||
                               ($"{user.FirstName} {user.LastName}".ToLower().Contains(searchLower)) ||
                               (user.EmployeeId?.ToLower().Contains(searchLower) ?? false) ||
                               (user.Department?.ToLower().Contains(searchLower) ?? false) ||
                               (user.UserType?.ToLower().Contains(searchLower) ?? false) ||
                               (user.Username?.ToLower().Contains(searchLower) ?? false);
                }
            }).ToList();

            return filtered;
        }

        // Helper method to filter and refresh the table
        private void FilterAndRefreshTable()
        {
            try
            {
                if (cloudUsers != null && dgvUsers != null)
                {
                    LoadUsersIntoTable();
                }
            }
            catch (Exception ex)
            {
                SetStatusText($"Error filtering users: {ex.Message}");
            }
        }

        // NEW: Populate hidden form fields with selected user data
        private void PopulateFormFields(User user)
        {
            if (user == null) return;

            txtFirstName.Text = user.FirstName ?? "";
            txtLastName.Text = user.LastName ?? "";
            // Email field removed - not available in PDF parse data
            txtPassword.Text = ""; // Don't populate password for security
            txtStudentId.Text = user.EmployeeId ?? ""; // Using EmployeeId as StudentId
            txtFacultyId.Text = user.EmployeeId ?? ""; // Using EmployeeId as FacultyId
            txtDepartment.Text = user.Department ?? "";
            
            // Set user type
            if (!string.IsNullOrEmpty(user.UserType))
            {
                var userTypeIndex = cmbUserType.Items.IndexOf(user.UserType);
                if (userTypeIndex >= 0)
                    cmbUserType.SelectedIndex = userTypeIndex;
            }
            
            // Set status based on IsActive
            var statusText = user.IsActive ? "Active" : "Inactive";
            var statusIndex = cmbStatus.Items.IndexOf(statusText);
            if (statusIndex >= 0)
                cmbStatus.SelectedIndex = statusIndex;
            
            // Year level not available in current User model, leave empty
            cmbYearLevel.SelectedIndex = 0;
        }

        // NEW: Event handlers for the table interface
        private void DgvUsers_SelectionChanged(object sender, EventArgs e)
        {
            if (dgvUsers.SelectedRows.Count > 0)
            {
                var selectedRow = dgvUsers.SelectedRows[0];
                selectedUser = selectedRow.Tag as User;
                
                if (selectedUser != null)
                {
                    isUserSelected = true;
                    PopulateFormFields(selectedUser);
                    
                    // Update selected user display
                    lblSelectedUser.Text = $"Selected: {selectedUser.FirstName} {selectedUser.LastName} ({selectedUser.UserType})";
                    lblSelectedUser.ForeColor = Color.DarkGreen;
                    
                    // Enable enrollment button
                    btnEnroll.Enabled = true;
                    btnEnroll.Text = $"Start Enrollment for {selectedUser.FirstName}";
                }
            }
            else
            {
                ClearSelection();
            }
        }

        private void DgvUsers_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                // Double-click to select and start enrollment
                DgvUsers_SelectionChanged(sender, e);
                if (isUserSelected)
                {
                    BtnEnroll_Click(sender, e);
                }
            }
        }

        private void TxtSearchUsers_TextChanged(object sender, EventArgs e)
        {
            // Auto-search as user types (with debouncing)
            if (searchTimer != null)
            {
                searchTimer.Stop();
            }
            
            searchTimer = new System.Windows.Forms.Timer();
            searchTimer.Interval = 500; // 500ms delay
            searchTimer.Tick += (s, args) =>
            {
                searchTimer.Stop();
                LoadUsersIntoTable();
            };
            searchTimer.Start();
        }

        private void BtnSearchUsers_Click(object sender, EventArgs e)
        {
            LoadUsersIntoTable();
        }

        private void BtnRefreshUserList_Click(object sender, EventArgs e)
        {
            // Clear search
            txtSearchUsers.Text = "";
            cmbSearchType.SelectedIndex = 0;
            
            // Refresh from database
            SyncUsersFromCloud();
            LoadUsersIntoTable();
        }

        private void BtnClearSelection_Click(object sender, EventArgs e)
        {
            ClearSelection();
        }

        private void ClearSelection()
        {
            selectedUser = null;
            isUserSelected = false;
            dgvUsers.ClearSelection();
            
            // Clear form fields
            txtFirstName.Text = "";
            txtLastName.Text = "";
            // Email field removed - not available in PDF parse data
            txtPassword.Text = "";
            txtStudentId.Text = "";
            txtFacultyId.Text = "";
            txtDepartment.Text = "";
            cmbUserType.SelectedIndex = 0;
            cmbStatus.SelectedIndex = 0;
            cmbYearLevel.SelectedIndex = 0;
            
            // Update UI
            lblSelectedUser.Text = "No user selected";
            lblSelectedUser.ForeColor = Color.Black;
            btnEnroll.Enabled = false;
            btnEnroll.Text = "Start Enrollment";
        }

        // Timer for search debouncing
        private System.Windows.Forms.Timer searchTimer;

        private void InitializeAttendanceTab()
        {
            // Identify button
            btnIdentify = new Button();
            btnIdentify.Location = new Point(20, 20);
            btnIdentify.Size = new Size(150, 50);
            btnIdentify.Text = "Start Attendance";
            btnIdentify.BackColor = Color.LightGreen;
            btnIdentify.Click += BtnIdentify_Click;
            attendanceTab.Controls.Add(btnIdentify);

            // Hide manual start when always-on is enabled
            if (alwaysOnAttendance)
            {
                btnIdentify.Visible = false;
            }

            // Export button
            btnExportAttendance = new Button();
            btnExportAttendance.Location = new Point(180, 20);
            btnExportAttendance.Size = new Size(120, 50);
            btnExportAttendance.Text = "Export to CSV";
            btnExportAttendance.BackColor = Color.LightYellow;
            btnExportAttendance.Click += BtnExportAttendance_Click;
            attendanceTab.Controls.Add(btnExportAttendance);

            // Location and Room controls
            InitializeLocationRoomControls();

            // Live clock display
            var clockPanel = new Panel();
            clockPanel.Location = new Point(20, 80);
            clockPanel.Size = new Size(300, 80); // Increased height to prevent text cutoff
            clockPanel.BackColor = Color.FromArgb(240, 248, 255);
            clockPanel.BorderStyle = BorderStyle.FixedSingle;
            attendanceTab.Controls.Add(clockPanel);
            
            lblLiveTime = new Label();
            lblLiveTime.Location = new Point(10, 10);
            lblLiveTime.Size = new Size(280, 25);
            lblLiveTime.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            lblLiveTime.ForeColor = Color.FromArgb(30, 60, 120);
            lblLiveTime.Text = "Time: Loading...";
            lblLiveTime.AutoSize = true;
            lblLiveTime.AutoEllipsis = true;
            clockPanel.Controls.Add(lblLiveTime);
            
            lblLiveDay = new Label();
            lblLiveDay.Location = new Point(10, 40);
            lblLiveDay.Size = new Size(280, 25);
            lblLiveDay.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            lblLiveDay.ForeColor = Color.FromArgb(60, 90, 140);
            lblLiveDay.Text = "Day: Loading...";
            lblLiveDay.AutoSize = true;
            lblLiveDay.AutoEllipsis = true;
            clockPanel.Controls.Add(lblLiveDay);
            
            // Initialize clock timer
            clockTimer = new System.Windows.Forms.Timer();
            clockTimer.Interval = 1000; // Update every second
            clockTimer.Tick += ClockTimer_Tick;
            clockTimer.Start();

            // Status text - improved layout with better sizing
            txtStatus = new TextBox();
            // Place to the right of the timer box with better positioning
            txtStatus.Location = new Point(330, 80);
            txtStatus.Size = new Size(650, 100); // Increased height to match clock panel
            txtStatus.Multiline = true;
            txtStatus.ReadOnly = true;
            txtStatus.BackColor = Color.White;
            txtStatus.ScrollBars = ScrollBars.Vertical;
            txtStatus.WordWrap = true;
            txtStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtStatus.Text = "Always-on attendance system ready. Scan your fingerprint twice to confirm attendance.";
            attendanceTab.Controls.Add(txtStatus);

            // NEW: Session state display (original fixed placement)
            lblSessionState = new Label();
            lblSessionState.Location = new Point(20, 190);
            lblSessionState.Size = new Size(400, 30);
            lblSessionState.Font = new Font(lblSessionState.Font, FontStyle.Bold);
            lblSessionState.ForeColor = Color.DarkBlue;
            lblSessionState.Text = "Session State: Inactive";
            attendanceTab.Controls.Add(lblSessionState);

            lblSessionInfo = new Label();
            lblSessionInfo.Location = new Point(20, 220);
            lblSessionInfo.Size = new Size(600, 20);
            lblSessionInfo.Text = "Waiting for instructor to start attendance session...";
            attendanceTab.Controls.Add(lblSessionInfo);

            // Force end session button (original position)
            btnForceEndSession = new Button();
            btnForceEndSession.Location = new Point(450, 190);
            btnForceEndSession.Size = new Size(120, 30);
            btnForceEndSession.Text = "Force End Session";
            btnForceEndSession.BackColor = Color.LightCoral;
            btnForceEndSession.Visible = false;
            btnForceEndSession.Click += BtnForceEndSession_Click;
            attendanceTab.Controls.Add(btnForceEndSession);

            // Modern Attendance grid (DataGridView) with responsive layout
            var attendancePanel = new Panel();
            attendancePanel.Location = new Point(20, 250);
            attendancePanel.Size = new Size(950, 300);
            attendancePanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            attendancePanel.Padding = new Padding(0);

            dgvAttendance = new DataGridView();
            dgvAttendance.Dock = DockStyle.Fill;
            dgvAttendance.AllowUserToAddRows = false;
            dgvAttendance.AllowUserToDeleteRows = false;
            dgvAttendance.ReadOnly = true;
            dgvAttendance.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvAttendance.MultiSelect = false;
            dgvAttendance.AutoGenerateColumns = false;
            dgvAttendance.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None; // explicit widths for clarity
            dgvAttendance.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            dgvAttendance.RowTemplate.Height = 22; // ~30% smaller
            dgvAttendance.RowHeadersVisible = false;
            dgvAttendance.BackgroundColor = Color.White;
            dgvAttendance.BorderStyle = BorderStyle.FixedSingle;
            dgvAttendance.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvAttendance.GridColor = Color.FromArgb(230, 230, 230);
            dgvAttendance.EnableHeadersVisualStyles = false;
            dgvAttendance.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(52, 58, 64);
            dgvAttendance.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvAttendance.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8F, FontStyle.Bold); // smaller
            dgvAttendance.ColumnHeadersHeight = 28;
            dgvAttendance.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgvAttendance.DefaultCellStyle.Font = new Font("Segoe UI", 8F);
            dgvAttendance.DefaultCellStyle.WrapMode = DataGridViewTriState.False; // no wrap for compact rows
            dgvAttendance.DefaultCellStyle.Padding = new Padding(3, 2, 3, 2);
            dgvAttendance.ScrollBars = ScrollBars.Both;

            // Explicit widths so headers are fully visible; last column stretches
            var colTime = new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Time", Width = 160, AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm:ss" } };
            var colUser = new DataGridViewTextBoxColumn { Name = "User", HeaderText = "User", Width = 220, AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells };
            var colAction = new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Action", Width = 260, AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells };
            var colStatus = new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", MinimumWidth = 260, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };

            dgvAttendance.Columns.Add(colTime);
            dgvAttendance.Columns.Add(colUser);
            dgvAttendance.Columns.Add(colAction);
            dgvAttendance.Columns.Add(colStatus);

            attendancePanel.Controls.Add(dgvAttendance);
            attendanceTab.Controls.Add(attendancePanel);
        }

        private void InitializeRfidAttendanceTab()
        {
            rfidAttendanceTab.Controls.Clear();
            
            // Title
            var lblTitle = new Label();
            lblTitle.Location = new Point(20, 20);
            lblTitle.Size = new Size(400, 25);
            lblTitle.Text = "RFID Attendance Tracking";
            lblTitle.Font = new Font(lblTitle.Font, FontStyle.Bold);
            lblTitle.ForeColor = Color.DarkBlue;
            rfidAttendanceTab.Controls.Add(lblTitle);

            // RFID Session State Display
            lblRfidSessionState = new Label();
            lblRfidSessionState.Location = new Point(20, 50);
            lblRfidSessionState.Size = new Size(300, 20);
            lblRfidSessionState.Text = "Session State: Inactive";
            lblRfidSessionState.ForeColor = Color.DarkRed;
            lblRfidSessionState.Font = new Font(lblRfidSessionState.Font, FontStyle.Bold);
            rfidAttendanceTab.Controls.Add(lblRfidSessionState);

            // RFID Session Info
            lblRfidSessionInfo = new Label();
            lblRfidSessionInfo.Location = new Point(20, 75);
            lblRfidSessionInfo.Size = new Size(600, 20);
            lblRfidSessionInfo.Text = "No active session";
            lblRfidSessionInfo.ForeColor = Color.Gray;
            rfidAttendanceTab.Controls.Add(lblRfidSessionInfo);

            // RFID Control Buttons
            btnStartRfidAttendance = new Button();
            btnStartRfidAttendance.Location = new Point(20, 110);
            btnStartRfidAttendance.Size = new Size(120, 30);
            btnStartRfidAttendance.Text = "Start RFID Attendance";
            btnStartRfidAttendance.BackColor = Color.LightGreen;
            btnStartRfidAttendance.Click += BtnStartRfidAttendance_Click;
            rfidAttendanceTab.Controls.Add(btnStartRfidAttendance);

            btnStopRfidAttendance = new Button();
            btnStopRfidAttendance.Location = new Point(150, 110);
            btnStopRfidAttendance.Size = new Size(120, 30);
            btnStopRfidAttendance.Text = "Stop RFID Attendance";
            btnStopRfidAttendance.BackColor = Color.LightCoral;
            btnStopRfidAttendance.Enabled = false;
            btnStopRfidAttendance.Click += BtnStopRfidAttendance_Click;
            rfidAttendanceTab.Controls.Add(btnStopRfidAttendance);

            // RFID Status Display
            txtRfidStatus = new TextBox();
            txtRfidStatus.Location = new Point(20, 150);
            txtRfidStatus.Size = new Size(400, 60);
            txtRfidStatus.Multiline = true;
            txtRfidStatus.ScrollBars = ScrollBars.Vertical;
            txtRfidStatus.ReadOnly = true;
            txtRfidStatus.Text = "RFID scanner ready. Connect RFID reader and start attendance session.";
            txtRfidStatus.BackColor = Color.LightGray;
            rfidAttendanceTab.Controls.Add(txtRfidStatus);

            // RFID Location and Room Controls
            InitializeRfidLocationRoomControls();

            // RFID Attendance Grid
            var rfidAttendancePanel = new Panel();
            rfidAttendancePanel.Location = new Point(20, 250);
            rfidAttendancePanel.Size = new Size(950, 300);
            rfidAttendancePanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            rfidAttendancePanel.Padding = new Padding(0);

            dgvRfidAttendance = new DataGridView();
            dgvRfidAttendance.Dock = DockStyle.Fill;
            dgvRfidAttendance.AllowUserToAddRows = false;
            dgvRfidAttendance.AllowUserToDeleteRows = false;
            dgvRfidAttendance.ReadOnly = true;
            dgvRfidAttendance.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvRfidAttendance.MultiSelect = false;
            dgvRfidAttendance.AutoGenerateColumns = false;
            dgvRfidAttendance.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            dgvRfidAttendance.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            dgvRfidAttendance.RowTemplate.Height = 22;
            dgvRfidAttendance.RowHeadersVisible = false;
            dgvRfidAttendance.BackgroundColor = Color.White;
            dgvRfidAttendance.BorderStyle = BorderStyle.FixedSingle;
            dgvRfidAttendance.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvRfidAttendance.GridColor = Color.FromArgb(230, 230, 230);
            dgvRfidAttendance.EnableHeadersVisualStyles = false;
            dgvRfidAttendance.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(52, 58, 64);
            dgvRfidAttendance.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvRfidAttendance.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            dgvRfidAttendance.ColumnHeadersHeight = 28;
            dgvRfidAttendance.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgvRfidAttendance.DefaultCellStyle.Font = new Font("Segoe UI", 8F);
            dgvRfidAttendance.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            dgvRfidAttendance.DefaultCellStyle.Padding = new Padding(3, 2, 3, 2);
            dgvRfidAttendance.ScrollBars = ScrollBars.Both;

            // RFID Attendance Columns
            var rfidColTime = new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Time", Width = 160, AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm:ss" } };
            var rfidColUser = new DataGridViewTextBoxColumn { Name = "User", HeaderText = "User", Width = 220, AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells };
            var rfidColRfid = new DataGridViewTextBoxColumn { Name = "RFID", HeaderText = "RFID", Width = 120, AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells };
            var rfidColAction = new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Action", Width = 200, AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells };
            var rfidColStatus = new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", MinimumWidth = 200, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };

            dgvRfidAttendance.Columns.Add(rfidColTime);
            dgvRfidAttendance.Columns.Add(rfidColUser);
            dgvRfidAttendance.Columns.Add(rfidColRfid);
            dgvRfidAttendance.Columns.Add(rfidColAction);
            dgvRfidAttendance.Columns.Add(rfidColStatus);

            rfidAttendancePanel.Controls.Add(dgvRfidAttendance);
            rfidAttendanceTab.Controls.Add(rfidAttendancePanel);

            // Export button
            btnExportRfidAttendance = new Button();
            btnExportRfidAttendance.Location = new Point(20, 560);
            btnExportRfidAttendance.Size = new Size(120, 30);
            btnExportRfidAttendance.Text = "Export RFID Data";
            btnExportRfidAttendance.BackColor = Color.LightBlue;
            btnExportRfidAttendance.Click += BtnExportRfidAttendance_Click;
            rfidAttendanceTab.Controls.Add(btnExportRfidAttendance);

            // Force end session button
            btnForceEndRfidSession = new Button();
            btnForceEndRfidSession.Location = new Point(450, 190);
            btnForceEndRfidSession.Size = new Size(120, 30);
            btnForceEndRfidSession.Text = "Force End Session";
            btnForceEndRfidSession.BackColor = Color.LightCoral;
            btnForceEndRfidSession.Visible = false;
            btnForceEndRfidSession.Click += BtnForceEndRfidSession_Click;
            rfidAttendanceTab.Controls.Add(btnForceEndRfidSession);
            
            // Auto-start RFID service (always on)
            try
            {
                StartRfidService();
                m_bRfidAttendanceActive = true;
                currentRfidSessionState = AttendanceSessionState.WaitingForInstructor;
                UpdateRfidSessionStateDisplay();
                
                // Update UI to reflect always-on state
                btnStartRfidAttendance.Enabled = false;
                btnStopRfidAttendance.Enabled = true;
                
                SetRfidStatusText("RFID scanner is always active. Ready for instructor scan...");
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error starting RFID scanner: {ex.Message}");
            }
        }

        private void InitializeRfidLocationRoomControls()
        {
            // Create a TableLayoutPanel for better layout management (RFID version)
            var rfidHeaderPanel = new TableLayoutPanel();
            rfidHeaderPanel.Location = new Point(320, 20);
            rfidHeaderPanel.Size = new Size(650, 80);
            rfidHeaderPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            rfidHeaderPanel.ColumnCount = 5;
            rfidHeaderPanel.RowCount = 2;
            rfidHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Location label
            rfidHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F)); // Location dropdown
            rfidHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Room label
            rfidHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // Room dropdown takes remaining space
            rfidHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F)); // Change button (fixed)
            rfidHeaderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F)); // Current Room row
            rfidHeaderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // Location/Room row
            rfidHeaderPanel.Padding = new Padding(5);
            rfidAttendanceTab.Controls.Add(rfidHeaderPanel);

            // Current room label for RFID - spans full width
            lblRfidCurrentRoom = new Label();
            lblRfidCurrentRoom.AutoSize = true;
            lblRfidCurrentRoom.AutoEllipsis = true;
            lblRfidCurrentRoom.Text = "Current Room: Loading...";
            lblRfidCurrentRoom.ForeColor = Color.DarkBlue;
            lblRfidCurrentRoom.Font = new Font(lblRfidCurrentRoom.Font, FontStyle.Bold);
            lblRfidCurrentRoom.Dock = DockStyle.Fill;
            rfidHeaderPanel.Controls.Add(lblRfidCurrentRoom, 0, 0);
            rfidHeaderPanel.SetColumnSpan(lblRfidCurrentRoom, 5);

            // Add tooltip for current room
            var rfidToolTip = new ToolTip();
            rfidToolTip.SetToolTip(lblRfidCurrentRoom, "Current room assignment");

            // Location selection for RFID
            var lblRfidLocation = new Label();
            lblRfidLocation.AutoSize = true;
            lblRfidLocation.Text = "Location:";
            lblRfidLocation.TextAlign = ContentAlignment.MiddleLeft;
            lblRfidLocation.Dock = DockStyle.None;
            lblRfidLocation.Anchor = AnchorStyles.Left;
            lblRfidLocation.Margin = new Padding(0, 0, 6, 0);
            rfidHeaderPanel.Controls.Add(lblRfidLocation, 0, 1);

            cmbRfidLocation = new ComboBox();
            cmbRfidLocation.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRfidLocation.Items.AddRange(new object[] { "inside", "outside" });
            cmbRfidLocation.SelectedIndex = 0;
            cmbRfidLocation.SelectedIndexChanged += CmbRfidLocation_SelectedIndexChanged;
            cmbRfidLocation.Dock = DockStyle.Fill;
            cmbRfidLocation.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            rfidHeaderPanel.Controls.Add(cmbRfidLocation, 1, 1);

            // Room selection for RFID
            var lblRfidRoom = new Label();
            lblRfidRoom.AutoSize = true;
            lblRfidRoom.Text = "Room:";
            lblRfidRoom.TextAlign = ContentAlignment.MiddleLeft;
            lblRfidRoom.Dock = DockStyle.None;
            lblRfidRoom.Anchor = AnchorStyles.Left;
            lblRfidRoom.Margin = new Padding(0, 0, 6, 0);
            rfidHeaderPanel.Controls.Add(lblRfidRoom, 2, 1);

            cmbRfidRoom = new ComboBox();
            cmbRfidRoom.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRfidRoom.Dock = DockStyle.Fill;
            cmbRfidRoom.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            cmbRfidRoom.MaximumSize = new Size(420, 0);
            rfidHeaderPanel.Controls.Add(cmbRfidRoom, 3, 1);

            // Change room button for RFID
            btnRfidChangeRoom = new Button();
            btnRfidChangeRoom.Text = "Change";
            btnRfidChangeRoom.BackColor = Color.LightCyan;
            btnRfidChangeRoom.Click += BtnRfidChangeRoom_Click;
            btnRfidChangeRoom.Dock = DockStyle.None;
            btnRfidChangeRoom.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnRfidChangeRoom.AutoSize = false;
            btnRfidChangeRoom.Width = 90; // keep width fixed
            btnRfidChangeRoom.Margin = new Padding(0);
            rfidHeaderPanel.Controls.Add(btnRfidChangeRoom, 4, 1);

            // Set dropdown widths after controls are added
            SetRfidComboBoxDropDownWidths();
        }

        private void InitializeLocationRoomControls()
        {
            // Create a TableLayoutPanel for better layout management
            var headerPanel = new TableLayoutPanel();
            headerPanel.Location = new Point(320, 20);
            headerPanel.Size = new Size(650, 110);
            headerPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            headerPanel.ColumnCount = 4;
            headerPanel.RowCount = 3;
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Location label
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F)); // Location dropdown
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Room label
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // Room dropdown takes remaining space
            headerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F)); // Current Room row
            headerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // Location/Room row
            headerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // Change button row
            headerPanel.Padding = new Padding(5);
            attendanceTab.Controls.Add(headerPanel);

            // Current room label - spans full width
            lblCurrentRoom = new Label();
            lblCurrentRoom.AutoSize = true;
            lblCurrentRoom.AutoEllipsis = true;
            lblCurrentRoom.Text = "Current Room: Loading...";
            lblCurrentRoom.ForeColor = Color.DarkBlue;
            lblCurrentRoom.Font = new Font(lblCurrentRoom.Font, FontStyle.Bold);
            lblCurrentRoom.Dock = DockStyle.Fill;
            headerPanel.Controls.Add(lblCurrentRoom, 0, 0);
            headerPanel.SetColumnSpan(lblCurrentRoom, 5);

            // Add tooltip for current room
            var toolTip = new ToolTip();
            toolTip.SetToolTip(lblCurrentRoom, "Current room assignment");

            // Location selection
            var lblLocation = new Label();
            lblLocation.AutoSize = true;
            lblLocation.Text = "Location:";
            lblLocation.TextAlign = ContentAlignment.MiddleLeft;
            lblLocation.Dock = DockStyle.None;
            lblLocation.Anchor = AnchorStyles.Left;
            lblLocation.Margin = new Padding(0, 0, 6, 0);
            headerPanel.Controls.Add(lblLocation, 0, 1);

            cmbLocation = new ComboBox();
            cmbLocation.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbLocation.Items.AddRange(new object[] { "inside", "outside" });
            cmbLocation.SelectedIndex = 0;
            cmbLocation.SelectedIndexChanged += CmbLocation_SelectedIndexChanged;
            cmbLocation.Dock = DockStyle.Fill; // width controlled by absolute column width
            cmbLocation.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            cmbLocation.Margin = new Padding(0, 0, 12, 0);
            headerPanel.Controls.Add(cmbLocation, 1, 1);

            // Room selection
            var lblRoom = new Label();
            lblRoom.AutoSize = true;
            lblRoom.Text = "Room:";
            lblRoom.TextAlign = ContentAlignment.MiddleLeft;
            lblRoom.Dock = DockStyle.None;
            lblRoom.Anchor = AnchorStyles.Left;
            lblRoom.Margin = new Padding(0, 0, 6, 0);
            headerPanel.Controls.Add(lblRoom, 2, 1);

            cmbRoom = new ComboBox();
            cmbRoom.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRoom.Dock = DockStyle.Fill; // fixed-width column keeps it reasonable
            cmbRoom.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            cmbRoom.Margin = new Padding(0, 0, 12, 0);
            cmbRoom.MaximumSize = new Size(420, 0); // cap visual width to column width
            headerPanel.Controls.Add(cmbRoom, 3, 1);

            // Change room button
            btnChangeRoom = new Button();
            btnChangeRoom.Text = "Change";
            btnChangeRoom.BackColor = Color.LightCyan;
            btnChangeRoom.Click += BtnChangeRoom_Click;
            btnChangeRoom.Dock = DockStyle.None;
            btnChangeRoom.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            btnChangeRoom.AutoSize = false;
            btnChangeRoom.Width = 60; // smaller width as requested
            btnChangeRoom.Height = 25; // ensure proper height
            btnChangeRoom.Margin = new Padding(0);
            headerPanel.Controls.Add(btnChangeRoom, 1, 2);

            // Set dropdown widths after controls are added
            SetComboBoxDropDownWidths();
            
            // DUAL SENSOR MODE: Disable location/room controls (managed by Device Configuration)
            if (isDualSensorMode)
            {
                if (cmbLocation != null)
                {
                    cmbLocation.Enabled = false;
                    cmbLocation.Items.Clear();
                    cmbLocation.Items.Add("Dual Sensor (Auto)");
                    cmbLocation.SelectedIndex = 0;
                }
                
                if (btnChangeRoom != null)
                {
                    btnChangeRoom.Enabled = false;
                    btnChangeRoom.Visible = false;
                }
            }
        }

        private void SetComboBoxDropDownWidths()
        {
            if (cmbLocation != null)
            {
                cmbLocation.DropDownWidth = MeasureDropDownWidth(cmbLocation);
            }
            if (cmbRoom != null)
            {
                cmbRoom.DropDownWidth = MeasureDropDownWidth(cmbRoom);
            }
        }

        private int MeasureDropDownWidth(ComboBox combo)
        {
            int maxWidth = combo.Width;
            using (var g = combo.CreateGraphics())
            {
                foreach (var item in combo.Items)
                {
                    var size = g.MeasureString(item.ToString(), combo.Font);
                    maxWidth = Math.Max(maxWidth, (int)size.Width + SystemInformation.VerticalScrollBarWidth + 16);
                }
            }
            return maxWidth;
        }

        private void SetRfidComboBoxDropDownWidths()
        {
            if (cmbRfidLocation != null)
            {
                cmbRfidLocation.DropDownWidth = MeasureDropDownWidth(cmbRfidLocation);
            }
            if (cmbRfidRoom != null)
            {
                cmbRfidRoom.DropDownWidth = MeasureDropDownWidth(cmbRfidRoom);
            }
        }

        private void InitializeDeviceManagementTab()
        {
            // Title label
            var lblTitle = new Label();
            lblTitle.Location = new Point(20, 20);
            lblTitle.Size = new Size(400, 25);
            lblTitle.Text = "Multi-Device Management";
            lblTitle.Font = new Font(lblTitle.Font, FontStyle.Bold);
            lblTitle.ForeColor = Color.DarkBlue;
            deviceManagementTab.Controls.Add(lblTitle);

            // Device name input
            var lblDeviceName = new Label();
            lblDeviceName.Location = new Point(20, 55);
            lblDeviceName.Size = new Size(100, 20);
            lblDeviceName.Text = "Device Name:";
            deviceManagementTab.Controls.Add(lblDeviceName);

            txtDeviceName = new TextBox();
            txtDeviceName.Location = new Point(125, 53);
            txtDeviceName.Size = new Size(200, 25);
            txtDeviceName.Text = config?.Device?.DeviceId ?? "Device1";
            deviceManagementTab.Controls.Add(txtDeviceName);

            // Room selection for device
            var lblDeviceRoom = new Label();
            lblDeviceRoom.Location = new Point(340, 55);
            lblDeviceRoom.Size = new Size(50, 20);
            lblDeviceRoom.Text = "Room:";
            deviceManagementTab.Controls.Add(lblDeviceRoom);

            cmbDeviceRoom = new ComboBox();
            cmbDeviceRoom.Location = new Point(395, 53);
            cmbDeviceRoom.Size = new Size(250, 25);
            cmbDeviceRoom.DropDownStyle = ComboBoxStyle.DropDownList;
            deviceManagementTab.Controls.Add(cmbDeviceRoom);

            // Initialize device button
            btnInitializeInRoom = new Button();
            btnInitializeInRoom.Location = new Point(655, 52);
            btnInitializeInRoom.Size = new Size(120, 27);
            btnInitializeInRoom.Text = "Initialize Device";
            btnInitializeInRoom.BackColor = Color.LightGreen;
            btnInitializeInRoom.Click += BtnInitializeInRoom_Click;
            deviceManagementTab.Controls.Add(btnInitializeInRoom);

            // Refresh devices button
            btnRefreshDevices = new Button();
            btnRefreshDevices.Location = new Point(785, 52);
            btnRefreshDevices.Size = new Size(80, 27);
            btnRefreshDevices.Text = "Refresh";
            btnRefreshDevices.BackColor = Color.LightCyan;
            btnRefreshDevices.Click += BtnRefreshDevices_Click;
            deviceManagementTab.Controls.Add(btnRefreshDevices);

            // Device list
            deviceListView = new ListView();
            deviceListView.Location = new Point(20, 90);
            deviceListView.Size = new Size(850, 400);
            deviceListView.View = View.Details;
            deviceListView.FullRowSelect = true;
            deviceListView.GridLines = true;

            deviceListView.Columns.Add("Device Name", 150);
            deviceListView.Columns.Add("Type", 120);
            deviceListView.Columns.Add("Room", 200);
            deviceListView.Columns.Add("Building", 150);
            deviceListView.Columns.Add("IP Address", 120);
            deviceListView.Columns.Add("Status", 80);
            deviceListView.Columns.Add("Last Seen", 130);

            deviceManagementTab.Controls.Add(deviceListView);

            // Instructions
            var lblInstructions = new Label();
            lblInstructions.Location = new Point(20, 500);
            lblInstructions.Size = new Size(850, 60);
            lblInstructions.Text = "Instructions:\n" +
                "1. Enter a unique device name for each fingerprint scanner\n" +
                "2. Select the room where this device will be installed\n" +
                "3. Click 'Initialize Device' to register it in the database\n" +
                "4. Copy this application to each device location with different device names in appsettings.json";
            lblInstructions.ForeColor = Color.DarkGreen;
            deviceManagementTab.Controls.Add(lblInstructions);
        }

        private void InitializeFingerprintUsersTab()
        {
            var title = new Label();
            title.Location = new Point(20, 20);
            title.Size = new Size(400, 25);
            title.Text = "Users With Enrolled Fingerprints";
            title.Font = new Font(title.Font, FontStyle.Bold);
            fingerprintUsersTab.Controls.Add(title);

            btnExportUsersCsv = new Button();
            btnExportUsersCsv.Location = new Point(20, 55);
            btnExportUsersCsv.Size = new Size(140, 27);
            btnExportUsersCsv.Text = "Export to CSV";
            btnExportUsersCsv.BackColor = Color.LightYellow;
            btnExportUsersCsv.Click += (s, e) => ExportFingerprintUsersToCsv();
            fingerprintUsersTab.Controls.Add(btnExportUsersCsv);

            btnDeleteFingerprint = new Button();
            btnDeleteFingerprint.Location = new Point(170, 55);
            btnDeleteFingerprint.Size = new Size(170, 27);
            btnDeleteFingerprint.Text = "Delete Selected";
            btnDeleteFingerprint.BackColor = Color.MistyRose;
            btnDeleteFingerprint.Click += (s, e) => DeleteSelectedFingerprint();
            fingerprintUsersTab.Controls.Add(btnDeleteFingerprint);

            btnRefreshFingerprintUsers = new Button();
            btnRefreshFingerprintUsers.Location = new Point(350, 55);
            btnRefreshFingerprintUsers.Size = new Size(90, 27);
            btnRefreshFingerprintUsers.Text = "Refresh";
            btnRefreshFingerprintUsers.BackColor = Color.LightCyan;
            btnRefreshFingerprintUsers.Click += (s, e) => LoadFingerprintUsers();
            fingerprintUsersTab.Controls.Add(btnRefreshFingerprintUsers);

            fingerprintUsersListView = new ListView();
            fingerprintUsersListView.Location = new Point(20, 90);
            fingerprintUsersListView.Size = new Size(850, 450);
            fingerprintUsersListView.View = View.Details;
            fingerprintUsersListView.FullRowSelect = true;
            fingerprintUsersListView.GridLines = true;
            fingerprintUsersListView.Columns.Add("Name", 220);
            // Email column removed - not available in PDF parse data
            fingerprintUsersListView.Columns.Add("User Type", 100);
            fingerprintUsersListView.Columns.Add("Department", 150);
            fingerprintUsersListView.Columns.Add("GUID", 140);
            fingerprintUsersTab.Controls.Add(fingerprintUsersListView);
        }

        private void InitializeScenariosTab()
        {
            scenariosTab.Controls.Clear();
            
            // Title
            lblScenariosTitle = new Label();
            lblScenariosTitle.Location = new Point(20, 20);
            lblScenariosTitle.Size = new Size(600, 30);
            lblScenariosTitle.Text = "📋 Attendance System Scenarios Configuration";
            lblScenariosTitle.Font = new Font(lblScenariosTitle.Font.FontFamily, 14, FontStyle.Bold);
            lblScenariosTitle.ForeColor = Color.DarkBlue;
            scenariosTab.Controls.Add(lblScenariosTitle);

            // Description
            var lblDescription = new Label();
            lblDescription.Location = new Point(20, 55);
            lblDescription.Size = new Size(800, 40);
            lblDescription.Text = "Configure time windows and tolerances for different attendance scenarios. " +
                                 "All values are in minutes. Use 'Reset to Defaults' to restore original values.";
            lblDescription.ForeColor = Color.DarkSlateGray;
            scenariosTab.Controls.Add(lblDescription);

            int yPos = 110;
            int labelWidth = 350;
            int controlWidth = 80;
            int spacing = 35;

            // 1. Instructor Early Window (Scenario 1)
            lblInstructorEarlyWindow = new Label();
            lblInstructorEarlyWindow.Location = new Point(20, yPos);
            lblInstructorEarlyWindow.Size = new Size(labelWidth, 25);
            lblInstructorEarlyWindow.Text = "1. Instructor Early Arrival Window (minutes):";
            lblInstructorEarlyWindow.Font = new Font(lblInstructorEarlyWindow.Font, FontStyle.Bold);
            scenariosTab.Controls.Add(lblInstructorEarlyWindow);

            numInstructorEarlyWindow = new NumericUpDown();
            numInstructorEarlyWindow.Location = new Point(380, yPos);
            numInstructorEarlyWindow.Size = new Size(controlWidth, 25);
            numInstructorEarlyWindow.Minimum = 0;
            numInstructorEarlyWindow.Maximum = 60;
            numInstructorEarlyWindow.Value = 15; // Default: 15 minutes
            numInstructorEarlyWindow.Increment = 5;
            scenariosTab.Controls.Add(numInstructorEarlyWindow);

            yPos += spacing;

            // 2. Student Grace Period (Scenario 1)
            lblStudentGracePeriod = new Label();
            lblStudentGracePeriod.Location = new Point(20, yPos);
            lblStudentGracePeriod.Size = new Size(labelWidth, 25);
            lblStudentGracePeriod.Text = "2. Student Grace Period (minutes):";
            lblStudentGracePeriod.Font = new Font(lblStudentGracePeriod.Font, FontStyle.Bold);
            scenariosTab.Controls.Add(lblStudentGracePeriod);

            numStudentGracePeriod = new NumericUpDown();
            numStudentGracePeriod.Location = new Point(380, yPos);
            numStudentGracePeriod.Size = new Size(controlWidth, 25);
            numStudentGracePeriod.Minimum = 0;
            numStudentGracePeriod.Maximum = 60;
            numStudentGracePeriod.Value = 15; // Default: 15 minutes
            numStudentGracePeriod.Increment = 5;
            scenariosTab.Controls.Add(numStudentGracePeriod);

            yPos += spacing;

            // 3. Instructor Late Tolerance (Scenario 4)
            lblInstructorLateTolerance = new Label();
            lblInstructorLateTolerance.Location = new Point(20, yPos);
            lblInstructorLateTolerance.Size = new Size(labelWidth, 25);
            lblInstructorLateTolerance.Text = "3. Instructor Late Tolerance (minutes):";
            lblInstructorLateTolerance.Font = new Font(lblInstructorLateTolerance.Font, FontStyle.Bold);
            scenariosTab.Controls.Add(lblInstructorLateTolerance);

            numInstructorLateTolerance = new NumericUpDown();
            numInstructorLateTolerance.Location = new Point(380, yPos);
            numInstructorLateTolerance.Size = new Size(controlWidth, 25);
            numInstructorLateTolerance.Minimum = 0;
            numInstructorLateTolerance.Maximum = 120;
            numInstructorLateTolerance.Value = 30; // Default: 30 minutes
            numInstructorLateTolerance.Increment = 5;
            scenariosTab.Controls.Add(numInstructorLateTolerance);

            yPos += spacing;

            // 4. Auto Close Delay (Scenario 5)
            lblAutoCloseDelay = new Label();
            lblAutoCloseDelay.Location = new Point(20, yPos);
            lblAutoCloseDelay.Size = new Size(labelWidth, 25);
            lblAutoCloseDelay.Text = "4. Auto Close Delay (minutes):";
            lblAutoCloseDelay.Font = new Font(lblAutoCloseDelay.Font, FontStyle.Bold);
            scenariosTab.Controls.Add(lblAutoCloseDelay);

            numAutoCloseDelay = new NumericUpDown();
            numAutoCloseDelay.Location = new Point(380, yPos);
            numAutoCloseDelay.Size = new Size(controlWidth, 25);
            numAutoCloseDelay.Minimum = 0;
            numAutoCloseDelay.Maximum = 120;
            numAutoCloseDelay.Value = 30; // Default: 30 minutes
            numAutoCloseDelay.Increment = 5;
            scenariosTab.Controls.Add(numAutoCloseDelay);

            yPos += spacing;

            // 5. Student Early Arrival Window (Scenario 15)
            lblStudentEarlyArrivalWindow = new Label();
            lblStudentEarlyArrivalWindow.Location = new Point(20, yPos);
            lblStudentEarlyArrivalWindow.Size = new Size(labelWidth, 25);
            lblStudentEarlyArrivalWindow.Text = "5. Student Early Arrival Window (minutes):";
            lblStudentEarlyArrivalWindow.Font = new Font(lblStudentEarlyArrivalWindow.Font, FontStyle.Bold);
            scenariosTab.Controls.Add(lblStudentEarlyArrivalWindow);

            numStudentEarlyArrivalWindow = new NumericUpDown();
            numStudentEarlyArrivalWindow.Location = new Point(380, yPos);
            numStudentEarlyArrivalWindow.Size = new Size(controlWidth, 25);
            numStudentEarlyArrivalWindow.Minimum = 0;
            numStudentEarlyArrivalWindow.Maximum = 60;
            numStudentEarlyArrivalWindow.Value = 15; // Default: 15 minutes
            numStudentEarlyArrivalWindow.Increment = 5;
            scenariosTab.Controls.Add(numStudentEarlyArrivalWindow);

            yPos += spacing;

            // 6. Instructor End Tolerance (Scenario 2)
            lblInstructorEndTolerance = new Label();
            lblInstructorEndTolerance.Location = new Point(20, yPos);
            lblInstructorEndTolerance.Size = new Size(labelWidth, 25);
            lblInstructorEndTolerance.Text = "6. Instructor End Session Tolerance (minutes):";
            lblInstructorEndTolerance.Font = new Font(lblInstructorEndTolerance.Font, FontStyle.Bold);
            scenariosTab.Controls.Add(lblInstructorEndTolerance);

            numInstructorEndTolerance = new NumericUpDown();
            numInstructorEndTolerance.Location = new Point(380, yPos);
            numInstructorEndTolerance.Size = new Size(controlWidth, 25);
            numInstructorEndTolerance.Minimum = 0;
            numInstructorEndTolerance.Maximum = 60;
            numInstructorEndTolerance.Value = 15; // Default: 15 minutes
            numInstructorEndTolerance.Increment = 5;
            scenariosTab.Controls.Add(numInstructorEndTolerance);

            yPos += 50;

            // Control buttons
            btnResetToDefaults = new Button();
            btnResetToDefaults.Location = new Point(20, yPos);
            btnResetToDefaults.Size = new Size(150, 35);
            btnResetToDefaults.Text = "Reset to Defaults";
            btnResetToDefaults.BackColor = Color.LightCoral;
            btnResetToDefaults.Font = new Font(btnResetToDefaults.Font, FontStyle.Bold);
            btnResetToDefaults.Click += BtnResetToDefaults_Click;
            scenariosTab.Controls.Add(btnResetToDefaults);

            btnSaveScenarios = new Button();
            btnSaveScenarios.Location = new Point(180, yPos);
            btnSaveScenarios.Size = new Size(120, 35);
            btnSaveScenarios.Text = "Save Settings";
            btnSaveScenarios.BackColor = Color.LightGreen;
            btnSaveScenarios.Font = new Font(btnSaveScenarios.Font, FontStyle.Bold);
            btnSaveScenarios.Click += BtnSaveScenarios_Click;
            scenariosTab.Controls.Add(btnSaveScenarios);

            btnLoadScenarios = new Button();
            btnLoadScenarios.Location = new Point(310, yPos);
            btnLoadScenarios.Size = new Size(120, 35);
            btnLoadScenarios.Text = "Load Settings";
            btnLoadScenarios.BackColor = Color.LightBlue;
            btnLoadScenarios.Font = new Font(btnLoadScenarios.Font, FontStyle.Bold);
            btnLoadScenarios.Click += BtnLoadScenarios_Click;
            scenariosTab.Controls.Add(btnLoadScenarios);

            yPos += 50;

            // Scenario descriptions panel
            var descriptionPanel = new Panel();
            descriptionPanel.Location = new Point(20, yPos);
            descriptionPanel.Size = new Size(800, 300);
            descriptionPanel.BorderStyle = BorderStyle.FixedSingle;
            descriptionPanel.BackColor = Color.FromArgb(248, 248, 255);
            scenariosTab.Controls.Add(descriptionPanel);

            var lblDescriptionsTitle = new Label();
            lblDescriptionsTitle.Location = new Point(10, 10);
            lblDescriptionsTitle.Size = new Size(780, 25);
            lblDescriptionsTitle.Text = "📋 Scenario Descriptions:";
            lblDescriptionsTitle.Font = new Font(lblDescriptionsTitle.Font, FontStyle.Bold);
            lblDescriptionsTitle.ForeColor = Color.DarkBlue;
            descriptionPanel.Controls.Add(lblDescriptionsTitle);

            var txtDescriptions = new TextBox();
            txtDescriptions.Location = new Point(10, 40);
            txtDescriptions.Size = new Size(780, 250);
            txtDescriptions.Multiline = true;
            txtDescriptions.ReadOnly = true;
            txtDescriptions.ScrollBars = ScrollBars.Vertical;
            txtDescriptions.BackColor = Color.White;
            txtDescriptions.Text = @"1. Instructor Early Window: How early an instructor can start a session before scheduled time
2. Student Grace Period: How late a student can arrive and still be marked 'Present'
3. Instructor Late Tolerance: How late an instructor can be and still start a session (marked as 'Unscheduled')
4. Auto Close Delay: How long after scheduled end time the system will auto-close an active session
5. Student Early Arrival Window: How early students can scan and be marked as 'Early Arrival'
6. Instructor End Tolerance: How early/late an instructor can end a session relative to scheduled end time

These settings allow you to customize the attendance system behavior for different academic policies and requirements.";
            descriptionPanel.Controls.Add(txtDescriptions);

            // Load current settings
            LoadScenariosFromConfig();
        }

        // ============= Attendance Scenarios Configuration Methods =============
        
        private void BtnResetToDefaults_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to reset all attendance scenario values to their defaults?",
                "Reset to Defaults",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                numInstructorEarlyWindow.Value = 15;
                numStudentGracePeriod.Value = 15;
                numInstructorLateTolerance.Value = 30;
                numAutoCloseDelay.Value = 30;
                numStudentEarlyArrivalWindow.Value = 15;
                numInstructorEndTolerance.Value = 15;

                MessageBox.Show("All values have been reset to defaults.", "Reset Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void BtnSaveScenarios_Click(object sender, EventArgs e)
        {
            try
            {
                var scenariosConfig = new
                {
                    InstructorEarlyWindow = (int)numInstructorEarlyWindow.Value,
                    StudentGracePeriod = (int)numStudentGracePeriod.Value,
                    InstructorLateTolerance = (int)numInstructorLateTolerance.Value,
                    AutoCloseDelay = (int)numAutoCloseDelay.Value,
                    StudentEarlyArrivalWindow = (int)numStudentEarlyArrivalWindow.Value,
                    InstructorEndTolerance = (int)numInstructorEndTolerance.Value
                };

                string jsonString = System.Text.Json.JsonSerializer.Serialize(scenariosConfig, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "attendance_scenarios.json");
                File.WriteAllText(configPath, jsonString);

                MessageBox.Show($"Scenarios configuration saved to:\n{configPath}", "Save Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving scenarios configuration: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnLoadScenarios_Click(object sender, EventArgs e)
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "attendance_scenarios.json");
                
                if (!File.Exists(configPath))
                {
                    MessageBox.Show("No saved configuration found. Using default values.", "Load Configuration", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string jsonString = File.ReadAllText(configPath);
                var scenariosConfig = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);

                if (scenariosConfig != null)
                {
                    if (scenariosConfig.TryGetValue("InstructorEarlyWindow", out var instructorEarlyWindow))
                        numInstructorEarlyWindow.Value = Convert.ToDecimal(instructorEarlyWindow.ToString());
                    
                    if (scenariosConfig.TryGetValue("StudentGracePeriod", out var studentGracePeriod))
                        numStudentGracePeriod.Value = Convert.ToDecimal(studentGracePeriod.ToString());
                    
                    if (scenariosConfig.TryGetValue("InstructorLateTolerance", out var instructorLateTolerance))
                        numInstructorLateTolerance.Value = Convert.ToDecimal(instructorLateTolerance.ToString());
                    
                    if (scenariosConfig.TryGetValue("AutoCloseDelay", out var autoCloseDelay))
                        numAutoCloseDelay.Value = Convert.ToDecimal(autoCloseDelay.ToString());
                    
                    if (scenariosConfig.TryGetValue("StudentEarlyArrivalWindow", out var studentEarlyArrivalWindow))
                        numStudentEarlyArrivalWindow.Value = Convert.ToDecimal(studentEarlyArrivalWindow.ToString());
                    
                    if (scenariosConfig.TryGetValue("InstructorEndTolerance", out var instructorEndTolerance))
                        numInstructorEndTolerance.Value = Convert.ToDecimal(instructorEndTolerance.ToString());

                    MessageBox.Show("Scenarios configuration loaded successfully.", "Load Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading scenarios configuration: {ex.Message}", "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadScenariosFromConfig()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "attendance_scenarios.json");
                
                if (File.Exists(configPath))
                {
                    string jsonString = File.ReadAllText(configPath);
                    var scenariosConfig = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);

                    if (scenariosConfig != null)
                    {
                        if (scenariosConfig.TryGetValue("InstructorEarlyWindow", out var instructorEarlyWindow))
                            numInstructorEarlyWindow.Value = Convert.ToDecimal(instructorEarlyWindow.ToString());
                        
                        if (scenariosConfig.TryGetValue("StudentGracePeriod", out var studentGracePeriod))
                            numStudentGracePeriod.Value = Convert.ToDecimal(studentGracePeriod.ToString());
                        
                        if (scenariosConfig.TryGetValue("InstructorLateTolerance", out var instructorLateTolerance))
                            numInstructorLateTolerance.Value = Convert.ToDecimal(instructorLateTolerance.ToString());
                        
                        if (scenariosConfig.TryGetValue("AutoCloseDelay", out var autoCloseDelay))
                            numAutoCloseDelay.Value = Convert.ToDecimal(autoCloseDelay.ToString());
                        
                        if (scenariosConfig.TryGetValue("StudentEarlyArrivalWindow", out var studentEarlyArrivalWindow))
                            numStudentEarlyArrivalWindow.Value = Convert.ToDecimal(studentEarlyArrivalWindow.ToString());
                        
                        if (scenariosConfig.TryGetValue("InstructorEndTolerance", out var instructorEndTolerance))
                            numInstructorEndTolerance.Value = Convert.ToDecimal(instructorEndTolerance.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently fail and use default values if config can't be loaded
                Console.WriteLine($"Could not load scenarios config: {ex.Message}");
            }
        }

        // ============= End of Attendance Scenarios Configuration Methods =============

        private bool PromptForAdminPassword()
        {
            using (var form = new Form())
            using (var lbl = new Label())
            using (var txt = new TextBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            {
                form.Text = "Admin Authentication";
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(360, 140);

                lbl.Text = "Enter admin password:";
                lbl.SetBounds(12, 15, 320, 20);
                txt.UseSystemPasswordChar = true;
                txt.SetBounds(15, 40, 330, 24);
                ok.Text = "OK";
                ok.SetBounds(190, 85, 70, 28);
                ok.DialogResult = DialogResult.OK;
                cancel.Text = "Cancel";
                cancel.SetBounds(275, 85, 70, 28);
                cancel.DialogResult = DialogResult.Cancel;

                form.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                var result = form.ShowDialog(this);
                if (result == DialogResult.OK)
                {
                    return txt.Text == "admin123";
                }
                return false;
            }
        }

        private void LoadFingerprintUsers()
        {
            try
            {
                SetStatusText("Loading fingerprint users...");
                fingerprintUsers = dbManager?.LoadAllUsers() ?? new List<User>();

                fingerprintUsersListView.Items.Clear();
                foreach (var u in fingerprintUsers)
                {
                    var fullName = ($"{u.FirstName} {u.LastName}").Trim();
                    if (string.IsNullOrWhiteSpace(fullName)) fullName = u.Username ?? u.Email;
                    var item = new ListViewItem(fullName);
                    item.SubItems.Add(u.Email ?? "");
                    item.SubItems.Add(u.UserType ?? "");
                    item.SubItems.Add(u.Department ?? "");
                    item.SubItems.Add(u.EmployeeId ?? "");
                    fingerprintUsersListView.Items.Add(item);
                }
                SetStatusText($"Loaded {fingerprintUsers.Count} users with fingerprints.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load users: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportFingerprintUsersToCsv()
        {
            if (fingerprintUsers == null || fingerprintUsers.Count == 0)
            {
                MessageBox.Show("No users to export.", "No Data", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var dlg = new SaveFileDialog();
            dlg.Filter = "CSV files (*.csv)|*.csv";
            dlg.FileName = $"FingerprintUsers_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    using (var writer = new System.IO.StreamWriter(dlg.FileName, false, Encoding.UTF8))
                    {
                        writer.WriteLine("FirstName,LastName,UserType,Department,UserGUID");
                        foreach (var u in fingerprintUsers)
                        {
                            string first = (u.FirstName ?? "").Replace(",", " ");
                            string last = (u.LastName ?? "").Replace(",", " ");
                            // Email removed - not available in PDF parse data
                            string type = (u.UserType ?? "").Replace(",", " ");
                            string dept = (u.Department ?? "").Replace(",", " ");
                            string guid = (u.EmployeeId ?? "").Replace(",", " ");
                            writer.WriteLine($"{first},{last},{type},{dept},{guid}");
                        }
                    }
                    MessageBox.Show($"Exported to {dlg.FileName}", "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export: {ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void DeleteSelectedFingerprint()
        {
            try
            {
                if (fingerprintUsersListView == null || fingerprintUsersListView.SelectedItems.Count == 0)
                {
                    MessageBox.Show("Select a user first.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var selected = fingerprintUsersListView.SelectedItems[0];
                var guid = selected.SubItems.Count >= 5 ? selected.SubItems[4].Text : null;
                if (string.IsNullOrWhiteSpace(guid))
                {
                    MessageBox.Show("Selected user has no GUID.", "Invalid Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var confirm = MessageBox.Show("Permanently delete the fingerprint for this user?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;

                if (dbManager.DeleteUserFingerprintByGuid(guid))
                {
                    SetStatusText("Fingerprint deleted. Refreshing users and scanner...");
                    // Refresh cloud users and restart identification so removed templates are not used
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            SyncUsersFromCloud();
                            this.Invoke(new Action(() =>
                            {
                                LoadFingerprintUsers();
                                // If attendance loop exists, restart to reload templates
                                if (m_AttendanceOperation != null)
                                {
                                    RestartIdentification();
                                }
                            }));
                        }
                        catch { }
                    });
                }
                else
                {
                    MessageBox.Show("No changes made.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

		private void BtnEnroll_Click(object sender, EventArgs e)
        {
			// NEW: Check if a user is selected from the table
			if (!isUserSelected || selectedUser == null)
			{
				MessageBox.Show("Please select a user from the table first.", "No User Selected",
					MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			// Validate required fields (should be populated from selected user)
			if (string.IsNullOrWhiteSpace(txtFirstName.Text) || string.IsNullOrWhiteSpace(txtLastName.Text))
			{
				MessageBox.Show("Selected user data is incomplete. Please refresh and try again.", "Invalid User Data",
					MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			// Show confirmation dialog before starting enrollment
			string userFullName = $"{selectedUser.FirstName} {selectedUser.LastName}";
			string userType = selectedUser.UserType ?? "User";
			string userDepartment = selectedUser.Department ?? "N/A";
			
			var confirmResult = MessageBox.Show(
				$"Adding fingerprint to this user:\n\n" +
				$"Name: {userFullName}\n" +
				$"Type: {userType}\n" +
				$"Department: {userDepartment}\n\n" +
				$"Would you like to start fingerprint enrollment?",
				"Confirm Fingerprint Enrollment",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Question);

			if (confirmResult == DialogResult.Yes)
			{
			StartEnrollment();
			}
        }

        private void StartEnrollment()
        {
            try
            {
                // Set enrollment state
                m_bEnrollmentInProgress = true;
                // Reset guidance UI
                SetEnrollProgress(0);
                UpdateEnrollmentGuidance(0, "Get ready: You'll scan your thumb multiple times.");
                // Start smooth progress updates
                isEnrollmentActive = true;
                enrollProgressTarget = 5;
                try { enrollProgressTimer?.Start(); } catch { }
                
                // Completely stop attendance operation during enrollment
                StopAttendanceOperation();

                // Create user record (use full name as username - email not available in PDF parse data)
                string safeFirst = txtFirstName?.Text?.Trim();
                string safeLast = txtLastName?.Text?.Trim();
                string derivedUserName = ($"{safeFirst} {safeLast}".Trim());
                if (string.IsNullOrWhiteSpace(derivedUserName))
                {
                    derivedUserName = $"user_{DateTime.Now:yyyyMMddHHmmss}";
                }
                var user = new UserRecord { UserName = derivedUserName };
                m_OperationObj = user;

                // Create enrollment operation
                if (m_Operation != null)
                {
                    m_Operation.Dispose();
                    m_Operation = null;
                }

                m_Operation = new FutronicEnrollment();

                // Set properties (tuned to avoid false fake-finger detections)
                m_Operation.FakeDetection = false; // Disable fake finger detection to reduce false positives
                m_Operation.FFDControl = false;    // Let device manage FFD internally
                m_Operation.FastMode = true;
                m_Operation.FARN = 100;
                m_Operation.Version = VersionCompatible.ftr_version_compatible;
                ((FutronicEnrollment)m_Operation).MIOTControlOff = true; // Turn off MIOT control per SDK guidance
                
                // Additional settings to reduce false positives
                m_Operation.FARN = 100; // Higher FARN value = more strict matching

                // Register ENROLLMENT-SPECIFIC events (separate from attendance)
                m_Operation.OnPutOn += OnEnrollmentPutOn;
                m_Operation.OnTakeOff += OnEnrollmentTakeOff;
                m_Operation.UpdateScreenImage += OnEnrollmentUpdateScreenImage;
                m_Operation.OnFakeSource += OnEnrollmentFakeSource;
                ((FutronicEnrollment)m_Operation).OnEnrollmentComplete += OnEnrollmentComplete;
                if (m_Operation == null)
                {
                    throw new InvalidOperationException("Enrollment operation failed to initialize.");
                }

                EnableControls(false);
                SetStatusText("Starting enrollment... Please wait 3 seconds before placing your finger.");
                UpdateEnrollmentGuidance(1, "Step 1: Place your thumb on the sensor and hold steady.");

                // Reset false positive detection state
                m_bInitialStartup = true;
                m_lastPutOnTime = DateTime.Now;
                
                // Show ready message after initial startup period
                System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ =>
                {
                    if (!m_bExit && m_bEnrollmentInProgress)
                    {
                        this.Invoke(new Action(() => 
                        {
                            SetStatusText("Enrollment ready. Please place your finger on the scanner.");
                            UpdateEnrollmentGuidance(1, "Step 1: Place your thumb on the sensor and hold steady.");
                            // Small initial bump so user sees progress begin
                            try { enrollProgressTarget = Math.Max(enrollProgressTarget, 15); } catch { }
                        }));
                    }
                });

                // Start enrollment with timeout protection
                var enrollmentTask = System.Threading.Tasks.Task.Run(() =>
                {
                    ((FutronicEnrollment)m_Operation).Enrollment();
                });
                
                // Set a timeout to prevent hanging
                System.Threading.Tasks.Task.Delay(30000).ContinueWith(_ =>
                {
                    if (!enrollmentTask.IsCompleted)
                    {
                        this.Invoke(new Action(() =>
                        {
                            SetStatusText("Enrollment timeout. Please try again.");
                        m_bEnrollmentInProgress = false;
                        isIdentifying = false; // FIX: Reset identification flag after enrollment
                        EnableControls(true);
                        UnregisterEnrollmentEvents();
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                m_bEnrollmentInProgress = false;
                MessageBox.Show($"Failed to start enrollment: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                EnableControls(true);
            }
        }

        private void BtnIdentify_Click(object sender, EventArgs e)
        {
            StartIdentification();
        }

        private void StartIdentification()
        {
            try
            {
                // Check if enrollment is in progress
                if (m_bEnrollmentInProgress)
                {
                    SetStatusText("Enrollment in progress. Please wait for enrollment to complete.");
                    return;
                }

                if (isIdentifying)
                {
                    SetStatusText("Identification already in progress.");
                    return;
                }
                
                // Set identifying flag to prevent concurrent operations
                isIdentifying = true;
                    
                // Ensure cloud users are fresh before loading
                if (cloudUsers == null || cloudUsers.Count == 0)
                {
                    try { SyncUsersFromCloud(); } catch { }
                }

                // Load all users
                var users = LoadUsers();
                if (users.Count == 0)
                {
                    // Try one more sync attempt before giving up
                    try { SyncUsersFromCloud(); users = LoadUsers(); } catch { }
                    if (users.Count == 0)
                    {
                        if (!noUsersAlertShown)
                        {
                            noUsersAlertShown = true;
                            MessageBox.Show("No enrolled users found. Please enroll users first.", "No Users", 
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            SetStatusText("No enrolled users available yet. Please enroll a user.");
                        }
                        return;
                    }
                }

                // Keep a defensive copy for thread safety
                m_IdentificationUsers = new List<UserRecord>(users);
                SetStatusText($"Loaded {users.Count} users for identification.");

                // Create separate identification operation for attendance and keep it alive for continuous use
                if (m_AttendanceOperation == null)
                {
                    try
                    {
                        SetStatusText("Preparing device for attendance. Please wait...");
                        
                        // Check if fingerprint device is available
                        if (!IsFingerprintDeviceAvailable())
                        {
                            SetStatusText("ERROR: Fingerprint device not found or not working. Please check device connection and drivers.");
                            EnableControls(true);
                            return;
                        }
                        
                        // Create new operation with proper initialization
                        m_AttendanceOperation = new FutronicIdentification();

                        // Set properties (tuned to avoid false fake-finger detections)
                        m_AttendanceOperation.FakeDetection = false; // Disable fake finger detection to reduce false positives
                        m_AttendanceOperation.FFDControl = false;    // Let device manage FFD internally
                        m_AttendanceOperation.FastMode = true;
                        m_AttendanceOperation.FARN = 100;
                        m_AttendanceOperation.Version = VersionCompatible.ftr_version_compatible;
                        
                        // Additional settings to reduce false positives
                        m_AttendanceOperation.FARN = 100; // Higher FARN value = more strict matching
                        
                        // Add a small delay to ensure proper initialization
                        System.Threading.Thread.Sleep(100);

                        // Register ATTENDANCE-SPECIFIC events (separate from enrollment)
                        m_AttendanceOperation.OnPutOn += OnAttendancePutOn;
                        m_AttendanceOperation.OnTakeOff += OnAttendanceTakeOff;
                        m_AttendanceOperation.UpdateScreenImage += OnAttendanceUpdateScreenImage;
                        m_AttendanceOperation.OnFakeSource += OnAttendanceFakeSource;
                        m_AttendanceOperation.OnGetBaseTemplateComplete += OnGetBaseTemplateComplete;
                        
                        SetStatusText("Device ready for attendance.");
                    }
                    catch (Exception ex)
                    {
                        SetStatusText($"ERROR: Failed to initialize fingerprint device: {ex.Message}");
                        EnableControls(true);
                        
                        // Clean up if initialization failed
                        if (m_AttendanceOperation != null)
                        {
                            try
                            {
                                m_AttendanceOperation.Dispose();
                            }
                            catch { }
                            m_AttendanceOperation = null;
                        }
                        return;
                    }
                }

                EnableControls(false);
                SetStatusText("Device is getting ready. Please wait...");

                // Reset false positive detection state
                m_bInitialStartup = true;
                m_lastPutOnTime = DateTime.Now;
                
                // Show ready message after initial startup period
                System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ =>
                {
                    if (!m_bExit && m_bAttendanceActive)
                    {
                        this.Invoke(new Action(() => 
                            SetStatusText("Attendance ready. Scan your fingerprint twice to confirm.")));
                    }
                });

                // Start identification with proper error handling
                try
                {
                    // Ensure we're on the UI thread for SDK operations
                    if (this.InvokeRequired)
                    {
                        this.Invoke(new Action(() =>
                        {
                            try
                            {
                                m_AttendanceOperation.GetBaseTemplate();
                                isIdentifying = true;
                                m_bAttendanceActive = true;
                            }
                            catch (Exception ex)
                            {
                                SetStatusText($"ERROR: Failed to start fingerprint scanning: {ex.Message}");
                                EnableControls(true);
                                m_bAttendanceActive = false;
                            }
                        }));
                    }
                    else
                    {
                        m_AttendanceOperation.GetBaseTemplate();
                        isIdentifying = true;
                        m_bAttendanceActive = true;
                    }
                }
                catch (Exception ex)
                {
                    SetStatusText($"ERROR: Failed to start fingerprint scanning: {ex.Message}");
                    EnableControls(true);
                    m_bAttendanceActive = false;
                    return;
                }
            }
            catch (Exception ex)
            {
                SetStatusText($"Failed to start identification: {ex.Message}");
                MessageBox.Show($"Failed to start identification: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                EnableControls(true);
                isIdentifying = false;
                m_bAttendanceActive = false;
            }
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            if (m_Operation != null)
            {
                m_Operation.OnCalcel();
            }
        }

        private void CmbLocation_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbLocation.SelectedItem != null && dbManager != null)
            {
                string selectedLocation = cmbLocation.SelectedItem.ToString();
                dbManager.ChangeCurrentLocation(selectedLocation);
                SetStatusText($"Location changed to: {selectedLocation}");
            }
        }

        private void BtnChangeRoom_Click(object sender, EventArgs e)
        {
            if (cmbRoom.SelectedItem != null && dbManager != null)
            {
                var selectedRoom = (Database.Models.Room)cmbRoom.SelectedItem;
                if (dbManager.ChangeCurrentRoom(selectedRoom.RoomId))
                {
                    UpdateCurrentRoomDisplay();
                    SetStatusText($"Room changed to: {selectedRoom.DisplayName}");
                    dbManager.LogCurrentDeviceStatus(); // Debug logging
                }
                else
                {
                    SetStatusText("Failed to change room");
                }
            }
        }

        private void BtnInitializeInRoom_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtDeviceName.Text))
            {
                MessageBox.Show("Please enter a device name.", "Missing Device Name", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (cmbDeviceRoom.SelectedItem == null)
            {
                MessageBox.Show("Please select a room for the device.", "No Room Selected", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selectedRoom = (Database.Models.Room)cmbDeviceRoom.SelectedItem;
            var deviceName = txtDeviceName.Text.Trim();

            if (dbManager.InitializeDeviceInRoom(selectedRoom.RoomId, deviceName))
            {
                SetStatusText($"Device '{deviceName}' initialized in room '{selectedRoom.DisplayName}'");
                RefreshDeviceList();
                UpdateCurrentRoomDisplay();
            }
            else
            {
                MessageBox.Show("Failed to initialize device. Check the logs for details.", "Initialization Failed", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnRefreshDevices_Click(object sender, EventArgs e)
        {
            RefreshDeviceList();
        }

        private void BtnExportAttendance_Click(object sender, EventArgs e)
        {
            if (attendanceRecords.Count == 0)
            {
                MessageBox.Show("No attendance records to export.", "No Data", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SaveFileDialog saveDialog = new SaveFileDialog();
            saveDialog.Filter = "CSV files (*.csv)|*.csv";
            saveDialog.FileName = $"Attendance_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    ExportToCSV(saveDialog.FileName);
                    MessageBox.Show($"Attendance records exported successfully to:\n{saveDialog.FileName}", 
                        "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export: {ex.Message}", "Export Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        
        private void BtnForceEndSession_Click(object sender, EventArgs e)
        {
            try
            {
                if (currentSessionState != AttendanceSessionState.Inactive)
                {
                    // Force end the current session
                    if (dbManager != null && !string.IsNullOrEmpty(currentScheduleId))
                    {
                        dbManager.EndClassSession(currentInstructorId, currentScheduleId);
                    }
                    
                    // Reset session state
                    currentSessionState = AttendanceSessionState.Inactive;
                    currentInstructorId = null;
                    currentScheduleId = null;
                    
                    UpdateSessionStateDisplay();
                    SetStatusText("Session force ended by administrator.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error force ending session: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void UpdateSessionStateDisplay()
        {
            // Ensure this runs on the UI thread
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateSessionStateDisplay()));
                return;
            }
            
            if (lblSessionState == null || lblSessionInfo == null) return;
            
            try
            {
                switch (currentSessionState)
                {
                    case AttendanceSessionState.Inactive:
                        lblSessionState.Text = "Session State: Inactive";
                        lblSessionState.ForeColor = Color.DarkRed;
                        lblSessionInfo.Text = "Waiting for instructor to start attendance session...";
                        if (btnForceEndSession != null) btnForceEndSession.Visible = false;
                        break;
                        
                    case AttendanceSessionState.WaitingForInstructor:
                        lblSessionState.Text = "Session State: Waiting for Instructor";
                        lblSessionState.ForeColor = Color.Orange;
                        lblSessionInfo.Text = "Instructor must scan to start the attendance session...";
                        if (btnForceEndSession != null) btnForceEndSession.Visible = false;
                        break;
                        
                    case AttendanceSessionState.ActiveForStudents:
                        lblSessionState.Text = "Session State: Active - Students Can Sign In";
                        lblSessionState.ForeColor = Color.Green;
                        lblSessionInfo.Text = "Students can now scan to sign in for attendance...";
                        if (btnForceEndSession != null) btnForceEndSession.Visible = true;
                        break;
                        
                    case AttendanceSessionState.WaitingForInstructorSignOut:
                        lblSessionState.Text = "Session State: Waiting for Instructor Sign-Out";
                        lblSessionState.ForeColor = Color.Orange;
                        lblSessionInfo.Text = "Instructor must scan to open sign-out for students...";
                        if (btnForceEndSession != null) btnForceEndSession.Visible = true;
                        break;
                        
                    case AttendanceSessionState.ActiveForSignOut:
                        lblSessionState.Text = "Session State: Active - Students Can Sign Out";
                        lblSessionState.ForeColor = Color.Blue;
                        lblSessionInfo.Text = "Students can now scan to sign out...";
                        if (btnForceEndSession != null) btnForceEndSession.Visible = true;
                        break;
                        
                    case AttendanceSessionState.WaitingForInstructorClose:
                        lblSessionState.Text = "Session State: Waiting for Instructor to Close";
                        lblSessionState.ForeColor = Color.Orange;
                        lblSessionInfo.Text = "Instructor must scan to close the attendance session...";
                        if (btnForceEndSession != null) btnForceEndSession.Visible = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating session state display: {ex.Message}");
            }
        }
        
        private void UpdateRfidSessionStateDisplay()
        {
            // Ensure this runs on the UI thread
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateRfidSessionStateDisplay()));
                return;
            }
            
            if (lblRfidSessionState == null || lblRfidSessionInfo == null) return;
            
            try
            {
                switch (currentRfidSessionState)
                {
                    case AttendanceSessionState.Inactive:
                        lblRfidSessionState.Text = "RFID Session State: Inactive";
                        lblRfidSessionState.ForeColor = Color.DarkRed;
                        lblRfidSessionInfo.Text = "Waiting for instructor to start RFID attendance session...";
                        if (btnForceEndRfidSession != null) btnForceEndRfidSession.Visible = false;
                        break;
                        
                    case AttendanceSessionState.WaitingForInstructor:
                        lblRfidSessionState.Text = "RFID Session State: Waiting for Instructor";
                        lblRfidSessionState.ForeColor = Color.Orange;
                        lblRfidSessionInfo.Text = "Instructor must scan RFID to start the attendance session...";
                        if (btnForceEndRfidSession != null) btnForceEndRfidSession.Visible = false;
                        break;
                        
                    case AttendanceSessionState.ActiveForStudents:
                        lblRfidSessionState.Text = "RFID Session State: Active - Students Can Sign In";
                        lblRfidSessionState.ForeColor = Color.Green;
                        lblRfidSessionInfo.Text = "Students can now scan RFID to sign in for attendance...";
                        if (btnForceEndRfidSession != null) btnForceEndRfidSession.Visible = true;
                        break;
                        
                    case AttendanceSessionState.WaitingForInstructorSignOut:
                        lblRfidSessionState.Text = "RFID Session State: Waiting for Instructor Sign-Out";
                        lblRfidSessionState.ForeColor = Color.Orange;
                        lblRfidSessionInfo.Text = "Instructor must scan RFID to open sign-out for students...";
                        if (btnForceEndRfidSession != null) btnForceEndRfidSession.Visible = true;
                        break;
                        
                    case AttendanceSessionState.ActiveForSignOut:
                        lblRfidSessionState.Text = "RFID Session State: Active - Students Can Sign Out";
                        lblRfidSessionState.ForeColor = Color.Blue;
                        lblRfidSessionInfo.Text = "Students can now scan RFID to sign out...";
                        if (btnForceEndRfidSession != null) btnForceEndRfidSession.Visible = true;
                        break;
                        
                    case AttendanceSessionState.WaitingForInstructorClose:
                        lblRfidSessionState.Text = "RFID Session State: Waiting for Instructor to Close";
                        lblRfidSessionState.ForeColor = Color.Orange;
                        lblRfidSessionInfo.Text = "Instructor must scan RFID to close the attendance session...";
                        if (btnForceEndRfidSession != null) btnForceEndRfidSession.Visible = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating RFID session state display: {ex.Message}");
            }
        }
        
        private void ProcessAttendanceScan(string userName)
        {
            try
            {
                // Update watchdog timestamp
                lastSuccessfulOperation = DateTime.Now;
                
                // Debouncing: Check if same user was processed recently
                var timeSinceLastProcess = DateTime.Now - lastProcessedTime;
                if (userName == lastProcessedUser && timeSinceLastProcess.TotalMilliseconds < DEBOUNCE_INTERVAL_MS)
                {
                    // Only show debouncing message occasionally to reduce spam
                    if (timeSinceLastProcess.TotalMilliseconds < 500) // Only show in first 500ms
                    {
                        Console.WriteLine($"⏳ {userName} - Please wait {Math.Ceiling((DEBOUNCE_INTERVAL_MS - timeSinceLastProcess.TotalMilliseconds) / 1000)} seconds");
                    }
                    SetStatusText($"⏳ Please wait {Math.Ceiling((DEBOUNCE_INTERVAL_MS - timeSinceLastProcess.TotalMilliseconds) / 1000)} seconds before scanning again.");
                    ScheduleNextGetBaseTemplate(500);
                    return;
                }
                
                // Update debouncing variables
                lastProcessedUser = userName;
                lastProcessedTime = DateTime.Now;
                
                // Console.WriteLine($"Processing attendance scan for: {userName}");
                
                // Get user information from database to determine user type
                var userInfo = GetUserInfoFromDatabase(userName);
                if (userInfo == null)
                {
                    SetStatusText($"❌ User {userName} not found in database.");
                    ScheduleNextGetBaseTemplate(1000);
                    return;
                }
                
                string userType = userInfo.UserType?.ToLower();
                // Use GUID stored in EmployeeId for DB operations (USERID in DB)
                string userGuid = userInfo.EmployeeId;
                
                // Console.WriteLine($"User type: {userType}, Session state: {currentSessionState}");
                
                // Process based on user type and current session state
                if (userType == "instructor")
                {
                    ProcessInstructorScan(userName, userGuid);
                }
                else if (userType == "student")
                {
                    ProcessStudentScan(userName, userGuid);
                }
                else
                {
                    SetStatusText($"❌ Unknown user type for {userName}. Only instructors and students can use attendance system.");
                    ScheduleNextGetBaseTemplate(1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing attendance scan: {ex.Message}");
                SetStatusText($"❌ Error processing attendance scan: {ex.Message}");
                ScheduleNextGetBaseTemplate(1000);
            }
        }
        
        private void ProcessInstructorScan(string userName, string userGuid)
        {
            try
            {
                SetStatusText($"Processing instructor scan for {userName}...");
                
                switch (currentSessionState)
                {
                    case AttendanceSessionState.Inactive:
                    case AttendanceSessionState.WaitingForInstructor:
                        // SCAN 1: Instructor starting session and signing in
                        Console.WriteLine($"🎯 INSTRUCTOR {userName} SCAN 1 - SESSION START / SIGN-IN");
                        SetStatusText($"Verifying instructor schedule for {userName}...");
                        
                        // IMPORTANT: Check if instructor has a scheduled class BEFORE starting session
                        if (dbManager != null)
                        {
                            var validationResult = dbManager.TryRecordAttendanceByGuid(userGuid, "Instructor Schedule Check");
                            
                            if (!validationResult.Success)
                            {
                                // Instructor doesn't have a schedule at this time - DENY session start
                                SetStatusText($"❌ {userName}: {validationResult.Reason}. Cannot start attendance session.");
                                Console.WriteLine($"❌ INSTRUCTOR {userName} DENIED: {validationResult.Reason}");
                                
                                // Record denial for display
                                var denialRecord = new Database.Models.AttendanceRecord
                                {
                                    UserId = 0,
                                    Username = userName,
                                    Timestamp = DateTime.Now,
                                    Action = "Instructor Sign-In (Session Start)",
                                    Status = $"Denied: {validationResult.Reason}"
                                };
                                attendanceRecords.Add(denialRecord);
                                UpdateAttendanceDisplay(denialRecord);
                                
                                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                return; // Exit without starting session
                            }
                            
                            // Instructor has valid schedule - proceed with session start
                        currentInstructorId = userGuid;
                            currentScheduleId = validationResult.ScheduleId ?? "manual_session";
                        currentSessionState = AttendanceSessionState.ActiveForStudents;
                        
                        // Clear any previous signed-in/out students for new session
                        signedInStudentGuids.Clear();
                        signedOutStudentGuids.Clear();
                        
                        // Update UI immediately
                        UpdateSessionStateDisplay();
                            SetStatusText($"✅ Instructor {userName} signed in. Session started for {validationResult.SubjectName}. Students can now sign in.");
                        
                            Console.WriteLine($"✅ SESSION STARTED - Students can now sign in for {validationResult.SubjectName}");
                        
                        // Record instructor's sign-in attendance (session start = instructor sign-in)
                        System.Threading.Tasks.Task.Run(async () => {
                            try
                            {
                                RecordAttendance(userName, "Instructor Sign-In (Session Start)");
                                
                                // Request lock control for instructor
                                await RequestLockControl(userGuid, "Instructor Sign-In (Session Start)");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Could not record instructor sign-in: {ex.Message}");
                            }
                        });
                        }
                        else
                        {
                            // No database manager - fallback to manual session (should not happen)
                            SetStatusText($"⚠️ Warning: Cannot verify schedule. Database not available.");
                            currentInstructorId = userGuid;
                            currentScheduleId = "manual_session";
                            currentSessionState = AttendanceSessionState.ActiveForStudents;
                            signedInStudentGuids.Clear();
                            UpdateSessionStateDisplay();
                            SetStatusText($"✅ Instructor {userName} signed in (unverified). Students can now sign in.");
                        }
                        
                        // Schedule next scan with optimized timing
                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                        break;
                    
                    case AttendanceSessionState.ActiveForStudents:
                        // SCAN 2: Instructor opening sign-out phase
                        Console.WriteLine($"🔄 INSTRUCTOR {userName} SCAN 2 - OPENING SIGN-OUT");
                        SetStatusText($"Opening sign-out phase...");
                        currentSessionState = AttendanceSessionState.ActiveForSignOut;
                        UpdateSessionStateDisplay();
                        SetStatusText($"✅ Instructor {userName} opened sign-out. Students can now sign out. Instructor: scan again to end session.");
                        
                        Console.WriteLine($"🔄 SIGN-OUT PHASE ACTIVE - Students can now sign out, instructor scan again to end");
                        
                        // No attendance record for this middle scan - just session control
                        
                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                        break;
                        
                    case AttendanceSessionState.ActiveForSignOut:
                    case AttendanceSessionState.WaitingForInstructorClose:
                        // SCAN 3: Instructor ending session and signing out
                        Console.WriteLine($"🔚 INSTRUCTOR {userName} SCAN 3 - SESSION END / SIGN-OUT");
                        SetStatusText($"Ending session for instructor {userName}...");
                        
                        // Update session state first
                        currentSessionState = AttendanceSessionState.Inactive;
                        currentInstructorId = null;
                        currentScheduleId = null;
                        
                        // Clear signed-in/out students when session ends
                        signedInStudentGuids.Clear();
                        signedOutStudentGuids.Clear();
                        
                        UpdateSessionStateDisplay();
                        SetStatusText($"✅ Instructor {userName} signed out. Session ended.");
                        
                        Console.WriteLine($"🔚 SESSION CLOSED - Instructor signed out, all students cleared");
                        
                        // Record instructor's sign-out attendance (session end = instructor sign-out)
                        System.Threading.Tasks.Task.Run(async () => {
                            try
                            {
                                RecordAttendance(userName, "Instructor Sign-Out (Session End)");
                                
                                // Request lock control for instructor
                                await RequestLockControl(userGuid, "Instructor Sign-Out (Session End)");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Could not record instructor sign-out: {ex.Message}");
                            }
                        });
                        
                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                        break;
                    
                    default:
                        SetStatusText($"❌ Invalid session state for instructor action.");
                        break;
                }
            }
            catch (Exception ex)
            {
                SetStatusText($"❌ Error processing instructor scan: {ex.Message}");
                Console.WriteLine($"Instructor scan error: {ex.Message}");
            }
            finally
            {
                // Always schedule next scan to keep the system responsive
                ScheduleNextGetBaseTemplate(1000);
            }
        }
        
        private void ProcessStudentScan(string userName, string userGuid)
        {
            try
            {
                SetStatusText($"Processing student scan for {userName}...");
                
                switch (currentSessionState)
                {
                    case AttendanceSessionState.ActiveForStudents:
                        // Check for verification timeout first
                        if (awaitingVerificationScan)
                        {
                            var verificationElapsed = DateTime.Now - verificationScanStartTime;
                            if (verificationElapsed.TotalSeconds > VERIFICATION_TIMEOUT_SECONDS)
                            {
                                // Timeout - reset verification state
                                Console.WriteLine($"⏱️ VERIFICATION TIMEOUT: {pendingVerificationUser} took too long");
                                SetStatusText($"⏱️ Verification timeout for {pendingVerificationUser}. Starting over...");
                                awaitingVerificationScan = false;
                                pendingVerificationUser = "";
                                verificationScanStartTime = DateTime.MinValue;
                                // Don't return - treat this as a new first scan
                            }
                        }
                        
                        // TWO-SCAN VERIFICATION for students
                        if (awaitingVerificationScan)
                        {
                            // This is the SECOND scan - verify it matches the first
                            if (userName == pendingVerificationUser)
                            {
                                // ✅ VERIFIED: Both scans match!
                                Console.WriteLine($"✅ VERIFICATION SUCCESS: {userName} (both scans match)");
                                SetStatusText($"✅ Verified: {userName}. Processing attendance...");
                                
                                // Reset verification state
                                awaitingVerificationScan = false;
                                pendingVerificationUser = "";
                                verificationScanStartTime = DateTime.MinValue;
                                
                                // NOW check if already signed in (after verification)
                                if (signedInStudentGuids.Contains(userGuid))
                                {
                                    SetStatusText($"⚠️ Student {userName} already signed in - allowing door access.");
                                    Console.WriteLine($"⚠️ STUDENT {userName} ALREADY SIGNED IN - ALLOWING DOOR ACCESS");
                                    
                                    System.Threading.Tasks.Task.Run(() => {
                                        try
                                        {
                                            RecordAttendance(userName, "Student Already Signed In - Door Access", false);
                                            
                                            // STILL SEND LOCK CONTROL REQUEST for already signed in students
                                            // This allows them to go in and out during the session
                                            System.Threading.Tasks.Task.Run(async () => {
                                                try
                                                {
                                                    await RequestLockControl(userGuid, "Student Already Signed In - Door Access");
                                                }
                                                catch (Exception lockEx)
                                                {
                                                    Console.WriteLine($"Lock control request failed for already signed in student: {lockEx.Message}");
                                                }
                                            });
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Warning: Could not record attendance: {ex.Message}");
                                        }
                                    });
                                }
                                else
                                {
                                    // Process verified sign-in
                                    ProcessVerifiedStudentSignIn(userName, userGuid);
                                }
                            }
                            else
                            {
                                // ❌ MISMATCH: Scans don't match!
                                Console.WriteLine($"❌ VERIFICATION FAILED: First={pendingVerificationUser}, Second={userName}");
                                SetStatusText($"❌ Verification failed! First scan: {pendingVerificationUser}, Second scan: {userName}. Please try again.");
                                
                                // Reset verification state
                                awaitingVerificationScan = false;
                                pendingVerificationUser = "";
                                verificationScanStartTime = DateTime.MinValue;
                            }
                        }
                        else
                        {
                            // This is the FIRST scan
                            // CHECK: Is student already signed in? (before starting verification)
                            if (signedInStudentGuids.Contains(userGuid))
                            {
                                // Already signed in - no need to verify but still allow door access
                                SetStatusText($"⚠️ Student {userName} already signed in - allowing door access.");
                                Console.WriteLine($"⚠️ STUDENT {userName} ALREADY SIGNED IN - ALLOWING DOOR ACCESS");
                                
                                System.Threading.Tasks.Task.Run(() => {
                                    try
                                    {
                                        RecordAttendance(userName, "Student Already Signed In - Door Access", false);
                                        
                                        // STILL SEND LOCK CONTROL REQUEST for already signed in students
                                        // This allows them to go in and out during the session
                                        System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestLockControl(userGuid, "Student Already Signed In - Door Access");
                                            }
                                            catch (Exception lockEx)
                                            {
                                                Console.WriteLine($"Lock control request failed for already signed in student: {lockEx.Message}");
                                            }
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Warning: Could not record attendance: {ex.Message}");
                                    }
                                });
                            }
                            else
                            {
                                // Not signed in yet - request verification scan
                                Console.WriteLine($"🔍 FIRST SCAN: {userName} - Requesting verification scan");
                                SetStatusText($"👆 First scan: {userName}. Please scan the SAME finger again to verify.");
                                
                                // Set verification state
                                awaitingVerificationScan = true;
                                pendingVerificationUser = userName;
                                verificationScanStartTime = DateTime.Now;
                            }
                        }
                        
                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                        break;
                        
                    case AttendanceSessionState.ActiveForSignOut:
                        // Student signing out (no verification needed for sign-out)
                        // If already signed out in this session, show message but still allow door access
                        if (signedOutStudentGuids.Contains(userGuid))
                        {
                            SetStatusText($"⚠️ Student {userName} already signed out - allowing door access.");
                            System.Threading.Tasks.Task.Run(() => {
                                try { 
                                    RecordAttendance(userName, "Student Already Signed Out - Door Access", false);
                                    
                                    // STILL SEND LOCK CONTROL REQUEST for already signed out students
                                    // This allows them to go in and out during the session
                                    System.Threading.Tasks.Task.Run(async () => {
                                        try
                                        {
                                            await RequestLockControl(userGuid, "Student Already Signed Out - Door Access");
                                        }
                                        catch (Exception lockEx)
                                        {
                                            Console.WriteLine($"Lock control request failed for already signed out student: {lockEx.Message}");
                                        }
                                    });
                                } catch {}
                            });
                            ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                            break;
                        }

                        // Validate and record via DB first, then update local state based on result
                        System.Threading.Tasks.Task.Run(() => {
                            try
                            {
                                var attempt = dbManager?.TryRecordAttendanceByGuid(userGuid, "Student Sign-Out");
                                this.Invoke(new Action(() => {
                                    if (attempt != null && attempt.Success)
                                    {
                                        // Success: update local state and UI
                                        signedInStudentGuids.Remove(userGuid);
                                        signedOutStudentGuids.Add(userGuid);
                                        SetStatusText($"✅ Student {userName} signed out.");

                                        // Add local record for display
                                        int userIdInt = 0;
                                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userGuid, out var u))
                                        {
                                            userIdInt = u.Id;
                                        }
                                        var local = new Database.Models.AttendanceRecord
                                        {
                                            UserId = userIdInt,
                                            Username = userName,
                                            Timestamp = DateTime.Now,
                                            Action = "Student Sign-Out",
                                            Status = "Success"
                                        };
                                        attendanceRecords.Add(local);
                                        UpdateAttendanceDisplay(local);
                                        
                                        // Request lock control for successful student sign-out
                                        System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestLockControl(userGuid, "Student Sign-Out");
                                            }
                                            catch (Exception lockEx)
                                            {
                                                Console.WriteLine($"Lock control request failed: {lockEx.Message}");
                                            }
                                        });
                                    }
                                    else
                                    {
                                        // Denied: do not mark as signed out
                                        var reason = attempt?.Reason ?? "Denied";
                                        SetStatusText($"❌ {userName}: {reason}");

                                        int userIdInt = 0;
                                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userGuid, out var u2))
                                        {
                                            userIdInt = u2.Id;
                                        }
                                        var local = new Database.Models.AttendanceRecord
                                        {
                                            UserId = userIdInt,
                                            Username = userName,
                                            Timestamp = DateTime.Now,
                                            Action = "Student Sign-Out",
                                            Status = $"Denied: {reason}"
                                        };
                                        attendanceRecords.Add(local);
                                        UpdateAttendanceDisplay(local);
                                    }
                                }));
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Could not record sign-out: {ex.Message}");
                                this.Invoke(new Action(() => SetStatusText($"❌ Error during sign-out: {ex.Message}")));
                            }
                        });
                        
                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                        break;
                        
                    case AttendanceSessionState.Inactive:
                    case AttendanceSessionState.WaitingForInstructor:
                        SetStatusText("❌ No active session. Instructor must start the session first.");
                        break;
                        
                    case AttendanceSessionState.WaitingForInstructorSignOut:
                    case AttendanceSessionState.WaitingForInstructorClose:
                        SetStatusText("❌ Session not ready for student actions. Please wait for instructor.");
                        break;
                        
                    default:
                        SetStatusText("❌ Invalid session state for student action.");
                        break;
                }
            }
            catch (Exception ex)
            {
                SetStatusText($"❌ Error processing student scan: {ex.Message}");
                Console.WriteLine($"Student scan error: {ex.Message}");
            }
            finally
            {
                // Always schedule next scan to keep the system responsive
                ScheduleNextGetBaseTemplate(1000);
            }
        }
        
        /// <summary>
        /// Helper method to process a verified student sign-in after two-scan verification passes
        /// </summary>
        private void ProcessVerifiedStudentSignIn(string userName, string userGuid)
        {
            // Record attendance and manage signed-in state by GUID
            System.Threading.Tasks.Task.Run(() => {
                try
                {
                    // Get user info for database validation
                    User user = null;
                    if (userLookupByUsername != null)
                    {
                        userLookupByUsername.TryGetValue(userName, out user);
                    }
                    
                    if (user != null && !string.IsNullOrEmpty(user.EmployeeId) && dbManager != null)
                    {
                        // Try to record attendance
                        var attempt = dbManager.TryRecordAttendanceByGuid(user.EmployeeId, "Student Sign-In");
                        
                        // Update UI with result
                        this.Invoke(new Action(() => {
                            if (attempt.Success)
                            {
                                // Mark as signed-in immediately to avoid race where next scan says "Never Signed In"
                                signedInStudentGuids.Add(userGuid);
                                SetStatusText($"✅ Student {userName} signed in successfully.");
                                Console.WriteLine($"✅ STUDENT {userName} SIGNED IN");
                                
                                // Record for display
                                var local = new Database.Models.AttendanceRecord
                                {
                                    UserId = user.Id,
                                    Username = userName,
                                    Timestamp = DateTime.Now,
                                    Action = "Student Sign-In",
                                    Status = "Success"
                                };
                                attendanceRecords.Add(local);
                                UpdateAttendanceDisplay(local);
                                
                                // Request lock control for successful student sign-in
                                System.Threading.Tasks.Task.Run(async () => {
                                    try
                                    {
                                        await RequestLockControl(user.EmployeeId, "Student Sign-In");
                                    }
                                    catch (Exception lockEx)
                                    {
                                        Console.WriteLine($"Lock control request failed: {lockEx.Message}");
                                    }
                                });
                            }
                            else
                            {
                                // DENIED: Ensure not marked as signed-in
                                signedInStudentGuids.Remove(userGuid);
                                SetStatusText($"❌ {userName}: {attempt.Reason}");
                                Console.WriteLine($"❌ STUDENT {userName} DENIED: {attempt.Reason}");
                                
                                // Record denial for display
                                var local = new Database.Models.AttendanceRecord
                                {
                                    UserId = user.Id,
                                    Username = userName,
                                    Timestamp = DateTime.Now,
                                    Action = "Student Sign-In",
                                    Status = $"Denied: {attempt.Reason}"
                                };
                                attendanceRecords.Add(local);
                                UpdateAttendanceDisplay(local);
                            }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not record attendance: {ex.Message}");
                    this.Invoke(new Action(() => {
                        SetStatusText($"❌ Error processing {userName}: {ex.Message}");
                    }));
                }
            });
        }
        
        // PERFORMANCE OPTIMIZED: O(1) dictionary lookup instead of O(n) LINQ search
        private User GetUserInfoFromDatabase(string userName)
        {
            try
            {
                if (dbManager == null) return null;
                
                // Use cached dictionary for instant O(1) lookup
                if (userLookupByUsername != null && userLookupByUsername.TryGetValue(userName, out User user))
                {
                    return user;
                }
                
                // Fallback: If cache is not built or user not found, return null
                // This should rarely happen after caches are built
                Console.WriteLine($"WARNING: User '{userName}' not found in cache. Cache entries: {userLookupByUsername?.Count ?? 0}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting user info: {ex.Message}");
                return null;
            }
        }


        private void OnEnrollmentComplete(bool success, int retCode)
        {
            try
            {
                if (success)
                {
                    SetEnrollProgress(100);
                    UpdateEnrollmentGuidance(4, "Enrollment complete! Saving your fingerprint...");
                    // Save user template to database
                    var userRecord = (UserRecord)m_OperationObj;
                    userRecord.Template = ((FutronicEnrollment)m_Operation).Template;
                    isEnrollmentActive = false;
                    try { enrollProgressTimer?.Stop(); } catch { }

					try
					{
						// Add fingerprint to existing user (don't create new user)
						if (selectedUser != null && !string.IsNullOrEmpty(selectedUser.EmployeeId))
						{
							bool fingerprintAdded = dbManager.AddFingerprintToExistingUser(selectedUser.EmployeeId, userRecord.Template);
							
							if (fingerprintAdded)
							{
								SetStatusText($"Enrollment successful! Fingerprint added for '{selectedUser.FirstName} {selectedUser.LastName}'.");
								UpdateEnrollmentGuidance(5, "Done! You can proceed to attendance.");
								
								// Refresh user data from database and update display
								try
								{
									// Refresh cloudUsers from database
									if (dbManager != null)
									{
										cloudUsers = dbManager.LoadAllUsers();
									}
									RefreshUserList(); // Update table display
								}
								catch (Exception ex)
								{
									SetStatusText($"Enrollment successful, but failed to refresh user list: {ex.Message}");
								}
								
								// Clear selection after refresh
								ClearSelection();
							}
							else
							{
								SetStatusText("Failed to add fingerprint to existing user.");
								UpdateEnrollmentGuidance(0, "Enrollment failed. Please try again.");
							}
						}
						else
						{
							SetStatusText("Error: No user selected for enrollment.");
							UpdateEnrollmentGuidance(0, "Please select a user first.");
						}

                        // Force refresh users immediately so identification sees the new template
                        try
                        {
                            SyncUsersFromCloud();
                            this.Invoke(new Action(() =>
                            {
                                RefreshUserList();
                                // If admin has fingerprint users tab open/access, refresh it too
                                LoadFingerprintUsers();
                            }));
                        }
                        catch (Exception ex)
                        {
                            SetStatusText($"Error refreshing users: {ex.Message}");
                        }
					}
					catch (Exception dbEx)
					{
						SetStatusText($"Enrollment completed but failed to save to database: {dbEx.Message}");
					}
                }
                else
                {
                    SetStatusText($"Enrollment failed: {FutronicSdkBase.SdkRetCode2Message(retCode)}");
                    UpdateEnrollmentGuidance(0, "Enrollment failed. Press 'Start Enrollment' to try again.");
                    isEnrollmentActive = false;
                    try { enrollProgressTimer?.Stop(); } catch { }
                }
            }
            catch (Exception ex)
            {
                SetStatusText($"Enrollment error: {ex.Message}");
                UpdateEnrollmentGuidance(0, "An error occurred. Try again.");
            }
            finally
            {
                // Reset enrollment state
                m_bEnrollmentInProgress = false;
                isEnrollmentActive = false;
                try { enrollProgressTimer?.Stop(); } catch { }
                
                UnregisterEnrollmentEvents();
                EnableControls(true);
                
                // Restart attendance if always-on is enabled (regardless of previous state)
                if (alwaysOnAttendance)
                {
                    System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                    {
                        if (!m_bExit && alwaysOnAttendance && !m_bEnrollmentInProgress)
                        {
                            this.Invoke(new Action(() => 
                            {
                                SetStatusText("Restarting attendance after enrollment...");
                                StartIdentification();
                            }));
                        }
                    });
                }
            }
        }

        private void OnGetBaseTemplateComplete(bool success, int retCode)
        {
            try
            {
                // Update watchdog timestamp
                lastSuccessfulOperation = DateTime.Now;
                
                // Check if the operation is still valid to prevent AccessViolationException
                if (m_AttendanceOperation == null || m_bExit)
                {
                    SetStatusText("Operation cancelled or disposed.");
                    ScheduleNextGetBaseTemplate(2000);
                    return;
                }
                
                // Note: isIdentifying flag is managed in StartIdentification, not here
                // This method is called as a result of template capture completion
                
                // Console.WriteLine($"OnGetBaseTemplateComplete: success={success}, retCode={retCode}");
                lastActivityTime = DateTime.Now;
                
                // POWER MANAGEMENT: Activity detected, exit idle mode
                lastScanAttemptTime = DateTime.Now;
                if (isInIdleMode)
                {
                    ExitIdleMode();
                }
                
                if (success)
                {
                    lastScanTime = DateTime.Now;
                    SetStatusText("Processing identification...");
                    
                    // Take a snapshot to avoid concurrent modifications
                    var users = m_IdentificationUsers != null ? new List<UserRecord>(m_IdentificationUsers) : null;
                    if (users == null || users.Count == 0)
                    {
                        SetStatusText("No users available for identification.");
                        ScheduleNextGetBaseTemplate(1000);
                        return;
                    }
                    
                    // Filter out users with invalid templates to prevent errors
                    var validUsers = new List<UserRecord>();
                    foreach (var user in users)
                    {
                        if (user != null && user.Template != null && user.Template.Length > 0)
                        {
                            validUsers.Add(user);
                        }
                    }
                    
                    if (validUsers.Count == 0)
                    {
                        SetStatusText("No users with valid fingerprint templates found.");
                        ScheduleNextGetBaseTemplate(1000);
                        return;
                    }
                    
                    var records = new FtrIdentifyRecord[validUsers.Count];
                    
                    for (int i = 0; i < validUsers.Count; i++)
                    {
                        records[i] = validUsers[i].GetRecord();
                    }
                    
                    // Update users reference to use filtered list
                    users = validUsers;

                    int matchIndex = -1;
                    int result = -1;
                    
                    // Safely call Identification with null checks
                    if (m_AttendanceOperation != null && !m_bExit)
                    {
                        try
                        {
                            result = m_AttendanceOperation.Identification(records, ref matchIndex);
                        }
                        catch (System.AccessViolationException ex)
                        {
                            Console.WriteLine($"AccessViolationException in Identification: {ex.Message}");
                            SetStatusText("SDK memory error - performing safe restart...");
                            
                            // Immediately dispose and restart to prevent further corruption
                            System.Threading.Tasks.Task.Run(() =>
                            {
                                try
                                {
                                    // Force dispose the corrupted operation
                                    if (m_AttendanceOperation != null)
                                    {
                                        m_AttendanceOperation.Dispose();
                                        m_AttendanceOperation = null;
                                    }
                                    
                                    // Wait a bit for cleanup
                                    System.Threading.Thread.Sleep(3000);
                                    
                                    // Restart on UI thread
                                    this.Invoke(new Action(() =>
                                    {
                                        if (!m_bExit && alwaysOnAttendance)
                                        {
                                            SafeRestartFingerprintOperation();
                                        }
                                    }));
                                }
                                catch (Exception restartEx)
                                {
                                    Console.WriteLine($"Error during safe restart: {restartEx.Message}");
                                }
                            });
                            return;
                        }
                        catch (Exception ex)
                        {
                            SetStatusText($"Identification error: {ex.Message}. Restarting...");
                            nextRestartDelayMs = 3000;
                            ScheduleNextGetBaseTemplate(nextRestartDelayMs);
                            return;
                        }
                    }
                    else
                    {
                        SetStatusText("Operation cancelled during identification.");
                        return;
                    }

                    if (result == FutronicSdkBase.RETCODE_OK)
                    {
                        // Validate index from SDK before using
                        if (matchIndex >= 0 && matchIndex < users.Count)
                        {
                            string userName = users[matchIndex].UserName;
                            
                            // Process attendance scan directly (remove double-scan for now)
                            try
                            {
                                ProcessAttendanceScan(userName);
                                SetStatusText($"✅ {userName} - Attendance recorded");
                            }
                            catch (Exception ex)
                            {
                                SetStatusText($"Error processing scan: {ex.Message}");
                            }
                            
                            ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                        }
                        else
                        {
                            SetStatusText("❌ Fingerprint not recognized. Please try again or enroll first.");
                            ScheduleNextGetBaseTemplate(1000);
                        }
                    }
                    else
                    {
                        SetStatusText("❌ Fingerprint not recognized. Please try again or enroll first.");
                        ScheduleNextGetBaseTemplate(1000);
                    }
                }
                else
                {
                    // Handle template capture failure
                    SetStatusText($"Template capture failed: {retCode}. Please try again.");
                    ScheduleNextGetBaseTemplate(2000);
                }
            }
            catch (System.AccessViolationException ex)
            {
                Console.WriteLine($"AccessViolationException in OnGetBaseTemplateComplete: {ex.Message}");
                SetStatusText("Fingerprint SDK error - restarting scanner...");
                
                // Reset the lock immediately
                lock (getBaseTemplateLock)
                {
                    isGetBaseTemplateInProgress = false;
                }
                
                // Restart the fingerprint operation to recover from memory corruption
                System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ => {
                    if (!m_bExit && alwaysOnAttendance)
                    {
                        this.Invoke(new Action(() => {
                            try
                            {
                                SafeRestartFingerprintOperation();
                            }
                            catch (Exception restartEx)
                            {
                                Console.WriteLine($"Error restarting fingerprint operation: {restartEx.Message}");
                            }
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnGetBaseTemplateComplete: {ex.Message}");
                SetStatusText($"Error in fingerprint processing: {ex.Message}");
                ScheduleNextGetBaseTemplate(3000);
            }
            finally
            {
                // Keep operation and events alive for continuous scanning
                isIdentifying = false;
                EnableControls(true);
                
                // THREAD SAFETY: Reset the GetBaseTemplate lock
                lock (getBaseTemplateLock)
                {
                    isGetBaseTemplateInProgress = false;
                }
            }
        }

        private void ScheduleNextIdentification(int delayMs)
        {
            try
            {
                if (!alwaysOnAttendance || m_bExit) return;
                if (identifyRetryTimer == null)
                {
                    identifyRetryTimer = new System.Windows.Forms.Timer();
                    identifyRetryTimer.Tick += (s, e) =>
                    {
                        identifyRetryTimer.Stop();
                        if (!m_bExit && alwaysOnAttendance)
                        {
                            StartIdentification();
                        }
                    };
                }
                identifyRetryTimer.Stop();
                identifyRetryTimer.Interval = Math.Max(500, delayMs);
                identifyRetryTimer.Start();
            }
            catch { }
        }

        private void StartCountdownTimer(double seconds)
        {
            try
            {
                // Stop any existing countdown timer
                if (countdownTimer != null)
                {
                    countdownTimer.Stop();
                    countdownTimer.Dispose();
                }

                countdownTimer = new System.Windows.Forms.Timer();
                
                if (seconds >= 1.0)
                {
                    // For seconds >= 1, show countdown
                    countdownTimer.Interval = 1000; // Update every second
                    int remainingSeconds = (int)seconds;
                    SetStatusText($"✅ First scan: {pendingUserName}. Please wait {remainingSeconds} seconds, then scan again...");
                    
                    countdownTimer.Tick += (s, e) =>
                    {
                        remainingSeconds--;
                        if (remainingSeconds > 0)
                        {
                            SetStatusText($"✅ First scan: {pendingUserName}. Please wait {remainingSeconds} seconds, then scan again...");
                        }
                        else
                        {
                            countdownTimer.Stop();
                            SetStatusText($"✅ First scan: {pendingUserName}. Ready for second scan - please scan again to confirm attendance.");
                            nextRestartDelayMs = 100;
                            ScheduleNextGetBaseTemplateForSecondScan(nextRestartDelayMs);
                        }
                    };
                }
                else
                {
                    // For milliseconds, just delay
                    countdownTimer.Interval = (int)(seconds * 1000);
                    SetStatusText($"✅ First scan: {pendingUserName}. Please wait...");
                    
                    countdownTimer.Tick += (s, e) =>
                    {
                        countdownTimer.Stop();
                        SetStatusText($"✅ First scan: {pendingUserName}. Ready for second scan - please scan again to confirm attendance.");
                        nextRestartDelayMs = 100;
                        ScheduleNextGetBaseTemplateForSecondScan(nextRestartDelayMs);
                    };
                }
                
                countdownTimer.Start();
            }
            catch
            {
                // Fallback to simple delay
                SetStatusText($"✅ First scan: {pendingUserName}. Please wait, then scan again...");
                nextRestartDelayMs = 500;
                ScheduleNextGetBaseTemplateForSecondScan(nextRestartDelayMs);
            }
        }

        private void ScheduleNextGetBaseTemplateForSecondScan(int delayMs)
        {
            try
            {
                if (!alwaysOnAttendance || m_bExit) return;
                
                // Use Task.Delay instead of Timer for more reliable operation
                System.Threading.Tasks.Task.Delay(Math.Max(200, delayMs)).ContinueWith(_ =>
                {
                    if (!m_bExit && alwaysOnAttendance)
                    {
                        this.Invoke(new Action(() =>
                        {
                            RestartIdentification(false); // Don't reset confirmation state
                        }));
                    }
                });
            }
            catch
            {
                // Immediate fallback
                if (!m_bExit && alwaysOnAttendance)
                {
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        System.Threading.Thread.Sleep(1000);
                        if (!m_bExit && alwaysOnAttendance)
                        {
                            this.Invoke(new Action(() =>
                            {
                                RestartIdentification(false);
                            }));
                        }
                    });
                }
            }
        }

        // OPTIMIZED: Continuous scanning with smart power management
        private void ScheduleNextGetBaseTemplate(int delayMs)
        {
            try
            {
                if (!alwaysOnAttendance || m_bExit || m_AttendanceOperation == null) return;
                
                // Update activity timestamp
                lastScanAttemptTime = DateTime.Now;
                
                // Determine scan interval based on idle state
                int scanInterval = isInIdleMode ? SCAN_INTERVAL_IDLE_MS : SCAN_INTERVAL_ACTIVE_MS;
                
                // Use consistent, optimized delay
                int actualDelay = Math.Max(scanInterval, delayMs);
                
                // Schedule next scan without full restart (much faster and more efficient)
                System.Threading.Tasks.Task.Delay(actualDelay).ContinueWith(_ =>
                {
                    if (m_bExit || !alwaysOnAttendance || m_AttendanceOperation == null) return;
                    
                    try
                    {
                        // Update watchdog - system is alive
                        lastSuccessfulOperation = DateTime.Now;
                        
                        // Check if we should enter idle mode
                        var timeSinceLastScan = DateTime.Now - lastScanAttemptTime;
                        if (timeSinceLastScan.TotalSeconds > IDLE_TIMEOUT_SECONDS && !isInIdleMode)
                        {
                            EnterIdleMode();
                        }
                        else if (timeSinceLastScan.TotalSeconds <= IDLE_TIMEOUT_SECONDS && isInIdleMode)
                        {
                            ExitIdleMode();
                        }
                        
                        this.Invoke(new Action(() =>
                        {
                            if (m_AttendanceOperation != null && !m_bExit && !m_bEnrollmentInProgress)
                            {
                                // THREAD-SAFE: Prevent concurrent GetBaseTemplate calls
                                lock (getBaseTemplateLock)
                                {
                                    // Skip if already in progress
                                    if (isGetBaseTemplateInProgress)
                                    {
                                        Console.WriteLine("GetBaseTemplate already in progress, skipping this call");
                                        return;
                                    }
                                    
                                    isGetBaseTemplateInProgress = true;
                                }
                                
                                // OPTIMIZED: Just call GetBaseTemplate directly, no restart needed
                                try
                                {
                                    m_AttendanceOperation.GetBaseTemplate();
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"GetBaseTemplate error: {ex.Message}");
                                    lock (getBaseTemplateLock)
                                    {
                                        isGetBaseTemplateInProgress = false;
                                    }
                                    // Only restart on actual error
                                    RestartIdentification();
                                }
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Schedule error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ScheduleNextGetBaseTemplate error: {ex.Message}");
            }
        }
        
        // POWER MANAGEMENT: Enter low-power idle mode
        private void EnterIdleMode()
        {
            try
            {
                if (isInIdleMode) return;
                isInIdleMode = true;
                Console.WriteLine("🔋 Entering idle mode - reduced scanning frequency to save power");
                this.Invoke(new Action(() =>
                {
                    SetStatusText("💤 Idle mode - place finger to activate");
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EnterIdleMode error: {ex.Message}");
            }
        }
        
        // POWER MANAGEMENT: Exit idle mode and return to active scanning
        private void ExitIdleMode()
        {
            try
            {
                if (!isInIdleMode) return;
                isInIdleMode = false;
                Console.WriteLine("⚡ Exiting idle mode - returning to active scanning");
                this.Invoke(new Action(() =>
                {
                    SetStatusText("✅ Active - ready for fingerprint scanning");
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ExitIdleMode error: {ex.Message}");
            }
        }

        private void IdentifyRetryTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                identifyRetryTimer.Stop();
                if (m_bExit || !alwaysOnAttendance)
                    return;

                RestartIdentification();
            }
            catch
            {
                // Try again shortly if device busy
                identifyRetryTimer.Interval = 1000;
                identifyRetryTimer.Start();
            }
        }

        private void RestartIdentification(bool resetConfirmationState = true)
        {
            try
            {
                // Check if enrollment is in progress
                if (m_bEnrollmentInProgress)
                {
                    SetStatusText("Enrollment in progress. Attendance paused.");
                    return;
                }

                lastActivityTime = DateTime.Now;
                
                // Only reset confirmation state when explicitly requested (not during second scan)
                if (resetConfirmationState)
                {
                    awaitingSecondScan = false;
                    pendingUserName = "";
                    firstScanTime = DateTime.MinValue;
                    lastScanTime = DateTime.MinValue;
                    
                    // Stop any countdown timer
                    if (countdownTimer != null)
                    {
                        countdownTimer.Stop();
                        countdownTimer.Dispose();
                        countdownTimer = null;
                    }
                }
                
                // Refresh user list for new enrollments
                m_IdentificationUsers = LoadUsers();
                if (m_AttendanceOperation != null)
                {
                    if (!resetConfirmationState && awaitingSecondScan)
                    {
                        // Don't change status message if we're in the middle of confirmation
                        // Just restart the device
                    }
                    else
                    {
                        SetStatusText("Ready for next attendance. Scan your fingerprint twice to confirm.");
                    }
                    try
                    {
                        // Ensure we're on the UI thread for SDK operations
                        if (this.InvokeRequired)
                        {
                            this.Invoke(new Action(() =>
                            {
                                try
                                {
                                    // THREAD-SAFE: Use the same locking mechanism
                                    lock (getBaseTemplateLock)
                                    {
                                        if (isGetBaseTemplateInProgress)
                                        {
                                            Console.WriteLine("GetBaseTemplate already in progress during restart, skipping");
                                            return;
                                        }
                                        isGetBaseTemplateInProgress = true;
                                    }
                                    
                                    m_AttendanceOperation.GetBaseTemplate();
                                    m_bAttendanceActive = true;
                                }
                                catch (Exception ex)
                                {
                                    SetStatusText($"ERROR: Failed to restart fingerprint scanning: {ex.Message}");
                                    EnableControls(true);
                                    m_bAttendanceActive = false;
                                    
                                    lock (getBaseTemplateLock)
                                    {
                                        isGetBaseTemplateInProgress = false;
                                    }
                                }
                            }));
                        }
                        else
                        {
                            // THREAD-SAFE: Use the same locking mechanism
                            lock (getBaseTemplateLock)
                            {
                                if (isGetBaseTemplateInProgress)
                                {
                                    Console.WriteLine("GetBaseTemplate already in progress during restart, skipping");
                                    return;
                                }
                                isGetBaseTemplateInProgress = true;
                            }
                            
                            try
                            {
                                m_AttendanceOperation.GetBaseTemplate();
                                m_bAttendanceActive = true;
                            }
                            catch (Exception ex)
                            {
                                SetStatusText($"ERROR: Failed to restart fingerprint scanning: {ex.Message}");
                                EnableControls(true);
                                m_bAttendanceActive = false;
                                
                                lock (getBaseTemplateLock)
                                {
                                    isGetBaseTemplateInProgress = false;
                                }
                                throw;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        SetStatusText($"ERROR: Failed to restart fingerprint scanning: {ex.Message}");
                        EnableControls(true);
                        m_bAttendanceActive = false;
                        return;
                    }
                }
                else
                {
                    StartIdentification();
                }
            }
            catch
            {
                // Fallback with a simple delay
                System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ =>
                {
                    if (!m_bExit && alwaysOnAttendance && !m_bEnrollmentInProgress)
                    {
                        this.Invoke(new Action(() => RestartIdentification()));
                    }
                });
            }
        }

        // PERFORMANCE OPTIMIZED: Use cached dictionary lookup
        private void RecordAttendance(string userName, string action = null, bool sendToDatabase = true)
        {
            try
            {
                // Console.WriteLine($"Recording attendance for {userName} with action {action}");
                
                // PERFORMANCE: Use O(1) dictionary lookup instead of O(n) LINQ search
                User user = null;
                if (userLookupByUsername != null)
                {
                    userLookupByUsername.TryGetValue(userName, out user);
                }
                
                if (user != null)
                {
                    // Use the GUID stored in EmployeeId field for database recording
                    string userGuid = user.EmployeeId;
                    string actionToRecord = action ?? DetermineAction(userName);
                    
                    // Create local record but don't show it until we have the final result
                    var local = new Database.Models.AttendanceRecord
                    {
                        UserId = user.Id,
                        Username = userName,
                        Timestamp = DateTime.Now,
                        Action = actionToRecord,
                        Status = "Processing..." // This won't be shown in UI
                    };
                    
                    if (sendToDatabase && !string.IsNullOrEmpty(userGuid) && dbManager != null)
                    {
                        // Start database operation asynchronously without waiting
                        System.Threading.Tasks.Task.Run(() => {
                            try
                            {
                                var attempt = dbManager.TryRecordAttendanceByGuid(userGuid, actionToRecord);
                                
                                // Update the record on UI thread with final result
                                this.Invoke(new Action(() => {
                                    local.Status = attempt.Success ? "Success" : $"Denied: {attempt.Reason}";
                                    // Only add to records and display when we have the final result
                                    attendanceRecords.Add(local);
                                    UpdateAttendanceDisplay(local);
                                    
                                    if (!attempt.Success)
                                    {
                                        Console.WriteLine($"ATTENDANCE DENIED - Full error: {attempt.Reason}");
                                    }
                                    else
                                    {
                                        // If attendance successful, check if we need to control the lock
                                        System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestLockControl(userGuid, actionToRecord);
                                            }
                                            catch (Exception lockEx)
                                            {
                                                Console.WriteLine($"Lock control request failed: {lockEx.Message}");
                                            }
                                        });
                                    }
                                }));
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Database operation failed: {ex.Message}");
                                this.Invoke(new Action(() => {
                                    local.Status = $"Error: {ex.Message}";
                                    // Only add to records and display when we have the final result
                                    attendanceRecords.Add(local);
                                    UpdateAttendanceDisplay(local);
                                }));
                            }
                        });
                        
                        // Don't show processing status - wait for final result
                    }
                    else
                    {
                        // No database operation or skip database, show final result immediately
                        local.Status = sendToDatabase ? "Success (No DB)" : "Already Signed In";
                        attendanceRecords.Add(local);
                        UpdateAttendanceDisplay(local);
                    }
                    // Console.WriteLine($"Attendance recorded for {userName}: {local.Status}");
                }
                else
                {
                    Console.WriteLine($"User not found in database: {userName}");
                    SetStatusText($"❌ User not found in database: {userName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Attendance recording error: {ex.Message}");
                SetStatusText($"Error recording attendance: {ex.Message}");
            }
        }

        private string DetermineAction(string userName)
        {
            // Simple logic: if last action was check-in within 12 hours, then check-out
            var lastRecord = attendanceRecords.FindLast(r => r.Username == userName);
            
            if (lastRecord != null && 
                lastRecord.Action == "Check In" && 
                (DateTime.Now - lastRecord.Timestamp).TotalHours < 12)
            {
                return "Check Out";
            }
            
            return "Check In";
        }

        private void UpdateAttendanceDisplay(Database.Models.AttendanceRecord record)
        {
            // Ensure UI updates happen on the UI thread
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateAttendanceDisplay(record)));
                return;
            }
            
            try
            {
                if (dgvAttendance == null)
                    return;

                // Insert at top
                var displayStatus = string.IsNullOrEmpty(record.Status) ? "Success" : record.Status;
                int rowIndex = 0;
                dgvAttendance.Rows.Insert(rowIndex, record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"), record.Username, record.Action, displayStatus);

                // Tooltips for long content
                var row = dgvAttendance.Rows[rowIndex];
                row.Cells[0].ToolTipText = record.Timestamp.ToString("f");
                row.Cells[1].ToolTipText = record.Username ?? "";
                row.Cells[2].ToolTipText = record.Action ?? "";
                row.Cells[3].ToolTipText = displayStatus;

                // Row coloring (subtle)
                if (!string.IsNullOrEmpty(record.Status) && record.Status.StartsWith("Denied"))
                {
                    row.DefaultCellStyle.BackColor = Color.MistyRose;
                    row.DefaultCellStyle.ForeColor = Color.Black;
                }
                else if (record.Action == "Check In")
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(230, 255, 230);
                    row.DefaultCellStyle.ForeColor = Color.Black;
                }
                else
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(230, 240, 255);
                    row.DefaultCellStyle.ForeColor = Color.Black;
                }

                // Keep only last 200 rows
                while (dgvAttendance.Rows.Count > 200)
                {
                    dgvAttendance.Rows.RemoveAt(dgvAttendance.Rows.Count - 1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating attendance display: {ex.Message}");
            }
        }

        // Cache for discovered ESP32 devices
        private static Dictionary<string, string> discoveredESP32Devices = new Dictionary<string, string>();
        private static DateTime lastDiscoveryTime = DateTime.MinValue;

        // Helper method to determine if lock should open for user
        private bool ShouldOpenLockForUser(User user)
        {
            if (user.UserType?.ToLower() == "instructor")
                return true;
            
            if (user.UserType?.ToLower() == "student")
            {
                // Students can open door during active session (including already signed in students)
                return currentSessionState == AttendanceSessionState.ActiveForStudents ||
                       currentSessionState == AttendanceSessionState.ActiveForSignOut ||
                       currentSessionState == AttendanceSessionState.WaitingForInstructorSignOut;
            }
            
            return false;
        }

        // Request lock control for fingerprint scans - Auto-discover and send command to ESP32
        private async Task RequestLockControl(string userGuid, string action)
        {
            try
            {
                Console.WriteLine("=== LOCK CONTROL REQUEST START ===");
                Console.WriteLine($"User GUID: {userGuid}");
                Console.WriteLine($"Action: {action}");

                // Get user info to check if instructor
                User user = null;
                if (userLookupByGuid != null)
                {
                    userLookupByGuid.TryGetValue(userGuid, out user);
                }

                if (user == null)
                {
                    Console.WriteLine("❌ User not found");
                    return;
                }

                Console.WriteLine($"User: {user.FirstName} {user.LastName} - Type: {user.UserType}");
                Console.WriteLine($"Current Session State: {currentSessionState}");

                // Check if user should be allowed to open lock
                if (!ShouldOpenLockForUser(user))
                {
                    Console.WriteLine($"⚠️ User {user.FirstName} {user.LastName} ({user.UserType}) - access denied");
                    Console.WriteLine($"⚠️ Session State: {currentSessionState} - Not allowed for this user type");
                    return;
                }
                
                Console.WriteLine($"✅ User {user.FirstName} {user.LastName} ({user.UserType}) - access granted");

                // Determine lock action - both sign-in and sign-out should OPEN the door
                // Students need door access for both entering (sign-in) and leaving (sign-out)
                string lockAction = "open"; // Always open door for valid authentication
                Console.WriteLine($"Lock Action: {lockAction} (Always open for valid authentication)");

                // Auto-discover ESP32 on the network
                string esp32Ip = await DiscoverESP32();
                
                if (string.IsNullOrEmpty(esp32Ip))
                {
                    Console.WriteLine("❌ No ESP32 device found on network");
                    this.Invoke(new Action(() => {
                        SetStatusText("❌ No lock controller found");
                    }));
                    return;
                }

                string esp32Url = $"http://{esp32Ip}/api/lock-control";
                Console.WriteLine($"Sending to ESP32: {esp32Url}");

                // Create payload for ESP32
                var payload = new
                {
                    action = lockAction,
                    user = $"{user.FirstName} {user.LastName}",
                    userType = user.UserType?.ToLower(),
                    sessionActive = currentSessionState == AttendanceSessionState.ActiveForStudents || 
                                   currentSessionState == AttendanceSessionState.ActiveForSignOut
                };

                var json = JsonSerializer.Serialize(payload);
                Console.WriteLine($"Payload: {json}");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    
                    // Add API key to request header for security
                    string apiKey = "LDCU_IOT_2025_SECURE_KEY_XYZ123"; // Must match ESP32 API key
                    client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
                    Console.WriteLine($"Using API Key: {apiKey.Substring(0, 10)}...");
                    
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    var response = await client.PostAsync(esp32Url, content);
                    
                    Console.WriteLine($"ESP32 Response Status: {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"ESP32 Response: {responseContent}");
                        
                        if (lockAction == "open")
                        {
                            Console.WriteLine("🔓 Door unlocked for instructor");
                            this.Invoke(new Action(() => {
                                SetStatusText("🔓 Door unlocked");
                            }));
                        }
                        else
                        {
                            Console.WriteLine("🔒 Door locked by instructor");
                            this.Invoke(new Action(() => {
                                SetStatusText("🔒 Door locked");
                            }));
                        }
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"❌ ESP32 request failed: {response.StatusCode}");
                        Console.WriteLine($"Error: {errorContent}");
                        
                        this.Invoke(new Action(() => {
                            SetStatusText($"❌ Lock control failed: {response.StatusCode}");
                        }));
                    }
                }
                
                Console.WriteLine("=== LOCK CONTROL REQUEST END ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error requesting lock control: {ex.Message}");
                
                this.Invoke(new Action(() => {
                    SetStatusText($"❌ Lock error: {ex.Message}");
                }));
            }
        }

        // Request lock control for RFID scans - Auto-discover and send command to ESP32 RFID endpoint
        private async Task RequestRfidLockControl(string userGuid, string action, string rfidData)
        {
            try
            {
                Console.WriteLine("=== RFID LOCK CONTROL REQUEST START ===");
                Console.WriteLine($"User GUID: {userGuid}");
                Console.WriteLine($"Action: {action}");
                Console.WriteLine($"RFID Data: {rfidData}");

                // Get user info to check if instructor
                User user = null;
                if (userLookupByGuid != null)
                {
                    userLookupByGuid.TryGetValue(userGuid, out user);
                }

                if (user == null)
                {
                    Console.WriteLine("❌ User not found");
                    return;
                }

                Console.WriteLine($"User: {user.FirstName} {user.LastName} - Type: {user.UserType}");
                Console.WriteLine($"Current Session State: {currentSessionState}");

                // Check if user should be allowed to open lock
                if (!ShouldOpenLockForUser(user))
                {
                    Console.WriteLine($"⚠️ User {user.FirstName} {user.LastName} ({user.UserType}) - access denied");
                    Console.WriteLine($"⚠️ Session State: {currentSessionState} - Not allowed for this user type");
                    return;
                }
                
                Console.WriteLine($"✅ User {user.FirstName} {user.LastName} ({user.UserType}) - access granted");

                // For RFID scans, always open door for valid authentication
                // Students need door access for both entering (sign-in) and leaving (sign-out)
                Console.WriteLine($"RFID Lock Action: open (Always open for valid RFID authentication)");

                // Auto-discover ESP32 on the network
                string esp32Ip = await DiscoverESP32();
                
                if (string.IsNullOrEmpty(esp32Ip))
                {
                    Console.WriteLine("❌ No ESP32 device found on network");
                    this.Invoke(new Action(() => {
                        SetRfidStatusText("❌ No lock controller found");
                    }));
                    return;
                }

                string esp32Url = $"http://{esp32Ip}/api/rfid-scan";
                Console.WriteLine($"Sending RFID scan to ESP32: {esp32Url}");

                // Create payload for ESP32 RFID endpoint
                var payload = new
                {
                    rfid_data = rfidData,
                    user = $"{user.FirstName} {user.LastName}",
                    userType = user.UserType?.ToLower(),
                    sessionActive = currentSessionState == AttendanceSessionState.ActiveForStudents || 
                                   currentSessionState == AttendanceSessionState.ActiveForSignOut
                };

                var json = JsonSerializer.Serialize(payload);
                Console.WriteLine($"RFID Payload: {json}");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    
                    // Add API key to request header for security
                    string apiKey = "LDCU_IOT_2025_SECURE_KEY_XYZ123"; // Must match ESP32 API key
                    client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
                    Console.WriteLine($"Using API Key: {apiKey.Substring(0, 10)}...");
                    
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    var response = await client.PostAsync(esp32Url, content);
                    
                    Console.WriteLine($"ESP32 RFID Response Status: {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"ESP32 RFID Response: {responseContent}");
                        
                        Console.WriteLine("🔓 Door unlocked for RFID scan");
                        this.Invoke(new Action(() => {
                            SetRfidStatusText("🔓 Door unlocked");
                        }));
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"❌ ESP32 RFID request failed: {response.StatusCode}");
                        Console.WriteLine($"Error: {errorContent}");
                        
                        this.Invoke(new Action(() => {
                            SetRfidStatusText($"❌ RFID lock control failed: {response.StatusCode}");
                        }));
                    }
                }
                
                Console.WriteLine("=== RFID LOCK CONTROL REQUEST END ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error requesting RFID lock control: {ex.Message}");
                
                this.Invoke(new Action(() => {
                    SetRfidStatusText($"❌ RFID lock error: {ex.Message}");
                }));
            }
        }

        // Auto-discover ESP32 on the local network
        private async Task<string> DiscoverESP32()
        {
            try
            {
                // Use cached result if recent (within 5 minutes)
                if (discoveredESP32Devices.Count > 0 && 
                    (DateTime.Now - lastDiscoveryTime).TotalMinutes < 5)
                {
                    var cachedIp = discoveredESP32Devices.Values.FirstOrDefault();
                    Console.WriteLine($"Using cached ESP32 IP: {cachedIp}");
                    return cachedIp;
                }

                Console.WriteLine("🔍 Discovering ESP32 devices on network...");

                // Get local network range - prefer WiFi/Ethernet with valid IP
                var allIps = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName())
                    .AddressList
                    .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Where(ip => !ip.ToString().StartsWith("169.254")) // Skip APIPA
                    .Where(ip => !ip.ToString().StartsWith("192.168.56")) // Skip VirtualBox
                    .Where(ip => !ip.ToString().StartsWith("192.168.198")) // Skip VMware
                    .Where(ip => !ip.ToString().StartsWith("192.168.240")) // Skip VMware
                    .ToList();

                Console.WriteLine($"Found {allIps.Count} valid network adapters:");
                foreach (var ip in allIps)
                {
                    Console.WriteLine($"  - {ip}");
                }

                // Prefer 192.168.1.x or 192.168.0.x (common home networks)
                var localIp = allIps.FirstOrDefault(ip => ip.ToString().StartsWith("192.168.1.") || ip.ToString().StartsWith("192.168.0."))
                           ?? allIps.FirstOrDefault();

                if (localIp == null)
                {
                    Console.WriteLine("❌ Could not determine local IP (no valid network connection)");
                    Console.WriteLine("⚠️ Your computer may not be connected to WiFi/network");
                    Console.WriteLine("💡 Trying common network ranges...");
                    
                    // Try common network ranges as fallback
                    var commonRanges = new[] { "192.168.1", "192.168.0", "192.168.100", "10.0.0" };
                    foreach (var range in commonRanges)
                    {
                        Console.WriteLine($"Trying range: {range}.x");
                        var found = await ScanNetworkRange(range);
                        if (!string.IsNullOrEmpty(found))
                        {
                            return found;
                        }
                    }
                    
                    return null;
                }

                string networkPrefix = string.Join(".", localIp.ToString().Split('.').Take(3));
                Console.WriteLine($"Local IP: {localIp}");
                Console.WriteLine($"Scanning network: {networkPrefix}.x");
                
                return await ScanNetworkRange(networkPrefix);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Discovery error: {ex.Message}");
                return null;
            }
        }

        // Scan a specific network range for ESP32
        private async Task<string> ScanNetworkRange(string networkPrefix)
        {
            try
            {

                // Scan common IP ranges (fast scan of likely IPs)
                var likelyIPs = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 20, 50, 100, 101, 102, 200, 254 };
                
                Console.WriteLine($"Scanning IPs: {string.Join(", ", likelyIPs.Select(ip => $"{networkPrefix}.{ip}"))}");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMilliseconds(500); // Fast timeout

                    foreach (int lastOctet in likelyIPs)
                    {
                        string testIp = $"{networkPrefix}.{lastOctet}";
                        
                        try
                        {
                            Console.WriteLine($"  Checking {testIp}...");
                            var response = await client.GetAsync($"http://{testIp}/api/health");
                            
                            if (response.IsSuccessStatusCode)
                            {
                                var content = await response.Content.ReadAsStringAsync();
                                Console.WriteLine($"  Response from {testIp}: {content.Substring(0, Math.Min(100, content.Length))}...");
                                
                                // Check if it's an ESP32 lock controller
                                if (content.Contains("ESP32") && content.Contains("Lock"))
                                {
                                    Console.WriteLine($"✅ Found ESP32 at: {testIp}");
                                    discoveredESP32Devices[testIp] = testIp;
                                    lastDiscoveryTime = DateTime.Now;
                                    return testIp;
                                }
                                else
                                {
                                    Console.WriteLine($"  ❌ {testIp} responded but not an ESP32 Lock Controller");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Ignore connection errors, continue scanning
                            Console.WriteLine($"  ✗ {testIp} - {ex.Message.Split('\n')[0]}");
                        }
                    }
                }

                Console.WriteLine($"⚠️ No ESP32 found in {networkPrefix}.x");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Scan error: {ex.Message}");
                return null;
            }
        }

        private void EnableControls(bool enable)
        {
            if (m_bExit) return;

            if (this.InvokeRequired)
            {
                this.Invoke(new Action<bool>(EnableControls), enable);
            }
            else
            {
                btnEnroll.Enabled = enable;
                btnIdentify.Enabled = enable;
                btnStop.Enabled = !enable;
                // Enable/disable the new form fields
                if (txtFirstName != null) txtFirstName.Enabled = enable;
                if (txtLastName != null) txtLastName.Enabled = enable;
                if (txtEmail != null) txtEmail.Enabled = enable;
                if (txtPassword != null) txtPassword.Enabled = enable;
                if (cmbUserType != null) cmbUserType.Enabled = enable;
                if (cmbStatus != null) cmbStatus.Enabled = enable;
                if (txtStudentId != null) txtStudentId.Enabled = enable;
                if (txtFacultyId != null) txtFacultyId.Enabled = enable;
                if (cmbYearLevel != null) cmbYearLevel.Enabled = enable;
                if (txtDepartment != null) txtDepartment.Enabled = enable;
            }
        }

        private void SetStatusText(string text)
        {
            if (m_bExit) return;

            if (this.InvokeRequired)
            {
                try
                {
                    this.BeginInvoke(new Action<string>(SetStatusText), text);
                }
                catch
                {
                    // Ignore if form is disposing
                }
            }
            else
            {
                try
                {
                    // Only update UI controls if they exist
                    if (txtStatus != null)
                    {
                        // Append new message to create a log-like behavior
                        string newMessage = $"{DateTime.Now:HH:mm:ss} - {text}";
                        if (string.IsNullOrEmpty(txtStatus.Text))
                        {
                            txtStatus.Text = newMessage;
                        }
                        else
                        {
                            txtStatus.Text += Environment.NewLine + newMessage;
                        }
                        
                        // Auto-scroll to bottom to show latest message
                        txtStatus.SelectionStart = txtStatus.Text.Length;
                        txtStatus.ScrollToCaret();
                    }
                    // Don't call this.Update() - it forces synchronous repaint which can freeze the UI
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error setting status text: {ex.Message}");
                }
            }
        }
        
        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                var now = DateTime.Now;
                if (lblLiveTime != null)
                    lblLiveTime.Text = $"Time: {now:HH:mm:ss}";
                if (lblLiveDay != null)
                    lblLiveDay.Text = $"Day: {now.DayOfWeek} ({now:yyyy-MM-dd})";
            }
            catch (Exception ex)
            {
                // Handle any errors silently to prevent timer crashes
                Console.WriteLine($"Clock timer error: {ex.Message}");
            }
        }

        // Enrollment guidance helpers
        private void SetEnrollProgress(int value)
        {
            try
            {
                if (enrollProgressBar == null) return;
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action<int>(SetEnrollProgress), value);
                }
                else
                {
                    enrollProgressBar.Value = Math.Max(enrollProgressBar.Minimum, Math.Min(enrollProgressBar.Maximum, value));
                }
            }
            catch { }
        }

        private void UpdateEnrollmentGuidance(int step, string message)
        {
            try
            {
                if (lblEnrollStep == null) return;
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action<int, string>(UpdateEnrollmentGuidance), step, message);
                }
                else
                {
                    lblEnrollStep.Text = message;
                }
            }
            catch { }
        }

        // Separate event handlers for enrollment
        private void OnEnrollmentPutOn(FTR_PROGRESS progress)
        {
            // Filter out false positive detections
            if (!IsValidFingerDetection())
                return;
                
            SetStatusText("Enrollment: Finger detected. Please hold steady...");
            UpdateEnrollmentGuidance(2, "Good! Keep your thumb pressed until instructed to lift.");
            try
            {
                // SDK does not expose frame count here; use a steady bump
                int bump = 8;
                enrollProgressTarget = Math.Max(enrollProgressTarget, Math.Min(90, (enrollProgressBar?.Value ?? 0) + bump));
            }
            catch { }
        }

        private void OnEnrollmentTakeOff(FTR_PROGRESS progress)
        {
            SetStatusText("Enrollment: Finger removed. Please place finger again.");
            UpdateEnrollmentGuidance(3, "Lift your thumb. When prompted, press again for the next sample.");
            try
            {
                enrollProgressTarget = Math.Max(enrollProgressTarget, Math.Min(95, (enrollProgressBar?.Value ?? 0) + 3));
            }
            catch { }
        }

        private void OnEnrollmentUpdateScreenImage(Bitmap bitmap)
        {
            if (pictureFingerprint != null && bitmap != null)
            {
                pictureFingerprint.Image = bitmap;
            }
            try
            {
                if (isEnrollmentActive)
                {
                    enrollProgressTarget = Math.Min(98, enrollProgressTarget + 1);
                }
            }
            catch { }
        }

        private bool OnEnrollmentFakeSource(FTR_PROGRESS progress)
        {
            SetStatusText("Enrollment: Fake finger detected. Please use a real finger.");
            UpdateEnrollmentGuidance(0, "Fake finger detected. Use your real thumb and try again.");
            return false; // Continue enrollment
        }

        // Separate event handlers for attendance
        private void OnAttendancePutOn(FTR_PROGRESS progress)
        {
            // Filter out false positive detections
            if (!IsValidFingerDetection())
                return;
                
            SetStatusText("Attendance: Finger detected. Processing...");
        }

        private void OnAttendanceTakeOff(FTR_PROGRESS progress)
        {
            SetStatusText("Attendance: Finger removed. Please scan again.");
        }

        private void OnAttendanceUpdateScreenImage(Bitmap bitmap)
        {
            if (pictureFingerprint != null && bitmap != null)
            {
                pictureFingerprint.Image = bitmap;
            }
        }

        private bool OnAttendanceFakeSource(FTR_PROGRESS progress)
        {
            SetStatusText("Attendance: Fake finger detected. Please use a real finger.");
            return false; // Continue attendance
        }

        // Helper method to validate finger detection and filter out false positives
        private bool IsValidFingerDetection()
        {
            DateTime currentTime = DateTime.Now;
            
            // Skip initial startup false positives (first 3 seconds after operation starts)
            if (m_bInitialStartup)
            {
                if ((currentTime - m_lastPutOnTime).TotalSeconds < 3)
                {
                    return false; // Ignore initial false positives
                }
                m_bInitialStartup = false;
            }
            
            // Check minimum interval between put-on events
            if ((currentTime - m_lastPutOnTime).TotalMilliseconds < MIN_PUTON_INTERVAL_MS)
            {
                return false; // Too soon since last detection, likely false positive
            }
            
            // Update last detection time
            m_lastPutOnTime = currentTime;
            return true;
        }

        // Helper method to stop attendance operation completely
        private void StopAttendanceOperation()
        {
            try
            {
                if (m_AttendanceOperation != null)
                {
                    SetStatusText("Stopping attendance operation...");
                    m_AttendanceOperation.OnCalcel();
                    isIdentifying = false;
                    m_bAttendanceActive = false;
                    
                    // Unregister attendance events
                    m_AttendanceOperation.OnPutOn -= OnAttendancePutOn;
                    m_AttendanceOperation.OnTakeOff -= OnAttendanceTakeOff;
                    m_AttendanceOperation.UpdateScreenImage -= OnAttendanceUpdateScreenImage;
                    m_AttendanceOperation.OnFakeSource -= OnAttendanceFakeSource;
                    m_AttendanceOperation.OnGetBaseTemplateComplete -= OnGetBaseTemplateComplete;
                    
                    // Dispose the operation completely
                    m_AttendanceOperation.Dispose();
                    m_AttendanceOperation = null;
                    SetStatusText("Attendance operation stopped and disposed.");
                }
            }
            catch (Exception ex)
            {
                SetStatusText($"Error stopping attendance: {ex.Message}");
            }
        }
        
        // Helper method to safely restart fingerprint operation after AccessViolationException
        private static int restartAttempts = 0;
        private const int maxRestartAttempts = 3;
        
        private void SafeRestartFingerprintOperation()
        {
            try
            {
                restartAttempts++;
                Console.WriteLine($"SafeRestartFingerprintOperation: Starting safe restart (attempt {restartAttempts}/{maxRestartAttempts})...");
                SetStatusText($"Restarting fingerprint operation after error... (attempt {restartAttempts})");
                
                // Stop current operation safely
                StopAttendanceOperation();
                
                // Force dispose the operation to free memory and prevent AccessViolationException
                if (m_AttendanceOperation != null)
                {
                    try
                    {
                        m_AttendanceOperation.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error disposing operation: {ex.Message}");
                    }
                    m_AttendanceOperation = null;
                }
                
                // Wait longer for cleanup to prevent memory corruption
                System.Threading.Thread.Sleep(3000);
                
                // Reset identifying flag
                isIdentifying = false;
                
                // Restart if still needed and not exceeded max attempts
                if (!m_bExit && alwaysOnAttendance && restartAttempts <= maxRestartAttempts)
                {
                    SetStatusText("Reinitializing fingerprint device...");
                    
                    // Reinitialize the attendance operation
                    try
                    {
                        m_AttendanceOperation = new FutronicIdentification();
                        m_AttendanceOperation.FakeDetection = false;
                        m_AttendanceOperation.FFDControl = false;
                        m_AttendanceOperation.FastMode = true;
                        m_AttendanceOperation.FARN = 100;
                        m_AttendanceOperation.Version = VersionCompatible.ftr_version_compatible;
                        
                        // Register events
                        m_AttendanceOperation.OnPutOn += OnAttendancePutOn;
                        m_AttendanceOperation.OnTakeOff += OnAttendanceTakeOff;
                        m_AttendanceOperation.UpdateScreenImage += OnAttendanceUpdateScreenImage;
                        m_AttendanceOperation.OnFakeSource += OnAttendanceFakeSource;
                        m_AttendanceOperation.OnGetBaseTemplateComplete += OnGetBaseTemplateComplete;
                        
                        StartIdentification();
                        restartAttempts = 0; // Reset on successful restart
                        Console.WriteLine("SafeRestartFingerprintOperation: Restart completed successfully");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reinitializing attendance operation: {ex.Message}");
                        SetStatusText($"Error reinitializing fingerprint device: {ex.Message}");
                        
                        // Try again after a longer delay if not exceeded max attempts
                        if (restartAttempts < maxRestartAttempts)
                        {
                            System.Threading.Tasks.Task.Delay(5000).ContinueWith(_ =>
                            {
                                if (!m_bExit && alwaysOnAttendance)
                                {
                                    this.Invoke(new Action(() => SafeRestartFingerprintOperation()));
                                }
                            });
                        }
                        else
                        {
                            SetStatusText("Maximum restart attempts reached. Please restart the application manually.");
                            restartAttempts = 0; // Reset for next time
                        }
                    }
                }
                else if (restartAttempts > maxRestartAttempts)
                {
                    SetStatusText("Maximum restart attempts reached. Please restart the application manually.");
                    restartAttempts = 0; // Reset for next time
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SafeRestartFingerprintOperation: {ex.Message}");
                SetStatusText($"Error restarting fingerprint operation: {ex.Message}");
                
                // Only retry if not exceeded max attempts
                if (restartAttempts < maxRestartAttempts)
                {
                    System.Threading.Tasks.Task.Delay(5000).ContinueWith(_ =>
                    {
                        if (!m_bExit && alwaysOnAttendance)
                        {
                            this.Invoke(new Action(() => SafeRestartFingerprintOperation()));
                        }
                    });
                }
                else
                {
                    SetStatusText("Maximum restart attempts reached. Please restart the application manually.");
                    restartAttempts = 0; // Reset for next time
                }
            }
        }

        private void UnregisterEnrollmentEvents()
        {
            if (m_Operation != null)
            {
                m_Operation.OnPutOn -= OnEnrollmentPutOn;
                m_Operation.OnTakeOff -= OnEnrollmentTakeOff;
                m_Operation.UpdateScreenImage -= OnEnrollmentUpdateScreenImage;
                m_Operation.OnFakeSource -= OnEnrollmentFakeSource;

                if (m_Operation is FutronicEnrollment enrollment)
                {
                    enrollment.OnEnrollmentComplete -= OnEnrollmentComplete;
                }
            }
        }

        private void UnregisterEvents()
        {
            UnregisterEnrollmentEvents();

            // Unregister attendance operation events
            if (m_AttendanceOperation != null)
            {
                m_AttendanceOperation.OnPutOn -= OnAttendancePutOn;
                m_AttendanceOperation.OnTakeOff -= OnAttendanceTakeOff;
                m_AttendanceOperation.UpdateScreenImage -= OnAttendanceUpdateScreenImage;
                m_AttendanceOperation.OnFakeSource -= OnAttendanceFakeSource;
                m_AttendanceOperation.OnGetBaseTemplateComplete -= OnGetBaseTemplateComplete;
            }
        }

        // Database operations
        private string GetDatabaseDir()
        {
            string dbDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                kCompanyName,
                kProductName,
                kDbName);

            Directory.CreateDirectory(dbDir);
            return dbDir;
        }

        private List<UserRecord> LoadUsers()
        {
            var users = new List<UserRecord>();
            
            // Check if cloudUsers is initialized
            if (cloudUsers == null)
            {
                SetStatusText("Users not loaded from database yet. Please wait...");
                return users;
            }
            
            // Convert cloud users to UserRecord format for compatibility
            foreach (var cloudUser in cloudUsers)
            {
                try
                {
                    // Only add users with valid templates to prevent identification errors
                    if (cloudUser?.FingerprintTemplate != null && cloudUser.FingerprintTemplate.Length > 0 && !string.IsNullOrEmpty(cloudUser.Username))
                    {
                        var userRecord = new UserRecord
                        {
                            UserName = cloudUser.Username,
                            Template = cloudUser.FingerprintTemplate
                        };
                        users.Add(userRecord);
                    }
                    else
                    {
                        // Skip users with invalid templates (reduced logging)
                        // Console.WriteLine($"Skipping user '{cloudUser.Username}' - invalid or empty fingerprint template");
                    }
                }
                catch (Exception ex)
                {
                    // Skip invalid users and log the error
                    Console.WriteLine($"Error loading user: {ex.Message}");
                }
            }

            return users;
        }




        private void SafeRefreshUserList()
        {
            try
            {
                if (this.IsDisposed) return;
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(RefreshUserList));
                }
                else
                {
                    RefreshUserList();
                }
            }
            catch
            {
                // ignore cross-thread race conditions
            }
        }

        private void LoadAvailableRooms()
        {
            try
            {
                availableRooms = dbManager?.LoadAllRooms() ?? new List<Database.Models.Room>();
                
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(UpdateRoomComboBox));
                }
                else
                {
                    UpdateRoomComboBox();
                }
                
                UpdateCurrentRoomDisplay();
            }
            catch (Exception ex)
            {
                SetStatusText($"Failed to load rooms: {ex.Message}");
            }
        }

        private void UpdateRoomComboBox()
        {
            if (cmbRoom != null && availableRooms != null)
            {
                // DUAL SENSOR MODE: Set room from device configuration
                if (isDualSensorMode && deviceConfig != null)
                {
                    // In dual sensor mode, room is set via Device Configuration page
                    // Make combo box read-only and display configured room
                    cmbRoom.Enabled = false;
                    cmbRoom.Items.Clear();
                    
                    // Find the room in available rooms list
                    var configuredRoom = availableRooms.FirstOrDefault(r => r.RoomId == deviceConfig.RoomId);
                    if (configuredRoom != null)
                    {
                        cmbRoom.DisplayMember = "DisplayName";
                        cmbRoom.ValueMember = "RoomId";
                        cmbRoom.Items.Add(configuredRoom);
                        cmbRoom.SelectedItem = configuredRoom;
                    }
                    else
                    {
                        // Room not found in database, create a display-only entry
                        var displayRoom = new Database.Models.Room
                        {
                            RoomId = deviceConfig.RoomId ?? "unknown",
                            RoomName = deviceConfig.RoomName ?? "Unknown Room",
                            Building = deviceConfig.Building ?? "Unknown Building"
                        };
                        cmbRoom.Items.Add(displayRoom);
                        cmbRoom.DisplayMember = "DisplayName";
                        cmbRoom.ValueMember = "RoomId";
                        cmbRoom.SelectedIndex = 0;
                    }
                }
                // REGULAR MODE: Allow room selection
                else
                {
                    cmbRoom.Enabled = true;
                    cmbRoom.Items.Clear();
                    cmbRoom.DisplayMember = "DisplayName";
                    cmbRoom.ValueMember = "RoomId";
                    
                    foreach (var room in availableRooms)
                    {
                        cmbRoom.Items.Add(room);
                    }
                    
                    // Select current room if available
                    if (!string.IsNullOrEmpty(dbManager?.CurrentRoomId))
                    {
                        var currentRoom = availableRooms.FirstOrDefault(r => r.RoomId == dbManager.CurrentRoomId);
                        if (currentRoom != null)
                        {
                            cmbRoom.SelectedItem = currentRoom;
                        }
                    }
                }
            }

            // Also update device room combo
            if (cmbDeviceRoom != null && availableRooms != null)
            {
                cmbDeviceRoom.Items.Clear();
                cmbDeviceRoom.DisplayMember = "DisplayName";
                cmbDeviceRoom.ValueMember = "RoomId";
                
                foreach (var room in availableRooms)
                {
                    cmbDeviceRoom.Items.Add(room);
                }
            }
        }

        private void UpdateCurrentRoomDisplay()
        {
            try
            {
                if (lblCurrentRoom != null)
                {
                    // DUAL SENSOR MODE: Use device configuration if available
                    if (isDualSensorMode && deviceConfig != null)
                    {
                        string building = !string.IsNullOrEmpty(deviceConfig.Building) ? deviceConfig.Building : "Unknown Building";
                        lblCurrentRoom.Text = $"Current Room: {building} - {deviceConfig.RoomName}";
                    }
                    // REGULAR MODE: Use database device room
                    else if (dbManager?.CurrentDevice?.Room != null)
                    {
                        var room = dbManager.CurrentDevice.Room;
                        lblCurrentRoom.Text = $"Current Room: {room.FullDisplayName}";
                    }
                    else
                    {
                        lblCurrentRoom.Text = "Current Room: Not Set";
                    }
                }
            }
            catch (Exception ex)
            {
                if (lblCurrentRoom != null)
                {
                    lblCurrentRoom.Text = "Current Room: Error loading";
                }
                Console.WriteLine($"Error updating room display: {ex.Message}");
            }
        }

        private void RefreshDeviceList()
        {
            try
            {
                var devices = dbManager?.GetAllDevices() ?? new List<Database.Models.Device>();
                
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() => UpdateDeviceListView(devices)));
                }
                else
                {
                    UpdateDeviceListView(devices);
                }
            }
            catch (Exception ex)
            {
                SetStatusText($"Failed to refresh device list: {ex.Message}");
            }
        }

        private void UpdateDeviceListView(List<Database.Models.Device> devices)
        {
            if (deviceListView == null) return;

            deviceListView.Items.Clear();

            foreach (var device in devices)
            {
                var item = new ListViewItem(device.DeviceName);
                item.SubItems.Add(device.DeviceType);
                item.SubItems.Add(device.Room?.DisplayName ?? "Not Set");
                item.SubItems.Add(device.Room?.Building ?? "Unknown");
                item.SubItems.Add(device.IpAddress ?? "Unknown");
                item.SubItems.Add(device.Status);
                item.SubItems.Add(device.LastSeen?.ToString("MM/dd HH:mm") ?? "Never");

                // Highlight current device
                if (device.DeviceId == dbManager?.CurrentDeviceId)
                {
                    item.BackColor = Color.LightGreen;
                    item.Font = new Font(item.Font, FontStyle.Bold);
                }
                else if (device.Status != "Active")
                {
                    item.BackColor = Color.LightGray;
                }

                deviceListView.Items.Add(item);
            }
        }

        private void TabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (tabControl.SelectedTab == enrollmentTab)
                {
                    RefreshUserList();
                }
                else if (tabControl.SelectedTab == deviceManagementTab)
                {
                    RefreshDeviceList();
                }
                else if (tabControl.SelectedTab == fingerprintUsersTab)
                {
                    if (!fingerprintUsersAccessGranted)
                    {
                        var ok = PromptForAdminPassword();
                        if (!ok)
                        {
                            MessageBox.Show("Access denied.", "Authentication", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            tabControl.SelectedTab = enrollmentTab;
                            return;
                        }
                        fingerprintUsersAccessGranted = true;
                    }
                    LoadFingerprintUsers();
                }
            }
            catch { }
        }

        private void RefreshUserList()
        {
            try
            {
                // Use the table-based interface
                if (dgvUsers != null)
                {
                    LoadUsersIntoTable();
                }
            }
            catch
            {
                // ignore UI refresh errors
            }
        }

        private void BtnDeleteUser_Click(object sender, EventArgs e)
        {
            try
            {
                // NEW: Check if a user is selected from the table
                if (dgvUsers == null || dgvUsers.SelectedRows.Count == 0)
                {
                    MessageBox.Show("Select a user from the table to delete.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var selectedRow = dgvUsers.SelectedRows[0];
                var user = selectedRow.Tag as User;
                
                if (user == null)
                {
                    MessageBox.Show("Invalid user selection.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string selectedUser = $"{user.FirstName} {user.LastName}";
                var confirm = MessageBox.Show($"Delete template for '{selectedUser}'? This cannot be undone.",
                    "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes)
                    return;

                    try
                    {
                        dbManager.DeleteUser(user.Id);
                        SetStatusText($"User '{selectedUser}' deleted.");
                        
                        // Refresh users from database
                        SyncUsersFromCloud();
                        RefreshUserList();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete user: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Delete error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportToCSV(string fileName)
        {
            using (var writer = new StreamWriter(fileName))
            {
                writer.WriteLine("Timestamp,UserName,Action");
                foreach (var record in attendanceRecords)
                {
                    writer.WriteLine($"{record.Timestamp:yyyy-MM-dd HH:mm:ss},{record.Username},{record.Action}");
                }
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            m_bExit = true;
            
            // Stop all timers first
            if (countdownTimer != null)
            {
                countdownTimer.Stop();
                countdownTimer.Dispose();
                countdownTimer = null;
            }
            
            if (identifyRetryTimer != null)
            {
                identifyRetryTimer.Stop();
                identifyRetryTimer.Dispose();
                identifyRetryTimer = null;
            }
            
            if (heartbeatTimer != null)
            {
                heartbeatTimer.Stop();
                heartbeatTimer.Dispose();
                heartbeatTimer = null;
            }
            
            if (syncTimer != null)
            {
                syncTimer.Stop();
                syncTimer.Dispose();
                syncTimer = null;
            }
            
            if (clockTimer != null)
            {
                clockTimer.Stop();
                clockTimer.Dispose();
                clockTimer = null;
            }
            
            if (enrollProgressTimer != null)
            {
                enrollProgressTimer.Stop();
                enrollProgressTimer.Dispose();
                enrollProgressTimer = null;
            }
            
            // Dispose operations with proper error handling
            if (m_Operation != null)
            {
                try
                {
                    m_Operation.OnCalcel();
                    m_Operation.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error disposing m_Operation: {ex.Message}");
                }
                finally
                {
                    m_Operation = null;
                }
            }

            if (m_AttendanceOperation != null)
            {
                try
                {
                    m_AttendanceOperation.OnCalcel();
                    m_AttendanceOperation.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error disposing m_AttendanceOperation: {ex.Message}");
                }
                finally
                {
                    m_AttendanceOperation = null;
                }
            }
            
            // Stop RFID keyboard hook and cleanup
            if (_hookID != IntPtr.Zero)
            {
                try
                {
                    UnhookWindowsHookEx(_hookID);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error stopping RFID keyboard hook: {ex.Message}");
                }
                finally
                {
                    _hookID = IntPtr.Zero;
                }
            }
            
            if (rfidInputTimer != null)
            {
                rfidInputTimer.Stop();
                rfidInputTimer.Dispose();
                rfidInputTimer = null;
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (!m_bInitializationSuccess)
            {
                this.Close();
            }
            else if (alwaysOnAttendance)
            {
                // Start identification automatically
                StartIdentification();
            }
            
            // Update room display after everything is loaded
            UpdateCurrentRoomDisplay();
        }

        // RFID Event Handlers
        private void BtnStartRfidAttendance_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_bRfidAttendanceActive)
                {
                    SetRfidStatusText("RFID attendance is already active.");
                    return;
                }

                // Start RFID background service
                StartRfidService();
                
                // Initialize RFID session state
                currentRfidSessionState = AttendanceSessionState.WaitingForInstructor;
                m_bRfidAttendanceActive = true;
                
                // Update RFID session state display
                UpdateRfidSessionStateDisplay();
                
                // Update UI
                btnStartRfidAttendance.Enabled = false;
                btnStopRfidAttendance.Enabled = true;
                
                SetRfidStatusText("RFID attendance started. Waiting for instructor to scan...");
                AddRfidAttendanceRecord("System", "RFID Attendance Started", "System initialized");
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error starting RFID attendance: {ex.Message}");
            }
        }

        private void BtnStopRfidAttendance_Click(object sender, EventArgs e)
        {
            try
            {
                // Stop RFID background service
                StopRfidService();
                
                // Reset RFID session state
                currentRfidSessionState = AttendanceSessionState.Inactive;
                m_bRfidAttendanceActive = false;
                currentRfidInstructorId = null;
                currentRfidScheduleId = null;
                
                // Update RFID session state display
                UpdateRfidSessionStateDisplay();
                
                // Update UI
                btnStartRfidAttendance.Enabled = true;
                btnStopRfidAttendance.Enabled = false;
                lblRfidSessionState.Text = "Session State: Inactive";
                lblRfidSessionState.ForeColor = Color.DarkRed;
                lblRfidSessionInfo.Text = "No active session";
                
                SetRfidStatusText("RFID attendance stopped.");
                AddRfidAttendanceRecord("System", "RFID Attendance Stopped", "System stopped");
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error stopping RFID attendance: {ex.Message}");
            }
        }

        private void BtnForceEndRfidSession_Click(object sender, EventArgs e)
        {
            try
            {
                // Force end RFID session
                currentRfidSessionState = AttendanceSessionState.Inactive;
                currentRfidInstructorId = null;
                currentRfidScheduleId = null;
                
                // Update RFID session state display
                UpdateRfidSessionStateDisplay();
                
                // Update UI
                lblRfidSessionState.Text = "Session State: Inactive";
                lblRfidSessionState.ForeColor = Color.DarkRed;
                lblRfidSessionInfo.Text = "Session force ended";
                btnForceEndRfidSession.Visible = false;
                
                SetRfidStatusText("RFID session force ended.");
                AddRfidAttendanceRecord("System", "Session Force Ended", "Session terminated by user");
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error force ending session: {ex.Message}");
            }
        }

        private void BtnExportRfidAttendance_Click(object sender, EventArgs e)
        {
            try
            {
                var dlg = new SaveFileDialog();
                dlg.Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*";
                dlg.FileName = $"rfid_attendance_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    ExportRfidAttendanceToCsv(dlg.FileName);
                    MessageBox.Show($"RFID attendance data exported to {dlg.FileName}", "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export RFID data: {ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CmbRfidLocation_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Handle RFID location change
            SetRfidStatusText($"RFID location changed to: {cmbRfidLocation.SelectedItem}");
        }

        private void BtnRfidChangeRoom_Click(object sender, EventArgs e)
        {
            // Handle RFID room change
            SetRfidStatusText($"RFID room changed to: {cmbRfidRoom.SelectedItem}");
        }

        // RFID Native C# Implementation
        private void StartRfidService()
        {
            try
            {
                if (_hookID != IntPtr.Zero)
                {
                    SetRfidStatusText("RFID keyboard hook already active.");
                    return;
                }

                // Install global keyboard hook
                _hookID = SetHook(_proc);
                if (_hookID == IntPtr.Zero)
                {
                    SetRfidStatusText("Failed to install RFID keyboard hook.");
                    return;
                }

                // Initialize RFID input timer
                if (rfidInputTimer == null)
                {
                    rfidInputTimer = new System.Windows.Forms.Timer();
                    rfidInputTimer.Interval = RFID_TIMEOUT_MS;
                    rfidInputTimer.Tick += RfidInputTimer_Tick;
                }
                
                // Reset RFID buffer and state
                rfidBuffer = "";
                rfidCapturing = false;
                lastRfidInput = DateTime.MinValue;
                
                SetRfidStatusText("RFID keyboard capture started. Ready to scan RFID cards.");
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Failed to start RFID capture: {ex.Message}");
                Console.WriteLine($"RFID Service Error: {ex.Message}");
            }
        }

        private void StopRfidService()
        {
            try
            {
                // Uninstall global keyboard hook
                if (_hookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookID);
                    _hookID = IntPtr.Zero;
                }

                // Stop RFID input timer
                if (rfidInputTimer != null)
                {
                    rfidInputTimer.Stop();
                }

                // Reset RFID state
                rfidBuffer = "";
                rfidCapturing = false;
                
                SetRfidStatusText("RFID keyboard capture stopped.");
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error stopping RFID capture: {ex.Message}");
            }
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                
                // Check if this is a key down event
                if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                {
                    // Get the main form instance to process the key
                    var mainForm = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
                    if (mainForm != null)
                    {
                        mainForm.ProcessRfidKeyInput(vkCode);
                    }
                }
            }
            
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void ProcessRfidKeyInput(int vkCode)
        {
            try
            {
                if (!m_bRfidAttendanceActive) 
                {
                    return;
                }

                // Convert virtual key code to character
                char keyChar = ConvertVkCodeToChar(vkCode);
                
                if (keyChar != '\0')
                {
                    var currentTime = DateTime.Now;
                    
                    // Check if this is rapid input (RFID scanner characteristic)
                    bool isRapidInput = false;
                    if (lastRfidInput != DateTime.MinValue)
                    {
                        var timeSinceLastInput = currentTime - lastRfidInput;
                        isRapidInput = timeSinceLastInput.TotalMilliseconds < 100; // Increased to 100ms for better detection
                    }
                    
                    // Only process if this is rapid input (RFID) or if we're already capturing
                    // Also process the first input (when lastRfidInput is DateTime.MinValue)
                    if (isRapidInput || rfidCapturing || lastRfidInput == DateTime.MinValue)
                    {
                        // Reset timer
                        if (rfidInputTimer != null)
                        {
                            rfidInputTimer.Stop();
                            rfidInputTimer.Start();
                        }

                        // Add character to buffer
                        rfidBuffer += keyChar;
                        lastRfidInput = currentTime;
                        rfidCapturing = true;

                        // Check if this looks like RFID input (rapid succession)
                        if (rfidBuffer.Length == 1)
                        {
                            SetRfidStatusText("RFID card detected, capturing...");
                        }
                    }
                    else
                    {
                        // This is regular typing, ignore it
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error processing RFID input: {ex.Message}");
            }
        }

        private char ConvertVkCodeToChar(int vkCode)
        {
            // Convert virtual key code to character
            if (vkCode >= 48 && vkCode <= 57) // 0-9
                return (char)(vkCode);
            if (vkCode >= 65 && vkCode <= 90) // A-Z
                return (char)(vkCode);
            if (vkCode >= 96 && vkCode <= 105) // Numpad 0-9
                return (char)(vkCode - 48);
            if (vkCode == 13) // Enter key
                return '\r';
            if (vkCode == 32) // Space
                return ' ';
                
            return '\0'; // Unknown key
        }

        private void RfidInputTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (rfidCapturing && rfidBuffer.Length > 0)
                {
                    // Process the complete RFID input
                    ProcessRfidScan(rfidBuffer.Trim());
                    
                    // Reset RFID state for next scan
                    rfidBuffer = "";
                    rfidCapturing = false;
                    lastRfidInput = DateTime.MinValue; // Reset to allow new scans
                    
                    if (rfidInputTimer != null)
                    {
                        rfidInputTimer.Stop();
                    }
                }
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error processing RFID scan: {ex.Message}");
            }
        }

        private async void ProcessRfidScan(string rfidData)
        {
            try
            {
                // Clean and validate RFID data
                rfidData = rfidData?.Trim();
                
                // Check for valid RFID data (should be numeric and reasonable length)
                if (string.IsNullOrEmpty(rfidData) || 
                    rfidData.Length < 4 || 
                    rfidData.Length > 20 || 
                    !rfidData.All(char.IsDigit))
                {
                    SetRfidStatusText($"Invalid RFID data received: '{rfidData}'. Expected numeric data 4-20 digits.");
                    return;
                }

                SetRfidStatusText($"RFID Card Scanned: {rfidData}");
                AddRfidAttendanceRecord("RFID Scanner", $"Card Scanned: {rfidData}", "Processing...");

                Console.WriteLine($"====== RFID LOOKUP ======");
                Console.WriteLine($"RFID Data: {rfidData}");
                
                // Get user information first to determine role-based routing
                var userInfo = GetUserInfoFromRfid(rfidData);
                if (userInfo == null)
                {
                    Console.WriteLine($"❌ RFID {rfidData} NOT FOUND in USERS table");
                    Console.WriteLine($"   Check if this RFID is registered: SELECT * FROM USERS WHERE RFIDTAG = '{rfidData}'");
                    SetRfidStatusText($"❌ RFID {rfidData} not registered in database. Please register this card first.");
                    AddRfidAttendanceRecord("System", $"RFID {rfidData} Not Registered", "Error");
                    return;
                }
                
                Console.WriteLine($"✅ RFID found: {userInfo.Username} ({userInfo.UserType})");
                
                string userType = userInfo.UserType?.ToLower();
                
                // Route based on user type and session state (same logic as fingerprint system)
                if (userType == "instructor")
                {
                    // Instructor actions based on session state
                    switch (currentRfidSessionState)
                    {
                        case AttendanceSessionState.Inactive:
                        case AttendanceSessionState.WaitingForInstructor:
                            // Instructor starting session
                            HandleRfidInstructorStart(rfidData);
                            break;
                            
                        case AttendanceSessionState.ActiveForStudents:
                            // Instructor opening sign-out phase
                            HandleRfidInstructorSignOut(rfidData);
                            break;
                            
                        case AttendanceSessionState.ActiveForSignOut:
                        case AttendanceSessionState.WaitingForInstructorClose:
                            // Instructor closing session
                            await HandleRfidInstructorClose(rfidData);
                            break;
                            
                        default:
                            SetRfidStatusText("RFID session not active. Please start RFID attendance first.");
                            break;
                    }
                }
                else if (userType == "student")
                {
                    Console.WriteLine($"====== RFID STUDENT SCAN ======");
                    Console.WriteLine($"Student: {userInfo.Username}");
                    Console.WriteLine($"Current RFID Session State: {currentRfidSessionState}");
                    Console.WriteLine($"Current Fingerprint Session State: {currentSessionState}");
                    
                    // RFID attendance should work with ANY active session (fingerprint or RFID)
                    // Check both RFID and fingerprint session states
                    if (currentSessionState == AttendanceSessionState.ActiveForStudents || 
                        currentRfidSessionState == AttendanceSessionState.ActiveForStudents)
                    {
                        // Student sign-in (session active via fingerprint OR RFID)
                        Console.WriteLine("✅ Session is active (fingerprint or RFID), processing student sign-in...");
                        HandleRfidStudentSignIn(rfidData);
                    }
                    else if (currentSessionState == AttendanceSessionState.ActiveForSignOut || 
                             currentRfidSessionState == AttendanceSessionState.ActiveForSignOut)
                    {
                        // Student sign-out
                        Console.WriteLine("✅ Session in sign-out phase, processing student sign-out...");
                        HandleRfidStudentSignOut(rfidData);
                    }
                    else
                    {
                        Console.WriteLine($"❌ No active session found");
                        Console.WriteLine($"   RFID State: {currentRfidSessionState}");
                        Console.WriteLine($"   Fingerprint State: {currentSessionState}");
                        Console.WriteLine("   Instructor must start a session first (fingerprint or RFID)!");
                        SetRfidStatusText($"❌ No active session. Instructor must start attendance session first.");
                        AddRfidAttendanceRecord(userInfo.Username, "Session Not Active", $"RFID:{currentRfidSessionState}, FP:{currentSessionState}");
                    }
                }
                else
                {
                    SetRfidStatusText($"❌ RFID {rfidData} belongs to {userInfo.Username} ({userType}). Only instructors and students can use attendance system.");
                    AddRfidAttendanceRecord(userInfo.Username, "Access Denied", $"Invalid Role ({userType})");
                }
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error processing RFID scan: {ex.Message}");
            }
        }

        private void HandleRfidInstructorStart(string rfidData)
        {
            try
            {
                // Get user information from database using RFID data
                var userInfo = GetUserInfoFromRfid(rfidData);
                if (userInfo == null)
                {
                    SetRfidStatusText($"❌ RFID {rfidData} not found in database.");
                    AddRfidAttendanceRecord("System", "RFID Not Found", "Error");
                    return;
                }
                
                string userName = userInfo.Username;
                string userGuid = userInfo.EmployeeId;
                
                SetRfidStatusText($"Verifying instructor schedule for {userName}...");
                
                // Check if instructor has a scheduled class BEFORE starting session
                if (dbManager != null)
                {
                    var validationResult = dbManager.TryRecordAttendanceByGuid(userGuid, "Instructor Schedule Check");
                    
                    if (!validationResult.Success)
                    {
                        // Instructor doesn't have a schedule at this time - DENY session start
                        SetRfidStatusText($"❌ {userName}: {validationResult.Reason}. Cannot start RFID attendance session.");
                        AddRfidAttendanceRecord(userName, "Session Start Denied", validationResult.Reason);
                        return;
                    }
                    
                    // Instructor has valid schedule - proceed with session start
                    currentRfidInstructorId = userGuid;
                    currentRfidScheduleId = validationResult.ScheduleId ?? "manual_session";
                    currentRfidSessionState = AttendanceSessionState.ActiveForStudents;
                    
                    // Clear any previous signed-in/out students for new session (same as fingerprint system)
                    signedInStudentGuids.Clear();
                    signedOutStudentGuids.Clear();
                    
                    UpdateRfidSessionStateDisplay();
                    SetRfidStatusText($"✅ Instructor {userName} signed in. RFID session started for {validationResult.SubjectName}. Students can now scan.");
                    
                    AddRfidAttendanceRecord(userName, "Session Started", $"Active - {validationResult.SubjectName}");
                    
                    // Record instructor's sign-in attendance
                    System.Threading.Tasks.Task.Run(async () => {
                        try
                        {
                            RecordAttendance(userName, "Instructor Sign-In (RFID Session Start)");
                            
                            // Request lock control for instructor
                            await RequestRfidLockControl(userGuid, "Instructor Sign-In (RFID Session Start)", rfidData);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Could not record instructor sign-in: {ex.Message}");
                        }
                    });
                }
                else
                {
                    // No database manager - fallback to manual session
                    SetRfidStatusText($"⚠️ Warning: Cannot verify schedule. Database not available.");
                    currentRfidInstructorId = userGuid;
                    currentRfidScheduleId = "manual_session";
                    currentRfidSessionState = AttendanceSessionState.ActiveForStudents;
                    
                    // Clear any previous signed-in/out students for new session (same as fingerprint system)
                    signedInStudentGuids.Clear();
                    signedOutStudentGuids.Clear();
                    
                    UpdateRfidSessionStateDisplay();
                    SetRfidStatusText($"✅ Instructor {userName} signed in (unverified). Students can now scan.");
                    AddRfidAttendanceRecord(userName, "Session Started", "Active (Unverified)");
                }
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error handling instructor start: {ex.Message}");
                AddRfidAttendanceRecord("System", "Instructor Start Error", ex.Message);
            }
        }

        private void HandleRfidStudentSignIn(string rfidData)
        {
            try
            {
                SetRfidStatusText($"Processing RFID student scan: {rfidData}...");
                
                // Get user information from database using RFID data
                var userInfo = GetUserInfoFromRfid(rfidData);
                if (userInfo == null)
                {
                    SetRfidStatusText($"❌ RFID {rfidData} not found in database.");
                    AddRfidAttendanceRecord("System", "RFID Not Found", "Error");
                    return;
                }
                
                string userName = userInfo.Username;
                string userType = userInfo.UserType?.ToLower();
                string userGuid = userInfo.EmployeeId;
                
                // Verify this is a student
                if (userType != "student")
                {
                    SetRfidStatusText($"❌ RFID {rfidData} belongs to {userName} ({userType}). Only students can sign in during active session.");
                    AddRfidAttendanceRecord(userName, "Access Denied", $"Not Student ({userType})");
                    return;
                }
                
                // Check if student is already signed in
                if (signedInStudentGuids.Contains(userGuid))
                {
                    SetRfidStatusText($"⚠️ Student {userName} already signed in - allowing door access.");
                    AddRfidAttendanceRecord(userName, "Already Signed In - Door Access", "Duplicate");
                    
                                        // STILL SEND RFID LOCK CONTROL REQUEST for already signed in students
                                        // This allows them to go in and out during the session
                                        System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControl(userGuid, "RFID Student Already Signed In - Door Access", rfidData);
                                            }
                                            catch (Exception lockEx)
                                            {
                                                Console.WriteLine($"RFID lock control request failed for already signed in student: {lockEx.Message}");
                                            }
                                        });
                    return;
                }
                
                // Record student sign-in
                Console.WriteLine($"====== RECORDING RFID STUDENT ATTENDANCE ======");
                Console.WriteLine($"Student: {userName}");
                Console.WriteLine($"UserGUID: {userGuid}");
                Console.WriteLine($"Action: Student Sign-In (RFID)");
                
                signedInStudentGuids.Add(userGuid);
                SetRfidStatusText($"✅ Student {userName} signed in successfully.");
                AddRfidAttendanceRecord(userName, "Student Sign-In", "Success");
                
                // Record attendance in database using GUID directly
                System.Threading.Tasks.Task.Run(() => {
                    try
                    {
                        Console.WriteLine($"Calling TryRecordAttendanceByGuid for RFID student...");
                        var attempt = dbManager?.TryRecordAttendanceByGuid(userGuid, "Student Sign-In (RFID)");
                        if (attempt != null && attempt.Success)
                        {
                            Console.WriteLine($"✅ RFID attendance recorded successfully to database");
                            Console.WriteLine($"   ScheduleID: {attempt.ScheduleId}");
                            Console.WriteLine($"   Subject: {attempt.SubjectName}");
                        }
                        else
                        {
                            Console.WriteLine($"❌ Failed to record RFID attendance: {attempt?.Reason ?? "dbManager is null"}");
                        }
                        
                        // Request RFID lock control for successful student sign-in
                        System.Threading.Tasks.Task.Run(async () => {
                            try
                            {
                                await RequestRfidLockControl(userGuid, "Student Sign-In (RFID)", rfidData);
                            }
                            catch (Exception lockEx)
                            {
                                Console.WriteLine($"RFID lock control request failed for student sign-in: {lockEx.Message}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ ERROR recording student sign-in: {ex.Message}");
                        Console.WriteLine($"   Stack trace: {ex.StackTrace}");
                    }
                });
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error handling student sign-in: {ex.Message}");
                AddRfidAttendanceRecord("System", "Student Sign-In Error", ex.Message);
            }
        }

        private void HandleRfidInstructorSignOut(string rfidData)
        {
            try
            {
                SetRfidStatusText($"Processing RFID instructor scan for sign-out: {rfidData}...");
                
                // Get user information from database using RFID data
                var userInfo = GetUserInfoFromRfid(rfidData);
                if (userInfo == null)
                {
                    SetRfidStatusText($"❌ RFID {rfidData} not found in database.");
                    AddRfidAttendanceRecord("System", "RFID Not Found", "Error");
                    return;
                }
                
                string userName = userInfo.Username;
                string userGuid = userInfo.EmployeeId;
                
                // Verify this is the same instructor who started the session
                if (userGuid != currentRfidInstructorId)
                {
                    SetRfidStatusText($"❌ Only the instructor who started the session can open sign-out.");
                    AddRfidAttendanceRecord(userName, "Sign-Out Denied", "Wrong Instructor");
                    return;
                }
                
                currentRfidSessionState = AttendanceSessionState.ActiveForSignOut;
                UpdateRfidSessionStateDisplay();
                
                SetRfidStatusText($"✅ Instructor {userName} opened sign-out. Students can now sign out.");
                AddRfidAttendanceRecord(userName, "Sign-Out Opened", "Active");
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error handling instructor sign-out: {ex.Message}");
                AddRfidAttendanceRecord("System", "Instructor Sign-Out Error", ex.Message);
            }
        }

        private void HandleRfidStudentSignOut(string rfidData)
        {
            try
            {
                SetRfidStatusText($"Processing RFID student sign-out: {rfidData}...");
                
                // Get user information from database using RFID data
                var userInfo = GetUserInfoFromRfid(rfidData);
                if (userInfo == null)
                {
                    SetRfidStatusText($"❌ RFID {rfidData} not found in database.");
                    AddRfidAttendanceRecord("System", "RFID Not Found", "Error");
                    return;
                }
                
                string userName = userInfo.Username;
                string userType = userInfo.UserType?.ToLower();
                string userGuid = userInfo.EmployeeId;
                
                // Verify this is a student
                if (userType != "student")
                {
                    SetRfidStatusText($"❌ RFID {rfidData} belongs to {userName} ({userType}). Only students can sign out.");
                    AddRfidAttendanceRecord(userName, "Access Denied", $"Not Student ({userType})");
                    return;
                }
                
                // Check if student is already signed out
                if (signedOutStudentGuids.Contains(userGuid))
                {
                    SetRfidStatusText($"⚠️ Student {userName} already signed out - allowing door access.");
                    AddRfidAttendanceRecord(userName, "Already Signed Out - Door Access", "Duplicate");
                    
                    // STILL SEND RFID LOCK CONTROL REQUEST for already signed out students
                    // This allows them to go in and out during the session
                    System.Threading.Tasks.Task.Run(async () => {
                        try
                        {
                            await RequestRfidLockControl(userGuid, "RFID Student Already Signed Out - Door Access", rfidData);
                        }
                        catch (Exception lockEx)
                        {
                            Console.WriteLine($"RFID lock control request failed for already signed out student: {lockEx.Message}");
                        }
                    });
                    return;
                }
                
                // Check if student was signed in
                if (!signedInStudentGuids.Contains(userGuid))
                {
                    SetRfidStatusText($"⚠️ Student {userName} was not signed in.");
                    AddRfidAttendanceRecord(userName, "Sign-Out Denied", "Not Signed In");
                    return;
                }
                
                // Validate and record via database first, then update local state based on result
                System.Threading.Tasks.Task.Run(() => {
                    try
                    {
                        var attempt = dbManager?.TryRecordAttendanceByGuid(userGuid, "Student Sign-Out (RFID)");
                        this.Invoke(new Action(() => {
                            if (attempt != null && attempt.Success)
                            {
                                // Success: update local state and UI
                                signedInStudentGuids.Remove(userGuid);
                                signedOutStudentGuids.Add(userGuid);
                                SetRfidStatusText($"✅ Student {userName} signed out successfully.");
                                AddRfidAttendanceRecord(userName, "Student Sign-Out", "Success");
                                
                                // Add local record for display
                                int userIdInt = 0;
                                if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userGuid, out var u))
                                {
                                    userIdInt = u.Id;
                                }
                                var local = new Database.Models.AttendanceRecord
                                {
                                    UserId = userIdInt,
                                    Username = userName,
                                    Timestamp = DateTime.Now,
                                    Action = "Student Sign-Out (RFID)",
                                    Status = "Success"
                                };
                                attendanceRecords.Add(local);
                                UpdateAttendanceDisplay(local);
                                
                                // Request RFID lock control for successful student sign-out
                                System.Threading.Tasks.Task.Run(async () => {
                                    try
                                    {
                                        await RequestRfidLockControl(userGuid, "Student Sign-Out (RFID)", rfidData);
                                    }
                                    catch (Exception lockEx)
                                    {
                                        Console.WriteLine($"RFID lock control request failed for student sign-out: {lockEx.Message}");
                                    }
                                });
                            }
                            else
                            {
                                // Denied: do not mark as signed out
                                var reason = attempt?.Reason ?? "Denied";
                                SetRfidStatusText($"❌ {userName}: {reason}");
                                AddRfidAttendanceRecord(userName, "Sign-Out Denied", reason);
                                
                                int userIdInt = 0;
                                if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userGuid, out var u2))
                                {
                                    userIdInt = u2.Id;
                                }
                                var local = new Database.Models.AttendanceRecord
                                {
                                    UserId = userIdInt,
                                    Username = userName,
                                    Timestamp = DateTime.Now,
                                    Action = "Student Sign-Out (RFID)",
                                    Status = $"Denied: {reason}"
                                };
                                attendanceRecords.Add(local);
                                UpdateAttendanceDisplay(local);
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        this.Invoke(new Action(() => {
                            SetRfidStatusText($"❌ Error processing sign-out: {ex.Message}");
                            AddRfidAttendanceRecord("System", "Sign-Out Error", ex.Message);
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error handling student sign-out: {ex.Message}");
                AddRfidAttendanceRecord("System", "Student Sign-Out Error", ex.Message);
            }
        }

        private async Task HandleRfidInstructorClose(string rfidData)
        {
            try
            {
                SetRfidStatusText($"Processing RFID instructor scan for session close: {rfidData}...");
                
                // Get user information from database using RFID data
                var userInfo = GetUserInfoFromRfid(rfidData);
                if (userInfo == null)
                {
                    SetRfidStatusText($"❌ RFID {rfidData} not found in database.");
                    AddRfidAttendanceRecord("System", "RFID Not Found", "Error");
                    return;
                }
                
                string userName = userInfo.Username;
                string userGuid = userInfo.EmployeeId;
                
                // Verify this is the same instructor who started the session
                if (userGuid != currentRfidInstructorId)
                {
                    SetRfidStatusText($"❌ Only the instructor who started the session can close it.");
                    AddRfidAttendanceRecord(userName, "Session Close Denied", "Wrong Instructor");
                    return;
                }
                
                // Check if there are students who haven't signed out yet
                if (signedInStudentGuids.Count > 0)
                {
                    // Get student names for better warning message
                    var studentNames = new List<string>();
                    foreach (var studentGuid in signedInStudentGuids)
                    {
                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(studentGuid, out var student))
                        {
                            studentNames.Add(student.Username);
                        }
                    }
                    
                    var studentList = string.Join(", ", studentNames);
                    SetRfidStatusText($"⚠️ Warning: {signedInStudentGuids.Count} students still signed in: {studentList}. Closing session anyway...");
                    AddRfidAttendanceRecord("System", "Session Close Warning", $"{signedInStudentGuids.Count} students still signed in: {studentList}");
                    
                    // Give a brief delay to allow instructor to see the warning
                    await System.Threading.Tasks.Task.Delay(3000);
                }
                
                // Close the session
                currentRfidSessionState = AttendanceSessionState.Inactive;
                currentRfidInstructorId = null;
                currentRfidScheduleId = null;
                
                // Clear signed-in/out students when session ends (same as fingerprint system)
                signedInStudentGuids.Clear();
                signedOutStudentGuids.Clear();
                
                UpdateRfidSessionStateDisplay();
                SetRfidStatusText($"✅ Instructor {userName} closed RFID session.");
                AddRfidAttendanceRecord(userName, "Session Closed", "Inactive");
                
                // Record instructor's sign-out attendance
                _ = System.Threading.Tasks.Task.Run(async () => {
                    try
                    {
                        RecordAttendance(userName, "Instructor Sign-Out (RFID Session End)");
                        
                        // Request lock control for instructor sign-out
                        await RequestRfidLockControl(userGuid, "Instructor Sign-Out (RFID Session End)", rfidData);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not record instructor sign-out: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error handling instructor close: {ex.Message}");
                AddRfidAttendanceRecord("System", "Instructor Close Error", ex.Message);
            }
        }

        private async Task SendRfidScanToBackend(string rfidData)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    
                    var payload = new
                    {
                        rfid_data = rfidData,
                        scan_type = "rfid",
                        location = cmbRfidLocation?.SelectedItem?.ToString() ?? "inside",
                        timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    };

                    var json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("http://localhost:5000/api/logs/rfid-scan", content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                        
                        if (result.TryGetProperty("attendance", out var attendance))
                        {
                            var userName = attendance.TryGetProperty("user", out var user) && user.TryGetProperty("name", out var name) 
                                ? name.GetString() : "Unknown";
                            var status = attendance.TryGetProperty("status", out var statusProp) 
                                ? statusProp.GetString() : "Unknown";
                                
                            SetRfidStatusText($"✅ Attendance recorded: {userName} - {status}");
                            AddRfidAttendanceRecord(userName, "Attendance Recorded", status);
                        }
                        else
                        {
                            SetRfidStatusText("✅ RFID scan processed successfully");
                            AddRfidAttendanceRecord("System", "RFID Processed", "Success");
                        }
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        SetRfidStatusText($"❌ Server Error: {response.StatusCode} - {errorContent}");
                        AddRfidAttendanceRecord("System", "RFID Error", $"Server Error: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"❌ Network Error: {ex.Message}");
                AddRfidAttendanceRecord("System", "RFID Error", $"Network Error: {ex.Message}");
            }
        }

        private User GetUserInfoFromRfid(string rfidData)
        {
            try
            {
                if (dbManager == null)
                {
                    Console.WriteLine("Database manager not available for RFID lookup");
                    return null;
                }

                // Look up user by RFID tag in the database
                var user = dbManager.GetUserByRfidTag(rfidData);
                if (user != null)
                {
                    Console.WriteLine($"RFID lookup successful: {user.Username} ({user.UserType})");
                    return user;
                }
                else
                {
                    Console.WriteLine($"RFID {rfidData} not found in database");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error looking up RFID {rfidData}: {ex.Message}");
                return null;
            }
        }

        // RFID Helper Methods
        private void SetRfidStatusText(string text)
        {
            if (txtRfidStatus != null)
            {
                txtRfidStatus.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\r\n");
                txtRfidStatus.SelectionStart = txtRfidStatus.Text.Length;
                txtRfidStatus.ScrollToCaret();
            }
        }

        private void AddRfidAttendanceRecord(string user, string action, string status)
        {
            if (dgvRfidAttendance != null)
            {
                dgvRfidAttendance.Rows.Insert(0, DateTime.Now, user, "", action, status);
            }
        }

        private void ExportRfidAttendanceToCsv(string fileName)
        {
            try
            {
                using (var writer = new System.IO.StreamWriter(fileName))
                {
                    writer.WriteLine("Time,User,RFID,Action,Status");
                    
                    foreach (DataGridViewRow row in dgvRfidAttendance.Rows)
                    {
                        if (row.Cells.Count >= 5)
                        {
                            var time = row.Cells[0].Value?.ToString() ?? "";
                            var user = row.Cells[1].Value?.ToString() ?? "";
                            var rfid = row.Cells[2].Value?.ToString() ?? "";
                            var action = row.Cells[3].Value?.ToString() ?? "";
                            var status = row.Cells[4].Value?.ToString() ?? "";
                            
                            writer.WriteLine($"\"{time}\",\"{user}\",\"{rfid}\",\"{action}\",\"{status}\"");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to export RFID data: {ex.Message}");
            }
        }

        // ==================== DUAL SENSOR MODE METHODS ====================

        private void InitializeDualSensorTab()
        {
            Console.WriteLine("Initializing Dual Sensor Tab...");
            
            // Create dual sensor tab
            dualSensorTab = new TabPage("🎯 Dual Sensor System");
            dualSensorTab.Padding = new Padding(0);
            
            // Create and configure dual sensor panel
            dualSensorPanel = new DualSensorPanel();
            dualSensorPanel.Dock = DockStyle.Fill;
            
            // Set room info
            dualSensorPanel.UpdateRoomInfo(deviceConfig.RoomName, deviceConfig.Building ?? "");
            dualSensorPanel.UpdateInsideDeviceInfo(
                deviceConfig.InsideSensor?.DeviceId ?? "Not configured (None)");
            dualSensorPanel.UpdateOutsideDeviceInfo(
                deviceConfig.OutsideSensor?.DeviceId ?? "Not configured (None)");
            
            // Update UI for None sensors
            if (deviceConfig.InsideSensor == null)
            {
                dualSensorPanel.SetInsideSensorEnabled(false);
                dualSensorPanel.UpdateInsideStatus("Not configured (None)");
            }
            
            if (deviceConfig.OutsideSensor == null)
            {
                dualSensorPanel.SetOutsideSensorEnabled(false);
                dualSensorPanel.UpdateOutsideStatus("Not configured (None)");
            }
            
            // Wire up events
            dualSensorPanel.InsideSensorEnabledChanged += (s, enabled) =>
            {
                m_InsideSensorEnabled = enabled;
                DeviceConfigManager.Instance.UpdateSensorEnabledState("inside", enabled);
                Console.WriteLine($"Inside sensor {(enabled ? "enabled" : "disabled")}");
                
                if (enabled)
                {
                    StartInsideSensorOperation();
                }
                else
                {
                    StopInsideSensorOperation();
                }
            };
            
            dualSensorPanel.OutsideSensorEnabledChanged += (s, enabled) =>
            {
                m_OutsideSensorEnabled = enabled;
                DeviceConfigManager.Instance.UpdateSensorEnabledState("outside", enabled);
                Console.WriteLine($"Outside sensor {(enabled ? "enabled" : "disabled")}");
                
                if (enabled)
                {
                    StartOutsideSensorOperation();
                }
                else
                {
                    StopOutsideSensorOperation();
                }
            };
            
            dualSensorPanel.ChangeConfigurationRequested += (s, e) =>
            {
                var result = MessageBox.Show(
                    "Changing room configuration will restart the application.\n\nContinue?",
                    "Change Room Configuration",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                    
                if (result == DialogResult.Yes)
                {
                    DeviceConfigManager.Instance.DeleteConfiguration();
                    Application.Restart();
                }
            };
            
            dualSensorPanel.SensorReassignmentRequested += (s, sensorIndices) =>
            {
                try
                {
                    // Stop current operations
                    StopInsideSensorOperation();
                    StopOutsideSensorOperation();
                    
                    // Update device configuration
                    deviceConfig.InsideSensor.SensorIndex = sensorIndices.insideIndex;
                    deviceConfig.OutsideSensor.SensorIndex = sensorIndices.outsideIndex;
                    
                    // Save updated configuration
                    DeviceConfigManager.Instance.SaveConfiguration(deviceConfig);
                    
                    // Restart sensor operations with new indices
                    StartInsideSensorOperation();
                    StartOutsideSensorOperation();
                    
                    MessageBox.Show(
                        "Sensor configuration updated successfully!\n\nSensors have been reassigned and restarted.",
                        "Configuration Applied",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Error applying sensor configuration:\n{ex.Message}",
                        "Configuration Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    Console.WriteLine($"❌ Sensor reassignment error: {ex.Message}");
                }
            };
            
            dualSensorPanel.TestInsideSensorRequested += (s, e) =>
            {
                TestSensorScan("inside");
            };
            
            dualSensorPanel.TestOutsideSensorRequested += (s, e) =>
            {
                TestSensorScan("outside");
            };
            
            dualSensorTab.Controls.Add(dualSensorPanel);
            
            // Add tab as first tab
            if (tabControl != null)
            {
                tabControl.TabPages.Insert(0, dualSensorTab);
                tabControl.SelectedTab = dualSensorTab;
            }
            
            // Initialize device list for easy testing
            dualSensorPanel.InitializeDeviceList();
            
            Console.WriteLine("✅ Dual Sensor Tab initialized");
        }

        private void InitializeDeviceConfigTab()
        {
            // Create device config tab
            var configTab = new TabPage("⚙️ Device Configuration");
            configTab.Padding = new Padding(0);
            configTab.BackColor = Color.FromArgb(245, 247, 250);
            
            // Create main panel with better layout
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(245, 247, 250),
                Padding = new Padding(30)
            };
            
            // Header section with gradient-like effect
            var headerPanel = new Panel
            {
                Location = new Point(30, 20),
                Size = new Size(800, 100),
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            
            headerPanel.Paint += (s, e) =>
            {
                // Add subtle shadow/border
                ControlPaint.DrawBorder(e.Graphics, headerPanel.ClientRectangle,
                    Color.FromArgb(220, 223, 230), ButtonBorderStyle.Solid);
            };
            
            var lblTitle = new Label
            {
                Text = "⚙️ Device Configuration",
                Location = new Point(20, 15),
                Size = new Size(400, 35),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };
            headerPanel.Controls.Add(lblTitle);
            
            var lblSubtitle = new Label
            {
                Text = $"Managing room: {deviceConfig?.RoomName ?? "Not configured"}",
                Location = new Point(20, 55),
                Size = new Size(700, 25),
                Font = new Font("Segoe UI", 11),
                ForeColor = Color.FromArgb(108, 117, 125)
            };
            headerPanel.Controls.Add(lblSubtitle);
            
            mainPanel.Controls.Add(headerPanel);
            
            // Sensor configuration cards
            int cardY = 140;
            
            // Inside Sensor Card
            var insideCard = CreateSensorCard(
                "Inside Door Sensor", 
                deviceConfig?.InsideSensor?.DeviceId ?? "None",
                deviceConfig?.InsideSensor != null ? "✓ Configured" : "○ Not Configured",
                deviceConfig?.InsideSensor != null,
                Color.FromArgb(40, 167, 69),
                30, cardY);
            mainPanel.Controls.Add(insideCard);
            
            // Outside Sensor Card
            var outsideCard = CreateSensorCard(
                "Outside Door Sensor",
                deviceConfig?.OutsideSensor?.DeviceId ?? "None",
                deviceConfig?.OutsideSensor != null ? "✓ Configured" : "○ Not Configured",
                deviceConfig?.OutsideSensor != null,
                Color.FromArgb(220, 53, 69),
                insideCard.Right + 20, cardY);
            mainPanel.Controls.Add(outsideCard);
            
            // Action buttons section
            var actionPanel = new Panel
            {
                Location = new Point(30, cardY + 220),
                Size = new Size(800, 80),
                BackColor = Color.FromArgb(255, 248, 225),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            
            actionPanel.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, actionPanel.ClientRectangle,
                    Color.FromArgb(255, 193, 7), ButtonBorderStyle.Solid);
            };
            
            var lblActionTitle = new Label
            {
                Text = "⚡ Quick Actions",
                Location = new Point(15, 10),
                Size = new Size(300, 25),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };
            actionPanel.Controls.Add(lblActionTitle);
            
            var btnReconfigure = new Button
            {
                Text = "🔄 Reconfigure Sensors",
                Location = new Point(15, 40),
                Size = new Size(180, 35),
                BackColor = Color.FromArgb(0, 123, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnReconfigure.FlatAppearance.BorderSize = 0;
            btnReconfigure.Click += BtnReconfigure_Click;
            actionPanel.Controls.Add(btnReconfigure);
            
            var btnChangeRoom = new Button
            {
                Text = "🏫 Change Room",
                Location = new Point(205, 40),
                Size = new Size(150, 35),
                BackColor = Color.FromArgb(108, 117, 125),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnChangeRoom.FlatAppearance.BorderSize = 0;
            btnChangeRoom.Click += (s, e) =>
            {
                var result = MessageBox.Show(
                    "Changing the room will restart the configuration.\n\nContinue?",
                    "Change Room",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                
                if (result == DialogResult.Yes)
                {
                    DeviceConfigManager.Instance.DeleteConfiguration();
                    Application.Restart();
                }
            };
            actionPanel.Controls.Add(btnChangeRoom);
            
            // Fix Room Assignment button
            var btnFixRoomAssignment = new Button
            {
                Text = "🔧 Fix Room Assignment",
                Location = new Point(365, 40),
                Size = new Size(180, 35),
                BackColor = Color.FromArgb(220, 53, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnFixRoomAssignment.FlatAppearance.BorderSize = 0;
            btnFixRoomAssignment.Click += BtnFixRoomAssignment_Click;
            actionPanel.Controls.Add(btnFixRoomAssignment);
            
            mainPanel.Controls.Add(actionPanel);
            
            // Help section
            var helpPanel = new Panel
            {
                Location = new Point(30, actionPanel.Bottom + 20),
                Size = new Size(800, 120),
                BackColor = Color.FromArgb(230, 244, 255),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            
            helpPanel.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, helpPanel.ClientRectangle,
                    Color.FromArgb(0, 123, 255), ButtonBorderStyle.Solid);
            };
            
            var lblHelpTitle = new Label
            {
                Text = "ℹ️ How to Reconfigure",
                Location = new Point(15, 10),
                Size = new Size(300, 25),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };
            helpPanel.Controls.Add(lblHelpTitle);
            
            var lblHelpText = new Label
            {
                Text = "• Click 'Reconfigure Sensors' to reassign fingerprint devices\n" +
                       "• Sensors can be set to 'None' if you only need one scanner\n" +
                       "• Changes take effect immediately without restarting the application\n" +
                       "• Use 'Change Room' if you need to select a different room",
                Location = new Point(15, 40),
                Size = new Size(760, 70),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(52, 58, 64)
            };
            helpPanel.Controls.Add(lblHelpText);
            
            mainPanel.Controls.Add(helpPanel);
            
            configTab.Controls.Add(mainPanel);
            
            // Add to tab control
            if (tabControl != null)
            {
                tabControl.TabPages.Add(configTab);
            }
            
            Console.WriteLine("✅ Device Configuration Tab initialized");
        }
        
        private Panel CreateSensorCard(string title, string deviceName, string status, bool isConfigured, Color accentColor, int x, int y)
        {
            var card = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(380, 200),
                BackColor = Color.White
            };
            
            card.Paint += (s, e) =>
            {
                // Draw border
                ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle,
                    Color.FromArgb(220, 223, 230), ButtonBorderStyle.Solid);
                    
                // Draw colored top bar
                using (var brush = new SolidBrush(accentColor))
                {
                    e.Graphics.FillRectangle(brush, 0, 0, card.Width, 5);
                }
            };
            
            var lblTitle = new Label
            {
                Text = title,
                Location = new Point(20, 20),
                Size = new Size(340, 30),
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };
            card.Controls.Add(lblTitle);
            
            var lblDevice = new Label
            {
                Text = "Device:",
                Location = new Point(20, 65),
                Size = new Size(80, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(108, 117, 125)
            };
            card.Controls.Add(lblDevice);
            
            var lblDeviceName = new Label
            {
                Text = deviceName,
                Location = new Point(20, 85),
                Size = new Size(340, 40),
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(52, 58, 64)
            };
            card.Controls.Add(lblDeviceName);
            
            var lblStatus = new Label
            {
                Text = status,
                Location = new Point(20, 140),
                Size = new Size(340, 25),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = isConfigured ? Color.FromArgb(40, 167, 69) : Color.FromArgb(108, 117, 125)
            };
            card.Controls.Add(lblStatus);
            
            // Status indicator circle
            var statusCircle = new Panel
            {
                Location = new Point(20, 170),
                Size = new Size(15, 15),
                BackColor = isConfigured ? Color.FromArgb(40, 167, 69) : Color.FromArgb(220, 223, 230)
            };
            statusCircle.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(statusCircle.BackColor))
                {
                    e.Graphics.FillEllipse(brush, 0, 0, statusCircle.Width - 1, statusCircle.Height - 1);
                }
            };
            card.Controls.Add(statusCircle);
            
            var lblStatusText = new Label
            {
                Text = isConfigured ? "Active" : "Inactive",
                Location = new Point(40, 168),
                Size = new Size(100, 20),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(108, 117, 125)
            };
            card.Controls.Add(lblStatusText);
            
            return card;
        }
        
        private void BtnFixRoomAssignment_Click(object sender, EventArgs e)
        {
            try
            {
                // Check if there's a room mismatch
                if (dbManager?.CurrentRoomId != null && deviceConfig?.RoomId != null)
                {
                    if (dbManager.CurrentRoomId != deviceConfig.RoomId)
                    {
                        var result = MessageBox.Show(
                            $"Room mismatch detected!\n\n" +
                            $"Database Room ID: {dbManager.CurrentRoomId}\n" +
                            $"Config Room ID: {deviceConfig.RoomId}\n\n" +
                            $"This could cause schedule validation issues.\n\n" +
                            $"Fix by updating the configuration to match the database?",
                            "Room Mismatch Detected",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        
                        if (result == DialogResult.Yes)
                        {
                            // Update the device configuration
                            deviceConfig.RoomId = dbManager.CurrentRoomId;
                            
                            // Get room details from database
                            var room = dbManager.GetCurrentRoom();
                            if (room != null)
                            {
                                deviceConfig.RoomName = room.DisplayName;
                                deviceConfig.Building = room.Building;
                            }
                            
                            // Save the updated configuration
                            DeviceConfigManager.Instance.SaveConfiguration(deviceConfig);
                            
                            MessageBox.Show(
                                $"Room assignment fixed!\n\n" +
                                $"Updated configuration:\n" +
                                $"Room: {deviceConfig.RoomName}\n" +
                                $"Room ID: {deviceConfig.RoomId}\n\n" +
                                $"Please restart the application for changes to take effect.",
                                "Room Assignment Fixed",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            "No room mismatch detected.\n\n" +
                            $"Current room assignment is correct:\n" +
                            $"Room: {deviceConfig.RoomName}\n" +
                            $"Room ID: {deviceConfig.RoomId}",
                            "Room Assignment OK",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
                else
                {
                    MessageBox.Show(
                        "Unable to check room assignment.\n\n" +
                        "Database manager or device configuration not available.",
                        "Check Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error fixing room assignment: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnReconfigure_Click(object sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("🔄 Starting reconfiguration process...");
                
                // Stop current sensor operations
                StopInsideSensorOperation();
                StopOutsideSensorOperation();
                
                // Wait a bit to ensure threads are fully stopped
                Console.WriteLine("⏳ Waiting for sensor operations to stop completely...");
                System.Threading.Thread.Sleep(500);
                Application.DoEvents();
                
                // Show configuration dialog
                var dialog = new StartupConfigDialog(dbManager);
                var result = dialog.ShowDialog();
                
                if (result == DialogResult.OK && dialog.SelectedConfiguration != null)
                {
                    Console.WriteLine("✅ New configuration selected");
                    
                    // Update device configuration
                    deviceConfig = dialog.SelectedConfiguration;
                    
                    // Save configuration
                    DeviceConfigManager.Instance.SaveConfiguration(deviceConfig);
                    
                    // Update UI
                    UpdateDualSensorPanelConfiguration();
                    
                    // Restart sensor operations with new configuration
                    Console.WriteLine("🚀 Restarting sensor operations with new configuration...");
                    StartDualSensorOperations();
                    
                    MessageBox.Show(
                        "Configuration updated successfully!\n\nSensors have been reconfigured.",
                        "Configuration Updated",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    
                    // Refresh the config tab to show new values
                    RefreshDeviceConfigTab();
                    
                    Console.WriteLine("✅ Reconfiguration complete!");
                }
                else
                {
                    Console.WriteLine("⚠️ Reconfiguration cancelled - restarting previous configuration");
                    // User cancelled - restart with existing config
                    StartDualSensorOperations();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error reconfiguring sensors:\n{ex.Message}",
                    "Configuration Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Console.WriteLine($"❌ Reconfiguration error: {ex.Message}");
                
                // Try to restart with existing config
                try
                {
                    Console.WriteLine("🔄 Attempting to restart with previous configuration...");
                    StartDualSensorOperations();
                }
                catch (Exception restartEx)
                {
                    Console.WriteLine($"❌ Failed to restart sensors: {restartEx.Message}");
                }
            }
        }
        
        private void UpdateDualSensorPanelConfiguration()
        {
            if (dualSensorPanel == null) return;
            
            dualSensorPanel.UpdateRoomInfo(deviceConfig.RoomName, deviceConfig.Building ?? "");
            dualSensorPanel.UpdateInsideDeviceInfo(
                deviceConfig.InsideSensor?.DeviceId ?? "Not configured (None)");
            dualSensorPanel.UpdateOutsideDeviceInfo(
                deviceConfig.OutsideSensor?.DeviceId ?? "Not configured (None)");
            
            // Update UI for None sensors
            if (deviceConfig.InsideSensor == null)
            {
                dualSensorPanel.SetInsideSensorEnabled(false);
                dualSensorPanel.UpdateInsideStatus("Not configured (None)");
            }
            else
            {
                dualSensorPanel.SetInsideSensorEnabled(true);
            }
            
            if (deviceConfig.OutsideSensor == null)
            {
                dualSensorPanel.SetOutsideSensorEnabled(false);
                dualSensorPanel.UpdateOutsideStatus("Not configured (None)");
            }
            else
            {
                dualSensorPanel.SetOutsideSensorEnabled(true);
            }
        }
        
        private void RefreshDeviceConfigTab()
        {
            // Find and refresh the config tab
            if (tabControl != null)
            {
                foreach (TabPage tab in tabControl.TabPages)
                {
                    if (tab.Text == "⚙️ Device Configuration")
                    {
                        // Remove and recreate the tab
                        tabControl.TabPages.Remove(tab);
                        InitializeDeviceConfigTab();
                        break;
                    }
                }
            }
        }

        private void StartDualSensorOperations()
        {
            Console.WriteLine("Starting dual sensor operations...");
            
            try
            {
                // Load users ONCE for both sensors (fixes database reader conflict)
                var sharedUserRecords = LoadUserRecordsForIdentification();
                m_IdentificationUsers = sharedUserRecords;
                Console.WriteLine($"✅ Loaded {sharedUserRecords.Count} user records for identification");
                
                // Validate against actual hardware
                var availableDevices = UsbDeviceHelper.EnumerateFingerprintDevices();
                Console.WriteLine($"✅ Detected {availableDevices.Count} physical fingerprint scanner(s)");
                
                int maxDeviceIndex = availableDevices.Count - 1;
                
                // Check if configuration is invalid (references devices that don't exist)
                bool invalidConfig = false;
                if (deviceConfig?.InsideSensor?.SensorIndex > maxDeviceIndex ||
                    deviceConfig?.OutsideSensor?.SensorIndex > maxDeviceIndex)
                {
                    invalidConfig = true;
                }
                
                bool sameDevice = (deviceConfig?.InsideSensor?.SensorIndex ?? -1) == 
                                  (deviceConfig?.OutsideSensor?.SensorIndex ?? -2);
                
                if (invalidConfig || (sameDevice && deviceConfig?.InsideSensor?.SensorIndex >= 0))
                {
                    Console.WriteLine("⚠️ WARNING: Configuration mismatch detected!");
                    Console.WriteLine($"   Config expects: Inside={deviceConfig?.InsideSensor?.SensorIndex}, Outside={deviceConfig?.OutsideSensor?.SensorIndex}");
                    Console.WriteLine($"   Available devices: {availableDevices.Count}");
                    Console.WriteLine("⚠️ Only the INSIDE sensor will be started.");
                    
                    // Only start inside sensor
                    if (m_InsideSensorEnabled)
                    {
                        StartInsideSensorOperation();
                    }
                    
                    // Disable outside sensor to prevent conflicts
                    m_OutsideSensorEnabled = false;
                    
                    // Schedule the warning dialog to show after form is fully loaded
                    if (this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            dualSensorPanel?.SetOutsideSensorEnabled(false);
                            dualSensorPanel?.UpdateOutsideStatus("⚠️ Configuration Mismatch - Disabled");
                            
                            var result = MessageBox.Show(
                                "⚠️ Configuration Mismatch Detected!\n\n" +
                                $"Your configuration expects:\n" +
                                $"  • Inside sensor at index {deviceConfig?.InsideSensor?.SensorIndex}\n" +
                                $"  • Outside sensor at index {deviceConfig?.OutsideSensor?.SensorIndex}\n\n" +
                                $"But only {availableDevices.Count} fingerprint scanner(s) detected.\n\n" +
                                "Would you like to reconfigure now?\n\n" +
                                "Click YES to open configuration dialog\n" +
                                "Click NO to run with Inside sensor only",
                                "Configuration Mismatch",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);
                            
                            if (result == DialogResult.Yes)
                            {
                                DeviceConfigManager.Instance.DeleteConfiguration();
                                Application.Restart();
                            }
                        }));
                    }
                    else
                    {
                        // If handle not created yet, schedule for later
                        this.Load += (s, e) =>
                        {
                            dualSensorPanel?.SetOutsideSensorEnabled(false);
                            dualSensorPanel?.UpdateOutsideStatus("⚠️ Configuration Mismatch - Disabled");
                            
                            var result = MessageBox.Show(
                                "⚠️ Configuration Mismatch Detected!\n\n" +
                                $"Your configuration expects:\n" +
                                $"  • Inside sensor at index {deviceConfig?.InsideSensor?.SensorIndex}\n" +
                                $"  • Outside sensor at index {deviceConfig?.OutsideSensor?.SensorIndex}\n\n" +
                                $"But only {availableDevices.Count} fingerprint scanner(s) detected.\n\n" +
                                "Would you like to reconfigure now?\n\n" +
                                "Click YES to open configuration dialog\n" +
                                "Click NO to run with Inside sensor only",
                                "Configuration Mismatch",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);
                            
                            if (result == DialogResult.Yes)
                            {
                                DeviceConfigManager.Instance.DeleteConfiguration();
                                Application.Restart();
                            }
                        };
                    }
                }
                else
                {
                    // Different devices or properly configured - start both
                    Console.WriteLine($"✅ Inside sensor using device index: {deviceConfig?.InsideSensor?.SensorIndex}");
                    Console.WriteLine($"✅ Outside sensor using device index: {deviceConfig?.OutsideSensor?.SensorIndex}");
                    
                    // Only start sensors that are actually configured (not None)
                    if (m_InsideSensorEnabled && deviceConfig?.InsideSensor != null)
                    {
                        StartInsideSensorOperation();
                    }
                    else if (deviceConfig?.InsideSensor == null)
                    {
                        Console.WriteLine("⚠️ Inside sensor not configured (set to None)");
                        m_InsideSensorEnabled = false;
                    }
                    
                    if (m_OutsideSensorEnabled && deviceConfig?.OutsideSensor != null)
                    {
                        StartOutsideSensorOperation();
                    }
                    else if (deviceConfig?.OutsideSensor == null)
                    {
                        Console.WriteLine("⚠️ Outside sensor not configured (set to None)");
                        m_OutsideSensorEnabled = false;
                    }
                }
                
                Console.WriteLine("✅ Dual sensor operations started");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error starting dual sensor operations: {ex.Message}");
                MessageBox.Show($"Error starting sensors:\n\n{ex.Message}", "Sensor Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartInsideSensorOperation()
        {
            if (!m_InsideSensorEnabled) return;
            
            Console.WriteLine("Starting inside sensor operation...");
            
            Task.Run(() =>
            {
                try
                {
                    // Create identification operation for inside sensor
                    m_InsideSensorOperation = new FutronicIdentification();
                    m_InsideSensorOperation.FakeDetection = false;
                    m_InsideSensorOperation.FFDControl = false;
                    m_InsideSensorOperation.FastMode = true;
                    m_InsideSensorOperation.FARN = 100;
                    m_InsideSensorOperation.Version = VersionCompatible.ftr_version_compatible;
                    
                    // Note: Futronic SDK device selection is handled internally
                    // The first available device will be used for inside sensor
                    Console.WriteLine($"Inside sensor configured (device index: {deviceConfig?.InsideSensor?.SensorIndex ?? 0})");
                    
                    // Wire up events
                    m_InsideSensorOperation.OnPutOn += (progress) =>
                    {
                        this.Invoke(new Action(() =>
                        {
                            dualSensorPanel?.UpdateInsideStatus("Scanning...");
                        }));
                    };
                    
                    m_InsideSensorOperation.UpdateScreenImage += (bitmap) =>
                    {
                        this.Invoke(new Action(() =>
                        {
                            dualSensorPanel?.UpdateInsideFingerprintImage(bitmap);
                        }));
                    };
                    
                    // Use pre-loaded shared user records
                    if (m_IdentificationUsers == null || m_IdentificationUsers.Count == 0)
                    {
                        Console.WriteLine("❌ No user records loaded for identification!");
                        return;
                    }
                    
                    Console.WriteLine($"✅ Inside sensor operation configured with {m_IdentificationUsers.Count} users");
                    this.Invoke(new Action(() =>
                    {
                        dualSensorPanel?.UpdateInsideStatus("Active");
                    }));
                    
                    // Start continuous identification
                    while (!m_bExit && m_InsideSensorEnabled)
                    {
                        int matchIndex = -1;
                        try
                        {
                            // Check if operation is still valid
                            if (m_InsideSensorOperation == null)
                            {
                                Console.WriteLine("❌ Inside sensor operation is null, stopping...");
                                break;
                            }
                            
                            // Convert UserRecord list to FtrIdentifyRecord array
                            var records = new FtrIdentifyRecord[m_IdentificationUsers.Count];
                            for (int i = 0; i < m_IdentificationUsers.Count; i++)
                            {
                                records[i] = m_IdentificationUsers[i].GetRecord();
                            }
                            
                            var result = m_InsideSensorOperation.Identification(records, ref matchIndex);
                            if (result == FutronicSdkBase.RETCODE_OK)
                            {
                                this.Invoke(new Action(() =>
                                {
                                    HandleSensorScan(true, matchIndex, "inside");
                                }));
                            }
                            else
                            {
                                // Log non-OK results but don't spam
                                Console.WriteLine($"Inside sensor result: {result}");
                            }
                        }
                        catch (InvalidOperationException ex)
                        {
                            Console.WriteLine($"❌ Inside sensor operation invalid: {ex.Message}");
                            Console.WriteLine("Stopping inside sensor due to invalid operation state...");
                            break; // Exit the loop to prevent infinite error spam
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Inside sensor identification error: {ex.Message}");
                            
                            // If we get repeated errors, increase delay to reduce spam
                            System.Threading.Thread.Sleep(2000);
                            continue;
                        }
                        System.Threading.Thread.Sleep(500); // Small delay between scans
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Inside sensor error: {ex.Message}");
                    this.Invoke(new Action(() =>
                    {
                        dualSensorPanel?.UpdateInsideStatus($"Error: {ex.Message}");
                    }));
                }
            });
        }

        private void StartOutsideSensorOperation()
        {
            if (!m_OutsideSensorEnabled) return;
            
            Console.WriteLine("Starting outside sensor operation...");
            
            Task.Run(() =>
            {
                try
                {
                    // Create identification operation for outside sensor
                    m_OutsideSensorOperation = new FutronicIdentification();
                    m_OutsideSensorOperation.FakeDetection = false;
                    m_OutsideSensorOperation.FFDControl = false;
                    m_OutsideSensorOperation.FastMode = true;
                    m_OutsideSensorOperation.FARN = 100;
                    m_OutsideSensorOperation.Version = VersionCompatible.ftr_version_compatible;
                    
                    // Note: Futronic SDK device selection is handled internally  
                    // When multiple devices are present, SDK manages device access
                    Console.WriteLine($"Outside sensor configured (device index: {deviceConfig?.OutsideSensor?.SensorIndex ?? 1})");
                    
                    // Wire up events
                    m_OutsideSensorOperation.OnPutOn += (progress) =>
                    {
                        this.Invoke(new Action(() =>
                        {
                            dualSensorPanel?.UpdateOutsideStatus("Scanning...");
                        }));
                    };
                    
                    m_OutsideSensorOperation.UpdateScreenImage += (bitmap) =>
                    {
                        this.Invoke(new Action(() =>
                        {
                            dualSensorPanel?.UpdateOutsideFingerprintImage(bitmap);
                        }));
                    };
                    
                    // Use pre-loaded shared user records
                    if (m_IdentificationUsers == null || m_IdentificationUsers.Count == 0)
                    {
                        Console.WriteLine("❌ No user records loaded for identification!");
                        return;
                    }
                    
                    Console.WriteLine($"✅ Outside sensor operation configured with {m_IdentificationUsers.Count} users");
                    this.Invoke(new Action(() =>
                    {
                        dualSensorPanel?.UpdateOutsideStatus("Active");
                    }));
                    
                    // Start continuous identification
                    while (!m_bExit && m_OutsideSensorEnabled)
                    {
                        int matchIndex = -1;
                        try
                        {
                            // Check if operation is still valid
                            if (m_OutsideSensorOperation == null)
                            {
                                Console.WriteLine("❌ Outside sensor operation is null, stopping...");
                                break;
                            }
                            
                            // Convert UserRecord list to FtrIdentifyRecord array
                            var records = new FtrIdentifyRecord[m_IdentificationUsers.Count];
                            for (int i = 0; i < m_IdentificationUsers.Count; i++)
                            {
                                records[i] = m_IdentificationUsers[i].GetRecord();
                            }
                            
                            var result = m_OutsideSensorOperation.Identification(records, ref matchIndex);
                            if (result == FutronicSdkBase.RETCODE_OK)
                            {
                                this.Invoke(new Action(() =>
                                {
                                    HandleSensorScan(true, matchIndex, "outside");
                                }));
                            }
                            else
                            {
                                // Log non-OK results but don't spam
                                Console.WriteLine($"Outside sensor result: {result}");
                            }
                        }
                        catch (InvalidOperationException ex)
                        {
                            Console.WriteLine($"❌ Outside sensor operation invalid: {ex.Message}");
                            Console.WriteLine("Stopping outside sensor due to invalid operation state...");
                            break; // Exit the loop to prevent infinite error spam
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Outside sensor identification error: {ex.Message}");
                            
                            // If we get repeated errors, increase delay to reduce spam
                            System.Threading.Thread.Sleep(2000);
                            continue;
                        }
                        System.Threading.Thread.Sleep(500); // Small delay between scans
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Outside sensor error: {ex.Message}");
                    this.Invoke(new Action(() =>
                    {
                        dualSensorPanel?.UpdateOutsideStatus($"Error: {ex.Message}");
                    }));
                }
            });
        }

        private void StopInsideSensorOperation()
        {
            Console.WriteLine("Stopping inside sensor operation...");
            
            try
            {
                if (m_InsideSensorOperation != null)
                {
                    m_InsideSensorOperation.OnCalcel();
                    
                    // Wait for thread to actually stop (max 2 seconds)
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    while (m_InsideSensorOperation != null && stopwatch.ElapsedMilliseconds < 2000)
                    {
                        System.Threading.Thread.Sleep(100);
                        Application.DoEvents(); // Process UI events
                    }
                    
                    m_InsideSensorOperation = null;
                    Console.WriteLine("✅ Inside sensor operation stopped");
                }
                dualSensorPanel?.UpdateInsideStatus("Disabled");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping inside sensor: {ex.Message}");
            }
        }

        private void StopOutsideSensorOperation()
        {
            Console.WriteLine("Stopping outside sensor operation...");
            
            try
            {
                if (m_OutsideSensorOperation != null)
                {
                    m_OutsideSensorOperation.OnCalcel();
                    
                    // Wait for thread to actually stop (max 2 seconds)
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    while (m_OutsideSensorOperation != null && stopwatch.ElapsedMilliseconds < 2000)
                    {
                        System.Threading.Thread.Sleep(100);
                        Application.DoEvents(); // Process UI events
                    }
                    
                    m_OutsideSensorOperation = null;
                    Console.WriteLine("✅ Outside sensor operation stopped");
                }
                dualSensorPanel?.UpdateOutsideStatus("Disabled");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping outside sensor: {ex.Message}");
            }
        }

        private void HandleSensorScan(bool success, int matchIndex, string location)
        {
            try
            {
                if (success && matchIndex >= 0)
                {
                    // Match found
                    var users = LoadUserRecordsForIdentification();
                    if (matchIndex < users.Count)
                    {
                        var matchedUser = users[matchIndex];
                        string userName = matchedUser.UserName ?? "Unknown User";
                        
                        Console.WriteLine($"✅ Match found on {location} sensor: {userName}");
                        
                        // Get the user GUID from database
                        var dbUser = dbManager.LoadAllUsers().FirstOrDefault(u => 
                            u.Username.Equals(userName, StringComparison.OrdinalIgnoreCase));
                        
                        if (dbUser != null)
                        {
                            // Record attendance
                            string deviceId = location == "inside" ? 
                                deviceConfig.InsideSensor.DeviceId : 
                                deviceConfig.OutsideSensor.DeviceId;
                            
                            var attendanceResult = dbManager.RecordAttendanceWithDeviceId(
                                dbUser.EmployeeId,
                                deviceId,
                                location,
                                null);
                            
                            // Update UI
                            string action = location == "inside" ? "Time In" : "Time Out";
                            string statusMsg = attendanceResult.Success ? 
                                $"{action} recorded" : 
                                $"Failed: {attendanceResult.Reason}";
                            
                            if (location == "inside")
                            {
                                dualSensorPanel?.UpdateInsideLastScan(userName, statusMsg, attendanceResult.Success);
                                dualSensorPanel?.UpdateInsideStatus("Active");
                            }
                            else
                            {
                                dualSensorPanel?.UpdateOutsideLastScan(userName, statusMsg, attendanceResult.Success);
                                dualSensorPanel?.UpdateOutsideStatus("Active");
                            }
                            
                            // Add to activity feed
                            dualSensorPanel?.AddActivityItem(new ActivityItem
                            {
                                Timestamp = DateTime.Now,
                                UserName = userName,
                                Action = action,
                                Location = location,
                                Success = attendanceResult.Success,
                                StatusMessage = statusMsg
                            });
                            
                            // Update heartbeat
                            dbManager.UpdateDeviceHeartbeat(deviceConfig.RoomName, location);
                            
                            // Request lock control for successful authentication
                            System.Threading.Tasks.Task.Run(async () => {
                                try
                                {
                                    await RequestLockControl(dbUser.EmployeeId, action);
                                }
                                catch (Exception lockEx)
                                {
                                    Console.WriteLine($"Lock control request failed: {lockEx.Message}");
                                }
                            });
                        }
                        else
                        {
                            Console.WriteLine($"⚠ User {userName} not found in database");
                            if (location == "inside")
                            {
                                dualSensorPanel?.UpdateInsideLastScan(userName, "User not in database", false);
                            }
                            else
                            {
                                dualSensorPanel?.UpdateOutsideLastScan(userName, "User not in database", false);
                            }
                        }
                    }
                }
                else
                {
                    // No match or error
                    Console.WriteLine($"⚠ No match on {location} sensor (success: {success}, matchIndex: {matchIndex})");
                    
                    if (location == "inside")
                    {
                        dualSensorPanel?.UpdateInsideLastScan("Unknown", "No match found", false);
                        dualSensorPanel?.UpdateInsideStatus("Active");
                    }
                    else
                    {
                        dualSensorPanel?.UpdateOutsideLastScan("Unknown", "No match found", false);
                        dualSensorPanel?.UpdateOutsideStatus("Active");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error handling sensor scan: {ex.Message}");
            }
        }

        private void TestSensorScan(string location)
        {
            // Test mode simulation
            string testUser = "Test User";
            bool success = true;
            string status = "Test scan successful";
            
            if (location == "inside")
            {
                dualSensorPanel?.UpdateInsideLastScan(testUser, status, success);
            }
            else
            {
                dualSensorPanel?.UpdateOutsideLastScan(testUser, status, success);
            }
            
            dualSensorPanel?.AddActivityItem(new ActivityItem
            {
                Timestamp = DateTime.Now,
                UserName = testUser,
                Action = location == "inside" ? "Time In (Test)" : "Time Out (Test)",
                Location = location,
                Success = success,
                StatusMessage = status
            });
            
            MessageBox.Show($"Test scan on {location} sensor completed!", "Test Mode", 
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private List<UserRecord> LoadUserRecordsForIdentification()
        {
            try
            {
                var users = dbManager.LoadAllUsers();
                var userRecords = new List<UserRecord>();
                
                foreach (var user in users)
                {
                    if (user.FingerprintTemplate != null && user.FingerprintTemplate.Length > 0)
                    {
                        userRecords.Add(new UserRecord
                        {
                            UserName = user.Username,
                            Template = user.FingerprintTemplate
                        });
                    }
                }
                
                return userRecords;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading users for identification: {ex.Message}");
                return new List<UserRecord>();
            }
        }

        // ==================== END DUAL SENSOR MODE METHODS ====================
    }

    // Data classes
    public class UserRecord
    {
        public string UserName { get; set; }
        public byte[] Template { get; set; }

        public bool Save(string fileName)
        {
            try
            {
                using (var writer = new BinaryWriter(File.Create(fileName)))
                {
                    writer.Write(UserName);
                    writer.Write(Template.Length);
                    writer.Write(Template);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static UserRecord Load(string fileName)
        {
            try
            {
                using (var reader = new BinaryReader(File.OpenRead(fileName)))
                {
                    var user = new UserRecord();
                    user.UserName = reader.ReadString();
                    int templateLength = reader.ReadInt32();
                    user.Template = reader.ReadBytes(templateLength);
                    return user;
                }
            }
            catch
            {
                return null;
            }
        }

        public FtrIdentifyRecord GetRecord()
        {
            var record = new FtrIdentifyRecord();
            // Set key value (first 16 bytes of template or padded)
            byte[] keyValue = new byte[16];
            
            // Add null check to prevent index out of bounds error
            if (Template != null && Template.Length > 0)
            {
                if (Template.Length >= 16)
                {
                    Array.Copy(Template, keyValue, 16);
                }
                else
                {
                    Array.Copy(Template, keyValue, Template.Length);
                }
            }
            // If Template is null or empty, keyValue remains all zeros
            
            record.KeyValue = keyValue;
            record.Template = Template ?? new byte[0]; // Prevent null template
            return record;
        }

    }

}
