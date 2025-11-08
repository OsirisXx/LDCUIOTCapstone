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
        private const string LOCK_CONTROLLER_API_KEY = "0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567";

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
        private CheckBox chkAllowUnauthorizedFingerprints;
        private CheckBox chkFingerprintOnly;
        private CheckBox chkRfidOnly;
        private CheckBox chkAllowInstructorDoorAccess;

        private bool IsFingerprintOnlyMode => deviceConfig?.AllowFingerprintOnly == true;
        private bool IsRfidOnlyMode => deviceConfig?.AllowRfidOnly == true;
        private bool IsDualAuthRequired => !IsFingerprintOnlyMode && !IsRfidOnlyMode;
        
        // Track which sensor detected the current fingerprint scan
        private string currentScanLocation = "inside";
        private DateTime lastUnauthorizedDoorOpenTime = DateTime.MinValue;
        private const int UNAUTHORIZED_DOOR_DEBOUNCE_MS = 10000;
        
        // Operation state tracking
        private bool m_bEnrollmentInProgress = false;
        private bool m_bAttendanceActive = false;
        
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
        private string currentSubjectName = null;
        private string currentInstructorName = null;
        
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
                    testOperation.FARN = 150;
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
        
        // Debouncing for unknown scans to prevent spam
        private DateTime lastUnknownScanLogTime = DateTime.MinValue;
        private const int UNKNOWN_SCAN_DEBOUNCE_MS = 5000; // 5 seconds between unknown scan logs

        // INSTRUCTOR SCAN BLOCKING: Prevent spam during 5-second delays
        private bool isProcessingInstructorScan = false;
        private bool isPausedForInstructorAction = false;

        // TWO-SCAN VERIFICATION: For security and accuracy
        private DateTime verificationScanStartTime = DateTime.MinValue;
        private const int VERIFICATION_TIMEOUT_SECONDS = 15; // 15 seconds to complete verification
        
        // CROSS-TYPE DUAL-AUTHENTICATION: RFID OR Fingerprint first, then the OTHER type
        private bool awaitingCrossTypeVerification = false;
        private string firstScanType = ""; // "RFID" or "FINGERPRINT"
        private string pendingCrossVerificationUser = "";
        private string pendingCrossVerificationGuid = "";
        private DateTime crossVerificationStartTime = DateTime.MinValue;
        private const int CROSS_VERIFICATION_TIMEOUT_SECONDS = 20; // 20 seconds to complete cross-type verification
        
        // EARLY-ARRIVAL DUAL-AUTHENTICATION (outside sensor, no active session)
        private bool awaitingEarlyArrivalVerification = false;
        private string earlyFirstScanType = ""; // "RFID" or "FINGERPRINT"
        private string earlyPendingUser = "";
        private string earlyPendingGuid = "";
        private DateTime earlyVerificationStartTime = DateTime.MinValue;

        private void ResetCrossVerificationState()
        {
            awaitingCrossTypeVerification = false;
            firstScanType = "";
            pendingCrossVerificationUser = "";
            pendingCrossVerificationGuid = "";
            crossVerificationStartTime = DateTime.MinValue;
        }

        private void ResetEarlyArrivalVerificationState()
        {
            awaitingEarlyArrivalVerification = false;
            earlyFirstScanType = "";
            earlyPendingUser = "";
            earlyPendingGuid = "";
            earlyVerificationStartTime = DateTime.MinValue;
        }
        
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
        
        // Auto-refresh timer for live updates
        private System.Windows.Forms.Timer autoRefreshTimer;
		
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
        private Button btnForceEndSession;
        // Removed unused fields: lblStatus, userListBox, btnDeleteUser, btnRefreshUsers
        
        // Location and Room controls
        private ComboBox cmbLocation;
        private ComboBox cmbRoom;
        private Button btnChangeRoom;
        private Label lblCurrentRoom;

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
        
        // Finger detection state: Prevent repeated scans while finger is on scanner
        private bool isFingerOnScanner = false;
        private bool isFingerOnInsideSensor = false;
        private bool isFingerOnOutsideSensor = false;
        
        // Quality-based validation: Track valid finger placement before accepting scans
        // Only process scans if OnPutOn was called first (valid finger placement detected)
        private bool hasValidFingerPlacement = false;
        private DateTime lastValidPutOnTime = DateTime.MinValue;
        private const int VALID_PLACEMENT_TIMEOUT_MS = 2000; // Valid placement expires after 2 seconds
        
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
            // Set log level to Info to reduce console noise (set to LogLevel.Debug for verbose output)
            Utils.Logger.SetLogLevel(Utils.LogLevel.Info);
            
            Utils.Logger.Info("MainForm constructor starting...");
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
                Utils.Logger.Debug("Loading configuration...");
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
            // API key is now added per-request in SendApiHeartbeatAsync
            // Removed global API key setting to avoid duplication

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
                // Use ChangeCurrentRoom to properly update the database
                bool roomChanged = dbManager.ChangeCurrentRoom(deviceConfig.RoomId);
                if (roomChanged)
                {
                    Console.WriteLine($"  ✅ DatabaseManager room updated to: {deviceConfig.RoomName} (ID: {deviceConfig.RoomId})");
                }
                else
                {
                    Console.WriteLine($"  ⚠️ Failed to update DatabaseManager room to: {deviceConfig.RoomName} (ID: {deviceConfig.RoomId})");
                    // Fallback: set the property directly
                    dbManager.CurrentRoomId = deviceConfig.RoomId;
                    Console.WriteLine($"  ⚠️ Set DatabaseManager.CurrentRoomId directly = {deviceConfig.RoomId}");
                }
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
                // API key is now added per-request in SendApiHeartbeatAsync
                // Removed global API key setting to avoid duplication
                
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
            Console.WriteLine($"⏰ Starting heartbeat timer with interval: {config.Application.HeartbeatInterval}ms");
            heartbeatTimer.Tick += (s, e) => 
            {
                try
                {
                    Console.WriteLine("⏰ Heartbeat timer triggered");
                    dbManager?.UpdateHeartbeat();
                    // Also send heartbeat to web backend for dashboard device presence
                    _ = SendApiHeartbeatAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Heartbeat timer error: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Heartbeat failed: {ex.Message}");
                }
            };
            heartbeatTimer.Start();
            Console.WriteLine("✅ Heartbeat timer started");

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
                Console.WriteLine("🔄 Sending heartbeat to backend...");
                
                // Get room information from device configuration
                string roomNumber = null;
                string roomId = null;
                if (deviceConfig != null)
                {
                    roomNumber = deviceConfig.RoomName;
                    roomId = deviceConfig.RoomId;
                    Console.WriteLine($"📍 Using device config - Room: {roomNumber}, ID: {roomId}");
                }
                else
                {
                    // Fallback to ComboBox method for non-dual sensor mode
                    roomNumber = GetCurrentRoomNumberSafe();
                    Console.WriteLine($"📍 Using ComboBox fallback - Room: {roomNumber}");
                }

                var payload = new
                {
                    deviceType = "Fingerprint_Scanner",
                    deviceId = config?.Device?.DeviceId ?? Environment.MachineName,
                    location = config?.Device?.Location ?? null,
                    roomId = roomId,
                    roomNumber = roomNumber,
                    hostname = Environment.MachineName,
                    ipAddress = GetLocalIPv4Safe(),
                    appVersion = Application.ProductVersion,
                    capabilities = new[] { "fingerprint", "futronic" }
                };

                var json = JsonSerializer.Serialize(payload);
                Console.WriteLine($"📤 Heartbeat payload: {json}");
                
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    string apiKey = "0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567".Trim();
                    content.Headers.Add("x-device-api-key", apiKey);
                    Console.WriteLine($"🔑 API Key being sent: '{apiKey}' (length: {apiKey.Length})");
                    var url = $"{backendBaseUrl}/api/devices/heartbeat";
                    Console.WriteLine($"🌐 Sending to: {url}");
                    
                    using (var response = await http.PostAsync(url, content))
                    {
                        Console.WriteLine($"✅ Heartbeat sent - Status: {response.StatusCode}");
                        if (response.StatusCode != System.Net.HttpStatusCode.Created)
                        {
                            var responseContent = await response.Content.ReadAsStringAsync();
                            Console.WriteLine($"❌ Response content: {responseContent}");
                        }
                        // No throw; best-effort
                        _ = response.StatusCode;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SendApiHeartbeatAsync error: {ex.Message}");
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

            // Create unified attendance tab
            attendanceTab = new TabPage("Attendance");
            tabControl.TabPages.Add(attendanceTab);

            // Create attendance scenarios configuration tab
            scenariosTab = new TabPage("Attendance Scenarios");
            tabControl.TabPages.Add(scenariosTab);

            InitializeEnrollmentTab();
            InitializeAttendanceTab();
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

            // NEW: Registration instructions panel (below fingerprint image)
            var registrationInstructionsPanel = new Panel { Dock = DockStyle.Top, Height = 220, Padding = new Padding(8, 8, 8, 8) };
            registrationInstructionsPanel.BackColor = Color.FromArgb(249, 251, 253);
            registrationInstructionsPanel.BorderStyle = BorderStyle.FixedSingle;
            
            var generalTip = new Label 
            { 
                Dock = DockStyle.Fill, 
                Text = "💡 Tip: Users must register both fingerprint and RFID for dual authentication", 
                Font = new Font("Segoe UI", 8, FontStyle.Italic),
                ForeColor = Color.FromArgb(80, 100, 160),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 5, 0, 0)
            };
            registrationInstructionsPanel.Controls.Add(generalTip);
            
            var rfidInstructions = new Label 
            { 
                Dock = DockStyle.Top, 
                Height = 45, 
                Text = "🔑 RFID Card:\n• Double-click a user from the table\n• Click 'Assign RFID' in Actions column\n• Scan RFID card when prompted", 
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                ForeColor = Color.FromArgb(60, 90, 140),
                Padding = new Padding(0, 5, 0, 0)
            };
            registrationInstructionsPanel.Controls.Add(rfidInstructions);
            
            var fingerprintInstructions = new Label 
            { 
                Dock = DockStyle.Top, 
                Height = 60, 
                Text = "📷 Fingerprint:\n• Double-click a user from the table\n• Click 'Start Enrollment'\n• Place thumb on sensor 3-4 times until complete", 
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                ForeColor = Color.FromArgb(60, 90, 140),
                Padding = new Padding(0, 5, 0, 0)
            };
            registrationInstructionsPanel.Controls.Add(fingerprintInstructions);
            
            var instructionsTitle = new Label 
            { 
                Dock = DockStyle.Top, 
                Height = 25, 
                Text = "How to Register:", 
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 60, 120)
            };
            registrationInstructionsPanel.Controls.Add(instructionsTitle);
            
            leftPanel.Controls.Add(registrationInstructionsPanel);

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
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { 
                Name = "HasRfid", HeaderText = "RFID", Width = 80,
                DefaultCellStyle = new DataGridViewCellStyle { 
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 8F, FontStyle.Bold)
                }
            });
            
            // Add Actions column with both options displayed visually
            var actionsColumn = new DataGridViewTextBoxColumn {
                Name = "Actions",
                HeaderText = "Actions",
                Width = 200,
                ReadOnly = true
            };
            dgvUsers.Columns.Add(actionsColumn);
            
            // Handle cell click for action buttons
            dgvUsers.CellClick += DgvUsers_CellClick;
            dgvUsers.CellPainting += DgvUsers_CellPainting;
            
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
                
                // Sort users: enrolled users (with fingerprints) first
                filteredUsers = filteredUsers.OrderByDescending(u => 
                    u.FingerprintTemplate != null && u.FingerprintTemplate.Length > 0
                ).ThenBy(u => u.LastName).ThenBy(u => u.FirstName).ToList();
                
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
                    
                    // RFID registration status
                    var hasRfid = !string.IsNullOrEmpty(user.RfidTag);
                    dgvUsers.Rows[row].Cells["HasRfid"].Value = hasRfid ? "✓ YES" : "✗ NO";
                    if (hasRfid)
                    {
                        dgvUsers.Rows[row].Cells["HasRfid"].Style.BackColor = Color.FromArgb(212, 237, 218);
                        dgvUsers.Rows[row].Cells["HasRfid"].Style.ForeColor = Color.FromArgb(21, 87, 36);
                    }
                    else
                    {
                        dgvUsers.Rows[row].Cells["HasRfid"].Style.BackColor = Color.FromArgb(255, 243, 205);
                        dgvUsers.Rows[row].Cells["HasRfid"].Style.ForeColor = Color.FromArgb(133, 100, 4);
                    }
                    
                    // Set actions text based on fingerprint and RFID status
                    string actionsText = "";
                    
                    // Fingerprint action
                    if (hasFingerprint)
                    {
                        actionsText = "Delete FP";
                    }
                    else
                    {
                        actionsText = "Enroll FP";
                    }
                    
                    // RFID action
                    if (hasRfid)
                    {
                        actionsText += " | Delete RFID";
                    }
                    else
                    {
                        actionsText += " | Assign RFID";
                    }
                    
                    dgvUsers.Rows[row].Cells["Actions"].Value = actionsText;
                    
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

        private void DgvUsers_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            
            var columnName = dgvUsers.Columns[e.ColumnIndex].Name;
            
            // Check if Actions column was clicked
            if (columnName == "Actions")
            {
                var row = dgvUsers.Rows[e.RowIndex];
                var user = row.Tag as User;
                
                if (user == null) return;
                
                var actionsText = row.Cells["Actions"].Value?.ToString() ?? "";
                var hasFingerprint = user.FingerprintTemplate != null && user.FingerprintTemplate.Length > 0;
                var hasRfid = !string.IsNullOrEmpty(user.RfidTag);
                
                var cellBounds = dgvUsers.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                var clickX = dgvUsers.PointToClient(Control.MousePosition).X - cellBounds.Left;
                var cellWidth = cellBounds.Width;
                
                // Split cell into two halves: left = Fingerprint action, right = RFID action
                if (clickX < cellWidth / 2)
                {
                    // Left half clicked - Fingerprint action
                    if (hasFingerprint)
                    {
                        DeleteUserFingerprint(user);
                    }
                    else
                    {
                        ReEnrollUserFingerprint(user);
                    }
                }
                else
                {
                    // Right half clicked - RFID action
                    if (hasRfid)
                    {
                        DeleteUserRfid(user);
                    }
                    else
                    {
                        AssignUserRfid(user);
                    }
                }
            }
        }
        private void DgvUsers_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            // Only paint the Actions column
            if (e.ColumnIndex >= 0 && e.RowIndex >= 0 && dgvUsers.Columns[e.ColumnIndex].Name == "Actions")
            {
                var cellBounds = e.CellBounds;
                var actionsText = e.Value?.ToString() ?? "";
                
                // Clear the cell background
                e.PaintBackground(cellBounds, true);
                
                // Check if row is selected
                bool isSelected = (e.State & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;
                
                // Choose colors based on selection state
                Color leftColor = isSelected ? Color.White : Color.FromArgb(0, 123, 255);
                Color rightColor = isSelected ? Color.White : Color.FromArgb(40, 167, 69);
                Color dividerColor = isSelected ? Color.White : Color.Gray;
                
                // Check if we need to split the text into two parts
                if (actionsText.Contains("|"))
                {
                    var splitText = actionsText.Split('|');
                    var leftText = splitText[0].Trim();
                    var rightText = splitText[1].Trim();
                    
                    // Draw left part (Delete) with conditional color
                    var leftRect = new Rectangle(cellBounds.Left + 5, cellBounds.Top + 3, cellBounds.Width / 2 - 5, cellBounds.Height - 6);
                    using (var brush = new SolidBrush(leftColor))
                    {
                        e.Graphics.DrawString(leftText, e.CellStyle.Font, brush, leftRect, new StringFormat { 
                            Alignment = StringAlignment.Center, 
                            LineAlignment = StringAlignment.Center 
                        });
                    }
                    
                    // Draw divider with conditional color
                    using (var pen = new Pen(dividerColor))
                    {
                        e.Graphics.DrawLine(pen, cellBounds.Left + cellBounds.Width / 2, cellBounds.Top + 3, 
                                            cellBounds.Left + cellBounds.Width / 2, cellBounds.Bottom - 3);
                    }
                    
                    // Draw right part (Re-enroll) with conditional color
                    var rightRect = new Rectangle(cellBounds.Left + cellBounds.Width / 2 + 5, cellBounds.Top + 3, 
                                                 cellBounds.Width / 2 - 10, cellBounds.Height - 6);
                    using (var brush = new SolidBrush(rightColor))
                    {
                        e.Graphics.DrawString(rightText, e.CellStyle.Font, brush, rightRect, new StringFormat { 
                            Alignment = StringAlignment.Center, 
                            LineAlignment = StringAlignment.Center 
                        });
                    }
                }
                else
                {
                    // Single action (Enroll) with conditional color
                    using (var brush = new SolidBrush(rightColor))
                    {
                        e.Graphics.DrawString(actionsText, e.CellStyle.Font, brush, cellBounds, new StringFormat { 
                            Alignment = StringAlignment.Center, 
                            LineAlignment = StringAlignment.Center 
                        });
                    }
                }
                
                e.Handled = true;
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

        private void DeleteUserFingerprint(User user)
        {
            try
            {
                // Confirm deletion
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the fingerprint for {user.FirstName} {user.LastName}?",
                    "Confirm Fingerprint Deletion",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                
                if (result != DialogResult.Yes) return;
                
                // Delete the fingerprint from database
                if (dbManager != null && !string.IsNullOrEmpty(user.EmployeeId))
                {
                    bool deleted = dbManager.DeleteUserFingerprintByGuid(user.EmployeeId);
                    
                    if (deleted)
                    {
                        MessageBox.Show(
                            $"Fingerprint deleted successfully for {user.FirstName} {user.LastName}.",
                            "Success",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        
                        // Refresh user list
                        cloudUsers = dbManager.LoadAllUsers();
                        LoadUsersIntoTable();
                        SetStatusText($"Fingerprint deleted for {user.FirstName} {user.LastName}");
                    }
                    else
                    {
                        MessageBox.Show(
                            "Failed to delete fingerprint. It may not exist in the database.",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error deleting fingerprint: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                SetStatusText($"Error: {ex.Message}");
            }
        }

        private void ReEnrollUserFingerprint(User user)
        {
            try
            {
                // Select the user
                selectedUser = user;
                isUserSelected = true;
                
                // Update UI to show selected user
                lblSelectedUser.Text = $"Selected: {user.FirstName} {user.LastName} ({user.UserType})";
                lblSelectedUser.ForeColor = Color.DarkGreen;
                btnEnroll.Enabled = true;
                btnEnroll.Text = $"Start Enrollment for {user.FirstName}";
                
                // Check if user already has a fingerprint
                var hasFingerprint = user.FingerprintTemplate != null && user.FingerprintTemplate.Length > 0;
                
                if (hasFingerprint)
                {
                    // Confirm re-enrollment
                    var result = MessageBox.Show(
                        $"This user already has a fingerprint enrolled. Do you want to replace it?",
                        "Confirm Re-enrollment",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    
                    if (result != DialogResult.Yes) return;
                }
                
                // Start enrollment process
                SetStatusText($"Ready to enroll fingerprint for {user.FirstName} {user.LastName}");
                UpdateEnrollmentGuidance(0, "Click 'Start Enrollment' button to begin.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error preparing for enrollment: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                SetStatusText($"Error: {ex.Message}");
            }
        }

        private void AssignUserRfid(User user)
        {
            try
            {
                // Create a simple input dialog for RFID tag
                Form rfidDialog = new Form
                {
                    Text = $"Assign RFID - {user.FirstName} {user.LastName}",
                    Size = new Size(400, 200),
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false
                };

                Label lblInstruction = new Label
                {
                    Text = "Enter RFID tag code:",
                    Location = new Point(20, 20),
                    Size = new Size(350, 20)
                };

                TextBox txtRfidTag = new TextBox
                {
                    Location = new Point(20, 50),
                    Size = new Size(350, 25),
                    MaxLength = 50
                };

                Button btnAssign = new Button
                {
                    Text = "Assign RFID",
                    Location = new Point(180, 90),
                    Size = new Size(100, 30),
                    DialogResult = DialogResult.OK
                };

                Button btnCancel = new Button
                {
                    Text = "Cancel",
                    Location = new Point(290, 90),
                    Size = new Size(80, 30),
                    DialogResult = DialogResult.Cancel
                };

                rfidDialog.Controls.Add(lblInstruction);
                rfidDialog.Controls.Add(txtRfidTag);
                rfidDialog.Controls.Add(btnAssign);
                rfidDialog.Controls.Add(btnCancel);
                rfidDialog.AcceptButton = btnAssign;
                rfidDialog.CancelButton = btnCancel;

                if (rfidDialog.ShowDialog() == DialogResult.OK)
                {
                    string rfidTag = txtRfidTag.Text.Trim();
                    
                    if (string.IsNullOrEmpty(rfidTag))
                    {
                        MessageBox.Show("Please enter an RFID tag code.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Check if RFID tag is already assigned to another user
                    var existingUser = dbManager.GetUserByRfidTag(rfidTag);
                    if (existingUser != null && existingUser.EmployeeId != user.EmployeeId)
                    {
                        var assignedTo = (!string.IsNullOrWhiteSpace(existingUser.FirstName) || !string.IsNullOrWhiteSpace(existingUser.LastName))
                            ? ($"{existingUser.FirstName} {existingUser.LastName}".Trim())
                            : (!string.IsNullOrWhiteSpace(existingUser.Username) ? existingUser.Username : "another user");
                        MessageBox.Show(
                            $"RFID tag '{rfidTag}' is already assigned to {assignedTo}.\n\nPlease use a different RFID tag or remove it from the existing user first.",
                            "Duplicate RFID Tag",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    // Assign RFID directly to database
                    SetStatusText($"Assigning RFID tag to {user.FirstName} {user.LastName}...");
                    
                    bool success = dbManager.UpdateUserRfidTag(user.EmployeeId, rfidTag);
                    
                    if (success)
                    {
                        MessageBox.Show(
                            $"RFID tag '{rfidTag}' assigned successfully to {user.FirstName} {user.LastName}.",
                            "Success",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        
                        // Refresh user list
                        cloudUsers = dbManager.LoadAllUsers();
                        LoadUsersIntoTable();
                        SetStatusText($"RFID assigned successfully to {user.FirstName} {user.LastName}");
                    }
                    else
                    {
                        MessageBox.Show(
                            "Failed to assign RFID tag to database.",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error assigning RFID: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                SetStatusText($"Error: {ex.Message}");
            }
        }

        private void DeleteUserRfid(User user)
        {
            try
            {
                // Confirm deletion
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the RFID tag for {user.FirstName} {user.LastName}?",
                    "Confirm RFID Deletion",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                
                if (result != DialogResult.Yes) return;

                // Delete RFID directly from database
                SetStatusText($"Deleting RFID tag for {user.FirstName} {user.LastName}...");
                
                bool success = dbManager.UpdateUserRfidTag(user.EmployeeId, "");
                
                if (success)
                {
                    MessageBox.Show(
                        $"RFID tag deleted successfully for {user.FirstName} {user.LastName}.",
                        "Success",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    
                    // Refresh user list
                    cloudUsers = dbManager.LoadAllUsers();
                    LoadUsersIntoTable();
                    SetStatusText($"RFID deleted for {user.FirstName} {user.LastName}");
                }
                else
                {
                    MessageBox.Show(
                        "Failed to delete RFID tag from database.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error deleting RFID: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                SetStatusText($"Error: {ex.Message}");
            }
        }

        private string cachedAuthToken = null;
        private DateTime tokenExpiry = DateTime.MinValue;

        private string GetAuthToken()
        {
            // Return cached token if still valid
            if (!string.IsNullOrEmpty(cachedAuthToken) && DateTime.Now < tokenExpiry)
            {
                return cachedAuthToken;
            }

            // Auto-login to get a valid token
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    
                    var loginPayload = new
                    {
                        email = "admin@liceo.edu.ph",
                        password = "password123"
                    };
                    
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(loginPayload);
                    var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    
                    var response = client.PostAsync($"{backendBaseUrl}/api/auth/login", content).Result;
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = response.Content.ReadAsStringAsync().Result;
                        var result = Newtonsoft.Json.Linq.JObject.Parse(responseContent);
                        cachedAuthToken = result["token"]?.ToString();
                        // Set expiry to 7 days from now (matching backend JWT_EXPIRES_IN)
                        tokenExpiry = DateTime.Now.AddDays(7);
                        Console.WriteLine("✅ Successfully authenticated with backend");
                        return cachedAuthToken;
                    }
                    else
                    {
                        Console.WriteLine($"❌ Authentication failed: {response.StatusCode}");
                        var errorContent = response.Content.ReadAsStringAsync().Result;
                        Console.WriteLine($"Error: {errorContent}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting auth token: {ex.Message}");
                return null;
            }
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

            // Auto-refresh timer for live updates (poll every 30 seconds)
            autoRefreshTimer = new System.Windows.Forms.Timer();
            autoRefreshTimer.Interval = 30000; // Refresh every 30 seconds
            autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            autoRefreshTimer.Start();

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
            var colUser = new DataGridViewTextBoxColumn { Name = "User", HeaderText = "User", Width = 180, AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells };
            var colMethod = new DataGridViewTextBoxColumn { Name = "Method", HeaderText = "Method", Width = 80, AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells };
            var colAction = new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Action", Width = 220, AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells };
            var colStatus = new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", MinimumWidth = 250, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };

            dgvAttendance.Columns.Add(colTime);
            dgvAttendance.Columns.Add(colUser);
            dgvAttendance.Columns.Add(colMethod);
            dgvAttendance.Columns.Add(colAction);
            dgvAttendance.Columns.Add(colStatus);

            attendancePanel.Controls.Add(dgvAttendance);
            attendanceTab.Controls.Add(attendancePanel);
            
            // Auto-start both fingerprint identification and RFID service in always-on mode
            try
            {
                if (alwaysOnAttendance)
                {
                    // Start fingerprint identification
                    StartIdentification();
                    m_bAttendanceActive = true;
                    currentSessionState = AttendanceSessionState.WaitingForInstructor;
                    UpdateSessionStateDisplay();
                    
                    // Start RFID service
                    StartRfidService();
                    
                    SetStatusText("Unified attendance system ready. Scanner active for both fingerprint and RFID.");
                }
            }
            catch (Exception ex)
            {
                SetStatusText($"Error starting unified attendance system: {ex.Message}");
            }
        }

        // Removed obsolete InitializeRfidAttendanceTab() and InitializeRfidLocationRoomControls() - unified in InitializeAttendanceTab()

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
        // Removed obsolete SetRfidComboBoxDropDownWidths() - RFID uses unified location/room controls
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
            txtDescriptions.Text = "1. Instructor Early Window\r\n" +
"   How early an instructor can start a session before scheduled time.\r\n\r\n" +
"2. Student Grace Period\r\n" +
"   How late a student can arrive and still be marked as 'Present'.\r\n\r\n" +
"3. Instructor Late Tolerance\r\n" +
"   How late an instructor can be and still start a session (marked as 'Unscheduled').\r\n\r\n" +
"4. Auto Close Delay\r\n" +
"   How long after scheduled end time the system will auto-close an active session.\r\n\r\n" +
"5. Student Early Arrival Window\r\n" +
"   How early students can scan and be marked as 'Early Arrival'.\r\n\r\n" +
"6. Instructor End Tolerance\r\n" +
"   How early/late an instructor can end a session relative to scheduled end time.\r\n\r\n" +
"\r\n" +
"These settings allow you to customize the attendance system behavior for different academic policies and requirements.";
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
                // INCREASED: Higher FARN for stricter matching to prevent false positives
                m_Operation.FARN = 150; // Increased from 100 to 150
                m_Operation.Version = VersionCompatible.ftr_version_compatible;
                ((FutronicEnrollment)m_Operation).MIOTControlOff = true; // Turn off MIOT control per SDK guidance

                // FARN already set above

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
                        // INCREASED: Higher FARN for stricter matching to prevent false positives
                        m_AttendanceOperation.FARN = 150; // Increased from 100 to 150
                        m_AttendanceOperation.Version = VersionCompatible.ftr_version_compatible;
                        
                        // Additional settings to reduce false positives
                        // FARN already set to 150 above
                        
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
                    currentSubjectName = null;
                    currentInstructorName = null;
                    
                    // Reset cross-type verification state
                    awaitingCrossTypeVerification = false;
                    pendingCrossVerificationUser = "";
                    pendingCrossVerificationGuid = "";
                    firstScanType = "";
                    crossVerificationStartTime = DateTime.MinValue;
                    
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
            // Redirect to unified session state display
            UpdateSessionStateDisplay();
        }
        
        private void ProcessAttendanceScan(string userName)
        {
            try
            {
                // Update watchdog timestamp
                lastSuccessfulOperation = DateTime.Now;
                
                // Get user information early for debounce logic
                var userInfo = GetUserInfoFromDatabase(userName);
                
                // Enhanced debouncing: longer for instructors
                int debounceMs = (userInfo?.UserType?.ToLower() == "instructor") ? 2000 : DEBOUNCE_INTERVAL_MS;
                
                // Debouncing: Check if same user was processed recently
                var timeSinceLastProcess = DateTime.Now - lastProcessedTime;
                if (userName == lastProcessedUser && timeSinceLastProcess.TotalMilliseconds < debounceMs)
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
                
                // Update debouncing variables (only after successful processing)
                lastProcessedUser = userName;
                lastProcessedTime = DateTime.Now;
                
                // Reset finger-on flag after processing to allow next scan
                // This ensures we don't block subsequent scans unnecessarily
                isFingerOnScanner = false;
                
                // Console.WriteLine($"Processing attendance scan for: {userName}");
                
                // userInfo already retrieved above for debounce logic
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
                    // Block instructor scans if already processing one
                    if (isProcessingInstructorScan)
                    {
                        SetStatusText($"⏳ Processing instructor action. Please remove finger from scanner.");
                        ScheduleNextGetBaseTemplate(500);
                        return;
                    }
                    ProcessInstructorScan(userName, userGuid);
                }
                else if (userType == "custodian")
                {
                    ProcessCustodianScan(userName, userGuid);
                }
                else if (userType == "dean")
                {
                    ProcessDeanScan(userName, userGuid);
                }
                else if (userType == "student")
                {
                    ProcessStudentScan(userName, userGuid);
                }
                else
                {
                    SetStatusText($"❌ Unknown user type for {userName}. Only instructors, students, custodians, and deans can use attendance system.");
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
                // CRITICAL FIX: Verify user is actually an instructor before processing
                var userInfo = GetUserInfoFromDatabase(userName);
                if (userInfo == null || userInfo.UserType?.ToLower() != "instructor")
                {
                    Console.WriteLine($"❌ SECURITY: User {userName} is not an instructor! UserType: {userInfo?.UserType ?? "NULL"}");
                    SetStatusText($"❌ Security violation: {userName} is not authorized for instructor actions.");
                    ScheduleNextGetBaseTemplate(1000);
                    return;
                }

                Console.WriteLine($"✅ VERIFIED: {userName} is confirmed as instructor ({userInfo.UserType})");

                // Note: Security check for session ownership is now handled within each session state
                // to allow door access for instructors with no scheduled class while preventing session control interference

                // Set flag to block additional instructor scans during processing
                isProcessingInstructorScan = true;
                isPausedForInstructorAction = true; // NEW: Stop all scanning

                SetStatusText($"Processing instructor scan for {userName}...");
                switch (currentSessionState)
                {
                    case AttendanceSessionState.Inactive:
                    case AttendanceSessionState.WaitingForInstructor:
                        // Check for cross-type verification timeout first
                        if (awaitingCrossTypeVerification)
                        {
                            var verificationElapsed = DateTime.Now - crossVerificationStartTime;
                            if (verificationElapsed.TotalSeconds > CROSS_VERIFICATION_TIMEOUT_SECONDS)
                            {
                                // Timeout - reset verification state
                                Console.WriteLine($"⏱️ INSTRUCTOR VERIFICATION TIMEOUT: {pendingCrossVerificationUser} took too long");
                                SetStatusText($"⏱️ Verification timeout for {pendingCrossVerificationUser}. Starting over...");
                                
                                // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                                if (dbManager != null && !string.IsNullOrEmpty(pendingCrossVerificationGuid))
                                {
                                    try
                                    {
                                        dbManager.LogAccessAttempt(
                                            userId: pendingCrossVerificationGuid,
                                            roomId: null,
                                            authMethod: firstScanType == "FINGERPRINT" ? "Fingerprint" : "RFID",
                                            location: currentScanLocation ?? "inside",
                                            accessType: "attendance_scan",
                                            result: "denied",
                                            reason: $"Instructor verification timeout: {pendingCrossVerificationUser} did not complete dual authentication within {CROSS_VERIFICATION_TIMEOUT_SECONDS} seconds"
                                        );
                                        Console.WriteLine($"📝 Logged denied instructor verification timeout to ACCESSLOGS for {pendingCrossVerificationUser}");
                                    }
                                    catch (Exception logEx)
                                    {
                                        Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                    }
                                }
                                
                                awaitingCrossTypeVerification = false;
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                firstScanType = "";
                                crossVerificationStartTime = DateTime.MinValue;
                                // Don't return - treat this as a new first scan
                            }
                        }
                        
                        // Check if completing RFID-first verification
                        if (awaitingCrossTypeVerification && firstScanType == "RFID")
                        {
                            // RFID was scanned first, now verifying with fingerprint
                            if (userName == pendingCrossVerificationUser && userGuid == pendingCrossVerificationGuid)
                            {
                                Console.WriteLine($"✅ INSTRUCTOR VERIFIED: {userName} (RFID + Fingerprint match)");
                                SetStatusText($"✅ Verified: {userName}. Starting session...");
                                
                                // Check if instructor has scheduled class, if not and door access is enabled, grant door access only
                                var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userGuid);
                                if (scheduleValidation == null || !scheduleValidation.IsValid || string.IsNullOrEmpty(scheduleValidation.ScheduleId))
                                {
                                    // No scheduled class - check if door access is allowed
                                    if (deviceConfig?.AllowInstructorDoorAccess == true)
                                    {
                                        Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS: {userName} - No scheduled class, door access granted");
                                        SetStatusText($"🚪 Instructor {userName} - Door access granted (no scheduled class).");
                                        
                                        // Create door access record
                                        var doorAccessRecord = new Database.Models.AttendanceRecord
                                        {
                                            UserId = 0,
                                            Username = userName,
                                            Timestamp = DateTime.Now,
                                            Action = "Door Access",
                                            Status = "Instructor Door Access (No Schedule)"
                                        };
                                        attendanceRecords.Add(doorAccessRecord);
                                        UpdateAttendanceDisplay(doorAccessRecord);
                                        
                                        // Log successful door access to ACCESSLOGS
                                        if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userGuid,
                                                    roomId: null,
                                                    authMethod: "Fingerprint",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "success",
                                                    reason: "Instructor door access granted (no scheduled class)"
                                                );
                                                Console.WriteLine($"📝 Logged successful instructor door access to ACCESSLOGS for {userName}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Also record attendance so it appears in web logs (ATTENDANCERECORDS)
                                        RecordAttendance(userName, "Door Access", true, currentScanLocation);
                                        
                                        // Trigger door access
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestLockControl(userGuid, "Instructor Door Access");
                                            }
                                            catch (Exception lockEx)
                                            {
                                                Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                            }
                                        });
                                        
                                        // Reset verification state
                                        awaitingCrossTypeVerification = false;
                                        pendingCrossVerificationUser = "";
                                        pendingCrossVerificationGuid = "";
                                        firstScanType = "";
                                        crossVerificationStartTime = DateTime.MinValue;
                                        
                                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                        return;
                                    }
                                    else
                                    {
                                        // Door access not enabled - deny
                                        SetStatusText($"❌ {userName}: No scheduled class. Door access not enabled.");
                                        Console.WriteLine($"❌ INSTRUCTOR {userName} DENIED: No scheduled class and door access not enabled");
                                        
                                        // Log denied access attempt to ACCESSLOGS
                                        if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userGuid,
                                                    roomId: null,
                                                    authMethod: "Fingerprint",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "denied",
                                                    reason: "Instructor door access denied: No scheduled class and door access not enabled"
                                                );
                                                Console.WriteLine($"📝 Logged denied instructor access attempt to ACCESSLOGS for {userName}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Reset verification state
                                        awaitingCrossTypeVerification = false;
                                        pendingCrossVerificationUser = "";
                                        pendingCrossVerificationGuid = "";
                                        firstScanType = "";
                                        crossVerificationStartTime = DateTime.MinValue;
                                        
                                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                        return;
                                    }
                                }
                                
                                // Has scheduled class - proceed with session start
                                CompleteInstructorSessionStart(userName, userGuid);
                                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                return;
                            }
                            else
                            {
                                // Mismatch
                                Console.WriteLine($"❌ INSTRUCTOR VERIFICATION FAILED: RFID={pendingCrossVerificationUser}, Fingerprint={userName}");
                                SetStatusText($"❌ Verification failed! RFID: {pendingCrossVerificationUser}, Fingerprint: {userName}. Please try again.");
                                
                                // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                                if (dbManager != null && !string.IsNullOrEmpty(pendingCrossVerificationGuid))
                                {
                                    try
                                    {
                                        dbManager.LogAccessAttempt(
                                            userId: pendingCrossVerificationGuid,
                                            roomId: null,
                                            authMethod: "Fingerprint",
                                            location: currentScanLocation ?? "inside",
                                            accessType: "attendance_scan",
                                            result: "denied",
                                            reason: $"Instructor verification failed: RFID ({pendingCrossVerificationUser}) and Fingerprint ({userName}) mismatch"
                                        );
                                        Console.WriteLine($"📝 Logged denied instructor verification failure to ACCESSLOGS for {pendingCrossVerificationUser}");
                                    }
                                    catch (Exception logEx)
                                    {
                                        Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                    }
                                }
                                
                                // Reset verification state
                                awaitingCrossTypeVerification = false;
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                firstScanType = "";
                                crossVerificationStartTime = DateTime.MinValue;
                                
                                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                return;
                            }
                        }
                        else if (!awaitingCrossTypeVerification)
                        {
                            if (IsDualAuthRequired)
                            {
                                Console.WriteLine($"🔍 INSTRUCTOR FINGERPRINT: {userName} - Waiting for RFID");
                                SetStatusText($"Instructor fingerprint: {userName}. Please scan RFID to start session.");
                                
                                var waitingRecord = new Database.Models.AttendanceRecord
                                {
                                    UserId = 0,
                                    Username = userName,
                                    Timestamp = DateTime.Now,
                                    Action = "Waiting for RFID",
                                    Status = "Fingerprint First"
                                };
                                attendanceRecords.Add(waitingRecord);
                                UpdateAttendanceDisplay(waitingRecord);
                                
                                awaitingCrossTypeVerification = true;
                                firstScanType = "FINGERPRINT";
                                pendingCrossVerificationUser = userName;
                                pendingCrossVerificationGuid = userGuid;
                                crossVerificationStartTime = DateTime.Now;
                                
                                User user;
                                if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userGuid, out user))
                                {
                                    _ = Task.Run(async () => await SendIntermediateStatusToESP32(user, "FINGERPRINT", "RFID", "/api/lock-control"));
                                }
                            }
                            else
                            {
                                // Fingerprint-only mode - check schedule first
                                var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userGuid);
                                if (scheduleValidation == null || !scheduleValidation.IsValid || string.IsNullOrEmpty(scheduleValidation.ScheduleId))
                                {
                                    // No scheduled class - check if door access is allowed
                                    if (deviceConfig?.AllowInstructorDoorAccess == true)
                                    {
                                        Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS: {userName} - No scheduled class, door access granted");
                                        SetStatusText($"🚪 Instructor {userName} - Door access granted (no scheduled class).");
                                        
                                        // Create door access record
                                        var doorAccessRecord = new Database.Models.AttendanceRecord
                                        {
                                            UserId = 0,
                                            Username = userName,
                                            Timestamp = DateTime.Now,
                                            Action = "Door Access",
                                            Status = "Instructor Door Access (No Schedule)"
                                        };
                                        attendanceRecords.Add(doorAccessRecord);
                                        UpdateAttendanceDisplay(doorAccessRecord);
                                        
                                        // Log successful door access to ACCESSLOGS
                                        if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userGuid,
                                                    roomId: null,
                                                    authMethod: "Fingerprint",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "success",
                                                    reason: "Instructor door access granted (no scheduled class)"
                                                );
                                                Console.WriteLine($"📝 Logged successful instructor door access to ACCESSLOGS for {userName}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Trigger door access
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestLockControl(userGuid, "Instructor Door Access");
                                            }
                                            catch (Exception lockEx)
                                            {
                                                Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                            }
                                        });
                                        
                                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                        return;
                                    }
                                    else
                                    {
                                        // Door access not enabled - deny
                                        SetStatusText($"❌ {userName}: No scheduled class. Door access not enabled.");
                                        Console.WriteLine($"❌ INSTRUCTOR {userName} DENIED: No scheduled class and door access not enabled");
                                        
                                        // Log denied access attempt to ACCESSLOGS
                                        if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userGuid,
                                                    roomId: null,
                                                    authMethod: "Fingerprint",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "denied",
                                                    reason: "Instructor door access denied: No scheduled class and door access not enabled"
                                                );
                                                Console.WriteLine($"📝 Logged denied instructor access attempt to ACCESSLOGS for {userName}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                            }
                                        }
                                        
                                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                        return;
                                    }
                                }
                                
                                // Has scheduled class - proceed with session start
                                Console.WriteLine($"✅ Fingerprint-only mode active - completing instructor session start for {userName}");
                                CompleteInstructorSessionStart(userName, userGuid);
                            }

                            ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                            return;
                        }
                        
                        // If we reach here, we're still awaiting verification from first scan
                        // Do nothing - wait for the second scan to complete verification
                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                        break;
                    
                    case AttendanceSessionState.ActiveForStudents:
                        // CRITICAL: Check for verification timeout first
                        if (awaitingCrossTypeVerification)
                        {
                            var verificationElapsed = DateTime.Now - crossVerificationStartTime;
                            if (verificationElapsed.TotalSeconds > CROSS_VERIFICATION_TIMEOUT_SECONDS)
                            {
                                // Timeout - reset verification state
                                Console.WriteLine($"⏱️ INSTRUCTOR FINGERPRINT SIGN-OUT VERIFICATION TIMEOUT: {pendingCrossVerificationUser} took too long");
                                SetStatusText($"⏱️ Verification timeout for {pendingCrossVerificationUser}. Starting over...");
                                
                                // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                                if (dbManager != null && !string.IsNullOrEmpty(pendingCrossVerificationGuid))
                                {
                                    try
                                    {
                                        dbManager.LogAccessAttempt(
                                            userId: pendingCrossVerificationGuid,
                                            roomId: null,
                                            authMethod: firstScanType == "FINGERPRINT" ? "Fingerprint" : "RFID",
                                            location: currentScanLocation ?? "inside",
                                            accessType: "attendance_scan",
                                            result: "denied",
                                            reason: $"Instructor sign-out verification timeout: {pendingCrossVerificationUser} did not complete dual authentication within {CROSS_VERIFICATION_TIMEOUT_SECONDS} seconds"
                                        );
                                        Console.WriteLine($"📝 Logged denied instructor verification timeout to ACCESSLOGS for {pendingCrossVerificationUser}");
                                    }
                                    catch (Exception logEx)
                                    {
                                        Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                    }
                                }
                                
                                awaitingCrossTypeVerification = false;
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                firstScanType = "";
                                crossVerificationStartTime = DateTime.MinValue;
                                // Don't return - treat this as a new first scan
                            }
                        }
                        else if (!IsDualAuthRequired)
                        {
                            // SECURITY CHECK: If not session owner, check if door access is allowed
                            if (!string.IsNullOrEmpty(currentInstructorId) && userGuid != currentInstructorId)
                            {
                                // Different instructor - check if they have scheduled class
                                var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userGuid);
                                bool hasScheduledClass = scheduleValidation != null && scheduleValidation.IsValid && !string.IsNullOrEmpty(scheduleValidation.ScheduleId);
                                
                                if (hasScheduledClass)
                                {
                                    // Has scheduled class but different instructor owns session - deny (interference prevention)
                                    string sessionOwnerName = userName;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                    {
                                        sessionOwnerName = sessionOwner.Username;
                                    }
                                    
                                    Console.WriteLine($"⚠️ SECURITY: Instructor {userName} (has scheduled class) attempted to access session owned by {sessionOwnerName}");
                                    SetStatusText($"⚠️ Session is active for {sessionOwnerName}. Only the session owner can perform actions.");
                                    
                                    // Log this security event
                                    if (dbManager != null)
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "denied",
                                                reason: $"Attempted to access session owned by different instructor (Session Owner: {sessionOwnerName})"
                                            );
                                            Console.WriteLine($"📝 Logged unauthorized instructor access attempt to ACCESSLOGS for {userName}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                        }
                                    }
                                    
                                    // Create denial record
                                    var denialRecord = new Database.Models.AttendanceRecord
                                    {
                                        UserId = 0,
                                        Username = userName,
                                        Timestamp = DateTime.Now,
                                        Action = "Access Denied",
                                        Status = $"Session owned by {sessionOwnerName}"
                                    };
                                    attendanceRecords.Add(denialRecord);
                                    UpdateAttendanceDisplay(denialRecord);
                                    
                                    ScheduleNextGetBaseTemplate(1000);
                                    return;
                                }
                                else if (deviceConfig?.AllowInstructorDoorAccess == true)
                                {
                                    // No scheduled class but door access enabled - allow door access only
                                    Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (Fingerprint-only mode): {userName} - Door access granted (no schedule, different session active)");
                                    SetStatusText($"🚪 Instructor {userName} - Door access granted. Session remains active for students.");
                                    
                                    // Create door access record
                                    var doorAccessRecord = new Database.Models.AttendanceRecord
                                    {
                                        UserId = 0,
                                        Username = userName,
                                        Timestamp = DateTime.Now,
                                        Action = "Door Access",
                                        Status = "Instructor Door Access (No Schedule)"
                                    };
                                    attendanceRecords.Add(doorAccessRecord);
                                    UpdateAttendanceDisplay(doorAccessRecord);
                                    
                                    // Log door access to ACCESSLOGS
                                    if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "success",
                                                reason: "Instructor door access granted (no scheduled class, different session active, fingerprint-only mode)"
                                            );
                                            Console.WriteLine($"📝 Logged instructor door access to ACCESSLOGS for {userName}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                        }
                                    }
                                    
                                    // Record attendance for door access
                                    RecordAttendance(userName, "Door Access", true, currentScanLocation);
                                    
                                    // Trigger door access without changing session state
                                    _ = System.Threading.Tasks.Task.Run(async () => {
                                        try
                                        {
                                            await RequestLockControl(userGuid, "Instructor Door Access (No Schedule)");
                                        }
                                        catch (Exception lockEx)
                                        {
                                            Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                        }
                                    });
                                    
                                    ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                    return;
                                }
                                else
                                {
                                    // No scheduled class and door access not enabled - deny
                                    string sessionOwnerName = userName;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                    {
                                        sessionOwnerName = sessionOwner.Username;
                                    }
                                    
                                    Console.WriteLine($"⚠️ SECURITY: Instructor {userName} attempted to open sign-out for session owned by {sessionOwnerName}");
                                    SetStatusText($"⚠️ Only the session owner ({sessionOwnerName}) can open sign-out.");
                                    
                                    // Log security event
                                    if (dbManager != null)
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "denied",
                                                reason: $"Attempted to open sign-out for session owned by different instructor. No scheduled class and door access not enabled. (Session Owner: {sessionOwnerName})"
                                            );
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                        }
                                    }
                                    
                                    // Create denial record
                                    var denialRecord = new Database.Models.AttendanceRecord
                                    {
                                        UserId = 0,
                                        Username = userName,
                                        Timestamp = DateTime.Now,
                                        Action = "Access Denied",
                                        Status = $"Session owned by {sessionOwnerName}"
                                    };
                                    attendanceRecords.Add(denialRecord);
                                    UpdateAttendanceDisplay(denialRecord);
                                    
                                    ScheduleNextGetBaseTemplate(1000);
                                    return;
                                }
                            }
                            
                            Console.WriteLine($"✅ Fingerprint-only mode - opening sign-out for {userName}");

                            awaitingCrossTypeVerification = false;
                            firstScanType = "";
                            pendingCrossVerificationUser = "";
                            pendingCrossVerificationGuid = "";
                            crossVerificationStartTime = DateTime.MinValue;

                            currentSessionState = AttendanceSessionState.ActiveForSignOut;
                            UpdateSessionStateDisplay();
                            SetStatusText($"✅ Instructor {userName} opened sign-out. Students can now sign out.");

                            ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                            return;
                        }
                        
                        // Check if completing dual-auth for sign-out
                        if (awaitingCrossTypeVerification && firstScanType == "RFID")
                        {
                            // RFID was scanned first, now verifying with fingerprint
                            if (userName == pendingCrossVerificationUser && userGuid == pendingCrossVerificationGuid)
                            {
                                // SECURITY CHECK: Only session owner can open sign-out
                                if (!string.IsNullOrEmpty(currentInstructorId) && userGuid != currentInstructorId)
                                {
                                    string sessionOwnerName = userName;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                    {
                                        sessionOwnerName = sessionOwner.Username;
                                    }
                                    
                                    Console.WriteLine($"⚠️ SECURITY: Instructor {userName} attempted to open sign-out for session owned by {sessionOwnerName}");
                                    SetStatusText($"⚠️ Only the session owner ({sessionOwnerName}) can open sign-out.");
                                    
                                    // Reset verification state
                                    awaitingCrossTypeVerification = false;
                                    firstScanType = "";
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                    
                                    // Log security event
                                    if (dbManager != null)
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "denied",
                                                reason: $"Attempted to open sign-out for session owned by different instructor (Session Owner: {sessionOwnerName})"
                                            );
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                        }
                                    }
                                    
                                    ScheduleNextGetBaseTemplate(1000);
                                    return;
                                }
                                
                                // Verified! Reset state and proceed with sign-out
                                Console.WriteLine($"✅ INSTRUCTOR VERIFIED FOR SIGN-OUT: {userName} (RFID + Fingerprint match)");
                                SetStatusText($"✅ Verified: {userName}. Opening sign-out...");
                                
                                awaitingCrossTypeVerification = false;
                                firstScanType = "";
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                crossVerificationStartTime = DateTime.MinValue;
                                
                                // Proceed with sign-out
                                currentSessionState = AttendanceSessionState.ActiveForSignOut;
                                UpdateSessionStateDisplay();
                                SetStatusText($"✅ Instructor {userName} opened sign-out. Students can now sign out.");
                                
                                Console.WriteLine($"🔄 SIGN-OUT PHASE ACTIVE - Students can now sign out");
                                
                                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                return;
                            }
                            else
                            {
                                // Mismatch
                                Console.WriteLine($"❌ INSTRUCTOR VERIFICATION FAILED: RFID={pendingCrossVerificationUser}, Fingerprint={userName}");
                                SetStatusText($"❌ Verification failed! RFID: {pendingCrossVerificationUser}, Fingerprint: {userName}. Please try again.");
                                
                                // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                                if (dbManager != null && !string.IsNullOrEmpty(pendingCrossVerificationGuid))
                                {
                                    try
                                    {
                                        dbManager.LogAccessAttempt(
                                            userId: pendingCrossVerificationGuid,
                                            roomId: null,
                                            authMethod: "Fingerprint",
                                            location: currentScanLocation ?? "inside",
                                            accessType: "attendance_scan",
                                            result: "denied",
                                            reason: $"Instructor sign-out verification failed: RFID ({pendingCrossVerificationUser}) and Fingerprint ({userName}) mismatch"
                                        );
                                        Console.WriteLine($"📝 Logged denied instructor verification failure to ACCESSLOGS for {pendingCrossVerificationUser}");
                                    }
                                    catch (Exception logEx)
                                    {
                                        Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                    }
                                }
                                
                                awaitingCrossTypeVerification = false;
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                firstScanType = "";
                                crossVerificationStartTime = DateTime.MinValue;
                                
                                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                return;
                            }
                        }
                        else if (!awaitingCrossTypeVerification)
                        {
                            // SECURITY CHECK: If not session owner, check if door access is allowed
                            if (!string.IsNullOrEmpty(currentInstructorId) && userGuid != currentInstructorId)
                            {
                                // Different instructor - check if they have scheduled class
                                var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userGuid);
                                bool hasScheduledClass = scheduleValidation != null && scheduleValidation.IsValid && !string.IsNullOrEmpty(scheduleValidation.ScheduleId);
                                
                                if (hasScheduledClass)
                                {
                                    // Has scheduled class but different instructor owns session - deny (interference prevention)
                                    string sessionOwnerName = userName;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                    {
                                        sessionOwnerName = sessionOwner.Username;
                                    }
                                    
                                    Console.WriteLine($"⚠️ SECURITY: Instructor {userName} (has scheduled class) attempted to access session owned by {sessionOwnerName}");
                                    SetStatusText($"⚠️ Session is active for {sessionOwnerName}. Only the session owner can perform actions.");
                                    
                                    // Log this security event
                                    if (dbManager != null)
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "denied",
                                                reason: $"Attempted to access session owned by different instructor (Session Owner: {sessionOwnerName})"
                                            );
                                            Console.WriteLine($"📝 Logged unauthorized instructor access attempt to ACCESSLOGS for {userName}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                        }
                                    }
                                    
                                    // Create denial record
                                    var denialRecord = new Database.Models.AttendanceRecord
                                    {
                                        UserId = 0,
                                        Username = userName,
                                        Timestamp = DateTime.Now,
                                        Action = "Access Denied",
                                        Status = $"Session owned by {sessionOwnerName}"
                                    };
                                    attendanceRecords.Add(denialRecord);
                                    UpdateAttendanceDisplay(denialRecord);
                                    
                                    ScheduleNextGetBaseTemplate(1000);
                                    return;
                                }
                                else if (deviceConfig?.AllowInstructorDoorAccess == true)
                                {
                                    // No scheduled class but door access enabled - allow door access only
                                    Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (Fingerprint): {userName} - Door access granted (no schedule, different session active)");
                                    SetStatusText($"🚪 Instructor {userName} - Door access granted. Session remains active for students.");
                                    
                                    // Create door access record
                                    var doorAccessRecord = new Database.Models.AttendanceRecord
                                    {
                                        UserId = 0,
                                        Username = userName,
                                        Timestamp = DateTime.Now,
                                        Action = "Door Access",
                                        Status = "Instructor Door Access (No Schedule)"
                                    };
                                    attendanceRecords.Add(doorAccessRecord);
                                    UpdateAttendanceDisplay(doorAccessRecord);
                                    
                                    // Log door access to ACCESSLOGS
                                    if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "success",
                                                reason: "Instructor door access granted (no scheduled class, different session active)"
                                            );
                                            Console.WriteLine($"📝 Logged instructor door access to ACCESSLOGS for {userName}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                        }
                                    }
                                    
                                    // Record attendance for door access
                                    RecordAttendance(userName, "Door Access", true, currentScanLocation);
                                    
                                    // Trigger door access without changing session state
                                    _ = System.Threading.Tasks.Task.Run(async () => {
                                        try
                                        {
                                            await RequestLockControl(userGuid, "Instructor Door Access (No Schedule)");
                                        }
                                        catch (Exception lockEx)
                                        {
                                            Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                        }
                                    });
                                    
                                    ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                    return;
                                }
                                else
                                {
                                    // No scheduled class and door access not enabled - deny
                                    string sessionOwnerName = userName;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                    {
                                        sessionOwnerName = sessionOwner.Username;
                                    }
                                    
                                    Console.WriteLine($"⚠️ SECURITY: Instructor {userName} (no schedule) attempted to access session owned by {sessionOwnerName}");
                                    SetStatusText($"⚠️ Session is active for {sessionOwnerName}. Door access not enabled.");
                                    
                                    // Log denied access
                                    if (dbManager != null)
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "denied",
                                                reason: $"Attempted to access session owned by different instructor. No scheduled class and door access not enabled."
                                            );
                                            Console.WriteLine($"📝 Logged denied instructor access attempt to ACCESSLOGS for {userName}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                        }
                                    }
                                    
                                    ScheduleNextGetBaseTemplate(1000);
                                    return;
                                }
                            }
                            
                            // Session owner or no active session - proceed with normal flow
                            // NEW LOGIC: During active session, single scan grants door access without changing state
                            if (IsDualAuthRequired)
                            {
                                Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (Fingerprint): {userName} - Door access granted, session remains active");
                                SetStatusText($"🚪 Instructor {userName} - Door access granted. Session remains active for students.");
                                
                                // Create door access record
                                var doorAccessRecord = new Database.Models.AttendanceRecord
                                {
                                    UserId = 0,
                                    Username = userName,
                                    Timestamp = DateTime.Now,
                                    Action = "Door Access",
                                    Status = "Instructor Door Access (Session Active)"
                                };
                                attendanceRecords.Add(doorAccessRecord);
                                UpdateAttendanceDisplay(doorAccessRecord);
                                
                                // Log door access to ACCESSLOGS
                                if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                                {
                                    try
                                    {
                                        dbManager.LogAccessAttempt(
                                            userId: userGuid,
                                            roomId: null,
                                            authMethod: "Fingerprint",
                                            location: currentScanLocation ?? "inside",
                                            accessType: "attendance_scan",
                                            result: "success",
                                            reason: "Instructor door access granted during active session"
                                        );
                                        Console.WriteLine($"📝 Logged instructor door access to ACCESSLOGS for {userName}");
                                    }
                                    catch (Exception logEx)
                                    {
                                        Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                    }
                                }
                                
                                // Record attendance for door access
                                RecordAttendance(userName, "Door Access", true, currentScanLocation);
                                
                                // Trigger door access without changing session state
                                _ = System.Threading.Tasks.Task.Run(async () => {
                                    try
                                    {
                                        await RequestLockControl(userGuid, "Instructor Door Access (Session Active)");
                                    }
                                    catch (Exception lockEx)
                                    {
                                        Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                    }
                                });
                                
                                // Start waiting for second scan to open sign-out
                                awaitingCrossTypeVerification = true;
                                firstScanType = "FINGERPRINT";
                                pendingCrossVerificationUser = userName;
                                pendingCrossVerificationGuid = userGuid;
                                crossVerificationStartTime = DateTime.Now;
                                
                                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                return;
                            }
                            else
                            {
                                // Fingerprint-only mode: This shouldn't happen in ActiveForStudents state with single auth
                                // but keeping original logic as fallback
                                Console.WriteLine($"🔍 INSTRUCTOR FINGERPRINT: {userName} - Waiting for RFID to open sign-out");
                                SetStatusText($"Instructor fingerprint: {userName}. Please scan RFID to open sign-out.");
                                
                                // Add record to show "Waiting for RFID" status
                                var waitingRecord = new Database.Models.AttendanceRecord
                                {
                                    UserId = 0,
                                    Username = userName,
                                    Timestamp = DateTime.Now,
                                    Action = "Waiting for RFID",
                                    Status = "Fingerprint First"
                                };
                                attendanceRecords.Add(waitingRecord);
                                UpdateAttendanceDisplay(waitingRecord);
                                
                                awaitingCrossTypeVerification = true;
                                firstScanType = "FINGERPRINT";
                                pendingCrossVerificationUser = userName;
                                pendingCrossVerificationGuid = userGuid;
                                crossVerificationStartTime = DateTime.Now;
                                
                                // Send intermediate status to ESP32
                                User user;
                                if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userGuid, out user))
                                {
                                    _ = Task.Run(async () => await SendIntermediateStatusToESP32(user, "FINGERPRINT", "RFID", "/api/lock-control"));
                                }
                                
                                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                return;
                            }
                        }
                        
                        // If we reach here, we're still awaiting verification from first scan
                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                        break;
                        
                    case AttendanceSessionState.ActiveForSignOut:
                    case AttendanceSessionState.WaitingForInstructorClose:
                        // CRITICAL: Check for verification timeout first
                        if (awaitingCrossTypeVerification)
                        {
                            var verificationElapsed = DateTime.Now - crossVerificationStartTime;
                            if (verificationElapsed.TotalSeconds > CROSS_VERIFICATION_TIMEOUT_SECONDS)
                            {
                                // Timeout - reset verification state
                                Console.WriteLine($"⏱️ INSTRUCTOR FINGERPRINT SESSION-END VERIFICATION TIMEOUT: {pendingCrossVerificationUser} took too long");
                                SetStatusText($"⏱️ Verification timeout for {pendingCrossVerificationUser}. Starting over...");
                                
                                // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                                if (dbManager != null && !string.IsNullOrEmpty(pendingCrossVerificationGuid))
                                {
                                    try
                                    {
                                        dbManager.LogAccessAttempt(
                                            userId: pendingCrossVerificationGuid,
                                            roomId: null,
                                            authMethod: firstScanType == "FINGERPRINT" ? "Fingerprint" : "RFID",
                                            location: currentScanLocation ?? "inside",
                                            accessType: "attendance_scan",
                                            result: "denied",
                                            reason: $"Instructor session-end verification timeout: {pendingCrossVerificationUser} did not complete dual authentication within {CROSS_VERIFICATION_TIMEOUT_SECONDS} seconds"
                                        );
                                        Console.WriteLine($"📝 Logged denied instructor verification timeout to ACCESSLOGS for {pendingCrossVerificationUser}");
                                    }
                                    catch (Exception logEx)
                                    {
                                        Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                    }
                                }
                                
                                awaitingCrossTypeVerification = false;
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                firstScanType = "";
                                crossVerificationStartTime = DateTime.MinValue;
                                // Don't return - treat this as a new first scan
                            }
                        }
                        else if (!IsDualAuthRequired)
                        {
                            // Fingerprint-only mode
                            // SECURITY CHECK: If not session owner, check if door access is allowed
                            if (!string.IsNullOrEmpty(currentInstructorId) && userGuid != currentInstructorId)
                            {
                                // Different instructor - check if they have scheduled class
                                var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userGuid);
                                bool hasScheduledClass = scheduleValidation != null && scheduleValidation.IsValid && !string.IsNullOrEmpty(scheduleValidation.ScheduleId);
                                
                                if (hasScheduledClass)
                                {
                                    // Has scheduled class but different instructor owns session - deny (interference prevention)
                                    string sessionOwnerName = userName;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                    {
                                        sessionOwnerName = sessionOwner.Username;
                                    }
                                    
                                    Console.WriteLine($"⚠️ SECURITY: Instructor {userName} (has scheduled class) attempted to access session owned by {sessionOwnerName}");
                                    SetStatusText($"⚠️ Session is active for {sessionOwnerName}. Only the session owner can perform actions.");
                                    
                                    // Log this security event
                                    if (dbManager != null)
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "denied",
                                                reason: $"Attempted to access session owned by different instructor (Session Owner: {sessionOwnerName})"
                                            );
                                            Console.WriteLine($"📝 Logged unauthorized instructor access attempt to ACCESSLOGS for {userName}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                        }
                                    }
                                    
                                    // Create denial record
                                    var denialRecord = new Database.Models.AttendanceRecord
                                    {
                                        UserId = 0,
                                        Username = userName,
                                        Timestamp = DateTime.Now,
                                        Action = "Access Denied",
                                        Status = $"Session owned by {sessionOwnerName}"
                                    };
                                    attendanceRecords.Add(denialRecord);
                                    UpdateAttendanceDisplay(denialRecord);
                                    
                                    ScheduleNextGetBaseTemplate(1000);
                                    return;
                                }
                                else if (deviceConfig?.AllowInstructorDoorAccess == true)
                                {
                                    // No scheduled class but door access enabled - allow door access only
                                    Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (Fingerprint-only mode, sign-out state): {userName} - Door access granted (no schedule, different session active)");
                                    SetStatusText($"🚪 Instructor {userName} - Door access granted. Sign-out session remains active.");
                                    
                                    // Create door access record
                                    var doorAccessRecord = new Database.Models.AttendanceRecord
                                    {
                                        UserId = 0,
                                        Username = userName,
                                        Timestamp = DateTime.Now,
                                        Action = "Door Access",
                                        Status = "Instructor Door Access (No Schedule)"
                                    };
                                    attendanceRecords.Add(doorAccessRecord);
                                    UpdateAttendanceDisplay(doorAccessRecord);
                                    
                                    // Log door access to ACCESSLOGS
                                    if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "success",
                                                reason: "Instructor door access granted (no scheduled class, different session active, fingerprint-only mode, sign-out state)"
                                            );
                                            Console.WriteLine($"📝 Logged instructor door access to ACCESSLOGS for {userName}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                        }
                                    }
                                    
                                    // Record attendance for door access
                                    RecordAttendance(userName, "Door Access", true, currentScanLocation);
                                    
                                    // Trigger door access without ending session
                                    _ = System.Threading.Tasks.Task.Run(async () => {
                                        try
                                        {
                                            await RequestLockControl(userGuid, "Instructor Door Access (No Schedule)");
                                        }
                                        catch (Exception lockEx)
                                        {
                                            Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                        }
                                    });
                                    
                                    ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                    return;
                                }
                                else
                                {
                                    // No scheduled class and door access not enabled - deny
                                    string sessionOwnerName = userName;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                    {
                                        sessionOwnerName = sessionOwner.Username;
                                    }
                                    
                                    Console.WriteLine($"⚠️ SECURITY: Instructor {userName} (no schedule) attempted to access session owned by {sessionOwnerName}");
                                    SetStatusText($"⚠️ Session is active for {sessionOwnerName}. Door access not enabled.");
                                    
                                    // Log denied access
                                    if (dbManager != null)
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "denied",
                                                reason: $"Attempted to access session owned by different instructor. No scheduled class and door access not enabled. (Session Owner: {sessionOwnerName})"
                                            );
                                            Console.WriteLine($"📝 Logged denied instructor access attempt to ACCESSLOGS for {userName}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                        }
                                    }
                                    
                                    // Create denial record
                                    var denialRecord = new Database.Models.AttendanceRecord
                                    {
                                        UserId = 0,
                                        Username = userName,
                                        Timestamp = DateTime.Now,
                                        Action = "Access Denied",
                                        Status = $"Session owned by {sessionOwnerName}"
                                    };
                                    attendanceRecords.Add(denialRecord);
                                    UpdateAttendanceDisplay(denialRecord);
                                    
                                    ScheduleNextGetBaseTemplate(1000);
                                    return;
                                }
                            }
                            
                            Console.WriteLine($"✅ Fingerprint-only mode - ending session for {userName}");

                            awaitingCrossTypeVerification = false;
                            firstScanType = "";
                            pendingCrossVerificationUser = "";
                            pendingCrossVerificationGuid = "";
                            crossVerificationStartTime = DateTime.MinValue;

                            currentSessionState = AttendanceSessionState.Inactive;
                            currentInstructorId = null;
                            currentScheduleId = null;
                            currentSubjectName = null;
                            currentInstructorName = null;

                            signedInStudentGuids.Clear();
                            signedOutStudentGuids.Clear();

                            UpdateSessionStateDisplay();
                            SetStatusText($"✅ Instructor {userName} signed out. Session ended.");

                            _ = System.Threading.Tasks.Task.Run(async () =>
                            {
                                try
                                {
                                    RecordAttendance(userName, "Instructor Sign-Out (Session End)");
                                    await RequestLockControl(userGuid, "Instructor Sign-Out (Session End)");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Warning: Could not record instructor sign-out: {ex.Message}");
                                }
                            });

                            ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                            return;
                        }
                        
                        // Check if completing dual-auth for session-end
                        if (awaitingCrossTypeVerification && firstScanType == "RFID")
                        {
                            // RFID was scanned first, now verifying with fingerprint
                            if (userName == pendingCrossVerificationUser && userGuid == pendingCrossVerificationGuid)
                            {
                                // SECURITY CHECK: Only session owner can end session
                                if (!string.IsNullOrEmpty(currentInstructorId) && userGuid != currentInstructorId)
                                {
                                    string sessionOwnerName = userName;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                    {
                                        sessionOwnerName = sessionOwner.Username;
                                    }
                                    
                                    Console.WriteLine($"⚠️ SECURITY: Instructor {userName} attempted to end session owned by {sessionOwnerName}");
                                    SetStatusText($"⚠️ Only the session owner ({sessionOwnerName}) can end the session.");
                                    
                                    // Reset verification state
                                    awaitingCrossTypeVerification = false;
                                    firstScanType = "";
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                    
                                    // Log security event
                                    if (dbManager != null)
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "denied",
                                                reason: $"Attempted to end session owned by different instructor (Session Owner: {sessionOwnerName})"
                                            );
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                        }
                                    }
                                    
                                    ScheduleNextGetBaseTemplate(1000);
                                    return;
                                }
                                
                                // Verified! Reset state and proceed with session-end
                                Console.WriteLine($"✅ INSTRUCTOR VERIFIED FOR SESSION-END: {userName} (RFID + Fingerprint match)");
                                SetStatusText($"✅ Verified: {userName}. Ending session...");
                                
                                awaitingCrossTypeVerification = false;
                                firstScanType = "";
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                crossVerificationStartTime = DateTime.MinValue;
                                
                                // Proceed with session-end
                                currentSessionState = AttendanceSessionState.Inactive;
                                currentInstructorId = null;
                                currentScheduleId = null;
                                currentSubjectName = null;
                                currentInstructorName = null;
                                
                                signedInStudentGuids.Clear();
                                signedOutStudentGuids.Clear();
                                
                                UpdateSessionStateDisplay();
                                SetStatusText($"✅ Instructor {userName} signed out. Session ended.");
                                
                                Console.WriteLine($"🔚 SESSION CLOSED - Instructor signed out, all students cleared");
                                
                                // Record instructor's sign-out attendance
                                _ = System.Threading.Tasks.Task.Run(async () => {
                                    try
                                    {
                                        RecordAttendance(userName, "Instructor Sign-Out (Session End)");
                                        await RequestLockControl(userGuid, "Instructor Sign-Out (Session End)");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Warning: Could not record instructor sign-out: {ex.Message}");
                                    }
                                });
                                
                                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                return;
                            }
                            else
                            {
                                // Mismatch
                                Console.WriteLine($"❌ INSTRUCTOR VERIFICATION FAILED: RFID={pendingCrossVerificationUser}, Fingerprint={userName}");
                                SetStatusText($"❌ Verification failed! RFID: {pendingCrossVerificationUser}, Fingerprint: {userName}. Please try again.");
                                
                                // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                                if (dbManager != null && !string.IsNullOrEmpty(pendingCrossVerificationGuid))
                                {
                                    try
                                    {
                                        dbManager.LogAccessAttempt(
                                            userId: pendingCrossVerificationGuid,
                                            roomId: null,
                                            authMethod: "Fingerprint",
                                            location: currentScanLocation ?? "inside",
                                            accessType: "attendance_scan",
                                            result: "denied",
                                            reason: $"Instructor session-end verification failed: RFID ({pendingCrossVerificationUser}) and Fingerprint ({userName}) mismatch"
                                        );
                                        Console.WriteLine($"📝 Logged denied instructor verification failure to ACCESSLOGS for {pendingCrossVerificationUser}");
                                    }
                                    catch (Exception logEx)
                                    {
                                        Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                    }
                                }
                                
                                awaitingCrossTypeVerification = false;
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                firstScanType = "";
                                crossVerificationStartTime = DateTime.MinValue;
                                
                                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                return;
                            }
                        }
                        else if (!awaitingCrossTypeVerification)
                        {
                            // SECURITY CHECK: If not session owner, check if door access is allowed
                            if (!string.IsNullOrEmpty(currentInstructorId) && userGuid != currentInstructorId)
                            {
                                // Different instructor - check if they have scheduled class
                                var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userGuid);
                                bool hasScheduledClass = scheduleValidation != null && scheduleValidation.IsValid && !string.IsNullOrEmpty(scheduleValidation.ScheduleId);
                                
                                if (hasScheduledClass)
                                {
                                    // Has scheduled class but different instructor owns session - deny (interference prevention)
                                    string sessionOwnerName = userName;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                    {
                                        sessionOwnerName = sessionOwner.Username;
                                    }
                                    
                                    Console.WriteLine($"⚠️ SECURITY: Instructor {userName} (has scheduled class) attempted to access session owned by {sessionOwnerName}");
                                    SetStatusText($"⚠️ Session is active for {sessionOwnerName}. Only the session owner can perform actions.");
                                    
                                    // Log this security event
                                    if (dbManager != null)
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "denied",
                                                reason: $"Attempted to access session owned by different instructor (Session Owner: {sessionOwnerName})"
                                            );
                                            Console.WriteLine($"📝 Logged unauthorized instructor access attempt to ACCESSLOGS for {userName}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                        }
                                    }
                                    
                                    // Create denial record
                                    var denialRecord = new Database.Models.AttendanceRecord
                                    {
                                        UserId = 0,
                                        Username = userName,
                                        Timestamp = DateTime.Now,
                                        Action = "Access Denied",
                                        Status = $"Session owned by {sessionOwnerName}"
                                    };
                                    attendanceRecords.Add(denialRecord);
                                    UpdateAttendanceDisplay(denialRecord);
                                    
                                    ScheduleNextGetBaseTemplate(1000);
                                    return;
                                }
                                else if (deviceConfig?.AllowInstructorDoorAccess == true)
                                {
                                    // No scheduled class but door access enabled - allow door access only
                                    Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (Fingerprint): {userName} - Door access granted (no schedule, different session active)");
                                    SetStatusText($"🚪 Instructor {userName} - Door access granted. Sign-out session remains active.");
                                    
                                    // Create door access record
                                    var doorAccessRecord = new Database.Models.AttendanceRecord
                                    {
                                        UserId = 0,
                                        Username = userName,
                                        Timestamp = DateTime.Now,
                                        Action = "Door Access",
                                        Status = "Instructor Door Access (No Schedule)"
                                    };
                                    attendanceRecords.Add(doorAccessRecord);
                                    UpdateAttendanceDisplay(doorAccessRecord);
                                    
                                    // Log door access to ACCESSLOGS
                                    if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "success",
                                                reason: "Instructor door access granted (no scheduled class, different session active)"
                                            );
                                            Console.WriteLine($"📝 Logged instructor door access to ACCESSLOGS for {userName}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                        }
                                    }
                                    
                                    // Record attendance for door access
                                    RecordAttendance(userName, "Door Access", true, currentScanLocation);
                                    
                                    // Trigger door access without ending session
                                    _ = System.Threading.Tasks.Task.Run(async () => {
                                        try
                                        {
                                            await RequestLockControl(userGuid, "Instructor Door Access (No Schedule)");
                                        }
                                        catch (Exception lockEx)
                                        {
                                            Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                        }
                                    });
                                    
                                    ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                    return;
                                }
                                else
                                {
                                    // No scheduled class and door access not enabled - deny
                                    string sessionOwnerName = userName;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                    {
                                        sessionOwnerName = sessionOwner.Username;
                                    }
                                    
                                    Console.WriteLine($"⚠️ SECURITY: Instructor {userName} (no schedule) attempted to access session owned by {sessionOwnerName}");
                                    SetStatusText($"⚠️ Session is active for {sessionOwnerName}. Door access not enabled.");
                                    
                                    // Log denied access
                                    if (dbManager != null)
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userGuid,
                                                roomId: null,
                                                authMethod: "Fingerprint",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "denied",
                                                reason: $"Attempted to access session owned by different instructor. No scheduled class and door access not enabled."
                                            );
                                            Console.WriteLine($"📝 Logged denied instructor access attempt to ACCESSLOGS for {userName}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                        }
                                    }
                                    
                                    ScheduleNextGetBaseTemplate(1000);
                                    return;
                                }
                            }
                            
                            // Session owner or no active session - proceed with normal flow
                            // NEW LOGIC: During active sign-out session, single scan grants door access without ending session
                            if (IsDualAuthRequired)
                            {
                                Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (Fingerprint): {userName} - Door access granted, session remains active");
                                SetStatusText($"🚪 Instructor {userName} - Door access granted. Sign-out session remains active.");
                                
                                // Create door access record
                                var doorAccessRecord = new Database.Models.AttendanceRecord
                                {
                                    UserId = 0,
                                    Username = userName,
                                    Timestamp = DateTime.Now,
                                    Action = "Door Access",
                                    Status = "Instructor Door Access (Sign-Out Active)"
                                };
                                attendanceRecords.Add(doorAccessRecord);
                                UpdateAttendanceDisplay(doorAccessRecord);
                                
                                // Log door access to ACCESSLOGS
                                if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                                {
                                    try
                                    {
                                        dbManager.LogAccessAttempt(
                                            userId: userGuid,
                                            roomId: null,
                                            authMethod: "Fingerprint",
                                            location: currentScanLocation ?? "inside",
                                            accessType: "attendance_scan",
                                            result: "success",
                                            reason: "Instructor door access granted during sign-out session"
                                        );
                                        Console.WriteLine($"📝 Logged instructor door access to ACCESSLOGS for {userName}");
                                    }
                                    catch (Exception logEx)
                                    {
                                        Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                    }
                                }
                                
                                // Record attendance for door access
                                RecordAttendance(userName, "Door Access", true, currentScanLocation);
                                
                                // Trigger door access without ending session
                                _ = System.Threading.Tasks.Task.Run(async () => {
                                    try
                                    {
                                        await RequestLockControl(userGuid, "Instructor Door Access (Sign-Out Active)");
                                    }
                                    catch (Exception lockEx)
                                    {
                                        Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                    }
                                });
                                
                                // Start waiting for second scan to end session
                                awaitingCrossTypeVerification = true;
                                firstScanType = "FINGERPRINT";
                                pendingCrossVerificationUser = userName;
                                pendingCrossVerificationGuid = userGuid;
                                crossVerificationStartTime = DateTime.Now;
                                
                                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                return;
                            }
                            // Note: Fingerprint-only mode is already handled in the earlier else if (!IsDualAuthRequired) block
                            // This else block is only reached for dual-auth mode (IsDualAuthRequired == true)
                        }
                        
                        // If we reach here, we're still awaiting verification from first scan
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
                // Clear the blocking flag to allow new instructor scans
                isProcessingInstructorScan = false;
                isPausedForInstructorAction = false; // NEW: Resume scanning

                // Always schedule next scan to keep the system responsive
                ScheduleNextGetBaseTemplate(1000);
            }
        }
        
        private bool CompleteInstructorSessionStart(string userName, string userGuid, bool isRfidFlow = false, string rfidData = null)
        {
            ResetCrossVerificationState();

            if (dbManager != null)
            {
                SetStatusText($"Verifying instructor schedule for {userName}...");

                var validationResult = dbManager.TryRecordAttendanceByGuid(userGuid, "Instructor Schedule Check", null);
                if (validationResult == null || !validationResult.Success)
                {
                    var failureReason = validationResult?.Reason ?? "Cannot start attendance session";
                    SetStatusText($"❌ {userName}: {failureReason}. Cannot start attendance session.");
                    Console.WriteLine($"❌ INSTRUCTOR {userName} DENIED: {failureReason}");

                    // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                    if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                    {
                        try
                        {
                            dbManager.LogAccessAttempt(
                                userId: userGuid,
                                roomId: null,
                                authMethod: isRfidFlow ? "RFID" : "Fingerprint",
                                location: currentScanLocation ?? "inside",
                                accessType: "attendance_scan",
                                result: "denied",
                                reason: $"Instructor session start denied: {failureReason}"
                            );
                            Console.WriteLine($"📝 Logged denied instructor session start attempt to ACCESSLOGS for {userName}");
                        }
                        catch (Exception logEx)
                        {
                            Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                        }
                    }

                    if (isRfidFlow)
                    {
                        AddRfidAttendanceRecord(userName, "Session Start Denied", failureReason);
                    }
                    else
                    {
                        var denialRecord = new Database.Models.AttendanceRecord
                        {
                            UserId = 0,
                            Username = userName,
                            Timestamp = DateTime.Now,
                            Action = "Instructor Sign-In (Session Start)",
                            Status = $"Denied: {failureReason}"
                        };
                        attendanceRecords.Add(denialRecord);
                        UpdateAttendanceDisplay(denialRecord);
                    }

                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            await RequestLockControlDenial(userGuid, userName, failureReason, "instructor");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                        }
                    });

                    return false;
                }

                currentInstructorId = userGuid;
                currentScheduleId = validationResult.ScheduleId ?? "manual_session";
                currentSessionState = AttendanceSessionState.ActiveForStudents;
                
                // Store session info for OLED display
                currentSubjectName = string.IsNullOrWhiteSpace(validationResult.SubjectName) ? "Current Subject" : validationResult.SubjectName;
                currentInstructorName = userName;

                signedInStudentGuids.Clear();
                signedOutStudentGuids.Clear();

                UpdateSessionStateDisplay();
                var subjectName = string.IsNullOrWhiteSpace(validationResult.SubjectName) ? "current subject" : validationResult.SubjectName;
                SetStatusText($"✅ Instructor {userName} signed in. Session started for {subjectName}. Students can now sign in.");
                Console.WriteLine($"✅ SESSION STARTED - Students can now sign in for {subjectName}");

                if (isRfidFlow)
                {
                    AddRfidAttendanceRecord(userName, "Session Started", $"Active - {subjectName}");

                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            RecordAttendance(userName, "Instructor Sign-In (Session Start)");
                            await RequestRfidLockControl(userGuid, "Instructor Sign-In (Session Start)", rfidData);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Could not record instructor sign-in: {ex.Message}");
                        }
                    });
                }
                else
                {
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            RecordAttendance(userName, "Instructor Sign-In (Session Start)");
                            await RequestLockControl(userGuid, "Instructor Sign-In (Session Start)");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Could not record instructor sign-in: {ex.Message}");
                        }
                    });
                }
                
                return true;
            }
            else
            {
                SetStatusText($"⚠️ Warning: Cannot verify schedule. Database not available.");
                currentInstructorId = userGuid;
                currentScheduleId = "manual_session";
                currentSessionState = AttendanceSessionState.ActiveForStudents;
                
                // Store session info for OLED display
                currentSubjectName = "Manual Session";
                currentInstructorName = userName;

                signedInStudentGuids.Clear();
                signedOutStudentGuids.Clear();

                UpdateSessionStateDisplay();
                SetStatusText($"✅ Instructor {userName} signed in (unverified). Students can now sign in.");
                
                if (isRfidFlow)
                {
                    AddRfidAttendanceRecord(userName, "Session Started", "Active (Unverified)");
                }
                
                return true;
            }
        }

        private void ProcessCustodianScan(string userName, string userGuid)
        {
            try
            {
                Console.WriteLine($"🧹 CUSTODIAN SCAN: {userName} - Door access only (no attendance)");
                SetStatusText($"🧹 Custodian access granted: {userName}. Door unlocked.");
                
                // Create door access record (no attendance recording for custodians)
                var doorAccessRecord = new Database.Models.AttendanceRecord
                {
                    UserId = 0,
                    Username = userName,
                    Timestamp = DateTime.Now,
                    Action = "Door Access",
                    Status = "Custodian Access"
                };
                attendanceRecords.Add(doorAccessRecord);
                UpdateAttendanceDisplay(doorAccessRecord);
                
                // Trigger door access
                _ = System.Threading.Tasks.Task.Run(async () => {
                    try
                    {
                        await RequestLockControl(userGuid, "Custodian Door Access");
                    }
                    catch (Exception lockEx)
                    {
                        Console.WriteLine($"Lock control failed: {lockEx.Message}");
                    }
                });
                
                // Also record attendance so it appears in web logs
                RecordAttendance(userName, "Door Access", true, currentScanLocation);
                
                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing custodian scan: {ex.Message}");
                SetStatusText($"❌ Error processing custodian scan: {ex.Message}");
                ScheduleNextGetBaseTemplate(1000);
            }
        }
        
        private void ProcessDeanScan(string userName, string userGuid)
        {
            try
            {
                Console.WriteLine($"🎓 DEAN SCAN: {userName} - Checking for scheduled class");
                
                // Check if dean has a scheduled class (since deans can be instructors)
                var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userGuid);
                
                if (scheduleValidation != null && scheduleValidation.IsValid && !string.IsNullOrEmpty(scheduleValidation.ScheduleId))
                {
                    // Dean has a scheduled class - record attendance
                    Console.WriteLine($"🎓 DEAN WITH SCHEDULE: {userName} - {scheduleValidation.SubjectName}");
                    SetStatusText($"🎓 Dean {userName} - Scheduled class: {scheduleValidation.SubjectName}. Door unlocked.");
                    
                    // Record attendance for the scheduled class
                    RecordAttendance(userName, "Dean Check-In", true, currentScanLocation);
                }
                else
                {
                    // Dean without scheduled class - door access only
                    Console.WriteLine($"🎓 DEAN WITHOUT SCHEDULE: {userName} - Administrative access");
                    SetStatusText($"🎓 Dean {userName} - Administrative access. Door unlocked.");
                    
                    // Create door access record (no attendance recording for administrative access)
                    var doorAccessRecord = new Database.Models.AttendanceRecord
                    {
                        UserId = 0,
                        Username = userName,
                        Timestamp = DateTime.Now,
                        Action = "Door Access",
                        Status = "Dean Administrative Access"
                    };
                    attendanceRecords.Add(doorAccessRecord);
                    UpdateAttendanceDisplay(doorAccessRecord);

                    // Also record administrative access as attendance for visibility
                    RecordAttendance(userName, "Door Access", true, currentScanLocation);
                }
                
                // Always trigger door access for deans
                _ = System.Threading.Tasks.Task.Run(async () => {
                    try
                    {
                        await RequestLockControl(userGuid, "Dean Door Access");
                    }
                    catch (Exception lockEx)
                    {
                        Console.WriteLine($"Lock control failed: {lockEx.Message}");
                    }
                });
                
                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing dean scan: {ex.Message}");
                SetStatusText($"❌ Error processing dean scan: {ex.Message}");
                ScheduleNextGetBaseTemplate(1000);
            }
        }
        private void ProcessStudentScan(string userName, string userGuid)
        {
            try
            {
                SetStatusText($"Processing student scan for {userName}...");
                
                // Check if student is scanning at outside sensor - door access only, no attendance
                if (isDualSensorMode && currentScanLocation == "outside")
                {
                    // Check if session is active for students
                    if (currentSessionState != AttendanceSessionState.ActiveForStudents &&
                        currentSessionState != AttendanceSessionState.ActiveForSignOut &&
                        currentSessionState != AttendanceSessionState.WaitingForInstructorSignOut)
                    {
                        // No active session - handle EARLY ARRIVAL dual-auth
                        if (IsFingerprintOnlyMode && !awaitingEarlyArrivalVerification)
                        {
                            awaitingEarlyArrivalVerification = true;
                            earlyFirstScanType = "RFID";
                            earlyPendingUser = userName;
                            earlyPendingGuid = userGuid;
                            earlyVerificationStartTime = DateTime.Now;
                        }
                        
                        // Timeout check
                        if (awaitingEarlyArrivalVerification)
                        {
                            var elapsed = DateTime.Now - earlyVerificationStartTime;
                            if (elapsed.TotalSeconds > CROSS_VERIFICATION_TIMEOUT_SECONDS)
                            {
                                Console.WriteLine($"⏱️ EARLY ARRIVAL VERIFICATION TIMEOUT: {earlyPendingUser} took too long");
                                SetStatusText($"⏱️ Verification timeout for {earlyPendingUser}. Starting over...");
                                awaitingEarlyArrivalVerification = false;
                                earlyFirstScanType = "";
                                earlyPendingUser = "";
                                earlyPendingGuid = "";
                            }
                        }

                            if (awaitingEarlyArrivalVerification && earlyFirstScanType == "RFID")
                            {
                                // RFID was scanned first, now verifying with fingerprint
                                if (userName == earlyPendingUser && userGuid == earlyPendingGuid)
                                {
                                    Console.WriteLine($"✅ EARLY ARRIVAL VERIFIED: {userName} (RFID + Fingerprint)");
                                    SetStatusText($"✅ Verified: {userName}. Recording early arrival...");
                                    awaitingEarlyArrivalVerification = false;
                                    earlyFirstScanType = "";
                                    earlyPendingUser = "";
                                    earlyPendingGuid = "";

                                    System.Threading.Tasks.Task.Run(async () => {
                                        try { await RecordEarlyArrivalFingerprint(userName, userGuid); } catch (Exception ex) { Console.WriteLine($"Early arrival record failed: {ex.Message}"); }
                                    });
                                }
                                else
                                {
                                    Console.WriteLine($"❌ EARLY ARRIVAL VERIFICATION FAILED: RFID={earlyPendingUser}, Fingerprint={userName}");
                                    SetStatusText($"❌ Verification failed! RFID: {earlyPendingUser}, Fingerprint: {userName}. Please try again.");
                                    awaitingEarlyArrivalVerification = false;
                                    earlyFirstScanType = "";
                                    earlyPendingUser = "";
                                    earlyPendingGuid = "";
                                }
                            }
                        else if (awaitingEarlyArrivalVerification && earlyFirstScanType == "FINGERPRINT")
                        {
                            // Already waiting for RFID - ignore this fingerprint scan
                            Console.WriteLine($"⏳ Early arrival: Waiting for RFID for {earlyPendingUser}. Ignoring duplicate fingerprint scan.");
                            SetStatusText($"Waiting for RFID scan. Please scan your RFID card to complete early arrival.");
                            
                            // Remind user on OLED to scan RFID
                            _ = System.Threading.Tasks.Task.Run(async () => {
                                try { await RequestInfoDisplay(userGuid, userName, "Waiting for RFID scan..."); } catch { }
                            });
                            
                            ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                            return;
                        }
                        else if (!awaitingEarlyArrivalVerification)
                        {
                            // Start early-arrival verification with fingerprint first
                            awaitingEarlyArrivalVerification = true;
                            earlyFirstScanType = "FINGERPRINT";
                            earlyPendingUser = userName;
                            earlyPendingGuid = userGuid;
                            earlyVerificationStartTime = DateTime.Now;
                            Console.WriteLine($"🧭 EARLY ARRIVAL: Fingerprint captured for {userName}. Awaiting RFID...");
                            SetStatusText($"Fingerprint OK. Please scan RFID to complete early arrival.");

                            // Show first scan acceptance on OLED
                            _ = System.Threading.Tasks.Task.Run(async () => {
                                try { await RequestInfoDisplay(userGuid, userName, "Fingerprint OK. Scan RFID now."); } catch { }
                            });
                        }

                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                        return;
                    }
                    
                    // Session is active - allow door access
                    Console.WriteLine($"🚪 OUTSIDE SENSOR: {userName} - Door access only (no attendance)");
                    SetStatusText($"🚪 Door access granted: {userName}. Attendance not recorded (outside sensor).");
                    
                    // Create door access record
                    var doorAccessRecord = new Database.Models.AttendanceRecord
                    {
                        UserId = 0,
                        Username = userName,
                        Timestamp = DateTime.Now,
                        Action = "Door Access",
                        Status = "Outside Sensor"
                    };
                    attendanceRecords.Add(doorAccessRecord);
                    UpdateAttendanceDisplay(doorAccessRecord);
                    
                    // Trigger door access
                    System.Threading.Tasks.Task.Run(async () => {
                        try
                        {
                            await RequestLockControl(userGuid, "Student Door Access (Outside)");
                        }
                        catch (Exception lockEx)
                        {
                            Console.WriteLine($"Lock control failed: {lockEx.Message}");
                        }
                    });
                    
                    ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                    return;
                }
                
                switch (currentSessionState)
                {
                    case AttendanceSessionState.ActiveForStudents:
                        if (IsFingerprintOnlyMode)
                        {
                            if (signedInStudentGuids.Contains(userGuid))
                            {
                                SetStatusText($"⚠️ Student {userName} already signed in - allowing door access.");
                                Console.WriteLine($"⚠️ STUDENT {userName} ALREADY SIGNED IN - ALLOWING DOOR ACCESS");

                                System.Threading.Tasks.Task.Run(() =>
                                {
                                    try
                                    {
                                        RecordAttendance(userName, "Student Already Signed In - Door Access", false);

                                        System.Threading.Tasks.Task.Run(async () =>
                                        {
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
                                ProcessVerifiedStudentSignIn(userName, userGuid);
                            }

                            break;
                        }

                        // Check for cross-type verification timeout first
                        if (awaitingCrossTypeVerification)
                        {
                            var verificationElapsed = DateTime.Now - crossVerificationStartTime;
                            if (verificationElapsed.TotalSeconds > CROSS_VERIFICATION_TIMEOUT_SECONDS)
                            {
                                // Timeout - reset verification state
                                Console.WriteLine($"⏱️ CROSS-TYPE VERIFICATION TIMEOUT: {pendingCrossVerificationUser} took too long");
                                SetStatusText($"⏱️ Verification timeout for {pendingCrossVerificationUser}. Starting over...");
                                awaitingCrossTypeVerification = false;
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                firstScanType = "";
                                crossVerificationStartTime = DateTime.MinValue;
                                // Don't return - treat this as a new first scan
                            }
                        }
                        
                        // CROSS-TYPE DUAL-AUTHENTICATION for students
                        if (awaitingCrossTypeVerification)
                        {
                            // Check if this is completing a RFID-first verification
                            if (firstScanType == "RFID")
                            {
                                // RFID was scanned first, now verifying with fingerprint
                                if (userName == pendingCrossVerificationUser && userGuid == pendingCrossVerificationGuid)
                                {
                                    // ✅ VERIFIED: RFID + Fingerprint match!
                                    Console.WriteLine($"✅ CROSS-TYPE VERIFICATION SUCCESS: {userName} (RFID + Fingerprint match)");
                                    SetStatusText($"✅ Verified: {userName}. Processing attendance...");
                                    
                                    // Reset verification state
                                    awaitingCrossTypeVerification = false;
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    firstScanType = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                    
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
                                    // ❌ MISMATCH: Fingerprint doesn't match RFID scan
                                    Console.WriteLine($"❌ CROSS-TYPE VERIFICATION FAILED: RFID={pendingCrossVerificationUser}, Fingerprint={userName}");
                                    SetStatusText($"❌ Verification failed! RFID scan: {pendingCrossVerificationUser}, Fingerprint scan: {userName}. Please try again.");
                                    
                                    // Reset verification state
                                    awaitingCrossTypeVerification = false;
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    firstScanType = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                }
                            }
                            else if (firstScanType == "FINGERPRINT")
                            {
                                // Fingerprint was scanned first, now verifying with RFID
                                // This case is handled when RFID is scanned after fingerprint
                                if (userName == pendingCrossVerificationUser && userGuid == pendingCrossVerificationGuid)
                                {
                                    // ✅ VERIFIED: Fingerprint + RFID match!
                                    Console.WriteLine($"✅ CROSS-TYPE VERIFICATION SUCCESS: {userName} (Fingerprint + RFID match)");
                                    SetStatusText($"✅ Verified: {userName}. Processing attendance...");
                                    
                                    // Reset verification state
                                    awaitingCrossTypeVerification = false;
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    firstScanType = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                    
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
                                    Console.WriteLine($"❌ CROSS-TYPE VERIFICATION FAILED: First={pendingCrossVerificationUser}, Second={userName}");
                                    SetStatusText($"❌ Verification failed! First scan: {pendingCrossVerificationUser}, Second scan: {userName}. Please try again.");
                                    
                                    // Reset verification state
                                    awaitingCrossTypeVerification = false;
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    firstScanType = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                }
                            }
                        }
                        else
                        {
                            // This is the FIRST scan (fingerprint)
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
                                // Not signed in yet - request RFID scan as second verification
                                Console.WriteLine($"🔍 FIRST SCAN (FINGERPRINT): {userName} - Waiting for RFID scan");
                                SetStatusText($"👆 Fingerprint scanned: {userName}. Please scan your RFID card to verify.");
                                
                                // Add record to show "Waiting for RFID" status
                                var studentWaitingRecord = new Database.Models.AttendanceRecord
                                {
                                    UserId = 0,
                                    Username = userName,
                                    Timestamp = DateTime.Now,
                                    Action = "Waiting for RFID",
                                    Status = "Fingerprint First"
                                };
                                attendanceRecords.Add(studentWaitingRecord);
                                UpdateAttendanceDisplay(studentWaitingRecord);
                                
                                // Set cross-type verification state
                                awaitingCrossTypeVerification = true;
                                firstScanType = "FINGERPRINT";
                                pendingCrossVerificationUser = userName;
                                pendingCrossVerificationGuid = userGuid;
                                crossVerificationStartTime = DateTime.Now;
                                
                                // Send intermediate status to ESP32
                                User userForStatus;
                                if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userGuid, out userForStatus))
                                {
                                    _ = Task.Run(async () => await SendIntermediateStatusToESP32(userForStatus, "FINGERPRINT", "RFID", "/api/lock-control"));
                                }
                            }
                        }
                        
                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                        break;
                        
                    case AttendanceSessionState.ActiveForSignOut:
                        // Check if fingerprint-only mode - process sign-out immediately
                        if (IsFingerprintOnlyMode)
                        {
                            if (signedOutStudentGuids.Contains(userGuid))
                            {
                                SetStatusText($"⚠️ Student {userName} already signed out - allowing door access.");
                                Console.WriteLine($"⚠️ STUDENT {userName} ALREADY SIGNED OUT - ALLOWING DOOR ACCESS");

                                System.Threading.Tasks.Task.Run(() =>
                                {
                                    try
                                    {
                                        RecordAttendance(userName, "Student Already Signed Out - Door Access", false);

                                        System.Threading.Tasks.Task.Run(async () =>
                                        {
                                            try
                                            {
                                                await RequestLockControl(userGuid, "Student Already Signed Out - Door Access");
                                            }
                                            catch (Exception lockEx)
                                            {
                                                Console.WriteLine($"Lock control request failed for already signed out student: {lockEx.Message}");
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
                                // Process sign-out immediately with fingerprint-only
                                System.Threading.Tasks.Task.Run(() => {
                                    try
                                    {
                                        var attempt = dbManager?.TryRecordAttendanceByGuid(userGuid, "Student Sign-Out", null);
                                        this.Invoke(new Action(() => {
                                            if (attempt != null && attempt.Success)
                                            {
                                                // Success: update local state and UI
                                                // Only remove from signed in if they were actually signed in
                                                bool wasSignedIn = signedInStudentGuids.Contains(userGuid);
                                                if (wasSignedIn)
                                                {
                                                    signedInStudentGuids.Remove(userGuid);
                                                }
                                                signedOutStudentGuids.Add(userGuid);
                                                
                                                string successMessage = wasSignedIn 
                                                    ? $"✅ Student {userName} signed out."
                                                    : $"✅ Student {userName} signed out (was not signed in).";
                                                SetStatusText(successMessage);

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
                                        
                                        // Show session mode display after scan (both success and denied)
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await System.Threading.Tasks.Task.Delay(6000); // Wait for scan result to display
                                                await RequestSessionModeDisplay(isRfid: false);
                                            }
                                            catch { }
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Warning: Could not record sign-out: {ex.Message}");
                                        this.Invoke(new Action(() => SetStatusText($"❌ Error during sign-out: {ex.Message}")));
                                    }
                                });
                            }

                            ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                            break;
                        }

                        // CRITICAL: DUAL-AUTHENTICATION REQUIRED FOR STUDENT SIGN-OUT (dual-auth mode only)
                        // Check for verification timeout first
                        if (awaitingCrossTypeVerification)
                        {
                            var verificationElapsed = DateTime.Now - crossVerificationStartTime;
                            if (verificationElapsed.TotalSeconds > CROSS_VERIFICATION_TIMEOUT_SECONDS)
                            {
                                // Timeout - reset verification state
                                Console.WriteLine($"⏱️ STUDENT FINGERPRINT SIGN-OUT VERIFICATION TIMEOUT: {pendingCrossVerificationUser} took too long");
                                SetStatusText($"⏱️ Verification timeout for {pendingCrossVerificationUser}. Starting over...");
                                awaitingCrossTypeVerification = false;
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                firstScanType = "";
                                crossVerificationStartTime = DateTime.MinValue;
                                // Don't return - treat this as a new first scan
                            }
                        }
                        
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
                        
                        // Check if completing RFID-first verification for sign-out
                        if (awaitingCrossTypeVerification && firstScanType == "RFID")
                        {
                            // RFID was scanned first, now verifying with fingerprint
                            if (userName == pendingCrossVerificationUser && userGuid == pendingCrossVerificationGuid)
                            {
                                // ✅ VERIFIED: RFID + Fingerprint match!
                                Console.WriteLine($"✅ CROSS-TYPE VERIFICATION SUCCESS FOR SIGN-OUT: {userName} (RFID + Fingerprint match)");
                                SetStatusText($"✅ Verified: {userName}. Processing sign-out...");
                                
                                // Reset verification state
                                awaitingCrossTypeVerification = false;
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                firstScanType = "";
                                crossVerificationStartTime = DateTime.MinValue;
                                
                                // Process verified sign-out
                                System.Threading.Tasks.Task.Run(() => {
                                    try
                                    {
                                        var attempt = dbManager?.TryRecordAttendanceByGuid(userGuid, "Student Sign-Out", null);
                                        this.Invoke(new Action(() => {
                                            if (attempt != null && attempt.Success)
                                            {
                                                // Success: update local state and UI
                                                // Only remove from signed in if they were actually signed in
                                                bool wasSignedIn = signedInStudentGuids.Contains(userGuid);
                                                if (wasSignedIn)
                                                {
                                                    signedInStudentGuids.Remove(userGuid);
                                                }
                                                signedOutStudentGuids.Add(userGuid);
                                                
                                                string successMessage = wasSignedIn 
                                                    ? $"✅ Student {userName} signed out."
                                                    : $"✅ Student {userName} signed out (was not signed in).";
                                                SetStatusText(successMessage);

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
                                        
                                        // Show session mode display after scan (both success and denied)
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await System.Threading.Tasks.Task.Delay(6000); // Wait for scan result to display
                                                await RequestSessionModeDisplay(isRfid: false);
                                            }
                                            catch { }
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Warning: Could not record sign-out: {ex.Message}");
                                        this.Invoke(new Action(() => SetStatusText($"❌ Error during sign-out: {ex.Message}")));
                                    }
                                });
                                
                                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                return;
                            }
                            else
                            {
                                // ❌ MISMATCH: Fingerprint doesn't match RFID scan
                                Console.WriteLine($"❌ CROSS-TYPE VERIFICATION FAILED FOR SIGN-OUT: RFID={pendingCrossVerificationUser}, Fingerprint={userName}");
                                SetStatusText($"❌ Verification failed! RFID scan: {pendingCrossVerificationUser}, Fingerprint scan: {userName}. Please try again.");
                                
                                // Reset verification state
                                awaitingCrossTypeVerification = false;
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                firstScanType = "";
                                crossVerificationStartTime = DateTime.MinValue;
                                
                                ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                return;
                            }
                        }
                        
                        // Check if already awaiting verification from a previous fingerprint scan
                        if (awaitingCrossTypeVerification && firstScanType == "FINGERPRINT")
                        {
                            // Already have a pending fingerprint-first verification - this fingerprint scan is duplicate
                            SetStatusText($"👆 Fingerprint scanned: {userName}. Waiting for RFID verification...");
                            ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                            return;
                        }
                        
                        // Start the fingerprint-first flow - waiting for RFID verification
                        Console.WriteLine($"🔍 FIRST SCAN (FINGERPRINT) FOR SIGN-OUT: {userName} - Waiting for RFID scan");
                        SetStatusText($"👆 Fingerprint scanned: {userName}. Please scan your RFID to verify sign-out.");
                        
                        // Add record to show "Waiting for RFID" status
                        var studentSignOutWaitingRecord = new Database.Models.AttendanceRecord
                        {
                            UserId = 0,
                            Username = userName,
                            Timestamp = DateTime.Now,
                            Action = "Waiting for RFID",
                            Status = "Fingerprint First"
                        };
                        attendanceRecords.Add(studentSignOutWaitingRecord);
                        UpdateAttendanceDisplay(studentSignOutWaitingRecord);
                        
                        // Send intermediate status to ESP32
                        User userForSignOut;
                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userGuid, out userForSignOut))
                        {
                            _ = Task.Run(async () => await SendIntermediateStatusToESP32(userForSignOut, "FINGERPRINT", "RFID", "/api/lock-control"));
                        }
                        
                        // Set cross-type verification state
                        awaitingCrossTypeVerification = true;
                        firstScanType = "FINGERPRINT";
                        pendingCrossVerificationUser = userName;
                        pendingCrossVerificationGuid = userGuid;
                        crossVerificationStartTime = DateTime.Now;
                        
                        ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                        break;
                        
                    case AttendanceSessionState.Inactive:
                    case AttendanceSessionState.WaitingForInstructor:
                        SetStatusText("❌ No active session. Instructor must start the session first.");
                        
                        // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                        if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                        {
                            try
                            {
                                dbManager.LogAccessAttempt(
                                    userId: userGuid,
                                    roomId: null,
                                    authMethod: "Fingerprint",
                                    location: currentScanLocation ?? "inside",
                                    accessType: "attendance_scan",
                                    result: "denied",
                                    reason: "No active session. Instructor must start the session first."
                                );
                                Console.WriteLine($"📝 Logged denied access attempt to ACCESSLOGS for {userName}");
                            }
                            catch (Exception logEx)
                            {
                                Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                            }
                        }
                        
                        // Send denial message to ESP32 for OLED display
                        _ = System.Threading.Tasks.Task.Run(async () => {
                            try
                            {
                                await RequestLockControlDenial(userGuid, userName, "No active session. Instructor must start the session first.", "student");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                            }
                        });
                        break;
                        
                    case AttendanceSessionState.WaitingForInstructorSignOut:
                    case AttendanceSessionState.WaitingForInstructorClose:
                        SetStatusText("❌ Session not ready for student actions. Please wait for instructor.");
                        
                        // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                        if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                        {
                            try
                            {
                                dbManager.LogAccessAttempt(
                                    userId: userGuid,
                                    roomId: null,
                                    authMethod: "Fingerprint",
                                    location: currentScanLocation ?? "inside",
                                    accessType: "attendance_scan",
                                    result: "denied",
                                    reason: "Session not ready for student actions. Please wait for instructor."
                                );
                                Console.WriteLine($"📝 Logged denied access attempt to ACCESSLOGS for {userName}");
                            }
                            catch (Exception logEx)
                            {
                                Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                            }
                        }
                        
                        // Send denial message to ESP32 for OLED display
                        _ = System.Threading.Tasks.Task.Run(async () => {
                            try
                            {
                                await RequestLockControlDenial(userGuid, userName, "Session not ready for student actions. Please wait for instructor.", "student");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                            }
                        });
                        break;
                        
                    default:
                        SetStatusText("❌ Invalid session state for student action.");
                        
                        // Send denial message to ESP32 for OLED display
                        _ = System.Threading.Tasks.Task.Run(async () => {
                            try
                            {
                                await RequestLockControlDenial(userGuid, userName, "Invalid session state for student action.", "student");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                            }
                        });
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
                        var attempt = dbManager.TryRecordAttendanceByGuid(user.EmployeeId, "Student Sign-In", null);
                        
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
                            }
                            else
                            {
                                // Student validation failed - send denial message to ESP32
                                // DENIED: Ensure not marked as signed-in
                                signedInStudentGuids.Remove(userGuid);
                                SetStatusText($"❌ Student {userName}: {attempt.Reason}");
                                Console.WriteLine($"❌ STUDENT {userName} SIGN-IN DENIED: {attempt.Reason}");
                                
                                // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                                if (dbManager != null && !string.IsNullOrEmpty(user.EmployeeId))
                                {
                                    try
                                    {
                                        dbManager.LogAccessAttempt(
                                            userId: user.EmployeeId,
                                            roomId: null, // Will use CurrentRoomId from DatabaseManager
                                            authMethod: "Fingerprint",
                                            location: currentScanLocation ?? "inside",
                                            accessType: "attendance_scan",
                                            result: "denied",
                                            reason: attempt.Reason ?? "Validation failed"
                                        );
                                        Console.WriteLine($"📝 Logged denied access attempt to ACCESSLOGS for {userName}");
                                    }
                                    catch (Exception logEx)
                                    {
                                        Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                    }
                                }
                                
                                var denialRecord = new Database.Models.AttendanceRecord
                                {
                                    UserId = user.Id,
                                    Username = userName,
                                    Timestamp = DateTime.Now,
                                    Action = "Student Sign-In",
                                    Status = $"Denied: {attempt.Reason}"
                                };
                                attendanceRecords.Add(denialRecord);
                                UpdateAttendanceDisplay(denialRecord);
                                
                                // Send denial message to ESP32 for OLED display
                                _ = System.Threading.Tasks.Task.Run(async () => {
                                    try
                                    {
                                        await RequestLockControlDenial(userGuid, userName, attempt.Reason ?? "Validation failed", "student");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                                    }
                                });
                            }
                        }));
                        
                        // Request lock control for successful student sign-in (only if successful, outside UI thread)
                        if (attempt.Success)
                        {
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
                        
                        // Show session mode display after scan (both success and denied)
                        _ = System.Threading.Tasks.Task.Run(async () => {
                            try
                            {
                                await System.Threading.Tasks.Task.Delay(6000); // Wait for scan result to display
                                await RequestSessionModeDisplay(isRfid: false);
                            }
                            catch { }
                        });
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
                    var rawTemplate = ((FutronicEnrollment)m_Operation).Template;

                    // ENHANCED: Validate template quality before saving to prevent corrupted templates
                    if (!IsValidFingerprintTemplate(rawTemplate))
                    {
                        Console.WriteLine($"❌ ENROLLMENT FAILED: Invalid template quality detected");
                        SetStatusText($"❌ Enrollment failed: Template quality validation failed. Please try enrolling again.");
                        UpdateEnrollmentGuidance(0, "Template quality poor. Please try again.");
                        return;
                    }

                    Console.WriteLine($"✅ Template validation passed - Size: {rawTemplate.Length} bytes");
                    userRecord.Template = rawTemplate;
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
                
                // CRITICAL: Reset identifying flag to allow attendance restart
                isIdentifying = false;
                
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
                
                // NEW: Don't process scans if paused for instructor action
                if (isPausedForInstructorAction)
                {
                    Console.WriteLine("⏸️ Scanner paused - ignoring scan result during instructor processing");
                    return; // Don't schedule next, will resume when unpaused
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
                
                // QUALITY VALIDATION: Only process SUCCESSFUL scans if we had a valid finger placement
                // Failed scans (retCode != 0) don't require valid placement - they're errors that need to be handled
                bool placementStillValid = hasValidFingerPlacement && 
                                         (DateTime.Now - lastValidPutOnTime).TotalMilliseconds < VALID_PLACEMENT_TIMEOUT_MS;
                
                // For failed scans, always show error message and reset flags
                if (!success)
                {
                    // Reset flags on failure to allow retry
                    hasValidFingerPlacement = false;
                    isFingerOnScanner = false;
                    
                    // Show appropriate error message based on retCode
                    string errorMsg = retCode == 0 ? "No finger detected" : $"Template capture failed: {retCode}";
                    SetStatusText($"{errorMsg}. Please try again.");
                    Console.WriteLine($"⚠️ {errorMsg}");
                    ScheduleNextGetBaseTemplate(1000);
                    return;
                }
                
                // For successful template capture, require valid finger placement
                if (success && placementStillValid)
                {
                    // Fingerprint-only/RFID-only gating
                    if (deviceConfig?.AllowRfidOnly == true)
                    {
                        SetStatusText("RFID-only mode: please scan your RFID card.");
                        Console.WriteLine("⚠️ Fingerprint scan blocked - RFID-only mode active. Sending OLED denial warning...");
                        _ = ShowModeRestrictionWarningAsync(true);
                        // Reset flags to allow subsequent scans
                        hasValidFingerPlacement = false;
                        isFingerOnScanner = false;
                        ScheduleNextGetBaseTemplate(800);
                        return;
                    }
                    
                    lastScanTime = DateTime.Now;
                    SetStatusText("Processing identification...");
                    
                    // Reset valid placement flag since we're processing the scan
                    hasValidFingerPlacement = false;
                    isFingerOnScanner = false;
                    
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
                            
                            // Set location based on which sensor detected the scan
                            // In dual-sensor mode, determine which sensor is active
                            // Default to "inside" for manual tab scans
                            if (isDualSensorMode)
                            {
                                // Check device configuration to determine which sensor is active
                                // If only outside sensor is configured, use outside
                                // If only inside sensor is configured, use inside
                                // If both are configured, check which one is actually active
                                bool hasInsideSensor = deviceConfig?.InsideSensor != null && m_InsideSensorEnabled;
                                bool hasOutsideSensor = deviceConfig?.OutsideSensor != null && m_OutsideSensorEnabled;
                                
                                if (hasOutsideSensor && !hasInsideSensor)
                                {
                                    // Only outside sensor configured
                                    currentScanLocation = "outside";
                                    Console.WriteLine($"📍 Only outside sensor configured: location set to outside");
                                }
                                else if (hasInsideSensor && !hasOutsideSensor)
                                {
                                    // Only inside sensor configured
                                    currentScanLocation = "inside";
                                    Console.WriteLine($"📍 Only inside sensor configured: location set to inside");
                                }
                                else
                                {
                                    // Both configured or unknown, default to outside
                                    currentScanLocation = "outside";
                                    Console.WriteLine($"📍 Both sensors configured or unknown: location set to outside");
                                }
                            }
                            else
                            {
                                currentScanLocation = "inside";
                            }
                            
                            // Process attendance scan directly (remove double-scan for now)
                            try
                            {
                                ProcessAttendanceScan(userName);
                                // Don't show "Attendance recorded" here - let the ProcessInstructorScan/ProcessStudentScan handle messaging
                            }
                            catch (Exception ex)
                            {
                                SetStatusText($"Error processing scan: {ex.Message}");
                            }
                            
                                                          ScheduleNextGetBaseTemplate(SCAN_INTERVAL_ACTIVE_MS);
                                                    }
                            else
                            {
                              // Allow door override for unknown fingerprints if enabled (independent of finger-on state)
                              bool overrideOpened = false;
                              if (deviceConfig?.AllowUnauthorizedFingerprints == true)
                              {
                                  var sinceLast = DateTime.Now - lastUnauthorizedDoorOpenTime;
                                  if (sinceLast.TotalMilliseconds >= UNAUTHORIZED_DOOR_DEBOUNCE_MS)
                                  {
                                      overrideOpened = true;
                                      lastUnauthorizedDoorOpenTime = DateTime.Now;
                                      Console.WriteLine("🔓 Allowing door override for unknown fingerprint (attendance flow)");
                                      SetStatusText("🔓 Door override requested for unknown fingerprint.");
                                      _ = System.Threading.Tasks.Task.Run(async () => {
                                          try { await RequestAnonymousLockControl(currentScanLocation ?? "inside", "Door override for unknown fingerprint"); } catch { }
                                      });
                                  }
                                  else
                                  {
                                      Console.WriteLine("⏳ Door override debounced in attendance flow");
                                  }
                              }

                              // Don't log unknown scans while finger is still on scanner (prevents spam)
                              if (!isFingerOnScanner || overrideOpened)
                              {
                                  // Debounce unknown scan logging to prevent spam
                                  var timeSinceLastUnknownLog = DateTime.Now - lastUnknownScanLogTime;
                                  if (overrideOpened || timeSinceLastUnknownLog.TotalMilliseconds >= UNKNOWN_SCAN_DEBOUNCE_MS)
                                  {
                                      // Log unknown fingerprint scan to ACCESSLOGS table
                                      try
                                      {
                                          if (dbManager != null)
                                          {
                                              dbManager.LogAccessAttempt(
                                                  userId: null, // NULL for unknown user
                                                  roomId: null, // Will use CurrentRoomId from DatabaseManager
                                                  authMethod: "Fingerprint",
                                                  location: currentScanLocation ?? "inside",
                                                  accessType: "attendance_scan",
                                                  result: overrideOpened ? "granted" : "denied",
                                                  reason: overrideOpened ? "Door override enabled for unknown fingerprint" : $"Unknown fingerprint - no match found (matchIndex: {matchIndex}, users count: {users.Count})"
                                              );
                                              Console.WriteLine($"📝 Logged unknown fingerprint scan to ACCESSLOGS: {currentScanLocation ?? "inside"}");
                                              lastUnknownScanLogTime = DateTime.Now;
                                          }
                                      }
                                      catch (Exception logEx)
                                      {
                                          Console.WriteLine($"⚠️ Failed to log unknown fingerprint scan: {logEx.Message}");
                                          // Continue even if logging fails
                                      }
                                  }
                                  else
                                  {
                                      Console.WriteLine($"⏳ Unknown scan debounced - waiting {Math.Ceiling((UNKNOWN_SCAN_DEBOUNCE_MS - timeSinceLastUnknownLog.TotalMilliseconds) / 1000)}s before logging again");
                                  }
                              }
                              else
                              {
                                  Console.WriteLine("⏸️ Skipping unknown scan log - finger still on scanner");
                              }
                              
                              SetStatusText("❌ Fingerprint not recognized. Please try again or enroll first.");
                              // Notify ESP32 to show 'No match' on OLED        
                              _ = System.Threading.Tasks.Task.Run(async () => { 
                                  try { await RequestNoMatchDisplay(); } catch { }  
                              });
                              ScheduleNextGetBaseTemplate(1000);
                          }
                      }
                      else
                      {
                          // Allow door override for identification failure as well (attendance flow)
                          bool overrideOpened2 = false;
                          if (deviceConfig?.AllowUnauthorizedFingerprints == true)
                          {
                              var sinceLast = DateTime.Now - lastUnauthorizedDoorOpenTime;
                              if (sinceLast.TotalMilliseconds >= UNAUTHORIZED_DOOR_DEBOUNCE_MS)
                              {
                                  overrideOpened2 = true;
                                  lastUnauthorizedDoorOpenTime = DateTime.Now;
                                  Console.WriteLine("🔓 Allowing door override for unknown fingerprint (identification failed)");
                                  SetStatusText("🔓 Door override requested for unknown fingerprint.");
                                  _ = System.Threading.Tasks.Task.Run(async () => {
                                      try { await RequestAnonymousLockControl(currentScanLocation ?? "inside", "Door override for unknown fingerprint"); } catch { }
                                  });
                              }
                              else
                              {
                                  Console.WriteLine("⏳ Door override debounced in attendance flow (identification failed)");
                              }
                          }

                          // Don't log unknown scans while finger is still on scanner (prevents spam)
                          if (!isFingerOnScanner || overrideOpened2)
                          {
                              // Debounce unknown scan logging to prevent spam
                              var timeSinceLastUnknownLog = DateTime.Now - lastUnknownScanLogTime;
                              if (overrideOpened2 || timeSinceLastUnknownLog.TotalMilliseconds >= UNKNOWN_SCAN_DEBOUNCE_MS)
                              {
                                  // Log unknown fingerprint scan to ACCESSLOGS table (identification failed)
                                  try
                                  {
                                      if (dbManager != null)
                                      {
                                          dbManager.LogAccessAttempt(
                                              userId: null, // NULL for unknown user
                                              roomId: null, // Will use CurrentRoomId from DatabaseManager
                                              authMethod: "Fingerprint",
                                              location: currentScanLocation ?? "inside",
                                              accessType: "attendance_scan",
                                              result: overrideOpened2 ? "granted" : "denied",
                                              reason: overrideOpened2 ? "Door override enabled for unknown fingerprint" : $"Unknown fingerprint - identification failed (result: {result})"
                                          );
                                          Console.WriteLine($"📝 Logged unknown fingerprint scan to ACCESSLOGS: {currentScanLocation ?? "inside"}");
                                          lastUnknownScanLogTime = DateTime.Now;
                                      }
                                  }
                                  catch (Exception logEx)
                                  {
                                      Console.WriteLine($"⚠️ Failed to log unknown fingerprint scan: {logEx.Message}");
                                      // Continue even if logging fails
                                  }
                              }
                              else
                              {
                                  Console.WriteLine($"⏳ Unknown scan debounced - waiting {Math.Ceiling((UNKNOWN_SCAN_DEBOUNCE_MS - timeSinceLastUnknownLog.TotalMilliseconds) / 1000)}s before logging again");
                              }
                          }
                          else
                          {
                              Console.WriteLine("⏸️ Skipping unknown scan log - finger still on scanner");
                          }
                          
                          SetStatusText("❌ Fingerprint not recognized. Please try again or enroll first.");
                          // Notify ESP32 to show 'No match' on OLED
                          _ = System.Threading.Tasks.Task.Run(async () => {     
                              try { await RequestNoMatchDisplay(); } catch { }  
                          });
                          ScheduleNextGetBaseTemplate(1000);
                      }
                }
                else if (success && !placementStillValid)
                {
                    // Template capture succeeded but no valid finger placement was detected
                    // This prevents processing scans when OnPutOn wasn't called (false positives)
                    Console.WriteLine("⚠️ Template capture succeeded but no valid finger placement detected - ignoring scan");
                    hasValidFingerPlacement = false;
                    isFingerOnScanner = false;
                    SetStatusText("Please place your finger on the scanner.");
                    ScheduleNextGetBaseTemplate(1000);
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
                    SetStatusText($"✅ First scan: {pendingUserName}. Please wait, then scan again...");
                    
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
                
                // NEW: Don't schedule if paused for instructor action
                if (isPausedForInstructorAction)
                {
                    Console.WriteLine("⏸️ Scanner paused for instructor action - skipping schedule");
                    return;
                }
                
                // NEW: Don't schedule if finger is still on scanner (prevents repeated scans)
                if (isFingerOnScanner)
                {
                    Console.WriteLine("⏸️ Finger still on scanner - skipping schedule to prevent repeated scans");
                    return;
                }
                
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
        private void RecordAttendance(string userName, string action = null, bool sendToDatabase = true, string location = null)
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
                    
                    // In dual-sensor mode, use currentScanLocation if no location specified
                    if (isDualSensorMode && string.IsNullOrEmpty(location))
                    {
                        location = currentScanLocation;
                        Console.WriteLine($"📍 Using sensor location: {location}");
                    }
                    
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
                                var attempt = dbManager.TryRecordAttendanceByGuid(userGuid, actionToRecord, location);
                                
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
            // Use unified AddAttendanceRecord with Fingerprint method
            AddAttendanceRecord(record.Username, "Fingerprint", record.Action, record.Status ?? "Success");
        }
        
        private void UpdateAttendanceDisplayLegacy(Database.Models.AttendanceRecord record)
        {
            // Ensure UI updates happen on the UI thread
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateAttendanceDisplayLegacy(record)));
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
            
            // Custodian and Dean: Always allow door access
            if (user.UserType?.ToLower() == "custodian" || user.UserType?.ToLower() == "dean")
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

        private async Task<bool> PostLockPayloadAsync(string esp32Ip, object payload)
        {
            try
            {
                string esp32Url = $"http://{esp32Ip}/api/lock-control";
                Console.WriteLine($"Sending to ESP32: {esp32Url}");

                var json = JsonSerializer.Serialize(payload);
                Console.WriteLine($"Payload: {json}");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);

                    if (client.DefaultRequestHeaders.Contains("X-API-Key"))
                    {
                        client.DefaultRequestHeaders.Remove("X-API-Key");
                    }
                    client.DefaultRequestHeaders.Add("X-API-Key", LOCK_CONTROLLER_API_KEY);

                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(esp32Url, content);

                    Console.WriteLine($"ESP32 Response Status: {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"ESP32 Response: {responseContent}");
                        return true;
                    }

                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ ESP32 request failed: {response.StatusCode}");
                    Console.WriteLine($"Error: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error sending lock payload: {ex.Message}");
                return false;
            }
        }

        private async Task RequestAnonymousLockControl(string location, string reason)
        {
            try
            {
                Console.WriteLine("=== ANONYMOUS LOCK CONTROL START ===");
                Console.WriteLine($"Location: {location}");
                Console.WriteLine($"Reason: {reason}");

                string esp32Ip = await DiscoverESP32();

                if (string.IsNullOrEmpty(esp32Ip))
                {
                    Console.WriteLine("❌ No ESP32 device found on network");
                    if (!this.IsDisposed)
                    {
                        this.Invoke(new Action(() =>
                        {
                            SetStatusText("❌ Door override failed: no controller");
                        }));
                    }
                    return;
                }

                var payload = new
                {
                    action = "open",
                    // Use a privileged role to satisfy ESP32 policy checks
                    user = "System Override",
                    userType = "instructor",
                    sessionActive = true,
                    message = reason,
                    location = location,
                    overrideRequest = true
                };

                bool success = await PostLockPayloadAsync(esp32Ip, payload);

                if (!this.IsDisposed)
                {
                    this.Invoke(new Action(() =>
                    {
                        if (success)
                        {
                            SetStatusText("🔓 Door override granted for unknown fingerprint.");
                        }
                        else
                        {
                            SetStatusText("❌ Door override failed.");
                        }
                    }));
                }

                Console.WriteLine("=== ANONYMOUS LOCK CONTROL END ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Anonymous lock control error: {ex.Message}");
                if (!this.IsDisposed)
                {
                    this.Invoke(new Action(() =>
                    {
                        SetStatusText($"❌ Door override error: {ex.Message}");
                    }));
                }
            }
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

                // Special handling for early arrivals - allow door access even if session is not active
                bool isEarlyArrival = !string.IsNullOrEmpty(action) && 
                    (action.IndexOf("Early Arrival", StringComparison.OrdinalIgnoreCase) >= 0);

                // Check if user should be allowed to open lock (unless it's an early arrival)
                if (!isEarlyArrival && !ShouldOpenLockForUser(user))
                {
                    Console.WriteLine($"⚠️ User {user.FirstName} {user.LastName} ({user.UserType}) - access denied");
                    Console.WriteLine($"⚠️ Session State: {currentSessionState} - Not allowed for this user type");
                    return;
                }

                if (isEarlyArrival)
                {
                    Console.WriteLine($"✅ Early Arrival: Allowing door access for {user.FirstName} {user.LastName}");
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

                // Create payload for ESP32
                string displayMessage = null;
                if (!string.IsNullOrEmpty(action) && action.IndexOf("Early Arrival", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    displayMessage = "Early Arrival recorded. Scan inside at start.";
                }
                var payload = new
                {
                    action = lockAction,
                    user = $"{user.FirstName} {user.LastName}",
                    userType = user.UserType?.ToLower(),
                    sessionActive = currentSessionState == AttendanceSessionState.ActiveForStudents || 
                                   currentSessionState == AttendanceSessionState.ActiveForSignOut,
                    message = displayMessage
                };

                bool success = await PostLockPayloadAsync(esp32Ip, payload);

                if (success)
                {
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
                    this.Invoke(new Action(() => {
                        SetStatusText("❌ Lock control failed");
                    }));
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

        // Request lock control denial message for ESP32 OLED display
        private async Task RequestLockControlDenial(string userGuid, string userName, string denialReason, string userType)
        {
            try
            {
                Console.WriteLine("=== LOCK CONTROL DENIAL REQUEST START ===");
                Console.WriteLine($"User GUID: {userGuid}");
                Console.WriteLine($"User Name: {userName}");
                Console.WriteLine($"Denial Reason: {denialReason}");
                Console.WriteLine($"User Type: {userType}");

                // Auto-discover ESP32 on the network
                string esp32Ip = await DiscoverESP32();
                
                if (string.IsNullOrEmpty(esp32Ip))
                {
                    Console.WriteLine("❌ No ESP32 device found on network - cannot display denial message");
                    return;
                }

                // Create payload for ESP32 with denial information
                var payload = new
                {
                    action = "denied",
                    user = userName,
                    userType = userType?.ToLower(),
                    sessionActive = false,
                    denialReason = denialReason
                };

                bool success = await PostLockPayloadAsync(esp32Ip, payload);

                if (success)
                {
                    Console.WriteLine("✅ Denial message sent to ESP32 for display");
                }
                else
                {
                    Console.WriteLine("❌ Failed to send denial message to ESP32");
                }
                
                Console.WriteLine("=== LOCK CONTROL DENIAL REQUEST END ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error sending denial to ESP32: {ex.Message}");
            }
        }

        // Display 'No match' on OLED for fingerprint path
        private async Task RequestNoMatchDisplay()
        {
            try
            {
                string esp32Ip = await DiscoverESP32();
                if (string.IsNullOrEmpty(esp32Ip)) return;

                string esp32Url = $"http://{esp32Ip}/api/lock-control";
                var payload = new { action = "no_match" };
                var json = JsonSerializer.Serialize(payload);
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.Add("X-API-Key", "0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567");
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    await client.PostAsync(esp32Url, content);
                }
            }
            catch { }
        }

        // Display 'No match' on OLED for RFID path
        private async Task RequestRfidNoMatchDisplay(string rfidData)
        {
            try
            {
                string esp32Ip = await DiscoverESP32();
                if (string.IsNullOrEmpty(esp32Ip)) return;

                string esp32Url = $"http://{esp32Ip}/api/rfid-scan";
                var payload = new { action = "no_match", rfid_data = rfidData };
                var json = JsonSerializer.Serialize(payload);
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.Add("X-API-Key", "0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567");
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    await client.PostAsync(esp32Url, content);
                }
            }
            catch { }
        }

        // Request RFID lock control denial message for ESP32 OLED display
        private async Task RequestRfidLockControlDenial(string userGuid, string userName, string denialReason, string userType, string rfidData)
        {
            try
            {
                Console.WriteLine("=== RFID LOCK CONTROL DENIAL REQUEST START ===");
                Console.WriteLine($"User GUID: {userGuid}");
                Console.WriteLine($"User Name: {userName}");
                Console.WriteLine($"Denial Reason: {denialReason}");
                Console.WriteLine($"User Type: {userType}");
                Console.WriteLine($"RFID Data: {rfidData}");

                // Auto-discover ESP32 on the network
                string esp32Ip = await DiscoverESP32();
                
                if (string.IsNullOrEmpty(esp32Ip))
                {
                    Console.WriteLine("❌ No ESP32 device found on network - cannot display RFID denial message");
                    return;
                }

                string esp32Url = $"http://{esp32Ip}/api/rfid-scan";
                Console.WriteLine($"Sending RFID denial to ESP32: {esp32Url}");

                // Create payload for ESP32 RFID endpoint with denial information
                var payload = new
                {
                    rfid_data = rfidData,
                    user = userName,
                    userType = userType?.ToLower(),
                    sessionActive = false,
                    denialReason = denialReason
                };

                var json = JsonSerializer.Serialize(payload);
                Console.WriteLine($"RFID Denial Payload: {json}");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    
                    // Add API key to request header for security
                    string apiKey = "0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567"; // Must match ESP32 API key
                    client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
                    
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    var response = await client.PostAsync(esp32Url, content);
                    
                    Console.WriteLine($"ESP32 RFID Denial Response Status: {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"ESP32 RFID Denial Response: {responseContent}");
                        Console.WriteLine($"✅ RFID denial message sent to ESP32 for display");
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"❌ ESP32 RFID denial request failed: {response.StatusCode}");
                        Console.WriteLine($"Error: {errorContent}");
                    }
                }
                
                Console.WriteLine("=== RFID LOCK CONTROL DENIAL REQUEST END ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error sending RFID denial to ESP32: {ex.Message}");
            }
        }
        // Display informational message on OLED (fingerprint path)
        private async Task RequestInfoDisplay(string userGuid, string userName, string message)
        {
            try
            {
                string esp32Ip = await DiscoverESP32();
                if (string.IsNullOrEmpty(esp32Ip))
                {
                    Console.WriteLine("⚠️ ESP32 not found - cannot send info message to OLED");
                    return;
                }

                string esp32Url = $"http://{esp32Ip}/api/lock-control";
                // Add timestamp to payload to ensure ESP32 treats each message as unique and displays it every time
                var payload = new { action = "info", user = userName, message = message, timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), forceRefresh = true };
                var json = JsonSerializer.Serialize(payload);
                
                Console.WriteLine($"📤 Sending info message to ESP32: {message}");
                
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.Add("X-API-Key", "0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567");
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(esp32Url, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"✅ Info message sent successfully to ESP32");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ ESP32 returned status: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error sending info message to ESP32: {ex.Message}");
            }
        }
        // Display informational message on OLED (RFID path)
        private async Task RequestRfidInfoDisplay(string userGuid, string userName, string message, string rfidData)
        {
            try
            {
                string esp32Ip = await DiscoverESP32();
                if (string.IsNullOrEmpty(esp32Ip))
                {
                    Console.WriteLine("⚠️ ESP32 not found - cannot send RFID info message to OLED");
                    return;
                }

                string esp32Url = $"http://{esp32Ip}/api/rfid-scan";
                // Add timestamp to payload to ensure ESP32 treats each message as unique and displays it every time
                var payload = new { action = "info", rfid_data = rfidData, user = userName, message = message, timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), forceRefresh = true };
                var json = JsonSerializer.Serialize(payload);
                
                Console.WriteLine($"📤 Sending RFID info message to ESP32: {message} (RFID: {rfidData})");
                
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.Add("X-API-Key", "0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567");
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(esp32Url, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"✅ RFID info message sent successfully to ESP32");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ ESP32 returned status: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error sending RFID info message to ESP32: {ex.Message}");
            }
        }

        // Send session mode display to OLED (Sign In/Sign Out mode with subject and instructor)
        // Displays for 10 seconds, then clears the display
        private async Task RequestSessionModeDisplay(bool isRfid = false, string rfidData = null)
        {
            try
            {
                bool isSignInMode = currentSessionState == AttendanceSessionState.ActiveForStudents;
                bool isSignOutMode = currentSessionState == AttendanceSessionState.ActiveForSignOut;
                
                if (!isSignInMode && !isSignOutMode) return;
                
                string subjectName = currentSubjectName ?? "Unknown Subject";
                string instructorName = currentInstructorName ?? "Unknown Instructor";
                
                // Fallback: Get instructor name from lookup
                if ((instructorName == "Unknown Instructor" || string.IsNullOrEmpty(instructorName)) && 
                    !string.IsNullOrEmpty(currentInstructorId) && userLookupByGuid != null)
                {
                    if (userLookupByGuid.TryGetValue(currentInstructorId, out User instructor))
                    {
                        instructorName = instructor.Username ?? $"{instructor.FirstName} {instructor.LastName}".Trim();
                        if (!string.IsNullOrWhiteSpace(instructorName))
                            currentInstructorName = instructorName;
                    }
                }
                
                // Format display lines (OLED 128x64, ~21 chars per line)
                string line1 = "Currently in";
                string line2 = isSignInMode ? "Sign In Mode" : "Sign Out Mode";
                string line3 = $"Subject: {subjectName}";
                string line4 = $"Instructor: {instructorName}";
                
                // Truncate if too long
                if (line3.Length > 21) 
                    line3 = "Subject: " + (subjectName.Length > 12 ? subjectName.Substring(0, 9) + "..." : subjectName);
                if (line4.Length > 21) 
                    line4 = "Instructor: " + (instructorName.Length > 9 ? instructorName.Substring(0, 6) + "..." : instructorName);
                
                string esp32Ip = await DiscoverESP32();
                if (string.IsNullOrEmpty(esp32Ip)) return;
                
                string endpoint = isRfid ? "/api/rfid-scan" : "/api/lock-control";
                string esp32Url = $"http://{esp32Ip}{endpoint}";
                string modeText = isSignInMode ? "Sign In" : "Sign Out";
                
                // Send the message twice at 5-second intervals (10 seconds total)
                for (int i = 0; i < 2; i++)
                {
                    try
                    {
                        var payload = new { action = "status", rfid_data = isRfid ? (rfidData ?? "") : "", line1, line2, line3, line4 };
                        
                        var json = JsonSerializer.Serialize(payload);
                        using (var client = new HttpClient())
                        {
                            client.Timeout = TimeSpan.FromSeconds(5);
                            client.DefaultRequestHeaders.Add("X-API-Key", LOCK_CONTROLLER_API_KEY);
                            var content = new StringContent(json, Encoding.UTF8, "application/json");
                            await client.PostAsync(esp32Url, content);
                        }
                        if (i < 1) await System.Threading.Tasks.Task.Delay(5000);
                    }
                    catch { }
                }
                
                // Wait for 10 seconds total, then clear the display
                await System.Threading.Tasks.Task.Delay(5000);
                
                // Clear the display by sending empty lines
                try
                {
                    var clearPayload = new { action = "status", rfid_data = isRfid ? (rfidData ?? "") : "", line1 = "", line2 = "", line3 = "", line4 = "" };
                    var clearJson = JsonSerializer.Serialize(clearPayload);
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(5);
                        client.DefaultRequestHeaders.Add("X-API-Key", LOCK_CONTROLLER_API_KEY);
                        var content = new StringContent(clearJson, Encoding.UTF8, "application/json");
                        await client.PostAsync(esp32Url, content);
                    }
                }
                catch { }
            }
            catch { }
        }

        private Task ShowModeRestrictionWarningAsync(bool isRfidOnlyMode, string rfidData = null)
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (isRfidOnlyMode)
                    {
                        await RequestLockControlDenial(
                            string.Empty,
                            "RFID-Only Mode",
                            "Please scan your RFID card to continue.",
                            "system");
                    }
                    else
                    {
                        await RequestRfidLockControlDenial(
                            string.Empty,
                            "Fingerprint-Only Mode",
                            "Please use the fingerprint scanner to continue.",
                            "system",
                            rfidData ?? string.Empty);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Warning: failed to display mode restriction warning: {ex.Message}");
                }
            });
        }

        // Record early arrival using direct database access for fingerprint
        private async Task RecordEarlyArrivalFingerprint(string userName, string userGuid)
        {
            try
            {
                Console.WriteLine("=== EARLY ARRIVAL FINGERPRINT SCAN START ===");
                Console.WriteLine($"Student: {userName}");
                Console.WriteLine($"User GUID: {userGuid}");

                if (dbManager == null)
                {
                    Console.WriteLine("❌ Database manager not available");
                    SetStatusText("❌ System error - database not available");
                    return;
                }

                // Call database manager to record early arrival
                var result = dbManager.TryRecordAttendanceByGuid(userGuid, "Early Arrival (Fingerprint)", "outside");

                if (result.Success)
                {
                    Console.WriteLine($"✅ Early arrival recorded for {result.SubjectName}");
                    
                    this.Invoke(new Action(() => {
                        SetStatusText($"⏰ Early arrival: {userName} for {result.SubjectName}. Scan inside when class starts.");
                        
                        // Create early arrival record
                        var earlyArrivalRecord = new Database.Models.AttendanceRecord
                        {
                            UserId = 0,
                            Username = userName,
                            Timestamp = DateTime.Now,
                            Action = "Early Arrival",
                            Status = $"Awaiting Confirmation - {result.SubjectName}"
                        };
                        attendanceRecords.Add(earlyArrivalRecord);
                        UpdateAttendanceDisplay(earlyArrivalRecord);
                    }));

                    // Show final success message on OLED (will display for 5 seconds)
                    _ = System.Threading.Tasks.Task.Run(async () => {
                        try { await RequestInfoDisplay(userGuid, userName, "Early Arrival Recorded!"); } catch { }
                    });

                    // Trigger door access for early arrivals
                    await RequestLockControl(userGuid, "Student Early Arrival (Fingerprint)");
                }
                else
                {
                    Console.WriteLine($"❌ Early arrival denied: {result.Reason}");
                    
                    // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                    if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                    {
                        try
                        {
                            dbManager.LogAccessAttempt(
                                userId: userGuid,
                                roomId: null,
                                authMethod: "Fingerprint",
                                location: "outside",
                                accessType: "attendance_scan",
                                result: "denied",
                                reason: $"Early arrival denied: {result.Reason ?? "Validation failed"}"
                            );
                            Console.WriteLine($"📝 Logged denied early arrival attempt to ACCESSLOGS for {userName}");
                        }
                        catch (Exception logEx)
                        {
                            Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                        }
                    }
                    
                    this.Invoke(new Action(() => {
                        string statusMessage;
                        string recordStatus;
                        
                        if (result.Reason.Contains("No class starting"))
                        {
                            statusMessage = $"❌ Too early. {result.Reason}";
                            recordStatus = "Too Early";
                        }
                        else if (result.Reason.Contains("Not enrolled"))
                        {
                            statusMessage = $"❌ Not enrolled. {result.Reason}";
                            recordStatus = "Not Enrolled";
                        }
                        else if (result.Reason.Contains("Already recorded"))
                        {
                            statusMessage = $"❌ Already scanned. {result.Reason}";
                            recordStatus = "Duplicate";
                        }
                        else
                        {
                            statusMessage = $"❌ {result.Reason}";
                            recordStatus = "Error";
                        }
                        
                        SetStatusText(statusMessage);
                        
                        // Create denial record
                        var denialRecord = new Database.Models.AttendanceRecord
                        {
                            UserId = 0,
                            Username = userName,
                            Timestamp = DateTime.Now,
                            Action = "Early Arrival Denied",
                            Status = recordStatus
                        };
                        attendanceRecords.Add(denialRecord);
                        UpdateAttendanceDisplay(denialRecord);
                    }));
                    
                    // Send denial message to ESP32 for OLED display
                    _ = System.Threading.Tasks.Task.Run(async () => {
                        try
                        {
                            await RequestLockControlDenial(userGuid, userName, result.Reason ?? "Early arrival denied", "student");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Could not send early arrival denial to ESP32: {ex.Message}");
                        }
                    });
                }
                
                Console.WriteLine("=== EARLY ARRIVAL FINGERPRINT SCAN END ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error recording early arrival: {ex.Message}");
                this.Invoke(new Action(() => {
                    SetStatusText($"❌ Early arrival error: {ex.Message}");
                    
                    // Create error record
                    var errorRecord = new Database.Models.AttendanceRecord
                    {
                        UserId = 0,
                        Username = userName,
                        Timestamp = DateTime.Now,
                        Action = "Early Arrival Error",
                        Status = ex.Message
                    };
                    attendanceRecords.Add(errorRecord);
                    UpdateAttendanceDisplay(errorRecord);
                }));
            }
        }

        // Record early arrival using direct database access
        private async Task RecordEarlyArrivalRfid(string userName, string userGuid, string rfidTag)
        {
            try
            {
                Console.WriteLine("=== EARLY ARRIVAL RFID SCAN START ===");
                Console.WriteLine($"Student: {userName}");
                Console.WriteLine($"User GUID: {userGuid}");
                Console.WriteLine($"RFID Tag: {rfidTag}");

                if (dbManager == null)
                {
                    Console.WriteLine("❌ Database manager not available");
                    SetRfidStatusText("❌ System error - database not available");
                    return;
                }

                // Call database manager to record early arrival
                var result = dbManager.TryRecordAttendanceByGuid(userGuid, "Early Arrival (RFID)", "outside");

                if (result.Success)
                {
                    Console.WriteLine($"✅ Early arrival recorded for {result.SubjectName}");
                    
                    this.Invoke(new Action(() => {
                        SetRfidStatusText($"⏰ Early arrival: {userName} for {result.SubjectName}. Scan inside when class starts.");
                        AddRfidAttendanceRecord(userName, "Early Arrival", $"Awaiting Confirmation - {result.SubjectName}");
                    }));

                    // Show final success message on OLED (will display for 5 seconds)
                    _ = System.Threading.Tasks.Task.Run(async () => {
                        try { await RequestRfidInfoDisplay(userGuid, userName, "Early Arrival Recorded!", rfidTag); } catch { }
                    });

                    // Trigger door access for early arrivals
                    await RequestRfidLockControl(userGuid, "Student Early Arrival (RFID)", rfidTag);
                }
                else
                {
                    Console.WriteLine($"❌ Early arrival denied: {result.Reason}");
                    
                    // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                    if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                    {
                        try
                        {
                            dbManager.LogAccessAttempt(
                                userId: userGuid,
                                roomId: null,
                                authMethod: "RFID",
                                location: "outside",
                                accessType: "attendance_scan",
                                result: "denied",
                                reason: $"Early arrival denied: {result.Reason ?? "Validation failed"}"
                            );
                            Console.WriteLine($"📝 Logged denied early arrival attempt to ACCESSLOGS for {userName}");
                        }
                        catch (Exception logEx)
                        {
                            Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                        }
                    }
                    
                    this.Invoke(new Action(() => {
                        if (result.Reason.Contains("No class starting"))
                        {
                            SetRfidStatusText($"❌ Too early. {result.Reason}");
                            AddRfidAttendanceRecord(userName, "Early Arrival Denied", "Too Early");
                        }
                        else if (result.Reason.Contains("Not enrolled"))
                        {
                            SetRfidStatusText($"❌ Not enrolled. {result.Reason}");
                            AddRfidAttendanceRecord(userName, "Early Arrival Denied", "Not Enrolled");
                        }
                        else if (result.Reason.Contains("Already recorded"))
                        {
                            SetRfidStatusText($"❌ Already scanned. {result.Reason}");
                            AddRfidAttendanceRecord(userName, "Early Arrival Denied", "Duplicate");
                        }
                        else
                        {
                            SetRfidStatusText($"❌ {result.Reason}");
                            AddRfidAttendanceRecord(userName, "Early Arrival Denied", "Error");
                        }
                    }));
                    
                    // Send denial message to ESP32 for OLED display (RFID)
                    _ = System.Threading.Tasks.Task.Run(async () => {
                        try
                        {
                            await RequestRfidLockControlDenial(userGuid, userName, result.Reason ?? "Early arrival denied", "student", rfidTag);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Could not send RFID early arrival denial to ESP32: {ex.Message}");
                        }
                    });
                }
                
                Console.WriteLine("=== EARLY ARRIVAL RFID SCAN END ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error recording early arrival: {ex.Message}");
                this.Invoke(new Action(() => {
                    SetRfidStatusText($"❌ Early arrival error: {ex.Message}");
                    AddRfidAttendanceRecord(userName, "Early Arrival Error", ex.Message);
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
                string displayMessage = null;
                if (!string.IsNullOrEmpty(action) && action.IndexOf("Early Arrival", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    displayMessage = "Early Arrival recorded. Scan inside at start.";
                }
                var payload = new
                {
                    rfid_data = rfidData,
                    user = $"{user.FirstName} {user.LastName}",
                    userType = user.UserType?.ToLower(),
                    sessionActive = currentSessionState == AttendanceSessionState.ActiveForStudents || 
                                   currentSessionState == AttendanceSessionState.ActiveForSignOut,
                    message = displayMessage
                };

                var json = JsonSerializer.Serialize(payload);
                Console.WriteLine($"RFID Payload: {json}");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    
                    // Add API key to request header for security
                    string apiKey = "0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567"; // Must match ESP32 API key
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

        // Send intermediate status update to ESP32 when waiting for second scan
        private async Task SendIntermediateStatusToESP32(
            User user, 
            string firstScanType, 
            string requiredScan,
            string endpoint)
        {
            try
            {
                Console.WriteLine($"=== SENDING INTERMEDIATE STATUS TO ESP32 ===");
                Console.WriteLine($"User: {user.FirstName} {user.LastName}");
                Console.WriteLine($"First Scan: {firstScanType}");
                Console.WriteLine($"Required: {requiredScan}");
                Console.WriteLine($"Endpoint: {endpoint}");

                // Auto-discover ESP32 on the network
                string esp32Ip = await DiscoverESP32();
                
                if (string.IsNullOrEmpty(esp32Ip))
                {
                    Console.WriteLine("❌ No ESP32 device found on network for intermediate status");
                    return;
                }

                string esp32Url = $"http://{esp32Ip}{endpoint}";
                Console.WriteLine($"Sending intermediate status to ESP32: {esp32Url}");

                // Create payload for intermediate status
                var payload = new
                {
                    user = $"{user.FirstName} {user.LastName}",
                    userType = user.UserType?.ToLower(),
                    sessionActive = currentSessionState == AttendanceSessionState.ActiveForStudents || 
                                   currentSessionState == AttendanceSessionState.ActiveForSignOut,
                    awaitingSecondScan = true,
                    firstScanType = firstScanType,
                    requiredScan = requiredScan
                };

                var json = JsonSerializer.Serialize(payload);
                Console.WriteLine($"Intermediate Status Payload: {json}");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    
                    // Add API key to request header for security
                    string apiKey = "0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567"; // Must match ESP32 API key
                    client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
                    Console.WriteLine($"Using API Key: {apiKey.Substring(0, 10)}...");
                    
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    var response = await client.PostAsync(esp32Url, content);
                    
                    Console.WriteLine($"ESP32 Intermediate Status Response: {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"ESP32 Intermediate Response: {responseContent}");
                        Console.WriteLine("✅ Intermediate status sent successfully");
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"❌ ESP32 intermediate status request failed: {response.StatusCode}");
                        Console.WriteLine($"Error: {errorContent}");
                    }
                }
                
                Console.WriteLine("=== INTERMEDIATE STATUS REQUEST END ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error sending intermediate status: {ex.Message}");
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
                
                Utils.Logger.Debug($"Scanning IPs: {string.Join(", ", likelyIPs.Select(ip => $"{networkPrefix}.{ip}"))}");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMilliseconds(500); // Fast timeout

                    foreach (int lastOctet in likelyIPs)
                    {
                        string testIp = $"{networkPrefix}.{lastOctet}";
                        
                        try
                        {
                            Utils.Logger.Debug($"Checking {testIp}...");
                            var response = await client.GetAsync($"http://{testIp}/api/health");
                            
                            if (response.IsSuccessStatusCode)
                            {
                                var content = await response.Content.ReadAsStringAsync();
                                
                                // Check if it's an ESP32 lock controller
                                if (content.Contains("ESP32") && content.Contains("Lock"))
                                {
                                    Utils.Logger.Success($"Found ESP32 at: {testIp}");
                                    discoveredESP32Devices[testIp] = testIp;
                                    lastDiscoveryTime = DateTime.Now;
                                    return testIp;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Ignore connection errors silently
                            Utils.Logger.Debug($"{testIp} - {ex.Message.Split('\n')[0]}");
                        }
                    }
                }

                Utils.Logger.Warning($"No ESP32 found in {networkPrefix}.x");
                return null;
            }
            catch (Exception ex)
            {
                Utils.Logger.Error($"Scan error: {ex.Message}");
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

        private void AutoRefreshTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Only refresh if on enrollment tab and not currently enrolling
                if (tabControl?.SelectedTab == enrollmentTab && !m_bEnrollmentInProgress && dbManager != null)
                {
                    // Refresh user list from database
                    cloudUsers = dbManager.LoadAllUsers();
                    LoadUsersIntoTable();
                    
                    // Optional: Log for debugging (can be removed)
                    // Console.WriteLine($"Auto-refreshed user list at {DateTime.Now:HH:mm:ss}");
                }
            }
            catch (Exception ex)
            {
                // Handle errors silently to prevent timer crashes
                Console.WriteLine($"Auto-refresh error: {ex.Message}");
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
            
            // QUALITY CHECK: Track valid finger placement
            // Accept OnPutOn as valid finger detection (SDK filters low-quality detections internally)
            hasValidFingerPlacement = true;
            lastValidPutOnTime = DateTime.Now;
            isFingerOnScanner = true;
            Console.WriteLine("👆 Finger detected on scanner - preventing repeated scans");
            SetStatusText("Attendance: Finger detected. Processing...");
        }

        private void OnAttendanceTakeOff(FTR_PROGRESS progress)
        {
            // Only process if we had a valid finger placement first
            // This prevents false positives from repeated OnTakeOff events
            if (hasValidFingerPlacement && isFingerOnScanner)
            {
                // Add a small delay before allowing next scan to prevent immediate re-scanning
                System.Threading.Tasks.Task.Delay(300).ContinueWith(_ =>
                {
                    isFingerOnScanner = false;
                    hasValidFingerPlacement = false; // Reset valid placement flag
                    Console.WriteLine("👆 Finger removed from scanner - ready for next scan");
                });
                SetStatusText("Attendance: Finger removed. Please scan again.");
            }
            // If no valid placement, silently ignore OnTakeOff (it's a false positive)
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
                        // INCREASED: Higher FARN for stricter matching to prevent false positives
                        m_AttendanceOperation.FARN = 150; // Increased from 100 to 150
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

        private void TabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (tabControl.SelectedTab == enrollmentTab)
                {
                    RefreshUserList();
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
            
            if (autoRefreshTimer != null)
            {
                autoRefreshTimer.Stop();
                autoRefreshTimer.Dispose();
                autoRefreshTimer = null;
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
        // Removed obsolete RFID button handlers - unified in attendance tab

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
                if (!m_bAttendanceActive) 
                {
                    return;
                }

                // Convert virtual key code to character
                char keyChar = ConvertVkCodeToChar(vkCode);
                
                if (keyChar != '\0')
                {
                    var currentTime = DateTime.Now;
                    
                    // RFID scanners type VERY fast (typically 10-30ms between characters)
                    // Human typing is typically 150-300ms between characters
                    bool isRapidInput = false;
                    if (lastRfidInput != DateTime.MinValue)
                    {
                        var timeSinceLastInput = currentTime - lastRfidInput;
                        isRapidInput = timeSinceLastInput.TotalMilliseconds < 50; // Stricter: 50ms threshold
                    }
                    
                    // Only start capturing if rapid input detected OR first character OR already capturing with substantial data
                    if (isRapidInput || lastRfidInput == DateTime.MinValue)
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
                    else if (rfidCapturing && rfidBuffer.Length >= 8)
                    {
                        // Already capturing and have significant data - continue capturing
                        if (rfidInputTimer != null)
                        {
                            rfidInputTimer.Stop();
                            rfidInputTimer.Start();
                        }
                        
                        rfidBuffer += keyChar;
                        lastRfidInput = currentTime;
                    }
                    else
                    {
                        // Not rapid input and not already capturing - ignore as keyboard typing
                        rfidBuffer = "";
                        rfidCapturing = false;
                        lastRfidInput = DateTime.MinValue;
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
                    var trimmed = rfidBuffer.Trim();
                    
                    // RFID should be numeric; most readers send 10 digits, but allow 8-12 to be safe
                    if (trimmed.All(char.IsDigit) && trimmed.Length >= 8 && trimmed.Length <= 12)
                    {
                        // Valid RFID format - process it
                        ProcessRfidScan(trimmed);
                    }
                    else
                    {
                        // Invalid RFID format - likely keyboard typing
                        Console.WriteLine($"Rejected invalid RFID: '{trimmed}' (length: {trimmed.Length})");
                    }
                    
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
                // RFID-only/Fingerprint-only gating
                if (deviceConfig?.AllowFingerprintOnly == true)
                {
                    SetRfidStatusText("Fingerprint-only mode: please use the fingerprint scanner.");
                    Console.WriteLine($"⚠️ RFID scan blocked - Fingerprint-only mode active. RFID: {rfidData}. Sending OLED denial warning...");
                    _ = ShowModeRestrictionWarningAsync(false, rfidData);
                    // Reset RFID capture state to allow subsequent scans
                    rfidBuffer = "";
                    rfidCapturing = false;
                    lastRfidInput = DateTime.MinValue;
                    if (rfidInputTimer != null)
                    {
                        rfidInputTimer.Stop();
                    }
                    return;
                }
                // Update currentScanLocation based on current device configuration
                // This ensures RFID scans use the correct location even after reconfiguration
                if (isDualSensorMode && deviceConfig != null)
                {
                    bool hasInsideSensor = deviceConfig?.InsideSensor != null && m_InsideSensorEnabled;
                    bool hasOutsideSensor = deviceConfig?.OutsideSensor != null && m_OutsideSensorEnabled;
                    
                    if (hasOutsideSensor && !hasInsideSensor)
                    {
                        currentScanLocation = "outside";
                        Console.WriteLine($"📍 RFID: Only outside sensor configured: location set to outside");
                    }
                    else if (hasInsideSensor && !hasOutsideSensor)
                    {
                        currentScanLocation = "inside";
                        Console.WriteLine($"📍 RFID: Only inside sensor configured: location set to inside");
                    }
                    else
                    {
                        // Both configured or unknown, keep current location
                        Console.WriteLine($"📍 RFID: Both sensors configured or unknown: keeping current location {currentScanLocation}");
                    }
                }
                
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
                    
                    // Log unknown RFID scan to ACCESSLOGS table
                    try
                    {
                        if (dbManager != null)
                        {
                            dbManager.LogAccessAttempt(
                                userId: null, // NULL for unknown user
                                roomId: null, // Will use CurrentRoomId from DatabaseManager
                                authMethod: "RFID",
                                location: currentScanLocation ?? "inside",
                                accessType: "attendance_scan",
                                result: "denied",
                                reason: $"Unknown RFID card: {rfidData}"
                            );
                            Console.WriteLine($"📝 Logged unknown RFID scan to ACCESSLOGS: {rfidData}");
                        }
                    }
                    catch (Exception logEx)
                    {
                        Console.WriteLine($"⚠️ Failed to log unknown RFID scan: {logEx.Message}");
                        // Continue even if logging fails
                    }
                    
                    // Notify ESP32 to show 'No match' on OLED (RFID path)
                    _ = System.Threading.Tasks.Task.Run(async () => {
                        try { await RequestRfidNoMatchDisplay(rfidData); } catch { }
                    });
                    return;
                }
                
                Console.WriteLine($"✅ RFID found: {userInfo.Username} ({userInfo.UserType})");
                
                string userType = userInfo.UserType?.ToLower();
                
                // Route based on user type and session state (same logic as fingerprint system)
                if (userType == "instructor")
                {
                    // Note: Security check for session ownership is now handled within each session state
                    // to allow door access for instructors with no scheduled class while preventing session control interference
                    
                    // Instructor actions based on unified session state
                    switch (currentSessionState)
                    {
                        case AttendanceSessionState.Inactive:
                        case AttendanceSessionState.WaitingForInstructor:
                            // CRITICAL: Check for verification timeout first
                            if (awaitingCrossTypeVerification)
                            {
                                var verificationElapsed = DateTime.Now - crossVerificationStartTime;
                                if (verificationElapsed.TotalSeconds > CROSS_VERIFICATION_TIMEOUT_SECONDS)
                                {
                                    // Timeout - reset verification state
                                    Console.WriteLine($"⏱️ INSTRUCTOR RFID VERIFICATION TIMEOUT: {pendingCrossVerificationUser} took too long");
                                    SetRfidStatusText($"⏱️ Verification timeout for {pendingCrossVerificationUser}. Starting over...");
                                    awaitingCrossTypeVerification = false;
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    firstScanType = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                    // Don't return - treat this as a new first scan
                                }
                            }
                            
                            // Check if completing fingerprint-first verification
                            if (awaitingCrossTypeVerification && firstScanType == "FINGERPRINT")
                            {
                                // Fingerprint was scanned first, now verifying with RFID
                                if (userInfo.Username == pendingCrossVerificationUser && userInfo.EmployeeId == pendingCrossVerificationGuid)
                                {
                                    Console.WriteLine($"✅ INSTRUCTOR VERIFIED: {userInfo.Username} (Fingerprint + RFID match)");
                                    SetRfidStatusText($"✅ Verified: {userInfo.Username}. Starting session...");
                                    
                                    // Check if instructor has scheduled class, if not and door access is enabled, grant door access only
                                    var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userInfo.EmployeeId);
                                    if (scheduleValidation == null || !scheduleValidation.IsValid || string.IsNullOrEmpty(scheduleValidation.ScheduleId))
                                    {
                                        // No scheduled class - check if door access is allowed
                                        if (deviceConfig?.AllowInstructorDoorAccess == true)
                                        {
                                            Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (RFID): {userInfo.Username} - No scheduled class, door access granted");
                                            SetRfidStatusText($"🚪 Instructor {userInfo.Username} - Door access granted (no scheduled class).");
                                            AddRfidAttendanceRecord(userInfo.Username, "Door Access", "Instructor Door Access (No Schedule)");
                                            
                                            // Log successful door access to ACCESSLOGS
                                            if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                                            {
                                                try
                                                {
                                                    dbManager.LogAccessAttempt(
                                                        userId: userInfo.EmployeeId,
                                                        roomId: null,
                                                        authMethod: "RFID",
                                                        location: currentScanLocation ?? "inside",
                                                        accessType: "attendance_scan",
                                                        result: "success",
                                                        reason: "Instructor door access granted (no scheduled class)"
                                                    );
                                                    Console.WriteLine($"📝 Logged successful instructor door access to ACCESSLOGS for {userInfo.Username}");
                                                }
                                                catch (Exception logEx)
                                                {
                                                    Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                                }
                                            }
                                            
                                            // Also record attendance so it appears in web logs (ATTENDANCERECORDS)
                                            // Use "RFID Door Access" to indicate RFID authentication method
                                            RecordAttendance(userInfo.Username, "RFID Door Access", true, currentScanLocation);
                                            
                                            // Trigger door access
                                            _ = System.Threading.Tasks.Task.Run(async () => {
                                                try
                                                {
                                                    await RequestRfidLockControl(userInfo.EmployeeId, "Instructor Door Access", rfidData);
                                                }
                                                catch (Exception lockEx)
                                                {
                                                    Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                                }
                                            });
                                            
                                            // Reset verification state
                                            awaitingCrossTypeVerification = false;
                                            pendingCrossVerificationUser = "";
                                            pendingCrossVerificationGuid = "";
                                            firstScanType = "";
                                            crossVerificationStartTime = DateTime.MinValue;
                                            
                                            return;
                                        }
                                        else
                                        {
                                            // Door access not enabled - deny
                                            SetRfidStatusText($"❌ {userInfo.Username}: No scheduled class. Door access not enabled.");
                                            Console.WriteLine($"❌ INSTRUCTOR {userInfo.Username} DENIED: No scheduled class and door access not enabled");
                                            AddRfidAttendanceRecord(userInfo.Username, "Access Denied", "No scheduled class");
                                            
                                            // Log denied access attempt to ACCESSLOGS
                                            if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                                            {
                                                try
                                                {
                                                    dbManager.LogAccessAttempt(
                                                        userId: userInfo.EmployeeId,
                                                        roomId: null,
                                                        authMethod: "RFID",
                                                        location: currentScanLocation ?? "inside",
                                                        accessType: "attendance_scan",
                                                        result: "denied",
                                                        reason: "Instructor door access denied: No scheduled class and door access not enabled"
                                                    );
                                                    Console.WriteLine($"📝 Logged denied instructor access attempt to ACCESSLOGS for {userInfo.Username}");
                                                }
                                                catch (Exception logEx)
                                                {
                                                    Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                                }
                                            }
                                            
                                            // Reset verification state
                                            awaitingCrossTypeVerification = false;
                                            pendingCrossVerificationUser = "";
                                            pendingCrossVerificationGuid = "";
                                            firstScanType = "";
                                            crossVerificationStartTime = DateTime.MinValue;
                                            
                                            return;
                                        }
                                    }
                                    
                                    // Has scheduled class - proceed with session start
                                    if (!CompleteInstructorSessionStart(userInfo.Username, userInfo.EmployeeId, true, rfidData))
                                    {
                                        return;
                                    }
                                }
                                else
                                {
                                    // Mismatch
                                    Console.WriteLine($"❌ INSTRUCTOR VERIFICATION FAILED: Fingerprint={pendingCrossVerificationUser}, RFID={userInfo.Username}");
                                    SetRfidStatusText($"❌ Verification failed! Fingerprint: {pendingCrossVerificationUser}, RFID: {userInfo.Username}. Please try again.");
                                    
                                    // Reset verification state
                                    awaitingCrossTypeVerification = false;
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    firstScanType = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                }
                            }
                            else if (awaitingCrossTypeVerification && firstScanType == "RFID")
                            {
                                if (IsDualAuthRequired)
                                {
                                    Console.WriteLine($"⏳ Already waiting for fingerprint verification. Please scan fingerprint.");
                                    SetRfidStatusText($"⏳ Waiting for fingerprint verification. Please scan fingerprint to start session.");
                                }
                                else
                                {
                                    // RFID-only mode - check schedule first
                                    var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userInfo.EmployeeId);
                                    if (scheduleValidation == null || !scheduleValidation.IsValid || string.IsNullOrEmpty(scheduleValidation.ScheduleId))
                                    {
                                        // No scheduled class - check if door access is allowed
                                        if (deviceConfig?.AllowInstructorDoorAccess == true)
                                        {
                                            Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (RFID): {userInfo.Username} - No scheduled class, door access granted");
                                            SetRfidStatusText($"🚪 Instructor {userInfo.Username} - Door access granted (no scheduled class).");
                                            AddRfidAttendanceRecord(userInfo.Username, "Door Access", "Instructor Door Access (No Schedule)");
                                            
                                            // Log successful door access to ACCESSLOGS
                                            if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                                            {
                                                try
                                                {
                                                    dbManager.LogAccessAttempt(
                                                        userId: userInfo.EmployeeId,
                                                        roomId: null,
                                                        authMethod: "RFID",
                                                        location: currentScanLocation ?? "inside",
                                                        accessType: "attendance_scan",
                                                        result: "success",
                                                        reason: "Instructor door access granted (no scheduled class)"
                                                    );
                                                    Console.WriteLine($"📝 Logged successful instructor door access to ACCESSLOGS for {userInfo.Username}");
                                                }
                                                catch (Exception logEx)
                                                {
                                                    Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                                }
                                            }
                                            
                                            // Trigger door access
                                            _ = System.Threading.Tasks.Task.Run(async () => {
                                                try
                                                {
                                                    await RequestRfidLockControl(userInfo.EmployeeId, "Instructor Door Access", rfidData);
                                                }
                                                catch (Exception lockEx)
                                                {
                                                    Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                                }
                                            });
                                            
                                            return;
                                        }
                                        else
                                        {
                                            // Door access not enabled - deny
                                            SetRfidStatusText($"❌ {userInfo.Username}: No scheduled class. Door access not enabled.");
                                            Console.WriteLine($"❌ INSTRUCTOR {userInfo.Username} DENIED: No scheduled class and door access not enabled");
                                            AddRfidAttendanceRecord(userInfo.Username, "Access Denied", "No scheduled class");
                                            
                                            // Log denied access attempt to ACCESSLOGS
                                            if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                                            {
                                                try
                                                {
                                                    dbManager.LogAccessAttempt(
                                                        userId: userInfo.EmployeeId,
                                                        roomId: null,
                                                        authMethod: "RFID",
                                                        location: currentScanLocation ?? "inside",
                                                        accessType: "attendance_scan",
                                                        result: "denied",
                                                        reason: "Instructor door access denied: No scheduled class and door access not enabled"
                                                    );
                                                    Console.WriteLine($"📝 Logged denied instructor access attempt to ACCESSLOGS for {userInfo.Username}");
                                                }
                                                catch (Exception logEx)
                                                {
                                                    Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                                }
                                            }
                                            
                                            return;
                                        }
                                    }
                                    
                                    // Has scheduled class - proceed with session start
                                    Console.WriteLine($"✅ RFID-only mode active - completing instructor session start for {userInfo.Username}");
                                    if (!CompleteInstructorSessionStart(userInfo.Username, userInfo.EmployeeId, true, rfidData))
                                    {
                                        return;
                                    }
                                }
                            }
                            else
                            {
                                if (IsDualAuthRequired)
                                {
                                    // Start dual-auth: RFID first, wait for fingerprint
                                    Console.WriteLine($"🔍 INSTRUCTOR RFID: {userInfo.Username} - Waiting for fingerprint");
                                    SetRfidStatusText($"Instructor RFID: {userInfo.Username}. Please scan fingerprint to start session.");
                                    
                                    awaitingCrossTypeVerification = true;
                                    firstScanType = "RFID";
                                    pendingCrossVerificationUser = userInfo.Username;
                                    pendingCrossVerificationGuid = userInfo.EmployeeId;
                                    crossVerificationStartTime = DateTime.Now;
                                    AddRfidAttendanceRecord(userInfo.Username, "Waiting for Fingerprint", "RFID First");
                                    
                                    // Send intermediate status to ESP32
                                    User user;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userInfo.EmployeeId, out user))
                                    {
                                        _ = Task.Run(async () => await SendIntermediateStatusToESP32(user, "RFID", "FINGERPRINT", "/api/rfid-scan"));
                                    }
                                }
                                else
                                {
                                    // RFID-only mode - check schedule first
                                    var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userInfo.EmployeeId);
                                    if (scheduleValidation == null || !scheduleValidation.IsValid || string.IsNullOrEmpty(scheduleValidation.ScheduleId))
                                    {
                                        // No scheduled class - check if door access is allowed
                                        if (deviceConfig?.AllowInstructorDoorAccess == true)
                                        {
                                            Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (RFID): {userInfo.Username} - No scheduled class, door access granted");
                                            SetRfidStatusText($"🚪 Instructor {userInfo.Username} - Door access granted (no scheduled class).");
                                            AddRfidAttendanceRecord(userInfo.Username, "Door Access", "Instructor Door Access (No Schedule)");
                                            
                                            // Log successful door access to ACCESSLOGS
                                            if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                                            {
                                                try
                                                {
                                                    dbManager.LogAccessAttempt(
                                                        userId: userInfo.EmployeeId,
                                                        roomId: null,
                                                        authMethod: "RFID",
                                                        location: currentScanLocation ?? "inside",
                                                        accessType: "attendance_scan",
                                                        result: "success",
                                                        reason: "Instructor door access granted (no scheduled class)"
                                                    );
                                                    Console.WriteLine($"📝 Logged successful instructor door access to ACCESSLOGS for {userInfo.Username}");
                                                }
                                                catch (Exception logEx)
                                                {
                                                    Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                                }
                                            }
                                            
                                            // Also record attendance so it appears in web logs (ATTENDANCERECORDS)
                                            // Use "RFID Door Access" to indicate RFID authentication method
                                            RecordAttendance(userInfo.Username, "RFID Door Access", true, currentScanLocation);
                                            
                                            // Trigger door access
                                            _ = System.Threading.Tasks.Task.Run(async () => {
                                                try
                                                {
                                                    await RequestRfidLockControl(userInfo.EmployeeId, "Instructor Door Access", rfidData);
                                                }
                                                catch (Exception lockEx)
                                                {
                                                    Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                                }
                                            });
                                            
                                            return;
                                        }
                                        else
                                        {
                                            // Door access not enabled - deny
                                            SetRfidStatusText($"❌ {userInfo.Username}: No scheduled class. Door access not enabled.");
                                            Console.WriteLine($"❌ INSTRUCTOR {userInfo.Username} DENIED: No scheduled class and door access not enabled");
                                            AddRfidAttendanceRecord(userInfo.Username, "Access Denied", "No scheduled class");
                                            
                                            // Log denied access attempt to ACCESSLOGS
                                            if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                                            {
                                                try
                                                {
                                                    dbManager.LogAccessAttempt(
                                                        userId: userInfo.EmployeeId,
                                                        roomId: null,
                                                        authMethod: "RFID",
                                                        location: currentScanLocation ?? "inside",
                                                        accessType: "attendance_scan",
                                                        result: "denied",
                                                        reason: "Instructor door access denied: No scheduled class and door access not enabled"
                                                    );
                                                    Console.WriteLine($"📝 Logged denied instructor access attempt to ACCESSLOGS for {userInfo.Username}");
                                                }
                                                catch (Exception logEx)
                                                {
                                                    Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                                }
                                            }
                                            
                                            return;
                                        }
                                    }
                                    
                                    // Has scheduled class - proceed with session start
                                    Console.WriteLine($"✅ RFID-only mode active - completing instructor session start for {userInfo.Username}");
                                    if (!CompleteInstructorSessionStart(userInfo.Username, userInfo.EmployeeId, true, rfidData))
                                    {
                                        return;
                                    }
                                }
                            }
                            break;
                            
                        case AttendanceSessionState.ActiveForStudents:
                            // CRITICAL: Check for verification timeout first
                            if (awaitingCrossTypeVerification)
                            {
                                var verificationElapsed = DateTime.Now - crossVerificationStartTime;
                                if (verificationElapsed.TotalSeconds > CROSS_VERIFICATION_TIMEOUT_SECONDS)
                                {
                                    // Timeout - reset verification state
                                    Console.WriteLine($"⏱️ INSTRUCTOR RFID SIGN-OUT VERIFICATION TIMEOUT: {pendingCrossVerificationUser} took too long");
                                    SetRfidStatusText($"⏱️ Verification timeout for {pendingCrossVerificationUser}. Starting over...");
                                    awaitingCrossTypeVerification = false;
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    firstScanType = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                    // Don't return - treat this as a new first scan
                                }
                            }
                            else if (!IsDualAuthRequired)
                            {
                                // SECURITY CHECK: If not session owner, check if door access is allowed
                                if (!string.IsNullOrEmpty(currentInstructorId) && userInfo.EmployeeId != currentInstructorId)
                                {
                                    // Different instructor - check if they have scheduled class
                                    var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userInfo.EmployeeId);
                                    bool hasScheduledClass = scheduleValidation != null && scheduleValidation.IsValid && !string.IsNullOrEmpty(scheduleValidation.ScheduleId);
                                    
                                    if (hasScheduledClass)
                                    {
                                        // Has scheduled class but different instructor owns session - deny (interference prevention)
                                        string sessionOwnerName = userInfo.Username;
                                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                        {
                                            sessionOwnerName = sessionOwner.Username;
                                        }
                                        
                                        Console.WriteLine($"⚠️ SECURITY: Instructor {userInfo.Username} (has scheduled class) attempted to access session owned by {sessionOwnerName}");
                                        SetRfidStatusText($"⚠️ Session is active for {sessionOwnerName}. Only the session owner can perform actions.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Access Denied", $"Session owned by {sessionOwnerName}");
                                        
                                        // Log this security event
                                        if (dbManager != null)
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "denied",
                                                    reason: $"Attempted to access session owned by different instructor (Session Owner: {sessionOwnerName})"
                                                );
                                                Console.WriteLine($"📝 Logged unauthorized instructor access attempt to ACCESSLOGS for {userInfo.Username}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Notify ESP32 of denial
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControlDenial(userInfo.EmployeeId, userInfo.Username, $"Session owned by {sessionOwnerName}", "instructor", rfidData);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                    else if (deviceConfig?.AllowInstructorDoorAccess == true)
                                    {
                                        // No scheduled class but door access enabled - allow door access only
                                        Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (RFID-only mode): {userInfo.Username} - Door access granted (no schedule, different session active)");
                                        SetRfidStatusText($"🚪 Instructor {userInfo.Username} - Door access granted. Session remains active for students.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Door Access", "Instructor Door Access (No Schedule)");
                                        
                                        // Log door access to ACCESSLOGS
                                        if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "success",
                                                    reason: "Instructor door access granted (no scheduled class, different session active, RFID-only mode)"
                                                );
                                                Console.WriteLine($"📝 Logged instructor door access to ACCESSLOGS for {userInfo.Username}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Record attendance for door access
                                        RecordAttendance(userInfo.Username, "RFID Door Access", true, currentScanLocation);
                                        
                                        // Trigger door access without changing session state
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControl(userInfo.EmployeeId, "Instructor Door Access (No Schedule)", rfidData);
                                            }
                                            catch (Exception lockEx)
                                            {
                                                Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                    else
                                    {
                                        // No scheduled class and door access not enabled - deny
                                        string sessionOwnerName = userInfo.Username;
                                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                        {
                                            sessionOwnerName = sessionOwner.Username;
                                        }
                                        
                                        Console.WriteLine($"⚠️ SECURITY: Instructor {userInfo.Username} attempted to open sign-out for session owned by {sessionOwnerName}");
                                        SetRfidStatusText($"⚠️ Only the session owner ({sessionOwnerName}) can open sign-out.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Access Denied", $"Session owned by {sessionOwnerName}");
                                        
                                        // Log security event
                                        if (dbManager != null)
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "denied",
                                                    reason: $"Attempted to open sign-out for session owned by different instructor. No scheduled class and door access not enabled. (Session Owner: {sessionOwnerName})"
                                                );
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Notify ESP32 of denial
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControlDenial(userInfo.EmployeeId, userInfo.Username, $"Session owned by {sessionOwnerName}", "instructor", rfidData);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                }
                                
                                Console.WriteLine($"✅ RFID-only mode active - opening sign-out for {userInfo.Username}");

                                awaitingCrossTypeVerification = false;
                                firstScanType = "";
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                crossVerificationStartTime = DateTime.MinValue;

                                currentSessionState = AttendanceSessionState.ActiveForSignOut;
                                UpdateSessionStateDisplay();
                                SetRfidStatusText($"✅ Instructor {userInfo.Username} opened sign-out. Students can now sign out.");
                                AddRfidAttendanceRecord(userInfo.Username, "Sign-Out Opened", "Active");
                                return;
                            }
                            
                            // Check if completing fingerprint-first verification for sign-out
                            if (awaitingCrossTypeVerification && firstScanType == "FINGERPRINT")
                            {
                                // Fingerprint was scanned first, now verifying with RFID
                                if (userInfo.Username == pendingCrossVerificationUser && userInfo.EmployeeId == pendingCrossVerificationGuid)
                                {
                                    // SECURITY CHECK: Only session owner can open sign-out
                                    if (!string.IsNullOrEmpty(currentInstructorId) && userInfo.EmployeeId != currentInstructorId)
                                    {
                                        string sessionOwnerName = userInfo.Username;
                                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                        {
                                            sessionOwnerName = sessionOwner.Username;
                                        }
                                        
                                        Console.WriteLine($"⚠️ SECURITY: Instructor {userInfo.Username} attempted to open sign-out for session owned by {sessionOwnerName}");
                                        SetRfidStatusText($"⚠️ Only the session owner ({sessionOwnerName}) can open sign-out.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Access Denied", $"Session owned by {sessionOwnerName}");
                                        
                                        // Reset verification state
                                        awaitingCrossTypeVerification = false;
                                        firstScanType = "";
                                        pendingCrossVerificationUser = "";
                                        pendingCrossVerificationGuid = "";
                                        crossVerificationStartTime = DateTime.MinValue;
                                        
                                        // Log security event
                                        if (dbManager != null)
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "denied",
                                                    reason: $"Attempted to open sign-out for session owned by different instructor (Session Owner: {sessionOwnerName})"
                                                );
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Notify ESP32 of denial
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControlDenial(userInfo.EmployeeId, userInfo.Username, $"Session owned by {sessionOwnerName}", "instructor", rfidData);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                    
                                    // Verified! Reset state and proceed with sign-out
                                    Console.WriteLine($"✅ INSTRUCTOR VERIFIED FOR SIGN-OUT: {userInfo.Username} (Fingerprint + RFID match)");
                                    SetRfidStatusText($"✅ Verified: {userInfo.Username}. Opening sign-out...");
                                    
                                    awaitingCrossTypeVerification = false;
                                    firstScanType = "";
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                    
                                    // Now proceed with sign-out INLINE
                                    currentSessionState = AttendanceSessionState.ActiveForSignOut;
                                    UpdateSessionStateDisplay();
                                    SetRfidStatusText($"✅ Instructor {userInfo.Username} opened sign-out. Students can now sign out.");
                                    AddRfidAttendanceRecord(userInfo.Username, "Sign-Out Opened", "Active");
                                }
                                else
                                {
                                    // Mismatch
                                    Console.WriteLine($"❌ INSTRUCTOR VERIFICATION FAILED: Fingerprint={pendingCrossVerificationUser}, RFID={userInfo.Username}");
                                    SetRfidStatusText($"❌ Verification failed! Fingerprint: {pendingCrossVerificationUser}, RFID: {userInfo.Username}. Please try again.");
                                    
                                    awaitingCrossTypeVerification = false;
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    firstScanType = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                }
                            }
                            else if (awaitingCrossTypeVerification && firstScanType == "RFID")
                            {
                                // Already waiting for fingerprint from previous RFID scan
                                SetRfidStatusText($"⏳ Waiting for fingerprint verification. Please scan fingerprint to open sign-out.");
                            }
                            else
                            {
                                // SECURITY CHECK: If not session owner, check if door access is allowed
                                if (!string.IsNullOrEmpty(currentInstructorId) && userInfo.EmployeeId != currentInstructorId)
                                {
                                    // Different instructor - check if they have scheduled class
                                    var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userInfo.EmployeeId);
                                    bool hasScheduledClass = scheduleValidation != null && scheduleValidation.IsValid && !string.IsNullOrEmpty(scheduleValidation.ScheduleId);
                                    
                                    if (hasScheduledClass)
                                    {
                                        // Has scheduled class but different instructor owns session - deny (interference prevention)
                                        string sessionOwnerName = userInfo.Username;
                                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                        {
                                            sessionOwnerName = sessionOwner.Username;
                                        }
                                        
                                        Console.WriteLine($"⚠️ SECURITY: Instructor {userInfo.Username} (has scheduled class) attempted to access session owned by {sessionOwnerName}");
                                        SetRfidStatusText($"⚠️ Session is active for {sessionOwnerName}. Only the session owner can perform actions.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Access Denied", $"Session owned by {sessionOwnerName}");
                                        
                                        // Log this security event
                                        if (dbManager != null)
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "denied",
                                                    reason: $"Attempted to access session owned by different instructor (Session Owner: {sessionOwnerName})"
                                                );
                                                Console.WriteLine($"📝 Logged unauthorized instructor access attempt to ACCESSLOGS for {userInfo.Username}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Notify ESP32 of denial
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControlDenial(userInfo.EmployeeId, userInfo.Username, $"Session owned by {sessionOwnerName}", "instructor", rfidData);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                    else if (deviceConfig?.AllowInstructorDoorAccess == true)
                                    {
                                        // No scheduled class but door access enabled - allow door access only
                                        Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (RFID): {userInfo.Username} - Door access granted (no schedule, different session active)");
                                        SetRfidStatusText($"🚪 Instructor {userInfo.Username} - Door access granted. Session remains active for students.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Door Access", "Instructor Door Access (No Schedule)");
                                        
                                        // Log door access to ACCESSLOGS
                                        if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "success",
                                                    reason: "Instructor door access granted (no scheduled class, different session active)"
                                                );
                                                Console.WriteLine($"📝 Logged instructor door access to ACCESSLOGS for {userInfo.Username}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Record attendance for door access
                                        RecordAttendance(userInfo.Username, "RFID Door Access", true, currentScanLocation);
                                        
                                        // Trigger door access without changing session state
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControl(userInfo.EmployeeId, "Instructor Door Access (No Schedule)", rfidData);
                                            }
                                            catch (Exception lockEx)
                                            {
                                                Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                    else
                                    {
                                        // No scheduled class and door access not enabled - deny
                                        string sessionOwnerName = userInfo.Username;
                                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                        {
                                            sessionOwnerName = sessionOwner.Username;
                                        }
                                        
                                        Console.WriteLine($"⚠️ SECURITY: Instructor {userInfo.Username} (no schedule) attempted to access session owned by {sessionOwnerName}");
                                        SetRfidStatusText($"⚠️ Session is active for {sessionOwnerName}. Door access not enabled.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Access Denied", $"Session owned by {sessionOwnerName}");
                                        
                                        // Log denied access
                                        if (dbManager != null)
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "denied",
                                                    reason: $"Attempted to access session owned by different instructor. No scheduled class and door access not enabled."
                                                );
                                                Console.WriteLine($"📝 Logged denied instructor access attempt to ACCESSLOGS for {userInfo.Username}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Notify ESP32 of denial
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControlDenial(userInfo.EmployeeId, userInfo.Username, $"Session owned by {sessionOwnerName}", "instructor", rfidData);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                }
                                
                                // Session owner or no active session - proceed with normal flow
                                // NEW LOGIC: During active session, single scan grants door access without changing state
                                if (IsDualAuthRequired)
                                {
                                    Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (RFID): {userInfo.Username} - Door access granted, session remains active");
                                    SetRfidStatusText($"🚪 Instructor {userInfo.Username} - Door access granted. Session remains active for students.");
                                    AddRfidAttendanceRecord(userInfo.Username, "Door Access", "Instructor Door Access (Session Active)");
                                    
                                    // Log door access to ACCESSLOGS
                                    if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userInfo.EmployeeId,
                                                roomId: null,
                                                authMethod: "RFID",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "success",
                                                reason: "Instructor door access granted during active session"
                                            );
                                            Console.WriteLine($"📝 Logged instructor door access to ACCESSLOGS for {userInfo.Username}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                        }
                                    }
                                    
                                    // Record attendance for door access
                                    RecordAttendance(userInfo.Username, "RFID Door Access", true, currentScanLocation);
                                    
                                    // Trigger door access without changing session state
                                    _ = System.Threading.Tasks.Task.Run(async () => {
                                        try
                                        {
                                            await RequestRfidLockControl(userInfo.EmployeeId, "Instructor Door Access (Session Active)", rfidData);
                                        }
                                        catch (Exception lockEx)
                                        {
                                            Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                        }
                                    });
                                    
                                    // Start waiting for second scan to open sign-out
                                    awaitingCrossTypeVerification = true;
                                    firstScanType = "RFID";
                                    pendingCrossVerificationUser = userInfo.Username;
                                    pendingCrossVerificationGuid = userInfo.EmployeeId;
                                    crossVerificationStartTime = DateTime.Now;
                                    
                                    // Send intermediate status to ESP32
                                    User user;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userInfo.EmployeeId, out user))
                                    {
                                        _ = Task.Run(async () => await SendIntermediateStatusToESP32(user, "RFID", "FINGERPRINT", "/api/rfid-scan"));
                                    }
                                }
                                else
                                {
                                    // RFID-only mode: This shouldn't happen in ActiveForStudents state with single auth
                                    // but keeping original logic as fallback
                                    Console.WriteLine($"🔍 INSTRUCTOR RFID: {userInfo.Username} - Waiting for fingerprint to open sign-out");
                                    SetRfidStatusText($"Instructor RFID: {userInfo.Username}. Please scan fingerprint to open sign-out.");
                                    
                                    awaitingCrossTypeVerification = true;
                                    firstScanType = "RFID";
                                    pendingCrossVerificationUser = userInfo.Username;
                                    pendingCrossVerificationGuid = userInfo.EmployeeId;
                                    crossVerificationStartTime = DateTime.Now;
                                    AddRfidAttendanceRecord(userInfo.Username, "Waiting for Fingerprint", "RFID First");
                                    
                                    // Send intermediate status to ESP32
                                    User user;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userInfo.EmployeeId, out user))
                                    {
                                        _ = Task.Run(async () => await SendIntermediateStatusToESP32(user, "RFID", "FINGERPRINT", "/api/rfid-scan"));
                                    }
                                }
                            }
                            break;
                            
                        case AttendanceSessionState.ActiveForSignOut:
                        case AttendanceSessionState.WaitingForInstructorClose:
                            // CRITICAL: Check for verification timeout first
                            if (awaitingCrossTypeVerification)
                            {
                                var verificationElapsed = DateTime.Now - crossVerificationStartTime;
                                if (verificationElapsed.TotalSeconds > CROSS_VERIFICATION_TIMEOUT_SECONDS)
                                {
                                    // Timeout - reset verification state
                                    Console.WriteLine($"⏱️ INSTRUCTOR RFID SESSION-END VERIFICATION TIMEOUT: {pendingCrossVerificationUser} took too long");
                                    SetRfidStatusText($"⏱️ Verification timeout for {pendingCrossVerificationUser}. Starting over...");
                                    awaitingCrossTypeVerification = false;
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    firstScanType = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                    // Don't return - treat this as a new first scan
                                }
                            }
                            else if (!IsDualAuthRequired)
                            {
                                // RFID-only mode
                                // SECURITY CHECK: If not session owner, check if door access is allowed
                                if (!string.IsNullOrEmpty(currentInstructorId) && userInfo.EmployeeId != currentInstructorId)
                                {
                                    // Different instructor - check if they have scheduled class
                                    var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userInfo.EmployeeId);
                                    bool hasScheduledClass = scheduleValidation != null && scheduleValidation.IsValid && !string.IsNullOrEmpty(scheduleValidation.ScheduleId);
                                    
                                    if (hasScheduledClass)
                                    {
                                        // Has scheduled class but different instructor owns session - deny (interference prevention)
                                        string sessionOwnerName = userInfo.Username;
                                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                        {
                                            sessionOwnerName = sessionOwner.Username;
                                        }
                                        
                                        Console.WriteLine($"⚠️ SECURITY: Instructor {userInfo.Username} (has scheduled class) attempted to access session owned by {sessionOwnerName}");
                                        SetRfidStatusText($"⚠️ Session is active for {sessionOwnerName}. Only the session owner can perform actions.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Access Denied", $"Session owned by {sessionOwnerName}");
                                        
                                        // Log this security event
                                        if (dbManager != null)
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "denied",
                                                    reason: $"Attempted to access session owned by different instructor (Session Owner: {sessionOwnerName})"
                                                );
                                                Console.WriteLine($"📝 Logged unauthorized instructor access attempt to ACCESSLOGS for {userInfo.Username}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Notify ESP32 of denial
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControlDenial(userInfo.EmployeeId, userInfo.Username, $"Session owned by {sessionOwnerName}", "instructor", rfidData);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                    else if (deviceConfig?.AllowInstructorDoorAccess == true)
                                    {
                                        // No scheduled class but door access enabled - allow door access only
                                        Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (RFID-only mode, sign-out state): {userInfo.Username} - Door access granted (no schedule, different session active)");
                                        SetRfidStatusText($"🚪 Instructor {userInfo.Username} - Door access granted. Sign-out session remains active.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Door Access", "Instructor Door Access (No Schedule)");
                                        
                                        // Log door access to ACCESSLOGS
                                        if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "success",
                                                    reason: "Instructor door access granted (no scheduled class, different session active, RFID-only mode, sign-out state)"
                                                );
                                                Console.WriteLine($"📝 Logged instructor door access to ACCESSLOGS for {userInfo.Username}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Record attendance for door access
                                        RecordAttendance(userInfo.Username, "RFID Door Access", true, currentScanLocation);
                                        
                                        // Trigger door access without ending session
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControl(userInfo.EmployeeId, "Instructor Door Access (No Schedule)", rfidData);
                                            }
                                            catch (Exception lockEx)
                                            {
                                                Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                    else
                                    {
                                        // No scheduled class and door access not enabled - deny
                                        string sessionOwnerName = userInfo.Username;
                                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                        {
                                            sessionOwnerName = sessionOwner.Username;
                                        }
                                        
                                        Console.WriteLine($"⚠️ SECURITY: Instructor {userInfo.Username} attempted to end session owned by {sessionOwnerName}");
                                        SetRfidStatusText($"⚠️ Only the session owner ({sessionOwnerName}) can end the session.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Access Denied", $"Session owned by {sessionOwnerName}");
                                        
                                        // Log security event
                                        if (dbManager != null)
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "denied",
                                                    reason: $"Attempted to end session owned by different instructor. No scheduled class and door access not enabled. (Session Owner: {sessionOwnerName})"
                                                );
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Notify ESP32 of denial
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControlDenial(userInfo.EmployeeId, userInfo.Username, $"Session owned by {sessionOwnerName}", "instructor", rfidData);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                }
                                
                                // Session owner - proceed with ending session
                                Console.WriteLine($"✅ RFID-only mode active - ending session for {userInfo.Username}");

                                awaitingCrossTypeVerification = false;
                                firstScanType = "";
                                pendingCrossVerificationUser = "";
                                pendingCrossVerificationGuid = "";
                                crossVerificationStartTime = DateTime.MinValue;

                                if (signedInStudentGuids.Count > 0)
                                {
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

                                    await System.Threading.Tasks.Task.Delay(3000);
                                }

                                currentSessionState = AttendanceSessionState.Inactive;
                                currentInstructorId = null;
                                currentScheduleId = null;
                                currentSubjectName = null;
                                currentInstructorName = null;

                                signedInStudentGuids.Clear();
                                signedOutStudentGuids.Clear();

                                UpdateSessionStateDisplay();
                                SetRfidStatusText($"✅ Instructor {userInfo.Username} closed RFID session.");
                                AddRfidAttendanceRecord(userInfo.Username, "Session Closed", "Inactive");

                                _ = System.Threading.Tasks.Task.Run(async () =>
                                {
                                    try
                                    {
                                        RecordAttendance(userInfo.Username, "Instructor Sign-Out (Session End)");
                                        await RequestRfidLockControl(userInfo.EmployeeId, "Instructor Sign-Out (Session End)", rfidData);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Warning: Could not record instructor sign-out: {ex.Message}");
                                    }
                                });

                                return;
                            }
                            
                            // Check if completing fingerprint-first verification for session-end
                            if (awaitingCrossTypeVerification && firstScanType == "FINGERPRINT")
                            {
                                // Fingerprint was scanned first, now verifying with RFID
                                if (userInfo.Username == pendingCrossVerificationUser && userInfo.EmployeeId == pendingCrossVerificationGuid)
                                {
                                    // SECURITY CHECK: Only session owner can end session
                                    if (!string.IsNullOrEmpty(currentInstructorId) && userInfo.EmployeeId != currentInstructorId)
                                    {
                                        string sessionOwnerName = userInfo.Username;
                                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                        {
                                            sessionOwnerName = sessionOwner.Username;
                                        }
                                        
                                        Console.WriteLine($"⚠️ SECURITY: Instructor {userInfo.Username} attempted to end session owned by {sessionOwnerName}");
                                        SetRfidStatusText($"⚠️ Only the session owner ({sessionOwnerName}) can end the session.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Access Denied", $"Session owned by {sessionOwnerName}");
                                        
                                        // Reset verification state
                                        awaitingCrossTypeVerification = false;
                                        firstScanType = "";
                                        pendingCrossVerificationUser = "";
                                        pendingCrossVerificationGuid = "";
                                        crossVerificationStartTime = DateTime.MinValue;
                                        
                                        // Log security event
                                        if (dbManager != null)
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "denied",
                                                    reason: $"Attempted to end session owned by different instructor (Session Owner: {sessionOwnerName})"
                                                );
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Notify ESP32 of denial
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControlDenial(userInfo.EmployeeId, userInfo.Username, $"Session owned by {sessionOwnerName}", "instructor", rfidData);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                    
                                    // Verified! Reset state and proceed with session-end
                                    Console.WriteLine($"✅ INSTRUCTOR VERIFIED FOR SESSION-END: {userInfo.Username} (Fingerprint + RFID match)");
                                    SetRfidStatusText($"✅ Verified: {userInfo.Username}. Ending session...");
                                    
                                    awaitingCrossTypeVerification = false;
                                    firstScanType = "";
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                    
                                    // Now proceed with session-end INLINE
                                    // Check if there are students who haven't signed out yet
                                    if (signedInStudentGuids.Count > 0)
                                    {
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
                                        
                                        await System.Threading.Tasks.Task.Delay(3000);
                                    }
                                    
                                    // Close the session
                                    currentSessionState = AttendanceSessionState.Inactive;
                                    currentInstructorId = null;
                                    currentScheduleId = null;
                                    currentSubjectName = null;
                                    currentInstructorName = null;
                                    
                                    signedInStudentGuids.Clear();
                                    signedOutStudentGuids.Clear();
                                    
                                    awaitingCrossTypeVerification = false;
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    firstScanType = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                    
                                    UpdateSessionStateDisplay();
                                    SetRfidStatusText($"✅ Instructor {userInfo.Username} closed RFID session.");
                                    AddRfidAttendanceRecord(userInfo.Username, "Session Closed", "Inactive");
                                    
                                    _ = System.Threading.Tasks.Task.Run(async () => {
                                        try
                                        {
                                            RecordAttendance(userInfo.Username, "Instructor Sign-Out (Session End)");
                                            await RequestRfidLockControl(userInfo.EmployeeId, "Instructor Sign-Out (Session End)", rfidData);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Warning: Could not record instructor sign-out: {ex.Message}");
                                        }
                                    });
                                }
                                else
                                {
                                    // Mismatch
                                    Console.WriteLine($"❌ INSTRUCTOR VERIFICATION FAILED: Fingerprint={pendingCrossVerificationUser}, RFID={userInfo.Username}");
                                    SetRfidStatusText($"❌ Verification failed! Fingerprint: {pendingCrossVerificationUser}, RFID: {userInfo.Username}. Please try again.");
                                    
                                    awaitingCrossTypeVerification = false;
                                    pendingCrossVerificationUser = "";
                                    pendingCrossVerificationGuid = "";
                                    firstScanType = "";
                                    crossVerificationStartTime = DateTime.MinValue;
                                }
                            }
                            else if (awaitingCrossTypeVerification && firstScanType == "RFID")
                            {
                                // Already waiting for fingerprint from previous RFID scan
                                SetRfidStatusText($"⏳ Waiting for fingerprint verification. Please scan fingerprint to end session.");
                            }
                            else
                            {
                                // SECURITY CHECK: If not session owner, check if door access is allowed
                                if (!string.IsNullOrEmpty(currentInstructorId) && userInfo.EmployeeId != currentInstructorId)
                                {
                                    // Different instructor - check if they have scheduled class
                                    var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userInfo.EmployeeId);
                                    bool hasScheduledClass = scheduleValidation != null && scheduleValidation.IsValid && !string.IsNullOrEmpty(scheduleValidation.ScheduleId);
                                    
                                    if (hasScheduledClass)
                                    {
                                        // Has scheduled class but different instructor owns session - deny (interference prevention)
                                        string sessionOwnerName = userInfo.Username;
                                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                        {
                                            sessionOwnerName = sessionOwner.Username;
                                        }
                                        
                                        Console.WriteLine($"⚠️ SECURITY: Instructor {userInfo.Username} (has scheduled class) attempted to access session owned by {sessionOwnerName}");
                                        SetRfidStatusText($"⚠️ Session is active for {sessionOwnerName}. Only the session owner can perform actions.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Access Denied", $"Session owned by {sessionOwnerName}");
                                        
                                        // Log this security event
                                        if (dbManager != null)
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "denied",
                                                    reason: $"Attempted to access session owned by different instructor (Session Owner: {sessionOwnerName})"
                                                );
                                                Console.WriteLine($"📝 Logged unauthorized instructor access attempt to ACCESSLOGS for {userInfo.Username}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Notify ESP32 of denial
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControlDenial(userInfo.EmployeeId, userInfo.Username, $"Session owned by {sessionOwnerName}", "instructor", rfidData);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                    else if (deviceConfig?.AllowInstructorDoorAccess == true)
                                    {
                                        // No scheduled class but door access enabled - allow door access only
                                        Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (RFID): {userInfo.Username} - Door access granted (no schedule, different session active)");
                                        SetRfidStatusText($"🚪 Instructor {userInfo.Username} - Door access granted. Sign-out session remains active.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Door Access", "Instructor Door Access (No Schedule)");
                                        
                                        // Log door access to ACCESSLOGS
                                        if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "success",
                                                    reason: "Instructor door access granted (no scheduled class, different session active)"
                                                );
                                                Console.WriteLine($"📝 Logged instructor door access to ACCESSLOGS for {userInfo.Username}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Record attendance for door access
                                        RecordAttendance(userInfo.Username, "RFID Door Access", true, currentScanLocation);
                                        
                                        // Trigger door access without ending session
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControl(userInfo.EmployeeId, "Instructor Door Access (No Schedule)", rfidData);
                                            }
                                            catch (Exception lockEx)
                                            {
                                                Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                    else
                                    {
                                        // No scheduled class and door access not enabled - deny
                                        string sessionOwnerName = userInfo.Username;
                                        if (userLookupByGuid != null && userLookupByGuid.TryGetValue(currentInstructorId, out var sessionOwner))
                                        {
                                            sessionOwnerName = sessionOwner.Username;
                                        }
                                        
                                        Console.WriteLine($"⚠️ SECURITY: Instructor {userInfo.Username} (no schedule) attempted to access session owned by {sessionOwnerName}");
                                        SetRfidStatusText($"⚠️ Session is active for {sessionOwnerName}. Door access not enabled.");
                                        AddRfidAttendanceRecord(userInfo.Username, "Access Denied", $"Session owned by {sessionOwnerName}");
                                        
                                        // Log denied access
                                        if (dbManager != null)
                                        {
                                            try
                                            {
                                                dbManager.LogAccessAttempt(
                                                    userId: userInfo.EmployeeId,
                                                    roomId: null,
                                                    authMethod: "RFID",
                                                    location: currentScanLocation ?? "inside",
                                                    accessType: "attendance_scan",
                                                    result: "denied",
                                                    reason: $"Attempted to access session owned by different instructor. No scheduled class and door access not enabled."
                                                );
                                                Console.WriteLine($"📝 Logged denied instructor access attempt to ACCESSLOGS for {userInfo.Username}");
                                            }
                                            catch (Exception logEx)
                                            {
                                                Console.WriteLine($"⚠️ Failed to log security event: {logEx.Message}");
                                            }
                                        }
                                        
                                        // Notify ESP32 of denial
                                        _ = System.Threading.Tasks.Task.Run(async () => {
                                            try
                                            {
                                                await RequestRfidLockControlDenial(userInfo.EmployeeId, userInfo.Username, $"Session owned by {sessionOwnerName}", "instructor", rfidData);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Warning: Could not send denial to ESP32: {ex.Message}");
                                            }
                                        });
                                        
                                        return;
                                    }
                                }
                                
                                // Session owner or no active session - proceed with normal flow
                                // NEW LOGIC: During active sign-out session, single scan grants door access without ending session
                                if (IsDualAuthRequired)
                                {
                                    Console.WriteLine($"🚪 INSTRUCTOR DOOR ACCESS (RFID): {userInfo.Username} - Door access granted, session remains active");
                                    SetRfidStatusText($"🚪 Instructor {userInfo.Username} - Door access granted. Sign-out session remains active.");
                                    AddRfidAttendanceRecord(userInfo.Username, "Door Access", "Instructor Door Access (Sign-Out Active)");
                                    
                                    // Log door access to ACCESSLOGS
                                    if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                                    {
                                        try
                                        {
                                            dbManager.LogAccessAttempt(
                                                userId: userInfo.EmployeeId,
                                                roomId: null,
                                                authMethod: "RFID",
                                                location: currentScanLocation ?? "inside",
                                                accessType: "attendance_scan",
                                                result: "success",
                                                reason: "Instructor door access granted during sign-out session"
                                            );
                                            Console.WriteLine($"📝 Logged instructor door access to ACCESSLOGS for {userInfo.Username}");
                                        }
                                        catch (Exception logEx)
                                        {
                                            Console.WriteLine($"⚠️ Failed to log door access: {logEx.Message}");
                                        }
                                    }
                                    
                                    // Record attendance for door access
                                    RecordAttendance(userInfo.Username, "RFID Door Access", true, currentScanLocation);
                                    
                                    // Trigger door access without ending session
                                    _ = System.Threading.Tasks.Task.Run(async () => {
                                        try
                                        {
                                            await RequestRfidLockControl(userInfo.EmployeeId, "Instructor Door Access (Sign-Out Active)", rfidData);
                                        }
                                        catch (Exception lockEx)
                                        {
                                            Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                        }
                                    });
                                    
                                    // Start waiting for second scan to end session
                                    awaitingCrossTypeVerification = true;
                                    firstScanType = "RFID";
                                    pendingCrossVerificationUser = userInfo.Username;
                                    pendingCrossVerificationGuid = userInfo.EmployeeId;
                                    crossVerificationStartTime = DateTime.Now;
                                    
                                    // Send intermediate status to ESP32
                                    User user;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userInfo.EmployeeId, out user))
                                    {
                                        _ = Task.Run(async () => await SendIntermediateStatusToESP32(user, "RFID", "FINGERPRINT", "/api/rfid-scan"));
                                    }
                                }
                                else
                                {
                                    // RFID-only mode: This shouldn't happen in ActiveForSignOut state with single auth
                                    // but keeping original logic as fallback
                                    Console.WriteLine($"🔍 INSTRUCTOR RFID: {userInfo.Username} - Waiting for fingerprint to end session");
                                    SetRfidStatusText($"Instructor RFID: {userInfo.Username}. Please scan fingerprint to end session.");
                                    
                                    awaitingCrossTypeVerification = true;
                                    firstScanType = "RFID";
                                    pendingCrossVerificationUser = userInfo.Username;
                                    pendingCrossVerificationGuid = userInfo.EmployeeId;
                                    crossVerificationStartTime = DateTime.Now;
                                    AddRfidAttendanceRecord(userInfo.Username, "Waiting for Fingerprint", "RFID First");
                                    
                                    // Send intermediate status to ESP32
                                    User user;
                                    if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userInfo.EmployeeId, out user))
                                    {
                                        _ = Task.Run(async () => await SendIntermediateStatusToESP32(user, "RFID", "FINGERPRINT", "/api/rfid-scan"));
                                    }
                                }
                            }
                            break;
                            
                        default:
                            SetRfidStatusText("Session not active. Please start attendance first.");
                            break;
                    }
                }
                else if (userType == "custodian")
                {
                    Console.WriteLine($"====== RFID CUSTODIAN SCAN ======");
                    Console.WriteLine($"Custodian: {userInfo.Username}");
                    
                    Console.WriteLine($"🧹 CUSTODIAN RFID: {userInfo.Username} - Door access only (no attendance)");
                    SetRfidStatusText($"🧹 Custodian access granted: {userInfo.Username}. Door unlocked.");
                    AddRfidAttendanceRecord(userInfo.Username, "Door Access", "Custodian Access");
                    
                    // Trigger door access
                    _ = System.Threading.Tasks.Task.Run(async () => {
                        try
                        {
                            await RequestRfidLockControl(userInfo.EmployeeId, "Custodian Door Access", rfidData);
                        }
                        catch (Exception lockEx)
                        {
                            Console.WriteLine($"Lock control failed: {lockEx.Message}");
                        }
                    });
                }
                else if (userType == "dean")
                {
                    Console.WriteLine($"====== RFID DEAN SCAN ======");
                    Console.WriteLine($"Dean: {userInfo.Username}");
                    
                    Console.WriteLine($"🎓 DEAN RFID: {userInfo.Username} - Checking for scheduled class");
                    
                    // Check if dean has a scheduled class (since deans can be instructors)
                    var scheduleValidation = dbManager?.ValidateScheduleForCurrentTime(userInfo.EmployeeId);
                    
                    if (scheduleValidation != null && scheduleValidation.IsValid && !string.IsNullOrEmpty(scheduleValidation.ScheduleId))
                    {
                        // Dean has a scheduled class - record attendance
                        Console.WriteLine($"🎓 DEAN WITH SCHEDULE: {userInfo.Username} - {scheduleValidation.SubjectName}");
                        SetRfidStatusText($"🎓 Dean {userInfo.Username} - Scheduled class: {scheduleValidation.SubjectName}. Door unlocked.");
                        AddRfidAttendanceRecord(userInfo.Username, "Dean Check-In", $"Scheduled: {scheduleValidation.SubjectName}");
                        
                        // Record attendance for the scheduled class
                        RecordAttendance(userInfo.Username, "Dean Check-In", true, currentScanLocation);
                    }
                    else
                    {
                        // Dean without scheduled class - door access only
                        Console.WriteLine($"🎓 DEAN WITHOUT SCHEDULE: {userInfo.Username} - Administrative access");
                        SetRfidStatusText($"🎓 Dean {userInfo.Username} - Administrative access. Door unlocked.");
                        AddRfidAttendanceRecord(userInfo.Username, "Door Access", "Dean Administrative Access");
                    }
                    
                    // Always trigger door access for deans
                    _ = System.Threading.Tasks.Task.Run(async () => {
                        try
                        {
                            await RequestRfidLockControl(userInfo.EmployeeId, "Dean Door Access", rfidData);
                        }
                        catch (Exception lockEx)
                        {
                            Console.WriteLine($"Lock control failed: {lockEx.Message}");
                        }
                    });
                }
                else if (userType == "student")
                {
                    Console.WriteLine($"====== RFID STUDENT SCAN ======");
                    Console.WriteLine($"Student: {userInfo.Username}");
                    Console.WriteLine($"Current Session State: {currentSessionState}");
                    
                    // Use unified session state
                    if (currentSessionState == AttendanceSessionState.ActiveForStudents)
                    {
                        // Student sign-in
                        Console.WriteLine("✅ Session is active, processing student sign-in...");
                        HandleRfidStudentSignIn(rfidData);
                    }
                    else if (currentSessionState == AttendanceSessionState.ActiveForSignOut)
                    {
                        // Student sign-out
                        Console.WriteLine("✅ Session in sign-out phase, processing student sign-out...");
                        HandleRfidStudentSignOut(rfidData);
                    }
                    else
                    {
                        // Check if this is an outside sensor scan - try early arrival
                        if (isDualSensorMode && currentScanLocation == "outside")
                        {
                            if (IsRfidOnlyMode && !awaitingEarlyArrivalVerification)
                            {
                                awaitingEarlyArrivalVerification = true;
                                earlyFirstScanType = "FINGERPRINT";
                                earlyPendingUser = userInfo.Username;
                                earlyPendingGuid = userInfo.EmployeeId;
                                earlyVerificationStartTime = DateTime.Now;
                            }

                            Console.WriteLine($"⏰ No active session - handling early arrival (dual-auth) for {userInfo.Username}");
                            Console.WriteLine($"   Current early arrival state: awaiting={awaitingEarlyArrivalVerification}, firstType={earlyFirstScanType}, pendingUser={earlyPendingUser}");

                            // Timeout check
                            if (awaitingEarlyArrivalVerification)
                            {
                                var elapsed = DateTime.Now - earlyVerificationStartTime;
                                if (elapsed.TotalSeconds > CROSS_VERIFICATION_TIMEOUT_SECONDS)
                                {
                                    Console.WriteLine($"⏱️ EARLY ARRIVAL VERIFICATION TIMEOUT: {earlyPendingUser} took too long");
                                    SetRfidStatusText($"⏱️ Verification timeout for {earlyPendingUser}. Starting over...");
                                    awaitingEarlyArrivalVerification = false;
                                    earlyFirstScanType = "";
                                    earlyPendingUser = "";
                                    earlyPendingGuid = "";
                                }
                            }

                            if (awaitingEarlyArrivalVerification && earlyFirstScanType == "FINGERPRINT")
                            {
                                // Fingerprint was scanned first, now verifying with RFID
                                Console.WriteLine($"   Checking verification: RFID user={userInfo.Username}, GUID={userInfo.EmployeeId}");
                                Console.WriteLine($"   Against pending: user={earlyPendingUser}, GUID={earlyPendingGuid}");
                                if (userInfo.Username == earlyPendingUser && userInfo.EmployeeId == earlyPendingGuid)
                                {
                                    Console.WriteLine($"✅ EARLY ARRIVAL VERIFIED: {userInfo.Username} (Fingerprint + RFID)");
                                    SetRfidStatusText($"✅ Verified: {userInfo.Username}. Recording early arrival...");
                                    awaitingEarlyArrivalVerification = false;
                                    earlyFirstScanType = "";
                                    earlyPendingUser = "";
                                    earlyPendingGuid = "";

                                    _ = System.Threading.Tasks.Task.Run(async () => {
                                        try { await RecordEarlyArrivalRfid(userInfo.Username, userInfo.EmployeeId, rfidData); } catch (Exception ex) { Console.WriteLine($"Early arrival record failed: {ex.Message}"); }
                                    });
                                }
                                else
                                {
                                    Console.WriteLine($"❌ EARLY ARRIVAL VERIFICATION FAILED: Fingerprint={earlyPendingUser}, RFID={userInfo.Username}");
                                    SetRfidStatusText($"❌ Verification failed! Fingerprint: {earlyPendingUser}, RFID: {userInfo.Username}. Please try again.");
                                    awaitingEarlyArrivalVerification = false;
                                    earlyFirstScanType = "";
                                    earlyPendingUser = "";
                                    earlyPendingGuid = "";
                                }
                            }
                            else if (awaitingEarlyArrivalVerification && earlyFirstScanType == "RFID")
                            {
                                // Already waiting for fingerprint - ignore this RFID scan
                                Console.WriteLine($"⏳ Early arrival: Waiting for fingerprint for {earlyPendingUser}. Ignoring duplicate RFID scan.");
                                SetRfidStatusText($"Waiting for fingerprint scan. Please scan your fingerprint to complete early arrival.");
                                
                                // Remind user on OLED to scan fingerprint
                                _ = System.Threading.Tasks.Task.Run(async () => {
                                    try { await RequestRfidInfoDisplay(userInfo.EmployeeId, userInfo.Username, "Waiting for fingerprint...", rfidData); } catch { }
                                });
                                
                                return;
                            }
                            else if (!awaitingEarlyArrivalVerification)
                            {
                                // Start early-arrival verification with RFID first
                                awaitingEarlyArrivalVerification = true;
                                earlyFirstScanType = "RFID";
                                earlyPendingUser = userInfo.Username;
                                earlyPendingGuid = userInfo.EmployeeId;
                                earlyVerificationStartTime = DateTime.Now;
                                Console.WriteLine($"🧭 EARLY ARRIVAL: RFID captured for {userInfo.Username}. Awaiting fingerprint...");
                                SetRfidStatusText($"RFID OK. Please scan fingerprint to complete early arrival.");

                                // Show first scan acceptance on OLED
                                _ = System.Threading.Tasks.Task.Run(async () => {
                                    try { await RequestRfidInfoDisplay(userInfo.EmployeeId, userInfo.Username, "RFID OK. Scan fingerprint now.", rfidData); } catch { }
                                });
                            }
                        }
                        else
                        {
                            Console.WriteLine($"❌ No active session found");
                            Console.WriteLine($"   State: {currentSessionState}");
                            Console.WriteLine("   Instructor must start a session first!");
                            SetRfidStatusText($"❌ No active session. Instructor must start attendance session first.");
                            AddRfidAttendanceRecord(userInfo.Username, "Session Not Active", currentSessionState.ToString());
                            
                            // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                            if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                            {
                                try
                                {
                                    dbManager.LogAccessAttempt(
                                        userId: userInfo.EmployeeId,
                                        roomId: null,
                                        authMethod: "RFID",
                                        location: currentScanLocation ?? "inside",
                                        accessType: "attendance_scan",
                                        result: "denied",
                                        reason: "No active session. Instructor must start the session first."
                                    );
                                    Console.WriteLine($"📝 Logged denied access attempt to ACCESSLOGS for {userInfo.Username}");
                                }
                                catch (Exception logEx)
                                {
                                    Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                                }
                            }
                            
                            // Send denial message to ESP32 for OLED display (RFID)
                            _ = System.Threading.Tasks.Task.Run(async () => {
                                try
                                {
                                    await RequestRfidLockControlDenial(userInfo.EmployeeId, userInfo.Username, "No active session. Instructor must start the session first.", "student", rfidData);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Warning: Could not send RFID denial to ESP32: {ex.Message}");
                                }
                            });
                        }
                    }
                }
                else
                {
                    SetRfidStatusText($"❌ RFID {rfidData} belongs to {userInfo.Username} ({userType}). Only instructors, students, custodians, and deans can use attendance system.");
                    AddRfidAttendanceRecord(userInfo.Username, "Access Denied", $"Invalid Role ({userType})");
                    
                    // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                    if (dbManager != null && !string.IsNullOrEmpty(userInfo.EmployeeId))
                    {
                        try
                        {
                            dbManager.LogAccessAttempt(
                                userId: userInfo.EmployeeId,
                                roomId: null,
                                authMethod: "RFID",
                                location: currentScanLocation ?? "inside",
                                accessType: "attendance_scan",
                                result: "denied",
                                reason: $"Invalid role ({userType}). Only instructors, students, custodians, and deans can use attendance system."
                            );
                            Console.WriteLine($"📝 Logged denied access attempt to ACCESSLOGS for {userInfo.Username}");
                        }
                        catch (Exception logEx)
                        {
                            Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error processing RFID scan: {ex.Message}");
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
                    
                    // Log unknown RFID scan to ACCESSLOGS table
                    try
                    {
                        if (dbManager != null)
                        {
                            dbManager.LogAccessAttempt(
                                userId: null,
                                roomId: null,
                                authMethod: "RFID",
                                location: currentScanLocation ?? "inside",
                                accessType: "attendance_scan",
                                result: "denied",
                                reason: $"Unknown RFID card: {rfidData}"
                            );
                            Console.WriteLine($"📝 Logged unknown RFID scan to ACCESSLOGS: {rfidData}");
                        }
                    }
                    catch (Exception logEx)
                    {
                        Console.WriteLine($"⚠️ Failed to log unknown RFID scan: {logEx.Message}");
                    }
                    
                    // Notify ESP32 to show 'No match' on OLED (RFID path)
                    _ = System.Threading.Tasks.Task.Run(async () => {
                        try { await RequestRfidNoMatchDisplay(rfidData); } catch { }
                    });
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
                    
                    // Log denied access attempt to ACCESSLOGS for attendance logs visibility
                    if (dbManager != null && !string.IsNullOrEmpty(userGuid))
                    {
                        try
                        {
                            dbManager.LogAccessAttempt(
                                userId: userGuid,
                                roomId: null,
                                authMethod: "RFID",
                                location: currentScanLocation ?? "inside",
                                accessType: "attendance_scan",
                                result: "denied",
                                reason: $"Access denied. Only students can sign in during active session. User type: {userType}"
                            );
                            Console.WriteLine($"📝 Logged denied access attempt to ACCESSLOGS for {userName}");
                        }
                        catch (Exception logEx)
                        {
                            Console.WriteLine($"⚠️ Failed to log denied access attempt: {logEx.Message}");
                        }
                    }
                    
                    // Send denial message to ESP32 for OLED display (RFID)
                    _ = System.Threading.Tasks.Task.Run(async () => {
                        try
                        {
                            await RequestRfidLockControlDenial(userGuid, userName, $"Access denied. Only students can sign in during active session.", userType, rfidData);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Could not send RFID denial to ESP32: {ex.Message}");
                        }
                    });
                    return;
                }
                
                // Check if student is scanning at outside sensor - door access only, no attendance
                if (isDualSensorMode && currentScanLocation == "outside")
                {
                    // Check if session is active for students (same logic as fingerprint)
                    if (currentSessionState != AttendanceSessionState.ActiveForStudents &&
                        currentSessionState != AttendanceSessionState.ActiveForSignOut &&
                        currentSessionState != AttendanceSessionState.WaitingForInstructorSignOut)
                    {
                        Console.WriteLine($"❌ No active session - denying door access for student {userName} (RFID)");
                        SetRfidStatusText($"❌ No active session. Door access denied for {userName}.");
                        AddRfidAttendanceRecord(userName, "Door Access Denied", "No Active Session");
                        
                        // Send denial message to ESP32 for OLED display (RFID)
                        _ = System.Threading.Tasks.Task.Run(async () => {
                            try
                            {
                                await RequestRfidLockControlDenial(userGuid, userName, "No active session. Instructor must start the session first.", "student", rfidData);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Could not send RFID denial to ESP32: {ex.Message}");
                            }
                        });
                        return;
                    }
                    
                    // Outside sensor - door access only, no attendance
                    Console.WriteLine($"🚪 OUTSIDE SENSOR (RFID): {userName} - Door access only (no attendance)");
                    SetRfidStatusText($"🚪 Door access granted: {userName}. Attendance not recorded (outside sensor).");
                    AddRfidAttendanceRecord(userName, "Door Access", "Outside Sensor");
                    
                    // Trigger door access
                    System.Threading.Tasks.Task.Run(async () => {
                        try
                        {
                            await RequestRfidLockControl(userGuid, "Student Door Access (Outside)", rfidData);
                        }
                        catch (Exception lockEx)
                        {
                            Console.WriteLine($"Lock control failed: {lockEx.Message}");
                        }
                    });
                    
                    return;
                }
                
                if (IsRfidOnlyMode && !awaitingCrossTypeVerification)
                {
                    awaitingCrossTypeVerification = true;
                    firstScanType = "FINGERPRINT";
                    pendingCrossVerificationUser = userName;
                    pendingCrossVerificationGuid = userGuid;
                    crossVerificationStartTime = DateTime.Now;
                }

                // Check for cross-type verification timeout first
                if (awaitingCrossTypeVerification)
                {
                    var verificationElapsed = DateTime.Now - crossVerificationStartTime;
                    if (verificationElapsed.TotalSeconds > CROSS_VERIFICATION_TIMEOUT_SECONDS)
                    {
                        // Timeout - reset verification state
                        Console.WriteLine($"⏱️ CROSS-TYPE VERIFICATION TIMEOUT: {pendingCrossVerificationUser} took too long");
                        SetRfidStatusText($"⏱️ Verification timeout for {pendingCrossVerificationUser}. Starting over...");
                        awaitingCrossTypeVerification = false;
                        pendingCrossVerificationUser = "";
                        pendingCrossVerificationGuid = "";
                        firstScanType = "";
                        crossVerificationStartTime = DateTime.MinValue;
                    }
                }
                
                // Check if this RFID scan is completing a fingerprint-first verification
                if (awaitingCrossTypeVerification && firstScanType == "FINGERPRINT")
                {
                    // Fingerprint was scanned first, now verifying with RFID
                    if (userName == pendingCrossVerificationUser && userGuid == pendingCrossVerificationGuid)
                    {
                        // ✅ VERIFIED: Fingerprint + RFID match!
                        Console.WriteLine($"✅ CROSS-TYPE VERIFICATION SUCCESS: {userName} (Fingerprint + RFID match)");
                        SetRfidStatusText($"✅ Verified: {userName}. Processing attendance...");
                        
                        // Reset verification state
                        awaitingCrossTypeVerification = false;
                        pendingCrossVerificationUser = "";
                        pendingCrossVerificationGuid = "";
                        firstScanType = "";
                        crossVerificationStartTime = DateTime.MinValue;
                        
                        // Check if already signed in
                        if (signedInStudentGuids.Contains(userGuid))
                        {
                            SetRfidStatusText($"⚠️ Student {userName} already signed in - allowing door access.");
                            AddRfidAttendanceRecord(userName, "Already Signed In - Door Access", "Duplicate");
                            
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
                        
                        // Process verified sign-in
                        System.Threading.Tasks.Task.Run(() => {
                            try
                            {
                                ProcessVerifiedStudentSignIn(userName, userGuid);
                                
                                // Show session mode display after scan
                                _ = System.Threading.Tasks.Task.Run(async () => {
                                    try
                                    {
                                        await System.Threading.Tasks.Task.Delay(6000); // Wait for scan result to display
                                        await RequestSessionModeDisplay(isRfid: true, rfidData: rfidData);
                                    }
                                    catch { }
                                });
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error processing verified sign-in: {ex.Message}");
                            }
                        });
                        return;
                    }
                    else
                    {
                        // ❌ MISMATCH: RFID doesn't match fingerprint scan
                        Console.WriteLine($"❌ CROSS-TYPE VERIFICATION FAILED: Fingerprint={pendingCrossVerificationUser}, RFID={userName}");
                        SetRfidStatusText($"❌ Verification failed! Fingerprint scan: {pendingCrossVerificationUser}, RFID scan: {userName}. Please try again.");
                        
                        // Reset verification state
                        awaitingCrossTypeVerification = false;
                        pendingCrossVerificationUser = "";
                        pendingCrossVerificationGuid = "";
                        firstScanType = "";
                        crossVerificationStartTime = DateTime.MinValue;
                        return;
                    }
                }
                
                // Check if already awaiting verification from a previous RFID scan
                if (awaitingCrossTypeVerification && firstScanType == "RFID")
                {
                    // Already have a pending RFID-first verification - this RFID scan is duplicate
                    SetRfidStatusText($"💳 RFID scanned: {userName}. Waiting for fingerprint verification...");
                    AddRfidAttendanceRecord(userName, "Waiting for Fingerprint", "Verify");
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
                
                // Start the RFID-first flow - waiting for fingerprint verification
                Console.WriteLine($"🔍 FIRST SCAN (RFID): {userName} - Waiting for fingerprint scan");
                SetRfidStatusText($"💳 RFID scanned: {userName}. Please scan your fingerprint to verify.");
                
                // Set cross-type verification state
                awaitingCrossTypeVerification = true;
                firstScanType = "RFID";
                pendingCrossVerificationUser = userName;
                pendingCrossVerificationGuid = userGuid;
                crossVerificationStartTime = DateTime.Now;
                AddRfidAttendanceRecord(userName, "Waiting for Fingerprint", "First Scan");
                
                // Send intermediate status to ESP32
                User user;
                if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userGuid, out user))
                {
                    _ = Task.Run(async () => await SendIntermediateStatusToESP32(user, "RFID", "FINGERPRINT", "/api/rfid-scan"));
                }
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error handling student sign-in: {ex.Message}");
                AddRfidAttendanceRecord("System", "Student Sign-In Error", ex.Message);
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
                    
                    // Log unknown RFID scan to ACCESSLOGS table
                    try
                    {
                        if (dbManager != null)
                        {
                            dbManager.LogAccessAttempt(
                                userId: null,
                                roomId: null,
                                authMethod: "RFID",
                                location: currentScanLocation ?? "inside",
                                accessType: "attendance_scan",
                                result: "denied",
                                reason: $"Unknown RFID card: {rfidData}"
                            );
                            Console.WriteLine($"📝 Logged unknown RFID scan to ACCESSLOGS: {rfidData}");
                        }
                    }
                    catch (Exception logEx)
                    {
                        Console.WriteLine($"⚠️ Failed to log unknown RFID scan: {logEx.Message}");
                    }
                    
                    // Notify ESP32 to show 'No match' on OLED (RFID path)
                    _ = System.Threading.Tasks.Task.Run(async () => {
                        try { await RequestRfidNoMatchDisplay(rfidData); } catch { }
                    });
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
                
                // Check if student is scanning at outside sensor - door access only, no attendance
                if (isDualSensorMode && currentScanLocation == "outside")
                {
                    // Outside sensor - door access only, no attendance
                    Console.WriteLine($"🚪 OUTSIDE SENSOR (RFID SIGN-OUT): {userName} - Door access only (no attendance)");
                    SetRfidStatusText($"🚪 Door access granted: {userName}. Attendance not recorded (outside sensor).");
                    AddRfidAttendanceRecord(userName, "Door Access", "Outside Sensor");
                    
                    // Trigger door access
                    System.Threading.Tasks.Task.Run(async () => {
                        try
                        {
                            await RequestRfidLockControl(userGuid, "Student Door Access (Outside)", rfidData);
                        }
                        catch (Exception lockEx)
                        {
                            Console.WriteLine($"Lock control failed: {lockEx.Message}");
                        }
                    });
                    
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
                
                if (IsRfidOnlyMode && !awaitingCrossTypeVerification)
                {
                    awaitingCrossTypeVerification = true;
                    firstScanType = "FINGERPRINT";
                    pendingCrossVerificationUser = userName;
                    pendingCrossVerificationGuid = userGuid;
                    crossVerificationStartTime = DateTime.Now;
                }

                // CRITICAL: Check for verification timeout first
                if (awaitingCrossTypeVerification)
                {
                    var verificationElapsed = DateTime.Now - crossVerificationStartTime;
                    if (verificationElapsed.TotalSeconds > CROSS_VERIFICATION_TIMEOUT_SECONDS)
                    {
                        // Timeout - reset verification state
                        Console.WriteLine($"⏱️ STUDENT RFID SIGN-OUT VERIFICATION TIMEOUT: {pendingCrossVerificationUser} took too long");
                        SetRfidStatusText($"⏱️ Verification timeout for {pendingCrossVerificationUser}. Starting over...");
                        awaitingCrossTypeVerification = false;
                        pendingCrossVerificationUser = "";
                        pendingCrossVerificationGuid = "";
                        firstScanType = "";
                        crossVerificationStartTime = DateTime.MinValue;
                        // Don't return - treat this as a new first scan
                    }
                }
                
                // Check if this RFID scan is completing a fingerprint-first verification
                if (awaitingCrossTypeVerification && firstScanType == "FINGERPRINT")
                {
                    // Fingerprint was scanned first, now verifying with RFID
                    if (userName == pendingCrossVerificationUser && userGuid == pendingCrossVerificationGuid)
                    {
                        // ✅ VERIFIED: Fingerprint + RFID match!
                        Console.WriteLine($"✅ CROSS-TYPE VERIFICATION SUCCESS FOR SIGN-OUT: {userName} (Fingerprint + RFID match)");
                        SetRfidStatusText($"✅ Verified: {userName}. Processing sign-out...");
                        
                        // Reset verification state
                        awaitingCrossTypeVerification = false;
                        pendingCrossVerificationUser = "";
                        pendingCrossVerificationGuid = "";
                        firstScanType = "";
                        crossVerificationStartTime = DateTime.MinValue;
                        
                        // Process verified sign-out
                        System.Threading.Tasks.Task.Run(() => {
                            try
                            {
                                Console.WriteLine($"🔍 CALLING TryRecordAttendanceByGuid for sign-out: User={userName}, GUID={userGuid}");
                                var attempt = dbManager?.TryRecordAttendanceByGuid(userGuid, "Student Sign-Out (RFID)", null);
                                Console.WriteLine($"🔍 TryRecordAttendanceByGuid result: Success={attempt?.Success}, Reason={attempt?.Reason}");
                                this.Invoke(new Action(() => {
                                    if (attempt != null && attempt.Success)
                                    {
                                        // Success: update local state and UI
                                        // Only remove from signed in if they were actually signed in
                                        bool wasSignedIn = signedInStudentGuids.Contains(userGuid);
                                        if (wasSignedIn)
                                        {
                                            signedInStudentGuids.Remove(userGuid);
                                        }
                                        signedOutStudentGuids.Add(userGuid);
                                        
                                        string successMessage = wasSignedIn 
                                            ? $"✅ Student {userName} signed out successfully."
                                            : $"✅ Student {userName} signed out (was not signed in).";
                                        SetRfidStatusText(successMessage);
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
                                
                                // Show session mode display after scan (both success and denied)
                                _ = System.Threading.Tasks.Task.Run(async () => {
                                    try
                                    {
                                        await System.Threading.Tasks.Task.Delay(6000); // Wait for scan result to display
                                        await RequestSessionModeDisplay(isRfid: true, rfidData: rfidData);
                                    }
                                    catch { }
                                });
                            }
                            catch (Exception ex)
                            {
                                this.Invoke(new Action(() => {
                                    SetRfidStatusText($"❌ Error processing sign-out: {ex.Message}");
                                    AddRfidAttendanceRecord("System", "Sign-Out Error", ex.Message);
                                }));
                            }
                        });
                        return;
                    }
                    else
                    {
                        // ❌ MISMATCH: RFID doesn't match fingerprint scan
                        Console.WriteLine($"❌ CROSS-TYPE VERIFICATION FAILED FOR SIGN-OUT: Fingerprint={pendingCrossVerificationUser}, RFID={userName}");
                        SetRfidStatusText($"❌ Verification failed! Fingerprint scan: {pendingCrossVerificationUser}, RFID scan: {userName}. Please try again.");
                        
                        // Reset verification state
                        awaitingCrossTypeVerification = false;
                        pendingCrossVerificationUser = "";
                        pendingCrossVerificationGuid = "";
                        firstScanType = "";
                        crossVerificationStartTime = DateTime.MinValue;
                        return;
                    }
                }
                
                // Check if already awaiting verification from a previous RFID scan
                if (awaitingCrossTypeVerification && firstScanType == "RFID")
                {
                    // Already have a pending RFID-first verification - this RFID scan is duplicate
                    SetRfidStatusText($"💳 RFID scanned: {userName}. Waiting for fingerprint verification...");
                    AddRfidAttendanceRecord(userName, "Waiting for Fingerprint", "Verify");
                    return;
                }
                
                // Start the RFID-first flow - waiting for fingerprint verification
                Console.WriteLine($"🔍 FIRST SCAN (RFID) FOR SIGN-OUT: {userName} - Waiting for fingerprint scan");
                SetRfidStatusText($"💳 RFID scanned: {userName}. Please scan your fingerprint to verify sign-out.");
                
                // Set cross-type verification state
                awaitingCrossTypeVerification = true;
                firstScanType = "RFID";
                pendingCrossVerificationUser = userName;
                pendingCrossVerificationGuid = userGuid;
                crossVerificationStartTime = DateTime.Now;
                AddRfidAttendanceRecord(userName, "Waiting for Fingerprint", "First Scan");
                
                // Send intermediate status to ESP32
                User user;
                if (userLookupByGuid != null && userLookupByGuid.TryGetValue(userGuid, out user))
                {
                    _ = Task.Run(async () => await SendIntermediateStatusToESP32(user, "RFID", "FINGERPRINT", "/api/rfid-scan"));
                }
            }
            catch (Exception ex)
            {
                SetRfidStatusText($"Error handling student sign-out: {ex.Message}");
                AddRfidAttendanceRecord("System", "Student Sign-Out Error", ex.Message);
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
                        location = cmbLocation?.SelectedItem?.ToString() ?? "inside",
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
            // Redirect to unified status text
            SetStatusText(text);
        }

        private void AddRfidAttendanceRecord(string user, string action, string status)
        {
            // Use unified attendance record with RFID method
            AddAttendanceRecord(user, "RFID", action, status);
        }
        
        private void AddAttendanceRecord(string user, string method, string action, string status)
        {
            if (dgvAttendance != null)
            {
                dgvAttendance.Rows.Insert(0, DateTime.Now, user, method, action, status);
            }
        }

        private void ExportRfidAttendanceToCsv(string fileName)
        {
            // Redirect to unified export - uses dgvAttendance with Method column
            ExportToCSV(fileName);
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
            
            // Removed Change Room and Fix Room buttons as requested
            
            mainPanel.Controls.Add(actionPanel);

            // Events configuration section
            var eventsPanel = new Panel
            {
                Location = new Point(30, actionPanel.Location.Y + actionPanel.Height + 20),
                Size = new Size(800, 180),
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            eventsPanel.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, eventsPanel.ClientRectangle,
                    Color.FromArgb(13, 110, 253), ButtonBorderStyle.Solid);
            };

            var lblEventsTitle = new Label
            {
                Text = "🎛️ Events Configuration",
                Location = new Point(15, 10),
                Size = new Size(400, 25),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };
            eventsPanel.Controls.Add(lblEventsTitle);

            var lblEventsDescription = new Label
            {
                Text = "Control how the door reacts to unmatched fingerprints.",
                Location = new Point(15, 40),
                Size = new Size(750, 20),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(73, 80, 87)
            };
            eventsPanel.Controls.Add(lblEventsDescription);

            chkAllowUnauthorizedFingerprints = new CheckBox
            {
                Text = "Allow unauthorized fingerprints to open doors",
                Location = new Point(20, 70),
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 53, 69),
                Checked = deviceConfig?.AllowUnauthorizedFingerprints ?? false
            };
            chkAllowUnauthorizedFingerprints.CheckedChanged += ChkAllowUnauthorizedFingerprints_CheckedChanged;
            eventsPanel.Controls.Add(chkAllowUnauthorizedFingerprints);

            var lblUnauthorizedHint = new Label
            {
                Text = "Warning: when enabled, any detected fingerprint will unlock the door.",
                Location = new Point(40, 95),
                Size = new Size(720, 20),
                Font = new Font("Segoe UI", 8, FontStyle.Italic),
                ForeColor = Color.FromArgb(220, 53, 69)
            };
            eventsPanel.Controls.Add(lblUnauthorizedHint);

            // NEW: Fingerprint-Only and RFID-Only toggles
            chkFingerprintOnly = new CheckBox
            {
                Text = "Allow Fingerprint Only",
                Location = new Point(20, 120),
                AutoSize = true,
                Font = new Font("Segoe UI", 10),
                Checked = deviceConfig?.AllowFingerprintOnly ?? false
            };
            chkFingerprintOnly.CheckedChanged += (s, e) =>
            {
                try
                {
                    if (deviceConfig == null) return;
                    deviceConfig.AllowFingerprintOnly = chkFingerprintOnly.Checked;
                    if (chkFingerprintOnly.Checked)
                    {
                        deviceConfig.AllowRfidOnly = false;
                        try { chkRfidOnly.Checked = false; } catch { }
                    }
                    DeviceConfigManager.Instance.SaveConfiguration(deviceConfig);
                    SetStatusText(chkFingerprintOnly.Checked
                        ? "🔧 Fingerprint-Only mode enabled (RFID not required)."
                        : "🔧 Fingerprint-Only mode disabled.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to update Fingerprint-Only setting:\n{ex.Message}",
                        "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            eventsPanel.Controls.Add(chkFingerprintOnly);

            chkRfidOnly = new CheckBox
            {
                Text = "Allow RFID Only",
                Location = new Point(220, 120),
                AutoSize = true,
                Font = new Font("Segoe UI", 10),
                Checked = deviceConfig?.AllowRfidOnly ?? false
            };
            chkRfidOnly.CheckedChanged += (s, e) =>
            {
                try
                {
                    if (deviceConfig == null) return;
                    deviceConfig.AllowRfidOnly = chkRfidOnly.Checked;
                    if (chkRfidOnly.Checked)
                    {
                        deviceConfig.AllowFingerprintOnly = false;
                        try { chkFingerprintOnly.Checked = false; } catch { }
                    }
                    DeviceConfigManager.Instance.SaveConfiguration(deviceConfig);
                    SetStatusText(chkRfidOnly.Checked
                        ? "🔧 RFID-Only mode enabled (fingerprint not required)."
                        : "🔧 RFID-Only mode disabled.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to update RFID-Only setting:\n{ex.Message}",
                        "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            eventsPanel.Controls.Add(chkRfidOnly);

            chkAllowInstructorDoorAccess = new CheckBox
            {
                Text = "Allow Instructors Door Access with no Scheduled time",
                Location = new Point(20, 150),
                AutoSize = true,
                Font = new Font("Segoe UI", 10),
                Checked = deviceConfig?.AllowInstructorDoorAccess ?? false
            };
            chkAllowInstructorDoorAccess.CheckedChanged += (s, e) =>
            {
                try
                {
                    if (deviceConfig == null) return;
                    deviceConfig.AllowInstructorDoorAccess = chkAllowInstructorDoorAccess.Checked;
                    DeviceConfigManager.Instance.SaveConfiguration(deviceConfig);
                    SetStatusText(chkAllowInstructorDoorAccess.Checked
                        ? "🔧 Instructor door access (no schedule) enabled."
                        : "🔧 Instructor door access (no schedule) disabled.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to update Instructor Door Access setting:\n{ex.Message}",
                        "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            eventsPanel.Controls.Add(chkAllowInstructorDoorAccess);

            mainPanel.Controls.Add(eventsPanel);
            
            // Help section
            var helpPanel = new Panel
            {
                Location = new Point(30, eventsPanel.Location.Y + eventsPanel.Height + 20),
                Size = new Size(800, 180),
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
                       "• If you have only one physical device, enable Test Mode to simulate dual sensors",
                Location = new Point(15, 40),
                Size = new Size(760, 130),
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
                
                // Show configuration dialog with current configuration preselected
                var dialog = new StartupConfigDialog(dbManager, deviceConfig);
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
                    
                    // Update attendance page UI components
                    UpdateCurrentRoomDisplay();
                    UpdateRoomComboBox();
                    
                    // Update location dropdown to match new configuration
                    if (cmbLocation != null)
                    {
                        // Set location to "inside" by default for dual sensor mode
                        cmbLocation.SelectedItem = "inside";
                        if (dbManager != null)
                        {
                            dbManager.ChangeCurrentLocation("inside");
                        }
                    }
                    
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

        private void ChkAllowUnauthorizedFingerprints_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAllowUnauthorizedFingerprints == null)
            {
                return;
            }

            bool allow = chkAllowUnauthorizedFingerprints.Checked;

            try
            {
                if (deviceConfig == null)
                {
                    deviceConfig = DeviceConfigManager.Instance.GetCurrentConfiguration() ?? new DeviceConfiguration();
                }

                deviceConfig.AllowUnauthorizedFingerprints = allow;
                DeviceConfigManager.Instance.SaveConfiguration(deviceConfig);

                var statusMessage = allow
                    ? "⚠️ Unauthorized fingerprints will now unlock the door."
                    : "✅ Only registered users can unlock the door.";
                SetStatusText(statusMessage);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to update unauthorized fingerprint setting:\n{ex.Message}",
                    "Configuration Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                chkAllowUnauthorizedFingerprints.CheckedChanged -= ChkAllowUnauthorizedFingerprints_CheckedChanged;
                chkAllowUnauthorizedFingerprints.Checked = !allow;
                chkAllowUnauthorizedFingerprints.CheckedChanged += ChkAllowUnauthorizedFingerprints_CheckedChanged;
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
                    // INCREASED: Higher FARN for stricter matching to prevent false positives
                    m_InsideSensorOperation.FARN = 150; // Increased from 100 to 150
                    m_InsideSensorOperation.Version = VersionCompatible.ftr_version_compatible;
                    
                    // Note: Futronic SDK device selection is handled internally
                    // The first available device will be used for inside sensor
                    Console.WriteLine($"Inside sensor configured (device index: {deviceConfig?.InsideSensor?.SensorIndex ?? 0})");
                    
                                          // Wire up events
                      m_InsideSensorOperation.OnPutOn += (progress) =>
                      {
                                                     // Track valid finger placement on inside sensor
                           isFingerOnInsideSensor = true;
                           Console.WriteLine("👆 Finger detected on inside sensor - preventing repeated scans");
                           this.Invoke(new Action(() =>
                           {
                               dualSensorPanel?.UpdateInsideStatus("Scanning...");
                           }));
                      };
                      
                      m_InsideSensorOperation.OnTakeOff += (progress) =>
                      {
                          // Only mark as removed if we actually had a finger on
                          // Add delay to prevent false positives
                          if (isFingerOnInsideSensor)
                          {
                              System.Threading.Tasks.Task.Delay(300).ContinueWith(_ =>
                              {
                                  isFingerOnInsideSensor = false;
                                  Console.WriteLine("👆 Finger removed from inside sensor - ready for next scan");
                              });
                          }
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
                          // Skip scan if finger is still on scanner (prevents repeated scans)
                          if (isFingerOnInsideSensor)
                          {
                              System.Threading.Thread.Sleep(200); // Short sleep while finger is on
                              continue;
                          }
                          
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
                                // Reset finger-on flag after successful identification
                                isFingerOnInsideSensor = false;
                                
                                this.Invoke(new Action(() =>
                                {
                                    HandleSensorScan(true, matchIndex, "inside");
                                }));
                            }
                            else if (!isFingerOnInsideSensor)
                            {
                                // Only log non-OK results if finger is not detected
                                // This prevents spam when no finger is present
                                Console.WriteLine($"Inside sensor result: {result}");
                                this.Invoke(new Action(() =>
                                {
                                    HandleSensorScan(false, matchIndex, "inside");
                                }));
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
                    // INCREASED: Higher FARN for stricter matching to prevent false positives
                    m_OutsideSensorOperation.FARN = 150; // Increased from 100 to 150
                    m_OutsideSensorOperation.Version = VersionCompatible.ftr_version_compatible;
                    
                    // Note: Futronic SDK device selection is handled internally  
                    // When multiple devices are present, SDK manages device access
                    Console.WriteLine($"Outside sensor configured (device index: {deviceConfig?.OutsideSensor?.SensorIndex ?? 1})");
                    
                                          // Wire up events
                      m_OutsideSensorOperation.OnPutOn += (progress) =>
                      {
                                                     // Track valid finger placement on outside sensor
                           isFingerOnOutsideSensor = true;
                           Console.WriteLine("👆 Finger detected on outside sensor - preventing repeated scans");
                           this.Invoke(new Action(() =>
                           {
                               dualSensorPanel?.UpdateOutsideStatus("Scanning...");
                           }));
                      };
                      
                      m_OutsideSensorOperation.OnTakeOff += (progress) =>
                      {
                          // Only mark as removed if we actually had a finger on
                          // Add delay to prevent false positives
                          if (isFingerOnOutsideSensor)
                          {
                              System.Threading.Tasks.Task.Delay(300).ContinueWith(_ =>
                              {
                                  isFingerOnOutsideSensor = false;
                                  Console.WriteLine("👆 Finger removed from outside sensor - ready for next scan");
                              });
                          }
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
                          // Skip scan if finger is still on scanner (prevents repeated scans)
                          if (isFingerOnOutsideSensor)
                          {
                              System.Threading.Thread.Sleep(200); // Short sleep while finger is on
                              continue;
                          }
                          
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
                                  // Reset finger-on flag after successful identification
                                  isFingerOnOutsideSensor = false;
                                  
                                  this.Invoke(new Action(() =>
                                  {
                                      HandleSensorScan(true, matchIndex, "outside");
                                  }));
                              }
                              else if (!isFingerOnOutsideSensor) // Only handle no-match if finger is not still on
                              {
                                  // Log non-OK results but don't spam
                                  Console.WriteLine($"Outside sensor result: {result}");
                                  this.Invoke(new Action(() =>
                                  {
                                      HandleSensorScan(false, matchIndex, "outside");
                                  }));
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
                        
                        // Debouncing: Prevent processing the same scan multiple times
                        var timeSinceLastProcess = DateTime.Now - lastProcessedTime;
                        if (userName == lastProcessedUser && timeSinceLastProcess.TotalMilliseconds < DEBOUNCE_INTERVAL_MS)
                        {
                            Console.WriteLine($"⏳ Debouncing {userName} scan - skipping duplicate processing");
                            return; // Skip duplicate processing
                        }
                        
                        // Update debouncing variables for successful match
                        lastProcessedUser = userName;
                        lastProcessedTime = DateTime.Now;
                        
                        // Reset finger-on flag after successful match
                        if (location == "inside")
                        {
                            isFingerOnInsideSensor = false;
                        }
                        else if (location == "outside")
                        {
                            isFingerOnOutsideSensor = false;
                        }

                        // ENHANCED DEBUGGING: Track detailed match information
                        Console.WriteLine($"🔍 FINGERPRINT MATCH DEBUG:");
                        Console.WriteLine($"   📍 Location: {location} sensor");
                        Console.WriteLine($"   👤 Matched User: {userName}");
                        Console.WriteLine($"   🔢 Match Index: {matchIndex}");
                        Console.WriteLine($"   📊 Total Users in DB: {users.Count}");
                        Console.WriteLine($"   🏫 Session State: {currentSessionState}");

                        // Get user details from database for verification
                        var dbUser = dbManager?.LoadAllUsers().FirstOrDefault(u =>
                            u.Username.Equals(userName, StringComparison.OrdinalIgnoreCase));

                        if (dbUser != null)
                        {
                            Console.WriteLine($"   🆔 DB User Type: {dbUser.UserType}");
                            Console.WriteLine($"   📧 Email: {dbUser.Email}");
                            Console.WriteLine($"   🏢 Department: {dbUser.Department}");
                        }

                        Console.WriteLine($"✅ Match found on {location} sensor: {userName}");
                        
                        // Set the current scan location based on current device configuration
                        // This ensures the location is correct even after reconfiguration
                        if (isDualSensorMode && deviceConfig != null)
                        {
                            bool hasInsideSensor = deviceConfig?.InsideSensor != null && m_InsideSensorEnabled;
                            bool hasOutsideSensor = deviceConfig?.OutsideSensor != null && m_OutsideSensorEnabled;
                            
                            if (hasOutsideSensor && !hasInsideSensor)
                            {
                                currentScanLocation = "outside";
                                Console.WriteLine($"📍 Only outside sensor configured: location set to outside");
                            }
                            else if (hasInsideSensor && !hasOutsideSensor)
                            {
                                currentScanLocation = "inside";
                                Console.WriteLine($"📍 Only inside sensor configured: location set to inside");
                            }
                            else
                            {
                                // Both configured or unknown, use the location parameter as fallback
                                currentScanLocation = location;
                                Console.WriteLine($"📍 Both sensors configured or unknown: using location parameter {location}");
                            }
                        }
                        else
                        {
                            currentScanLocation = location;
                        }

                        // Get the user GUID from database (reusing the dbUser from above)
                        var finalDbUser = dbManager.LoadAllUsers().FirstOrDefault(u =>
                            u.Username.Equals(userName, StringComparison.OrdinalIgnoreCase));
                        
                        if (finalDbUser != null)
                        {
                            // Check if student is scanning at outside sensor - door access only, no attendance
                            if (finalDbUser.UserType?.ToLower() == "student" && location == "outside")
                            {
                                // Student at outside sensor - door access only
                                Console.WriteLine($"🚪 Student {userName} at outside sensor - door access only");
                                
                                // Trigger door access
                                System.Threading.Tasks.Task.Run(async () => {
                                    try
                                    {
                                        await RequestLockControl(finalDbUser.EmployeeId, "Student Door Access (Outside)");
                                    }
                                    catch (Exception lockEx)
                                    {
                                        Console.WriteLine($"Lock control failed: {lockEx.Message}");
                                    }
                                });
                                
                                // Update UI
                                dualSensorPanel?.UpdateOutsideLastScan(userName, "Door access (no attendance)", true);
                                dualSensorPanel?.UpdateOutsideStatus("Active");
                                
                                // Add to activity feed
                                dualSensorPanel?.AddActivityItem(new ActivityItem
                                {
                                    Timestamp = DateTime.Now,
                                    UserName = userName,
                                    Action = "Door Access",
                                    Location = location,
                                    Success = true,
                                    StatusMessage = "Door access (no attendance)"
                                });
                                
                                return; // Don't record attendance
                            }
                            
                            // Record attendance
                            string deviceId = location == "inside" ?
                                deviceConfig.InsideSensor.DeviceId :
                                deviceConfig.OutsideSensor.DeviceId;

                            var attendanceResult = dbManager.RecordAttendanceWithDeviceId(
                                finalDbUser.EmployeeId,
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
                                    await RequestLockControl(finalDbUser.EmployeeId, action);
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
                    // No match or error - Enhanced debugging
                    Console.WriteLine($"❌ NO MATCH DEBUG:");
                    Console.WriteLine($"   📍 Location: {location} sensor");
                    Console.WriteLine($"   ✅ Success: {success}");
                    Console.WriteLine($"   🔢 Match Index: {matchIndex}");
                    Console.WriteLine($"   🏫 Session State: {currentSessionState}");

                    var users = LoadUserRecordsForIdentification();
                    Console.WriteLine($"   📊 Users in identification DB: {users.Count}");

                    if (matchIndex < 0)
                    {
                        Console.WriteLine($"   ⚠️ Match index is negative - possible poor fingerprint quality");
                    }
                                          else if (matchIndex >= users.Count)
                      {
                          Console.WriteLine($"   ⚠️ Match index ({matchIndex}) >= users count ({users.Count}) - SDK returned invalid index");
                      }
                      
                    // Reset valid placement on failure
                    hasValidFingerPlacement = false;
                      
                    // Don't log unknown scans while finger is still on scanner (prevents spam)
                    // Check the appropriate sensor based on location
                    bool fingerOnThisSensor = (location == "inside") ? isFingerOnInsideSensor : 
                                             (location == "outside") ? isFingerOnOutsideSensor : 
                                             isFingerOnScanner;

                    bool unauthorizedDoorOpened = false;
                    string panelStatusMessage = "No match found";
                    bool panelStatusSuccess = false;

                    // Evaluate override independently of finger-on state (debounced)
                    bool allowUnauthorizedOverride = deviceConfig?.AllowUnauthorizedFingerprints == true;
                    if (allowUnauthorizedOverride)
                    {
                        var timeSinceLastOverride = DateTime.Now - lastUnauthorizedDoorOpenTime;
                        if (timeSinceLastOverride.TotalMilliseconds >= UNAUTHORIZED_DOOR_DEBOUNCE_MS)
                        {
                            unauthorizedDoorOpened = true;
                            panelStatusSuccess = true;
                            panelStatusMessage = "Door override granted";
                            lastUnauthorizedDoorOpenTime = DateTime.Now;

                            Console.WriteLine("🔓 Allowing door override for unknown fingerprint (setting enabled)");
                            SetStatusText("🔓 Door override requested for unknown fingerprint.");

                            System.Threading.Tasks.Task.Run(async () =>
                            {
                                try
                                {
                                    await RequestAnonymousLockControl(location ?? "inside", "Door override for unknown fingerprint");
                                }
                                catch (Exception lockEx)
                                {
                                    Console.WriteLine($"❌ Door override request failed: {lockEx.Message}");
                                }
                            });
                        }
                        else
                        {
                            var remainingMs = UNAUTHORIZED_DOOR_DEBOUNCE_MS - (int)timeSinceLastOverride.TotalMilliseconds;
                            Console.WriteLine($"⏳ Door override debounced - waiting {Math.Max(0, remainingMs / 1000)}s before next override.");
                        }
                    }

                    // Logging is still tied to unknown-scan debounce and finger state to reduce noise
                    var timeSinceLastUnknownLog = DateTime.Now - lastUnknownScanLogTime;
                    bool shouldLog = unauthorizedDoorOpened || (!fingerOnThisSensor && timeSinceLastUnknownLog.TotalMilliseconds >= UNKNOWN_SCAN_DEBOUNCE_MS);

                    if (shouldLog)
                    {
                        try
                        {
                            if (dbManager != null)
                            {
                                string logResult = unauthorizedDoorOpened ? "granted" : "denied";
                                string logReason = unauthorizedDoorOpened
                                    ? $"Door override enabled for unknown fingerprint (location: {location ?? "inside"})"
                                    : $"Unknown fingerprint - no match found (matchIndex: {matchIndex}, success: {success})";

                                dbManager.LogAccessAttempt(
                                    userId: null,
                                    roomId: null,
                                    authMethod: "Fingerprint",
                                    location: location ?? "inside",
                                    accessType: "attendance_scan",
                                    result: logResult,
                                    reason: logReason
                                );
                                Console.WriteLine($"📝 Logged unknown fingerprint scan to ACCESSLOGS: {location} sensor ({logResult})");
                                lastUnknownScanLogTime = DateTime.Now;
                            }
                        }
                        catch (Exception logEx)
                        {
                            Console.WriteLine($"⚠️ Failed to log unknown fingerprint scan: {logEx.Message}");
                        }
                    }
                    else if (!unauthorizedDoorOpened && !fingerOnThisSensor)
                    {
                        Console.WriteLine($"⏳ Unknown scan debounced - waiting {Math.Ceiling((UNKNOWN_SCAN_DEBOUNCE_MS - timeSinceLastUnknownLog.TotalMilliseconds) / 1000)}s before logging again");
                    }

                    if (location == "inside")
                    {
                        dualSensorPanel?.UpdateInsideLastScan("Unknown", panelStatusMessage, panelStatusSuccess);
                        dualSensorPanel?.UpdateInsideStatus("Active");
                    }
                    else
                    {
                        dualSensorPanel?.UpdateOutsideLastScan("Unknown", panelStatusMessage, panelStatusSuccess);
                        dualSensorPanel?.UpdateOutsideStatus("Active");
                    }

                    if (unauthorizedDoorOpened)
                    {
                        dualSensorPanel?.AddActivityItem(new ActivityItem
                        {
                            Timestamp = DateTime.Now,
                            UserName = "Unknown Fingerprint",
                            Action = "Door Override",
                            Location = location ?? "inside",
                            Success = true,
                            StatusMessage = panelStatusMessage
                        });
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

                Utils.Logger.Debug($"Loading users for identification from database...");
                Utils.Logger.Debug($"Total users loaded: {users.Count}");

                foreach (var user in users)
                {
                    // Enhanced template validation
                    if (user.FingerprintTemplate != null &&
                        user.FingerprintTemplate.Length > 0 &&
                        IsValidFingerprintTemplate(user.FingerprintTemplate))
                    {
                        Utils.Logger.Debug($"Valid template for user: {user.Username} ({user.UserType}) - Template size: {user.FingerprintTemplate.Length} bytes");

                        userRecords.Add(new UserRecord
                        {
                            UserName = user.Username,
                            Template = user.FingerprintTemplate
                        });
                    }
                    else
                    {
                        // Don't log invalid templates - too noisy
                    }
                }

                Utils.Logger.Info($"Loaded {userRecords.Count} user records for identification");
                Utils.Logger.Debug($"Instructors: {userRecords.Count(u => u.UserName.Contains("aurora") || u.UserName.Contains("AURORA"))}");
                Utils.Logger.Debug($"Students: {userRecords.Count - userRecords.Count(u => u.UserName.Contains("aurora") || u.UserName.Contains("AURORA"))}");

                return userRecords;
            }
            catch (Exception ex)
            {
                Utils.Logger.Error($"Error loading users for identification: {ex.Message}");
                return new List<UserRecord>();
            }
        }

        private bool IsValidFingerprintTemplate(byte[] template)
        {
            if (template == null || template.Length == 0)
                return false;

            // Check minimum template size (Futronic templates are typically 500-2000 bytes)
            if (template.Length < 100 || template.Length > 5000)
            {
                Console.WriteLine($"⚠️ Template size out of range: {template.Length} bytes");
                return false;
            }

            // Check for all zeros (corrupted template)
            bool hasNonZeroData = false;
            for (int i = 0; i < Math.Min(template.Length, 100); i++)
            {
                if (template[i] != 0)
                {
                    hasNonZeroData = true;
                    break;
                }
            }

            if (!hasNonZeroData)
            {
                Console.WriteLine($"⚠️ Template appears to be all zeros (corrupted)");
                return false;
            }

            // Check for Futronic template header pattern (basic validation)
            // Futronic templates typically start with specific byte patterns
            if (template.Length >= 4)
            {
                // Check if it looks like a valid template structure
                int headerSum = template[0] + template[1] + template[2] + template[3];
                if (headerSum == 0 || headerSum > 1000) // Too uniform or too high
                {
                    Console.WriteLine($"⚠️ Template header pattern suspicious: {headerSum}");
                    return false;
                }
            }

            return true;
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