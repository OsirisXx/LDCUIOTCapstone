using System;

namespace FutronicAttendanceSystem.Database.Models
{
    public class Room
    {
        public string RoomId { get; set; }
        public string RoomNumber { get; set; }
        public string RoomName { get; set; }
        public string Building { get; set; }
        public int? Capacity { get; set; }
        public string DeviceId { get; set; }
        public string DoorStatus { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string DisplayName => $"{RoomNumber} - {RoomName} ({Building})";
        public string FullDisplayName => $"{Building} - {RoomNumber} {RoomName}";
    }
}

