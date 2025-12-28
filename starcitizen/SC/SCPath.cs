using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Linq;
using System.Text;
using System.IO;
using BarRaider.SdTools;
using Microsoft.Win32;
using p4ktest;
using TheUser = p4ktest.SC.TheUser;

//using SCJMapper_V2.Translation;

namespace SCJMapper_V2.SC
{
    /// <summary>
    /// Find the SC pathes and folders using multiple detection methods
    /// </summary>
    class SCPath
    {
        private static readonly string[] KNOWN_REGISTRY_KEYS = new[]
        {
            // Current and recent Star Citizen launcher registry keys
            @"SOFTWARE\81bfc699-f883-50c7-b674-2483b6baae23", // LIVE
            @"SOFTWARE\94a6df8a-d3f9-558d-bb04-097c192530b9", // PTU
            @"SOFTWARE\Cloud Imperium Games\Star Citizen",   // Alternative key
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\StarCitizen", // Uninstall key
        };

        private static readonly string[] COMMON_INSTALL_PATHS = new[]
        {
            // Standard RSI Launcher paths
            @"C:\Program Files\Roberts Space Industries\StarCitizen",
            @"C:\Program Files (x86)\Roberts Space Industries\StarCitizen",
            @"D:\Program Files\Roberts Space Industries\StarCitizen",
            @"D:\Program Files (x86)\Roberts Space Industries\StarCitizen",
            @"E:\Program Files\Roberts Space Industries\StarCitizen",
            @"E:\Program Files (x86)\Roberts Space Industries\StarCitizen",
            @"F:\Program Files\Roberts Space Industries\StarCitizen",
            @"F:\Program Files (x86)\Roberts Space Industries\StarCitizen",

            // Common game directories
            @"C:\Games\StarCitizen",
            @"D:\Games\StarCitizen",
            @"E:\Games\StarCitizen",
            @"F:\Games\StarCitizen",
            @"C:\StarCitizen",
            @"D:\StarCitizen",
            @"E:\StarCitizen",
            @"F:\StarCitizen",

            // Alternative drive letters
            @"G:\Program Files\Roberts Space Industries\StarCitizen",
            @"G:\Games\StarCitizen",
            @"G:\StarCitizen",
            @"H:\Program Files\Roberts Space Industries\StarCitizen",
            @"H:\Games\StarCitizen",
            @"H:\StarCitizen",

            // Epic Games Store paths
            @"C:\Program Files\Epic Games\StarCitizen",
            @"D:\Program Files\Epic Games\StarCitizen",
            @"E:\Program Files\Epic Games\StarCitizen",

            // Custom/user installations
            @"C:\Users\Public\Games\StarCitizen",
            @"D:\Users\Public\Games\StarCitizen",
            @"E:\Users\Public\Games\StarCitizen",
        };

        private static readonly string[] STEAM_LIBRARY_PATHS = new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\Star Citizen",
            @"D:\Steam\steamapps\common\Star Citizen",
            @"E:\Steam\steamapps\common\Star Citizen",
            @"F:\Steam\steamapps\common\Star Citizen",
            @"G:\Steam\steamapps\common\Star Citizen",
            @"C:\SteamLibrary\steamapps\common\Star Citizen",
            @"D:\SteamLibrary\steamapps\common\Star Citizen",
            @"E:\SteamLibrary\steamapps\common\Star Citizen",
        };

        private static readonly object PathCacheLock = new object();
        private static string cachedBasePath;
        private static bool cachedBasePathSet;

        private static readonly object ClientPathCacheLock = new object();
        private static string cachedClientPath;
        private static bool cachedClientPathSet;
        private static bool cachedClientPathUsePtu;
        private static string cachedClientBasePath;

        /// <summary>
        /// Try to find SC installation from RSI Launcher configuration files
        /// The RSI Launcher stores its library folder in %APPDATA%\rsilauncher\
        /// </summary>
        static private string FindInstallationFromRSILauncher()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "FindInstallationFromRSILauncher - Entry");

            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string rsiLauncherPath = Path.Combine(appDataPath, "rsilauncher");

                // Check for library_folder.json (newer launcher versions)
                string libraryFolderFile = Path.Combine(rsiLauncherPath, "library_folder.json");
                if (File.Exists(libraryFolderFile))
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindInstallationFromRSILauncher - Found library_folder.json: {libraryFolderFile}");
                    string json = File.ReadAllText(libraryFolderFile);
                    // Simple JSON parsing - look for path in quotes
                    int pathStart = json.IndexOf('"');
                    int pathEnd = json.LastIndexOf('"');
                    if (pathStart >= 0 && pathEnd > pathStart)
                    {
                        string libraryPath = json.Substring(pathStart + 1, pathEnd - pathStart - 1);
                        libraryPath = libraryPath.Replace("\\\\", "\\").Replace("\\/", "/").Replace("/", "\\");
                        Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindInstallationFromRSILauncher - Parsed library path: {libraryPath}");
                        
                        if (Directory.Exists(libraryPath) && IsValidStarCitizenInstallation(libraryPath))
                        {
                            Logger.Instance.LogMessage(TracingLevel.INFO, $"FindInstallationFromRSILauncher - Found via library_folder.json: {libraryPath}");
                            return libraryPath;
                        }
                    }
                }

                // Check for settings.json (contains InstallDir)
                string settingsFile = Path.Combine(rsiLauncherPath, "settings.json");
                if (File.Exists(settingsFile))
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindInstallationFromRSILauncher - Found settings.json: {settingsFile}");
                    string json = File.ReadAllText(settingsFile);
                    
                    // Look for "libraryFolder" or "installDir" in settings
                    string[] searchKeys = new[] { "\"libraryFolder\"", "\"library_folder\"", "\"installDir\"", "\"InstallDir\"" };
                    foreach (string key in searchKeys)
                    {
                        int keyIndex = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                        if (keyIndex >= 0)
                        {
                            // Find the value after the key
                            int colonIndex = json.IndexOf(':', keyIndex);
                            if (colonIndex >= 0)
                            {
                                int valueStart = json.IndexOf('"', colonIndex);
                                int valueEnd = json.IndexOf('"', valueStart + 1);
                                if (valueStart >= 0 && valueEnd > valueStart)
                                {
                                    string path = json.Substring(valueStart + 1, valueEnd - valueStart - 1);
                                    path = path.Replace("\\\\", "\\").Replace("\\/", "/").Replace("/", "\\");
                                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindInstallationFromRSILauncher - Found path in settings.json: {path}");
                                    
                                    if (Directory.Exists(path) && IsValidStarCitizenInstallation(path))
                                    {
                                        Logger.Instance.LogMessage(TracingLevel.INFO, $"FindInstallationFromRSILauncher - Found via settings.json: {path}");
                                        return path;
                                    }
                                }
                            }
                        }
                    }
                }

                // Check log files for installation path
                string logDir = Path.Combine(rsiLauncherPath, "logs");
                if (Directory.Exists(logDir))
                {
                    string[] logFiles = Directory.GetFiles(logDir, "*.log");
                    foreach (string logFile in logFiles.OrderByDescending(f => File.GetLastWriteTime(f)).Take(3))
                    {
                        try
                        {
                            string logContent = File.ReadAllText(logFile);
                            // Look for paths containing StarCitizen
                            var matches = System.Text.RegularExpressions.Regex.Matches(logContent, @"([A-Za-z]:\\[^""<>|\r\n]+?StarCitizen)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            foreach (System.Text.RegularExpressions.Match match in matches)
                            {
                                string path = match.Value;
                                // Clean up path
                                path = path.Replace("\\\\", "\\");
                                if (Directory.Exists(path) && IsValidStarCitizenInstallation(path))
                                {
                                    Logger.Instance.LogMessage(TracingLevel.INFO, $"FindInstallationFromRSILauncher - Found via log file: {path}");
                                    return path;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindInstallationFromRSILauncher - Error reading log {logFile}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindInstallationFromRSILauncher - Error: {ex.Message}");
            }

            Logger.Instance.LogMessage(TracingLevel.DEBUG, "FindInstallationFromRSILauncher - No valid installation found");
            return "";
        }

        /// <summary>
        /// Try to find SC launcher directory from various registry locations
        /// </summary>
        static private string FindLauncherFromRegistry()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "FindLauncherFromRegistry - Entry");

            foreach (string regKey in KNOWN_REGISTRY_KEYS)
            {
                try
                {
                    RegistryKey localKey;
                    if (Environment.Is64BitOperatingSystem)
                        localKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                    else
                        localKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);

                    using (RegistryKey key = localKey.OpenSubKey(regKey))
                    {
                        if (key != null)
                        {
                            object installLocation = key.GetValue("InstallLocation");
                            if (installLocation != null)
                            {
                                string scLauncher = installLocation.ToString();
                                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindLauncherFromRegistry - Found in {regKey}: {scLauncher}");

                                if (Directory.Exists(scLauncher))
                                {
                                    return scLauncher;
                                }
                                else
                                {
                                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindLauncherFromRegistry - Directory does not exist: {scLauncher}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindLauncherFromRegistry - Error checking {regKey}: {ex.Message}");
                }
            }

            Logger.Instance.LogMessage(TracingLevel.DEBUG, "FindLauncherFromRegistry - No valid launcher found in registry");
            return "";
        }

        /// <summary>
        /// Try to find SC installation by scanning common installation paths
        /// </summary>
        static private string FindInstallationFromCommonPaths()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "FindInstallationFromCommonPaths - Entry");

            foreach (string path in COMMON_INSTALL_PATHS)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindInstallationFromCommonPaths - Checking: {path}");

                if (Directory.Exists(path))
                {
                    // Check if this looks like a valid SC installation
                    if (IsValidStarCitizenInstallation(path))
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"FindInstallationFromCommonPaths - Found valid installation: {path}");
                        return path;
                    }
                }
            }

            Logger.Instance.LogMessage(TracingLevel.DEBUG, "FindInstallationFromCommonPaths - No valid installation found");
            return "";
        }

        /// <summary>
        /// Check if a directory contains a valid Star Citizen installation
        /// Supports multiple directory structures:
        /// 1. RSI Launcher style: path/StarCitizen/LIVE/Data.p4k
        /// 2. Direct style: path/LIVE/Data.p4k (when user points directly to StarCitizen folder)
        /// 3. Direct Data.p4k: path/Data.p4k
        /// </summary>
        static private bool IsValidStarCitizenInstallation(string path)
        {
            try
            {
                // Structure 1: RSI Launcher style - path/StarCitizen/LIVE
                string livePath = Path.Combine(path, "StarCitizen", "LIVE");
                string ptuPath = Path.Combine(path, "StarCitizen", "PTU");

                if (Directory.Exists(livePath))
                {
                    string dataP4k = Path.Combine(livePath, "Data.p4k");
                    if (File.Exists(dataP4k))
                    {
                        Logger.Instance.LogMessage(TracingLevel.DEBUG, $"IsValidStarCitizenInstallation - Found RSI style LIVE: {livePath}");
                        return true;
                    }
                }

                if (Directory.Exists(ptuPath))
                {
                    string dataP4k = Path.Combine(ptuPath, "Data.p4k");
                    if (File.Exists(dataP4k))
                    {
                        Logger.Instance.LogMessage(TracingLevel.DEBUG, $"IsValidStarCitizenInstallation - Found RSI style PTU: {ptuPath}");
                        return true;
                    }
                }

                // Structure 2: Direct style - path/LIVE (user points directly to StarCitizen folder)
                string directLivePath = Path.Combine(path, "LIVE");
                string directPtuPath = Path.Combine(path, "PTU");

                if (Directory.Exists(directLivePath))
                {
                    string dataP4k = Path.Combine(directLivePath, "Data.p4k");
                    if (File.Exists(dataP4k))
                    {
                        Logger.Instance.LogMessage(TracingLevel.DEBUG, $"IsValidStarCitizenInstallation - Found direct style LIVE: {directLivePath}");
                        return true;
                    }
                }

                if (Directory.Exists(directPtuPath))
                {
                    string dataP4k = Path.Combine(directPtuPath, "Data.p4k");
                    if (File.Exists(dataP4k))
                    {
                        Logger.Instance.LogMessage(TracingLevel.DEBUG, $"IsValidStarCitizenInstallation - Found direct style PTU: {directPtuPath}");
                        return true;
                    }
                }

                // Structure 3: Direct Data.p4k in the path
                string directDataP4k = Path.Combine(path, "Data.p4k");
                if (File.Exists(directDataP4k))
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"IsValidStarCitizenInstallation - Found direct Data.p4k: {directDataP4k}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"IsValidStarCitizenInstallation - Error checking {path}: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Try to find SC installation by scanning known Steam library paths
        /// </summary>
        static private string FindInstallationFromSteamPaths()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "FindInstallationFromSteamPaths - Entry");

            foreach (string path in STEAM_LIBRARY_PATHS)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindInstallationFromSteamPaths - Checking: {path}");

                if (Directory.Exists(path))
                {
                    // Check if this looks like a valid SC installation
                    if (IsValidStarCitizenInstallation(path))
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"FindInstallationFromSteamPaths - Found valid installation: {path}");
                        return path;
                    }
                }
            }

            Logger.Instance.LogMessage(TracingLevel.DEBUG, "FindInstallationFromSteamPaths - No valid installation found");
            return "";
        }

        /// <summary>
        /// Try to find SC installation by parsing Steam's config.vdf file for custom library locations
        /// </summary>
        static private string FindInstallationFromSteamConfig()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "FindInstallationFromSteamConfig - Entry");

            try
            {
                // Default Steam config location
                string steamConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "config", "config.vdf");

                if (!File.Exists(steamConfigPath))
                {
                    // Try SteamPath environment variable
                    string steamPath = Environment.GetEnvironmentVariable("SteamPath");
                    if (!string.IsNullOrEmpty(steamPath))
                    {
                        steamConfigPath = Path.Combine(steamPath, "config", "config.vdf");
                    }
                }

                if (!File.Exists(steamConfigPath))
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, "FindInstallationFromSteamConfig - Steam config not found");
                    return "";
                }

                // Parse the VDF file to find library folders
                var libraryPaths = ParseSteamConfigForLibraries(steamConfigPath);

                foreach (string libraryPath in libraryPaths)
                {
                    string scPath = Path.Combine(libraryPath, "steamapps", "common", "Star Citizen");
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindInstallationFromSteamConfig - Checking Steam library: {scPath}");

                    if (IsValidStarCitizenInstallation(scPath))
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"FindInstallationFromSteamConfig - Found valid installation: {scPath}");
                        return scPath;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FindInstallationFromSteamConfig - Error: {ex.Message}");
            }

            Logger.Instance.LogMessage(TracingLevel.DEBUG, "FindInstallationFromSteamConfig - No valid installation found");
            return "";
        }

        /// <summary>
        /// Parse Steam's config.vdf to extract library folder paths
        /// </summary>
        static private List<string> ParseSteamConfigForLibraries(string configPath)
        {
            var libraryPaths = new List<string>();

            try
            {
                string[] lines = File.ReadAllLines(configPath);
                bool inSoftwareSection = false;
                bool inSteamSection = false;
                bool inLibraryFolders = false;

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();

                    if (trimmed.Contains("\"Software\""))
                    {
                        inSoftwareSection = true;
                    }
                    else if (inSoftwareSection && trimmed.Contains("\"Valve\""))
                    {
                        inSteamSection = true;
                    }
                    else if (inSteamSection && trimmed.Contains("\"BaseInstallFolder\""))
                    {
                        // Extract the base install folder path
                        int start = trimmed.IndexOf('"', trimmed.IndexOf('"') + 1) + 1;
                        int end = trimmed.LastIndexOf('"');
                        if (start < end)
                        {
                            string path = trimmed.Substring(start, end - start);
                            libraryPaths.Add(path.Replace("\\\\", "\\"));
                        }
                    }
                    else if (inSteamSection && trimmed.Contains("\"LibraryFolders\""))
                    {
                        inLibraryFolders = true;
                    }
                    else if (inLibraryFolders && trimmed.Contains("{"))
                    {
                        // Skip opening brace
                        continue;
                    }
                    else if (inLibraryFolders && trimmed.Contains("}"))
                    {
                        // End of library folders
                        break;
                    }
                    else if (inLibraryFolders && trimmed.StartsWith("\"") && trimmed.Contains("\""))
                    {
                        // Extract library path
                        int firstQuote = trimmed.IndexOf('"');
                        int secondQuote = trimmed.IndexOf('"', firstQuote + 1);
                        int thirdQuote = trimmed.IndexOf('"', secondQuote + 1);

                        if (thirdQuote > secondQuote)
                        {
                            string path = trimmed.Substring(secondQuote + 1, thirdQuote - secondQuote - 1);
                            libraryPaths.Add(path.Replace("\\\\", "\\"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"ParseSteamConfigForLibraries - Error parsing config: {ex.Message}");
            }

            return libraryPaths;
        }

        /// <summary>
        /// Returns the base SC install path using multiple detection methods
        /// </summary>
        static private string SCBasePath
        {
            get
            {
                lock (PathCacheLock)
                {
                    if (cachedBasePathSet)
                    {
                        return cachedBasePath;
                    }

                    cachedBasePath = ResolveBasePath();
                    cachedBasePathSet = true;
                    return cachedBasePath;
                }
            }
        }

        private static string ResolveBasePath()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "SCBasePath - Entry");

            string scp = "";

            // Method 1: Check appsettings config first (user override)
            if (File.Exists("appSettings.config"))
            {
                try
                {
                    var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                    if (config.AppSettings.Settings["SCBasePath"] != null)
                    {
                        scp = config.AppSettings.Settings["SCBasePath"].Value;
                        if (!string.IsNullOrEmpty(scp) && Directory.Exists(scp) && IsValidStarCitizenInstallation(scp))
                        {
                            Logger.Instance.LogMessage(TracingLevel.INFO, $"SCBasePath - Using user-specified path: {scp}");
                            return scp;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"SCBasePath - Error reading config: {ex.Message}");
                }
            }

            // Method 2: RSI Launcher configuration files (%APPDATA%\rsilauncher\)
            scp = FindInstallationFromRSILauncher();
            if (!string.IsNullOrEmpty(scp))
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"SCBasePath - Found via RSI Launcher config: {scp}");
                return scp;
            }

            // Method 3: Registry-based detection
            scp = FindLauncherFromRegistry();
            if (!string.IsNullOrEmpty(scp))
            {
                scp = Path.GetDirectoryName(scp); // Get parent directory
                if (IsValidStarCitizenInstallation(scp))
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"SCBasePath - Found via registry: {scp}");
                    return scp;
                }
            }

            // Method 4: Common path scanning
            scp = FindInstallationFromCommonPaths();
            if (!string.IsNullOrEmpty(scp))
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"SCBasePath - Found via common paths: {scp}");
                return scp;
            }

            // Method 4: Steam library scanning
            scp = FindInstallationFromSteamPaths();
            if (!string.IsNullOrEmpty(scp))
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"SCBasePath - Found via Steam: {scp}");
                return scp;
            }

            // Method 5: Check Steam config files for custom library locations
            scp = FindInstallationFromSteamConfig();
            if (!string.IsNullOrEmpty(scp))
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"SCBasePath - Found via Steam config: {scp}");
                return scp;
            }

            Logger.Instance.LogMessage(TracingLevel.ERROR, "SCBasePath - Could not find Star Citizen installation. Please check your installation or set SCBasePath in appSettings.config");
            return "";
        }

        /// <summary>
        /// Determines whether to use PTU based on configuration
        /// </summary>
        static private bool UsePTU
        {
            get
            {
                if (File.Exists("appSettings.config"))
                {
                    try
                    {
                        var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                        if (config.AppSettings.Settings["UsePTU"] != null)
                        {
                            string ptuSetting = config.AppSettings.Settings["UsePTU"].Value;
                            if (bool.TryParse(ptuSetting, out bool usePTU))
                            {
                                return usePTU;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.LogMessage(TracingLevel.DEBUG, $"Error reading UsePTU config: {ex.Message}");
                    }
                }

                // Fallback to TheUser.UsePTU for backward compatibility
                return TheUser.UsePTU;
            }
        }

        /// <summary>
        /// Returns the SC Client path
        /// Supports multiple directory structures:
        /// 1. RSI Launcher style: basepath/StarCitizen/LIVE
        /// 2. Direct style: basepath/LIVE (when user points to StarCitizen folder directly)
        /// </summary>
        static public string SCClientPath
        {
            get
            {
                string scp = SCBasePath;
#if DEBUG
                //***************************************
                // scp += "X"; // TEST not found (COMMENT OUT FOR PRODUCTIVE BUILD)
                //***************************************
#endif
                if (string.IsNullOrEmpty(scp)) return ""; // no valid one can be found

                // Check configuration for PTU vs LIVE
                bool usePTU = UsePTU;

                lock (ClientPathCacheLock)
                {
                    if (cachedClientPathSet &&
                        cachedClientPathUsePtu == usePTU &&
                        string.Equals(cachedClientBasePath, scp, StringComparison.OrdinalIgnoreCase))
                    {
                        return cachedClientPath;
                    }

                    Logger.Instance.LogMessage(TracingLevel.DEBUG, "SCClientPath - Entry");
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"Using PTU: {usePTU}");

                    cachedClientPath = ResolveClientPath(scp, usePTU);
                    cachedClientPathUsePtu = usePTU;
                    cachedClientBasePath = scp;
                    cachedClientPathSet = true;
                    return cachedClientPath;
                }
            }
        }

        private static string ResolveClientPath(string scp, bool usePTU)
        {
            string targetFolder = usePTU ? "PTU" : "LIVE";

            // Try Structure 1: RSI Launcher style - basepath/StarCitizen/LIVE or PTU
            string rsiStylePath = Path.Combine(scp, "StarCitizen", targetFolder);
            if (Directory.Exists(rsiStylePath) && File.Exists(Path.Combine(rsiStylePath, "Data.p4k")))
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"Using RSI style {targetFolder} installation: {rsiStylePath}");
                return rsiStylePath;
            }

            // Try Structure 2: Direct style - basepath/LIVE or PTU (user points to StarCitizen folder)
            string directStylePath = Path.Combine(scp, targetFolder);
            if (Directory.Exists(directStylePath) && File.Exists(Path.Combine(directStylePath, "Data.p4k")))
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"Using direct style {targetFolder} installation: {directStylePath}");
                return directStylePath;
            }

            // If PTU was requested but not found, try fallback to LIVE
            if (usePTU)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, "PTU requested but not found, trying LIVE fallback");

                // Try RSI style LIVE
                rsiStylePath = Path.Combine(scp, "StarCitizen", "LIVE");
                if (Directory.Exists(rsiStylePath) && File.Exists(Path.Combine(rsiStylePath, "Data.p4k")))
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Fallback to RSI style LIVE: {rsiStylePath}");
                    return rsiStylePath;
                }

                // Try direct style LIVE
                directStylePath = Path.Combine(scp, "LIVE");
                if (Directory.Exists(directStylePath) && File.Exists(Path.Combine(directStylePath, "Data.p4k")))
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Fallback to direct style LIVE: {directStylePath}");
                    return directStylePath;
                }

                // Try legacy PTU structure
                string legacyPtuPath = Path.Combine(scp, "StarCitizenPTU", "LIVE");
                if (Directory.Exists(legacyPtuPath) && File.Exists(Path.Combine(legacyPtuPath, "Data.p4k")))
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Using legacy PTU: {legacyPtuPath}");
                    return legacyPtuPath;
                }
            }

            Logger.Instance.LogMessage(TracingLevel.ERROR, $"SCClientPath - Could not find Star Citizen {targetFolder} installation in: {scp}");
            return "";
        }

        /// <summary>
        /// Returns the SC ClientData path
        /// AC 3.0: E:\G\StarCitizen\StarCitizen\LIVE\USER
        /// AC 3.13: E:\G\StarCitizen\StarCitizen\LIVE\USER\Client\0
        /// </summary>
        static public string SCClientUSERPath
        {
            get
            {
                //Logger.Instance.LogMessage(TracingLevel.DEBUG,"SCClientUSERPath - Entry");
                string scp = SCClientPath;
                if (string.IsNullOrEmpty(scp)) return "";
                //
                string scpu = Path.Combine(scp, "USER", "Client", "0"); // 20210404 new path
                if (!Directory.Exists(scpu))
                {
                    scpu = Path.Combine(scp, "USER"); // 20210404 old path
                }

#if DEBUG
                //***************************************
                // scp += "X"; // TEST not found (COMMENT OUT FOR PRODUCTIVE BUILD)
                //***************************************
#endif
                if (Directory.Exists(scpu)) return scpu;

                //Logger.Instance.LogMessage(TracingLevel.DEBUG,@"SCClientUSERPath - StarCitizen\\LIVE\\USER[\Client\0] subfolder does not exist: {0}",scpu);
                return "";
            }
        }

        static public string SCClientProfilePath
        {
            get
            {
                if (File.Exists("appSettings.config") &&
                    ConfigurationManager.GetSection("appSettings") is NameValueCollection appSection)
                {
                    if ((!string.IsNullOrEmpty(appSection["SCClientProfilePath"]) && !string.IsNullOrEmpty(Path.GetDirectoryName(appSection["SCClientProfilePath"]))))
                    {
                        return appSection["SCClientProfilePath"];
                    }
                }

                //Logger.Instance.LogMessage(TracingLevel.DEBUG,"SCClientProfilePath - Entry");
                string scp = SCClientUSERPath; 
                if (string.IsNullOrEmpty(scp)) return "";
                //
                scp = Path.Combine(scp, "Profiles", "default");

                if (Directory.Exists(scp)) return scp;

                //Logger.Instance.LogMessage(TracingLevel.DEBUG,@"SCClientProfilePath - StarCitizen\LIVE\USER\[Client\0\]Profiles\default subfolder does not exist: {0}",scp);
                return "";
            }
        }



        /// <summary>
        /// Returns the SC Data.p4k file path
        /// SC Alpha 3.0: E:\G\StarCitizen\StarCitizen\LIVE\Data.p4k (contains the binary XML now)
        /// </summary>
        static public string SCData_p4k
        {
            get
            {
                if (File.Exists("appSettings.config") &&
                    ConfigurationManager.GetSection("appSettings") is NameValueCollection appSection)
                {
                    if ((!string.IsNullOrEmpty(appSection["SCData_p4k"]) && File.Exists(appSection["SCData_p4k"])))
                    {
                        return appSection["SCData_p4k"];
                    }
                }

                //Logger.Instance.LogMessage(TracingLevel.DEBUG,"SCDataXML_p4k - Entry");
                string scp = SCClientPath;
                if (string.IsNullOrEmpty(scp)) return "";
                //
                scp = Path.Combine(scp, "Data.p4k");
#if DEBUG
                //***************************************
                // scp += "X"; // TEST not found (COMMENT OUT FOR PRODUCTIVE BUILD)
                //***************************************
#endif
                if (File.Exists(scp)) return scp;

                //Logger.Instance.LogMessage(TracingLevel.DEBUG,@"SCData_p4k - StarCitizen\LIVE or PTU\Data\Data.p4k file does not exist: {0}", scp);
                return "";
            }
        }



    }
}
