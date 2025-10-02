namespace FutronicAttendanceSystem.Database.Config
{
    public class DatabaseConfig
    {
        public string Server { get; set; } = "localhost";
        public string Database { get; set; } = "iot_attendance";
        public string Username { get; set; } = "root";
        public string Password { get; set; } = "";
        public int Port { get; set; } = 3306;
        public int ConnectionTimeout { get; set; } = 30;
        public int CommandTimeout { get; set; } = 60;

        public string GetConnectionString()
        {
            return $"Server={Server};Port={Port};Database={Database};Uid={Username};Pwd={Password};Connection Timeout={ConnectionTimeout};Command Timeout={CommandTimeout};";
        }
    }
}
