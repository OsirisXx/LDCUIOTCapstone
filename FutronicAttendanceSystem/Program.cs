using System;
using System.Windows.Forms;
using FutronicAttendanceSystem.Utils;
using FutronicAttendanceSystem.Database;
using FutronicAttendanceSystem.UI;

namespace FutronicAttendanceSystem
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Test database connection first
            Console.WriteLine("=== Dual Sensor Attendance System Startup ===");
            
            try
            {
                Console.WriteLine("Loading configuration...");
                var config = ConfigManager.Instance;
                
                Console.WriteLine($"Config loaded: Database={config.Database?.Database}, Server={config.Database?.Server}");
                
                Console.WriteLine("Creating DatabaseManager...");
                var dbManager = new DatabaseManager(
                    config.Database,
                    config.Device.DeviceId,
                    config.Device.Location
                );
                
                Console.WriteLine("Database connection successful!");
                
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                // Check if device configuration exists
                var deviceConfigManager = DeviceConfigManager.Instance;
                DeviceConfiguration deviceConfig = deviceConfigManager.LoadConfiguration();
                
                // Validate configuration against actual connected devices
                var availableDevices = UsbDeviceHelper.EnumerateFingerprintDevices();
                bool needsReconfiguration = false;
                
                if (deviceConfig != null)
                {
                    Console.WriteLine($"✅ Configuration loaded from {System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_config.json")}");
                    Console.WriteLine($"   Room: {deviceConfig.RoomName} ({deviceConfig.Building})");
                    Console.WriteLine($"   Inside Sensor: {deviceConfig.InsideSensor?.DeviceId} (Enabled: {deviceConfig.InsideSensor?.Enabled})");
                    Console.WriteLine($"   Outside Sensor: {deviceConfig.OutsideSensor?.DeviceId} (Enabled: {deviceConfig.OutsideSensor?.Enabled})");
                    
                    // Check if configured devices still exist
                    if (availableDevices.Count == 0)
                    {
                        Console.WriteLine("⚠️ No fingerprint devices detected!");
                        needsReconfiguration = true;
                    }
                    else if (deviceConfig.InsideSensor?.SensorIndex == deviceConfig.OutsideSensor?.SensorIndex)
                    {
                        Console.WriteLine("⚠️ Both sensors are configured to use the same device index!");
                        Console.WriteLine("   This is OK for testing, but only one sensor will be active at a time.");
                    }
                }
                
                // If no saved configuration, show startup dialog
                if (deviceConfig == null || needsReconfiguration)
                {
                    if (deviceConfig == null)
                    {
                        Console.WriteLine("ℹ️ No device configuration found. Showing startup dialog...");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ Configuration validation failed. Showing startup dialog...");
                    }
                    
                    deviceConfig = StartupConfigDialog.ShowConfigDialog(dbManager);
                    
                    if (deviceConfig == null)
                    {
                        Console.WriteLine("Configuration cancelled by user. Exiting...");
                        dbManager.Dispose();
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"✅ Loaded existing configuration:");
                    Console.WriteLine($"   Room: {deviceConfig.RoomName}");
                    Console.WriteLine($"   Inside Sensor: {deviceConfig.InsideSensor?.DeviceId}");
                    Console.WriteLine($"   Outside Sensor: {deviceConfig.OutsideSensor?.DeviceId}");
                    Console.WriteLine($"   Test Mode: {deviceConfig.TestMode}");
                }
                
                Console.WriteLine("Starting Windows Forms application with dual sensor support...");
                
                // Start MainForm with device configuration
                Application.Run(new MainForm(dbManager, deviceConfig));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ STARTUP ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                
                MessageBox.Show(
                    $"Failed to start application:\n\n{ex.Message}\n\nPlease check the console for details.",
                    "Startup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }
        }
    }
}

