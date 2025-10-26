using System;

namespace FutronicAttendanceSystem.Utils
{
    public enum LogLevel
    {
        None = 0,
        Error = 1,
        Warning = 2,
        Info = 3,
        Debug = 4
    }

    public static class Logger
    {
        private static LogLevel currentLevel = LogLevel.Info; // Default to Info level

        public static void SetLogLevel(LogLevel level)
        {
            currentLevel = level;
        }

        public static void Error(string message)
        {
            if (currentLevel >= LogLevel.Error)
            {
                Console.WriteLine($"❌ ERROR: {message}");
            }
        }

        public static void Warning(string message)
        {
            if (currentLevel >= LogLevel.Warning)
            {
                Console.WriteLine($"⚠️ WARNING: {message}");
            }
        }

        public static void Info(string message)
        {
            if (currentLevel >= LogLevel.Info)
            {
                Console.WriteLine($"ℹ️ INFO: {message}");
            }
        }

        public static void Debug(string message)
        {
            if (currentLevel >= LogLevel.Debug)
            {
                Console.WriteLine($"🔍 DEBUG: {message}");
            }
        }

        public static void Success(string message)
        {
            if (currentLevel >= LogLevel.Info)
            {
                Console.WriteLine($"✅ SUCCESS: {message}");
            }
        }
    }
}

