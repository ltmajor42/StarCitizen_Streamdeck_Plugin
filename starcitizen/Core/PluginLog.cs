using System;
using System.IO;
using BarRaider.SdTools;

namespace starcitizen.Core
{
    internal static class PluginLog
    {
        private static readonly object FileLock = new object();
        private static readonly string LogFilePath;
        private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB

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
            try
            {
                lock (FileLock)
                {
                    RotateIfNeeded();
                    File.AppendAllText(LogFilePath, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.ffff}|{level}|{message}{Environment.NewLine}");
                }
            }
            catch
            {
                // swallow file logging errors to avoid breaking the plugin
            }
        }

        public static void Info(string message)
        {
            try { Logger.Instance.LogMessage(TracingLevel.INFO, message); } catch { }
            WriteToFile("INFO", message);
        }

        public static void Debug(string message)
        {
            try { Logger.Instance.LogMessage(TracingLevel.DEBUG, message); } catch { }
            WriteToFile("DEBUG", message);
        }

        public static void Warn(string message)
        {
            try { Logger.Instance.LogMessage(TracingLevel.WARN, message); } catch { }
            WriteToFile("WARN", message);
        }

        public static void Error(string message)
        {
            try { Logger.Instance.LogMessage(TracingLevel.ERROR, message); } catch { }
            WriteToFile("ERROR", message);
        }

        public static void Fatal(string message)
        {
            try { Logger.Instance.LogMessage(TracingLevel.FATAL, message); } catch { }
            WriteToFile("FATAL", message);
        }
    }
}
