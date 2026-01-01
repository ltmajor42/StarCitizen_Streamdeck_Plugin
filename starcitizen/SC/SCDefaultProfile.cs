using System;
using System.IO;
using System.Threading;
using BarRaider.SdTools;

namespace SCJMapper_V2.SC
{
    /// <summary>
    /// Finds and returns the DefaultProfile from SC GameData.pak
    /// it is located in GameData.pak \Libs\Config
    /// </summary>
    class SCDefaultProfile
    {
        private static string m_defProfileCached = ""; // cache...

        /// <summary>
        /// Returns a list of files found that match 'defaultProfile*.xml'
        /// 20151220BM: return only the single defaultProfile name
        /// </summary>
        static public string DefaultProfileName
        {
            get { return "defaultProfile.xml"; }
        }

        /// <summary>
        /// Returns the sought default profile as string from various locations
        /// SC Alpha 2.2: Have to find the new one in ...\DataXML.pak (contains the binary XML now)
        /// </summary>
        static public string DefaultProfile()
        {
            string retVal = m_defProfileCached;
            if (!string.IsNullOrEmpty(retVal))
            {
                return retVal; // Return cached defaultProfile
            }

            retVal = SCFiles.Instance.DefaultProfile;
            if (!string.IsNullOrEmpty(retVal))
            {
                m_defProfileCached = retVal;
                return retVal; // EXIT
            }

            return retVal; // EXIT
        }

        /// <summary>
        /// Reads the user's live keybind override file (actionmaps.xml) from the SC client profile path.
        /// Important: must allow FileShare.ReadWrite because the game may write it while we read it.
        /// </summary>
        public static string ActionMaps()
        {
            return ActionMaps(out _);
        }

        public static string ActionMaps(out string resolvedPath)
        {
            resolvedPath = SCPath.ResolveActionMapsPath();
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, "Could not resolve actionmaps.xml path.");
                return "";
            }

            Logger.Instance.LogMessage(TracingLevel.INFO, $"Attempting to read actionmaps.xml at: {resolvedPath}");
            var content = ReadActionMapsWithStabilization(resolvedPath);
            if (content != null)
            {
                return content;
            }

            Logger.Instance.LogMessage(TracingLevel.WARN, "actionmaps.xml not found.");
            return "";
        }

        /// <summary>
        /// Read the actionmaps file only after it appears stable (size/write time unchanged across reads).
        /// This reduces flaky parses when the game is still flushing the file.
        /// </summary>
        private static string ReadActionMapsWithStabilization(string path, int maxAttempts = 5, int stabilizationDelayMs = 150)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var firstInfo = new FileInfo(path);
                    var firstWrite = firstInfo.LastWriteTimeUtc;
                    var firstLength = firstInfo.Length;

                    string content;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                    {
                        content = reader.ReadToEnd();
                    }

                    Thread.Sleep(stabilizationDelayMs);

                    var secondInfo = new FileInfo(path);
                    var secondWrite = secondInfo.LastWriteTimeUtc;
                    var secondLength = secondInfo.Length;

                    if (firstWrite == secondWrite && firstLength == secondLength)
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"actionmaps.xml stabilized on attempt {attempt}: {path}");
                        return content;
                    }

                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"actionmaps.xml changed between reads (attempt {attempt}/{maxAttempts}), retrying...");
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"actionmaps.xml read failed (attempt {attempt}/{maxAttempts}): {ex.Message}");
                }
            }

            Logger.Instance.LogMessage(TracingLevel.WARN, $"actionmaps.xml never stabilized after {maxAttempts} attempts.");
            return null;
        }
    }
}
