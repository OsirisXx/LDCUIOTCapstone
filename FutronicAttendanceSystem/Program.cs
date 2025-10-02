using System;
using System.Windows.Forms;
using FutronicAttendanceSystem.Utils;
using FutronicAttendanceSystem.Database;

namespace FutronicAttendanceSystem
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Test database connection first
            Console.WriteLine("=== Database Connection Test ===");
            
            try
            {
                Console.WriteLine("Loading configuration...");
                var config = ConfigManager.Instance;
                
                Console.WriteLine($"Config loaded: Database={config.Database?.Database}, Server={config.Database?.Server}");
                
                Console.WriteLine("Creating DatabaseManager...");
                var dbManager = new DatabaseManager(
                    config.Database,
                    config.Device.DeviceId,
                    config.Device.Location
                );
                
                Console.WriteLine("Database connection successful!");
                dbManager.Dispose();
                Console.WriteLine("Starting Windows Forms application...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DATABASE ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }
            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}

