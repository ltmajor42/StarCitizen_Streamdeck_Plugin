using System;
using System.IO;
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

            Logger.Instance.LogMessage(TracingLevel.INFO, resolvedPath);

            if (File.Exists(resolvedPath))
            {
                try
                {
                    using (var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed reading actionmaps.xml: {ex}");
                    return "";
                }
            }

            Logger.Instance.LogMessage(TracingLevel.WARN, "actionmaps.xml not found.");
            return "";
        }
    }
}
