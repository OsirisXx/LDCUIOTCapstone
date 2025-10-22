using System;
using System.IO;
using Newtonsoft.Json;

namespace FutronicAttendanceSystem.Utils
{
    /// <summary>
    /// Configuration for a single sensor (inside or outside)
    /// </summary>
    public class SensorConfig
    {
        public string UsbDevicePath { get; set; }
        public string DeviceId { get; set; }
        public bool Enabled { get; set; } = true;
        public int SensorIndex { get; set; } // For Futronic SDK device enumeration
    }

    /// <summary>
    /// Device configuration for dual sensor setup
    /// </summary>
    public class DeviceConfiguration
    {
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string Building { get; set; }
        public SensorConfig InsideSensor { get; set; }
        public SensorConfig OutsideSensor { get; set; }
        public bool TestMode { get; set; } = false;
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Manages device configuration persistence
    /// </summary>
    public class DeviceConfigManager
    {
        private static DeviceConfigManager _instance;
        private static readonly object _lock = new object();
        
        private const string CONFIG_FILENAME = "device_config.json";
        private string _configFilePath;
        private DeviceConfiguration _currentConfig;

        private DeviceConfigManager()
        {
            // Store config in the application's directory
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _configFilePath = Path.Combine(appDirectory, CONFIG_FILENAME);
        }

        public static DeviceConfigManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new DeviceConfigManager();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Load configuration from file
        /// </summary>
        public DeviceConfiguration LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    _currentConfig = JsonConvert.DeserializeObject<DeviceConfiguration>(json);
                    
                    Console.WriteLine($"✅ Configuration loaded from {_configFilePath}");
                    Console.WriteLine($"   Room: {_currentConfig?.RoomName}");
                    Console.WriteLine($"   Inside Sensor: {_currentConfig?.InsideSensor?.DeviceId} (Enabled: {_currentConfig?.InsideSensor?.Enabled})");
                    Console.WriteLine($"   Outside Sensor: {_currentConfig?.OutsideSensor?.DeviceId} (Enabled: {_currentConfig?.OutsideSensor?.Enabled})");
                    
                    return _currentConfig;
                }
                else
                {
                    Console.WriteLine($"ℹ️ No existing configuration found at {_configFilePath}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading configuration: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save configuration to file
        /// </summary>
        public bool SaveConfiguration(DeviceConfiguration config)
        {
            try
            {
                config.LastUpdated = DateTime.Now;
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
                
                _currentConfig = config;
                
                Console.WriteLine($"✅ Configuration saved to {_configFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error saving configuration: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get current configuration
        /// </summary>
        public DeviceConfiguration GetCurrentConfiguration()
        {
            return _currentConfig;
        }

        /// <summary>
        /// Check if configuration exists
        /// </summary>
        public bool ConfigurationExists()
        {
            return File.Exists(_configFilePath);
        }

        /// <summary>
        /// Delete configuration file
        /// </summary>
        public bool DeleteConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    File.Delete(_configFilePath);
                    _currentConfig = null;
                    Console.WriteLine($"✅ Configuration deleted");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error deleting configuration: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Update sensor enabled state
        /// </summary>
        public bool UpdateSensorEnabledState(string sensorPosition, bool enabled)
        {
            try
            {
                if (_currentConfig == null)
                {
                    Console.WriteLine("❌ No configuration loaded");
                    return false;
                }

                if (sensorPosition.ToLower() == "inside" && _currentConfig.InsideSensor != null)
                {
                    _currentConfig.InsideSensor.Enabled = enabled;
                }
                else if (sensorPosition.ToLower() == "outside" && _currentConfig.OutsideSensor != null)
                {
                    _currentConfig.OutsideSensor.Enabled = enabled;
                }
                else
                {
                    Console.WriteLine($"❌ Invalid sensor position: {sensorPosition}");
                    return false;
                }

                return SaveConfiguration(_currentConfig);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error updating sensor state: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get config file path for debugging
        /// </summary>
        public string GetConfigFilePath()
        {
            return _configFilePath;
        }
    }
}





