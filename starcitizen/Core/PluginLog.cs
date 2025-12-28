using BarRaider.SdTools;

namespace starcitizen.Core
{
    internal static class PluginLog
    {
        public static void Info(string message) => Logger.Instance.LogMessage(TracingLevel.INFO, message);

        public static void Warn(string message) => Logger.Instance.LogMessage(TracingLevel.WARN, message);

        public static void Error(string message) => Logger.Instance.LogMessage(TracingLevel.ERROR, message);

        public static void Fatal(string message) => Logger.Instance.LogMessage(TracingLevel.FATAL, message);
    }
}
