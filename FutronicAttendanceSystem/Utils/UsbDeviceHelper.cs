using System;
using System.Collections.Generic;
using System.Management;

namespace FutronicAttendanceSystem.Utils
{
    /// <summary>
    /// Represents a USB device
    /// </summary>
    public class UsbDeviceInfo
    {
        public string DevicePath { get; set; }
        public string DeviceId { get; set; }
        public string Description { get; set; }
        public string Manufacturer { get; set; }
        public int DeviceIndex { get; set; }
        public string FriendlyName { get; set; }

        public override string ToString()
        {
            return FriendlyName ?? $"Sensor #{DeviceIndex + 1}";
        }
    }

    /// <summary>
    /// Helper class for enumerating USB fingerprint devices
    /// </summary>
    public static class UsbDeviceHelper
    {
        /// <summary>
        /// Enumerate available Futronic fingerprint scanners
        /// </summary>
        public static List<UsbDeviceInfo> EnumerateFingerprintDevices()
        {
            var devices = new List<UsbDeviceInfo>();

            try
            {
                // Query for USB devices
                using (var searcher = new ManagementObjectSearcher(@"Select * From Win32_PnPEntity"))
                {
                    int deviceIndex = 0;
                    foreach (var device in searcher.Get())
                    {
                        try
                        {
                            string deviceId = device["DeviceID"]?.ToString();
                            string description = device["Description"]?.ToString() ?? "";
                            string manufacturer = device["Manufacturer"]?.ToString() ?? "";
                            string name = device["Name"]?.ToString() ?? "";

                            // Check if this is a Futronic device or generic fingerprint reader
                            // Futronic VID: 1491 (hex) or 5265 (decimal)
                            if (deviceId != null && (
                                deviceId.Contains("VID_1491") || // Futronic vendor ID
                                deviceId.Contains("VID_147E") || // Another possible Futronic VID
                                description.ToLower().Contains("futronic") ||
                                description.ToLower().Contains("fingerprint") ||
                                name.ToLower().Contains("futronic") ||
                                name.ToLower().Contains("fingerprint")))
                            {
                                var usbDevice = new UsbDeviceInfo
                                {
                                    DevicePath = deviceId,
                                    DeviceId = deviceId,
                                    Description = description,
                                    Manufacturer = manufacturer,
                                    DeviceIndex = deviceIndex,
                                    FriendlyName = $"Sensor #{deviceIndex + 1} ({description})"
                                };

                                devices.Add(usbDevice);
                                deviceIndex++;

                                Console.WriteLine($"🔍 Found fingerprint device: {usbDevice.FriendlyName}");
                                Console.WriteLine($"   Device ID: {deviceId}");
                                Console.WriteLine($"   Manufacturer: {manufacturer}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Error processing device: {ex.Message}");
                        }
                    }
                }

                if (devices.Count == 0)
                {
                    Console.WriteLine("⚠️ No fingerprint devices detected");
                    Console.WriteLine("ℹ️ Please ensure fingerprint scanners are connected and drivers are installed");
                }
                else
                {
                    Console.WriteLine($"✅ Total fingerprint devices found: {devices.Count}");
                }
                
                return devices;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error enumerating USB devices: {ex.Message}");
                return new List<UsbDeviceInfo>(); // Return empty list, no mocks
            }
        }

        /// <summary>
        /// Get device by index (for Futronic SDK)
        /// </summary>
        public static UsbDeviceInfo GetDeviceByIndex(int index)
        {
            var devices = EnumerateFingerprintDevices();
            if (index >= 0 && index < devices.Count)
            {
                return devices[index];
            }
            return null;
        }

        /// <summary>
        /// Get total number of connected fingerprint devices
        /// </summary>
        public static int GetDeviceCount()
        {
            return EnumerateFingerprintDevices().Count;
        }
    }
}

