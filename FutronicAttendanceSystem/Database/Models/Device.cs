using System;

namespace FutronicAttendanceSystem.Database.Models
{
    public class Device
    {
        public string DeviceId { get; set; }
        public string DeviceType { get; set; }
        public string DeviceName { get; set; }
        public string Location { get; set; }
        public string RoomId { get; set; }
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public string FirmwareVersion { get; set; }
        public DateTime? LastMaintenance { get; set; }
        public DateTime? LastSeen { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation properties
        public Room Room { get; set; }

        public string DisplayName => $"{DeviceName} ({Location})";
        public bool IsActive => Status == "Active";
    }
}