using System;
using System.IO;
using BarRaider.SdTools;

namespace starcitizen.Core
{
    /// <summary>
    /// Centralized logging utility for the Star Citizen Stream Deck plugin.
    /// Writes to both Stream Deck's internal log and a plugin-specific file.
    /// </summary>
    /// <remarks>
    /// Log file location: %appdata%\Elgato\StreamDeck\Plugins\com.ltmajor42.starcitizen.sdPlugin\pluginlog.log
    /// Automatic rotation at 5MB with one backup file retained.
    /// </remarks>
    internal static class PluginLog
    {
        private static readonly object FileLock = new object();
        private static readonly string LogFilePath;
        private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB
        private static volatile bool _disposed;

        static PluginLog()
        {
            try
            {
                var pluginDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "Plugins", "com.ltmajor42.starcitizen.sdPlugin");
                if (!Directory.Exists(pluginDir))
                {
                    Directory.CreateDirectory(pluginDir);
                }

                LogFilePath = Path.Combine(pluginDir, "pluginlog.log");
                RotateIfNeeded();
            }
            catch
            {
                // fallback: use current directory
                LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "pluginlog.log");
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (File.Exists(LogFilePath))
                {
                    var fi = new FileInfo(LogFilePath);
                    if (fi.Length > MaxLogSizeBytes)
                    {
                        var archived = LogFilePath + ".1";
                        try { File.Delete(archived); } catch { }
                        File.Move(LogFilePath, archived);
                    }
                }
            }
            catch
            {
                // ignore rotation failures
            }
        }

        private static void WriteToFile(string level, string message)
        {
            if (_disposed) return;

            try
            {
                lock (FileLock)
                {
                    if (_disposed) return;
                    
                    RotateIfNeeded();
                    File.AppendAllText(LogFilePath, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.ffff}|{level}|{message}{Environment.NewLine}");
                }
            }
            catch
            {
                // swallow file logging errors to avoid breaking the plugin
            }
        }

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        public static void Info(string message)
        {
            if (_disposed) return;
            try { Logger.Instance.LogMessage(TracingLevel.INFO, message); } catch { }
            WriteToFile("INFO", message);
        }

        /// <summary>
        /// Logs a debug message. Useful for development troubleshooting.
        /// </summary>
        public static void Debug(string message)
        {
            if (_disposed) return;
            try { Logger.Instance.LogMessage(TracingLevel.DEBUG, message); } catch { }
            WriteToFile("DEBUG", message);
        }

        /// <summary>
        /// Logs a warning message. Indicates a potential issue that doesn't prevent operation.
        /// </summary>
        public static void Warn(string message)
        {
            if (_disposed) return;
            try { Logger.Instance.LogMessage(TracingLevel.WARN, message); } catch { }
            WriteToFile("WARN", message);
        }

        /// <summary>
        /// Logs an error message. Indicates a failure that may affect functionality.
        /// </summary>
        public static void Error(string message)
        {
            if (_disposed) return;
            try { Logger.Instance.LogMessage(TracingLevel.ERROR, message); } catch { }
            WriteToFile("ERROR", message);
        }

        /// <summary>
        /// Logs a fatal message. Indicates a critical failure that prevents operation.
        /// </summary>
        public static void Fatal(string message)
        {
            if (_disposed) return;
            try { Logger.Instance.LogMessage(TracingLevel.FATAL, message); } catch { }
            WriteToFile("FATAL", message);
        }

        /// <summary>
        /// Flushes any pending log entries and marks the logger as disposed.
        /// Call this during shutdown to ensure all messages are written.
        /// </summary>
        public static void Shutdown()
        {
            if (_disposed) return;
            
            // Write final shutdown marker
            WriteToFile("INFO", "PluginLog shutdown complete");
            
            _disposed = true;
        }
    }
}
