using System;

namespace FutronicAttendanceSystem.Database.Models
{
    public class FingerprintInfo
    {
        public string AuthId { get; set; }
        public string Identifier { get; set; }
        public int FingerprintNumber { get; set; }
        public DateTime DateRegistered { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}

