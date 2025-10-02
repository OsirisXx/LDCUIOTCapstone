using System;

namespace FutronicAttendanceSystem.Database.Models
{
    public class AttendanceRecord
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int DeviceId { get; set; }
        public string Action { get; set; } // "Check In" or "Check Out"
        public DateTime Timestamp { get; set; }
        public string Location { get; set; }
        public string Notes { get; set; }
		public string Status { get; set; } // "Success" or "Denied: <reason>"
        
        // Navigation properties (for joins)
        public string Username { get; set; } // Populated from User table
        public string DeviceName { get; set; } // Populated from Device table
    }
}
