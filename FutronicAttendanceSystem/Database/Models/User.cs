using System;
using Futronic.SDKHelper;

namespace FutronicAttendanceSystem.Database.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public byte[] FingerprintTemplate { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string UserType { get; set; }
        public string EmployeeId { get; set; }
        public string Department { get; set; }
        public string RfidTag { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public bool IsActive { get; set; } = true;
        public bool EnableRfid { get; set; } = true;
        public bool EnableFingerprint { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Helper method to convert to Futronic record
        public FtrIdentifyRecord GetFtrIdentifyRecord()
        {
            var record = new FtrIdentifyRecord();
            
            // Set key value (first 16 bytes of template or padded)
            byte[] keyValue = new byte[16];
            
            // Add null check to prevent index out of bounds error
            if (FingerprintTemplate != null && FingerprintTemplate.Length > 0)
            {
                if (FingerprintTemplate.Length >= 16)
                {
                    Array.Copy(FingerprintTemplate, keyValue, 16);
                }
                else
                {
                    Array.Copy(FingerprintTemplate, keyValue, FingerprintTemplate.Length);
                }
            }
            // If FingerprintTemplate is null or empty, keyValue remains all zeros
            
            record.KeyValue = keyValue;
            record.Template = FingerprintTemplate ?? new byte[0]; // Prevent null template
            return record;
        }
    }
}
