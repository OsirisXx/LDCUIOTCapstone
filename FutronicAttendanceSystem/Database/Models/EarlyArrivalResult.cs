namespace FutronicAttendanceSystem.Database.Models
{
    public class EarlyArrivalResult
    {
        public bool Success { get; set; }
        public string Reason { get; set; }
        public string SubjectCode { get; set; }
        public string SubjectName { get; set; }
        public string ClassTime { get; set; }
        public string AttendanceId { get; set; }
    }
}

