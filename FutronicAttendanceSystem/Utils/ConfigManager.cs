using System;
using System.IO;
using Newtonsoft.Json;
using FutronicAttendanceSystem.Database.Config;

namespace FutronicAttendanceSystem.Utils
{
    public class ConfigManager
    {
        private static ConfigManager _instance;
        private static readonly object _lock = new object();
        
        public DatabaseConfig Database { get; private set; }
        public DeviceConfig Device { get; private set; }
        public ApplicationConfig Application { get; private set; }

        private ConfigManager()
        {
            LoadConfiguration();
        }

        public static ConfigManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new ConfigManager();
                    }
                }
                return _instance;
            }
        }

        private void LoadConfiguration()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                System.Diagnostics.Debug.WriteLine($"Looking for config at: {configPath}");
                Console.WriteLine($"Looking for config at: {configPath}");
                
                if (!File.Exists(configPath))
                {
                    System.Diagnostics.Debug.WriteLine("Config file not found, creating default");
                Console.WriteLine("Config file not found, creating default");
                    // Create default configuration
                    CreateDefaultConfiguration(configPath);
                }

                string json = File.ReadAllText(configPath);
                System.Diagnostics.Debug.WriteLine($"Config JSON: {json}");
                
                var config = JsonConvert.DeserializeObject<AppConfig>(json);
                System.Diagnostics.Debug.WriteLine($"Deserialized config: {config != null}");

                // Null guards to avoid NREs when config sections are missing
                Database = config?.Database ?? new DatabaseConfig();
                Device = config?.Device ?? new DeviceConfig();
                Application = config?.Application ?? new ApplicationConfig();
                
                System.Diagnostics.Debug.WriteLine($"Database config: Server={Database?.Server}, Database={Database?.Database}");
                Console.WriteLine($"Database config: Server={Database?.Server}, Database={Database?.Database}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Configuration load error: {ex}");
                Console.WriteLine($"Configuration load error: {ex}");
                // Fallback to default configuration
                SetDefaultConfiguration();
            }
        }

        private void CreateDefaultConfiguration(string configPath)
        {
            var defaultConfig = new AppConfig
            {
                Database = new DatabaseConfig(),
                Device = new DeviceConfig(),
                Application = new ApplicationConfig()
            };

            string json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
            File.WriteAllText(configPath, json);
        }

        private void SetDefaultConfiguration()
        {
            Database = new DatabaseConfig
            {
                Server = "localhost",
                Database = "iot_attendance",
                Username = "root",
                Password = "",
                Port = 3306
            };
            Device = new DeviceConfig();
            Application = new ApplicationConfig();
        }

        public class AppConfig
        {
            public DatabaseConfig Database { get; set; }
            public DeviceConfig Device { get; set; }
            public ApplicationConfig Application { get; set; }
        }
    }

    public class DeviceConfig
    {
        public string DeviceId { get; set; } = Environment.MachineName + "_" + Environment.UserName;
        public string Location { get; set; } = "Main Office";
        public string Building { get; set; } = "Building A";
    }

    public class ApplicationConfig
    {
        public bool AlwaysOnAttendance { get; set; } = true;
        public int MaxSecondScanAttempts { get; set; } = 3;
        public int HeartbeatInterval { get; set; } = 30000; // 30 seconds
        public int SyncInterval { get; set; } = 300000; // 5 minutes
    }
}
