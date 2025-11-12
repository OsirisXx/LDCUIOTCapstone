namespace FutronicAttendanceSystem.Database.Models
{
    public class UserFingerprint
    {
        public string UserId { get; set; }
        public string Username { get; set; }
        public string UserType { get; set; }
        public string AuthId { get; set; }
        public byte[] Template { get; set; }
        public string Identifier { get; set; }
    }
}

