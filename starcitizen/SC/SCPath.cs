using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using starcitizen.Core;

namespace starcitizen.SC;

/// <summary>
/// Find the SC pathes and folders using multiple detection methods
/// </summary>
partial class SCPath
{
    private static readonly string[] KNOWN_REGISTRY_KEYS =
    [
        @"SOFTWARE\81bfc699-f883-50c7-b674-2483b6baae23",
        @"SOFTWARE\94a6df8a-d3f9-558d-bb04-097c192530b9",
        @"SOFTWARE\Cloud Imperium Games\Star Citizen",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\StarCitizen",
    ];

    private static readonly string[] COMMON_INSTALL_PATHS =
    [
        @"C:\Program Files\Roberts Space Industries\StarCitizen",
        @"C:\Program Files (x86)\Roberts Space Industries\StarCitizen",
        @"D:\Program Files\Roberts Space Industries\StarCitizen",
        @"D:\Program Files (x86)\Roberts Space Industries\StarCitizen",
        @"E:\Program Files\Roberts Space Industries\StarCitizen",
        @"E:\Program Files (x86)\Roberts Space Industries\StarCitizen",
        @"F:\Program Files\Roberts Space Industries\StarCitizen",
        @"F:\Program Files (x86)\Roberts Space Industries\StarCitizen",
        @"C:\Games\StarCitizen",
        @"D:\Games\StarCitizen",
        @"E:\Games\StarCitizen",
        @"F:\Games\StarCitizen",
        @"C:\StarCitizen",
        @"D:\StarCitizen",
        @"E:\StarCitizen",
        @"F:\StarCitizen",
        @"G:\Program Files\Roberts Space Industries\StarCitizen",
        @"G:\Games\StarCitizen",
        @"G:\StarCitizen",
        @"H:\Program Files\Roberts Space Industries\StarCitizen",
        @"H:\Games\StarCitizen",
        @"H:\StarCitizen",
        @"C:\Program Files\Epic Games\StarCitizen",
        @"D:\Program Files\Epic Games\StarCitizen",
        @"E:\Program Files\Epic Games\StarCitizen",
        @"C:\Users\Public\Games\StarCitizen",
        @"D:\Users\Public\Games\StarCitizen",
        @"E:\Users\Public\Games\StarCitizen",
    ];

    private static readonly string[] STEAM_LIBRARY_PATHS =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\Star Citizen",
        @"D:\Steam\steamapps\common\Star Citizen",
        @"E:\Steam\steamapps\common\Star Citizen",
        @"F:\Steam\steamapps\common\Star Citizen",
        @"G:\Steam\steamapps\common\Star Citizen",
        @"C:\SteamLibrary\steamapps\common\Star Citizen",
        @"D:\SteamLibrary\steamapps\common\Star Citizen",
        @"E:\SteamLibrary\steamapps\common\Star Citizen",
    ];

    private static readonly object PathCacheLock = new();
    private static string cachedBasePath;
    private static bool cachedBasePathSet;

    private static readonly object ClientPathCacheLock = new();
    private static string cachedClientPath;
    private static bool cachedClientPathSet;
    private static bool cachedClientPathUsePtu;
    private static string cachedClientBasePath;

    [GeneratedRegex(@"([A-Za-z]:\\[^\""<>|\r\n]+?StarCitizen)", RegexOptions.IgnoreCase)]
    private static partial Regex StarCitizenPathRegex();

    /// <summary>
    /// Try to find SC installation from RSI Launcher configuration files
    /// </summary>
    private static string FindInstallationFromRSILauncher()
    {
        PluginLog.Debug("FindInstallationFromRSILauncher - Entry");

        try
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string rsiLauncherPath = Path.Combine(appDataPath, "rsilauncher");

            string libraryFolderFile = Path.Combine(rsiLauncherPath, "library_folder.json");
            if (File.Exists(libraryFolderFile))
            {
                PluginLog.Debug($"FindInstallationFromRSILauncher - Found library_folder.json: {libraryFolderFile}");
                string json = File.ReadAllText(libraryFolderFile);
                int pathStart = json.IndexOf('"');
                int pathEnd = json.LastIndexOf('"');
                if (pathStart >= 0 && pathEnd > pathStart)
                {
                    string libraryPath = json[(pathStart + 1)..pathEnd];
                    libraryPath = libraryPath.Replace("\\\\", "\\").Replace("\\/", "/").Replace("/", "\\");
                    PluginLog.Debug($"FindInstallationFromRSILauncher - Parsed library path: {libraryPath}");
                    
                    if (Directory.Exists(libraryPath) && IsValidStarCitizenInstallation(libraryPath))
                    {
                        PluginLog.Info($"FindInstallationFromRSILauncher - Found via library_folder.json: {libraryPath}");
                        return libraryPath;
                    }
                }
            }

            string settingsFile = Path.Combine(rsiLauncherPath, "settings.json");
            if (File.Exists(settingsFile))
            {
                PluginLog.Debug($"FindInstallationFromRSILauncher - Found settings.json: {settingsFile}");
                string json = File.ReadAllText(settingsFile);
                
                string[] searchKeys = ["\"libraryFolder\"", "\"library_folder\"", "\"installDir\"", "\"InstallDir\""];
                foreach (string key in searchKeys)
                {
                    int keyIndex = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                    if (keyIndex >= 0)
                    {
                        int colonIndex = json.IndexOf(':', keyIndex);
                        if (colonIndex >= 0)
                        {
                            int valueStart = json.IndexOf('"', colonIndex);
                            int valueEnd = json.IndexOf('"', valueStart + 1);
                            if (valueStart >= 0 && valueEnd > valueStart)
                            {
                                string path = json[(valueStart + 1)..valueEnd];
                                path = path.Replace("\\\\", "\\").Replace("\\/", "/").Replace("/", "\\");
                                PluginLog.Debug($"FindInstallationFromRSILauncher - Found path in settings.json: {path}");
                                
                                if (Directory.Exists(path) && IsValidStarCitizenInstallation(path))
                                {
                                    PluginLog.Info($"FindInstallationFromRSILauncher - Found via settings.json: {path}");
                                    return path;
                                }
                            }
                        }
                    }
                }
            }

            string logDir = Path.Combine(rsiLauncherPath, "logs");
            if (Directory.Exists(logDir))
            {
                string[] logFiles = Directory.GetFiles(logDir, "*.log");
                foreach (string logFile in logFiles.OrderByDescending(f => File.GetLastWriteTime(f)).Take(3))
                {
                    try
                    {
                        string logContent = File.ReadAllText(logFile);
                        var matches = StarCitizenPathRegex().Matches(logContent);
                        foreach (Match match in matches)
                        {
                            string path = match.Value.Replace("\\\\", "\\");
                            if (Directory.Exists(path) && IsValidStarCitizenInstallation(path))
                            {
                                PluginLog.Info($"FindInstallationFromRSILauncher - Found via log file: {path}");
                                return path;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Debug($"FindInstallationFromRSILauncher - Error reading log {logFile}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"FindInstallationFromRSILauncher - Error: {ex.Message}");
        }

        PluginLog.Debug("FindInstallationFromRSILauncher - No valid installation found");
        return "";
    }

    private static string FindLauncherFromRegistry()
    {
        PluginLog.Debug("FindLauncherFromRegistry - Entry");

        foreach (string regKey in KNOWN_REGISTRY_KEYS)
        {
            try
            {
                RegistryKey localKey;
                if (Environment.Is64BitOperatingSystem)
                    localKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                else
                    localKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);

                using var key = localKey.OpenSubKey(regKey);
                if (key != null)
                {
                    object installLocation = key.GetValue("InstallLocation");
                    if (installLocation != null)
                    {
                        string scLauncher = installLocation.ToString();
                        PluginLog.Debug($"FindLauncherFromRegistry - Found in {regKey}: {scLauncher}");

                        if (Directory.Exists(scLauncher))
                        {
                            return scLauncher;
                        }
                        else
                        {
                            PluginLog.Debug($"FindLauncherFromRegistry - Directory does not exist: {scLauncher}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Debug($"FindLauncherFromRegistry - Error checking {regKey}: {ex.Message}");
            }
        }

        PluginLog.Debug("FindLauncherFromRegistry - No valid launcher found in registry");
        return "";
    }

    private static string FindInstallationFromCommonPaths()
    {
        PluginLog.Debug("FindInstallationFromCommonPaths - Entry");

        foreach (string path in COMMON_INSTALL_PATHS)
        {
            PluginLog.Debug($"FindInstallationFromCommonPaths - Checking: {path}");

            if (Directory.Exists(path) && IsValidStarCitizenInstallation(path))
            {
                PluginLog.Info($"FindInstallationFromCommonPaths - Found valid installation: {path}");
                return path;
            }
        }

        PluginLog.Debug("FindInstallationFromCommonPaths - No valid installation found");
        return "";
    }

    private static bool IsValidStarCitizenInstallation(string path)
    {
        try
        {
            string livePath = Path.Combine(path, "StarCitizen", "LIVE");
            string ptuPath = Path.Combine(path, "StarCitizen", "PTU");

            if (Directory.Exists(livePath))
            {
                string dataP4k = Path.Combine(livePath, "Data.p4k");
                if (File.Exists(dataP4k))
                {
                    PluginLog.Debug($"IsValidStarCitizenInstallation - Found RSI style LIVE: {livePath}");
                    return true;
                }
            }

            if (Directory.Exists(ptuPath))
            {
                string dataP4k = Path.Combine(ptuPath, "Data.p4k");
                if (File.Exists(dataP4k))
                {
                    PluginLog.Debug($"IsValidStarCitizenInstallation - Found RSI style PTU: {ptuPath}");
                    return true;
                }
            }

            string directLivePath = Path.Combine(path, "LIVE");
            string directPtuPath = Path.Combine(path, "PTU");

            if (Directory.Exists(directLivePath))
            {
                string dataP4k = Path.Combine(directLivePath, "Data.p4k");
                if (File.Exists(dataP4k))
                {
                    PluginLog.Debug($"IsValidStarCitizenInstallation - Found direct style LIVE: {directLivePath}");
                    return true;
                }
            }

            if (Directory.Exists(directPtuPath))
            {
                string dataP4k = Path.Combine(directPtuPath, "Data.p4k");
                if (File.Exists(dataP4k))
                {
                    PluginLog.Debug($"IsValidStarCitizenInstallation - Found direct style PTU: {directPtuPath}");
                    return true;
                }
            }

            string directDataP4k = Path.Combine(path, "Data.p4k");
            if (File.Exists(directDataP4k))
            {
                PluginLog.Debug($"IsValidStarCitizenInstallation - Found direct Data.p4k: {directDataP4k}");
                return true;
            }
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"IsValidStarCitizenInstallation - Error checking {path}: {ex.Message}");
        }

        return false;
    }

    private static string FindInstallationFromSteamPaths()
    {
        PluginLog.Debug("FindInstallationFromSteamPaths - Entry");

        foreach (string path in STEAM_LIBRARY_PATHS)
        {
            PluginLog.Debug($"FindInstallationFromSteamPaths - Checking: {path}");

            if (Directory.Exists(path) && IsValidStarCitizenInstallation(path))
            {
                PluginLog.Info($"FindInstallationFromSteamPaths - Found valid installation: {path}");
                return path;
            }
        }

        PluginLog.Debug("FindInstallationFromSteamPaths - No valid installation found");
        return "";
    }

    private static string FindInstallationFromSteamConfig()
    {
        PluginLog.Debug("FindInstallationFromSteamConfig - Entry");

        try
        {
            string steamConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "config", "config.vdf");

            if (!File.Exists(steamConfigPath))
            {
                string steamPath = Environment.GetEnvironmentVariable("SteamPath");
                if (!string.IsNullOrEmpty(steamPath))
                {
                    steamConfigPath = Path.Combine(steamPath, "config", "config.vdf");
                }
            }

            if (!File.Exists(steamConfigPath))
            {
                PluginLog.Debug("FindInstallationFromSteamConfig - Steam config not found");
                return "";
            }

            var libraryPaths = ParseSteamConfigForLibraries(steamConfigPath);

            foreach (string libraryPath in libraryPaths)
            {
                string scPath = Path.Combine(libraryPath, "steamapps", "common", "Star Citizen");
                PluginLog.Debug($"FindInstallationFromSteamConfig - Checking Steam library: {scPath}");

                if (IsValidStarCitizenInstallation(scPath))
                {
                    PluginLog.Info($"FindInstallationFromSteamConfig - Found valid installation: {scPath}");
                    return scPath;
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"FindInstallationFromSteamConfig - Error: {ex.Message}");
        }

        PluginLog.Debug("FindInstallationFromSteamConfig - No valid installation found");
        return "";
    }

    private static List<string> ParseSteamConfigForLibraries(string configPath)
    {
        List<string> libraryPaths = [];

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
                    int start = trimmed.IndexOf('\'', trimmed.IndexOf('\'') + 1) + 1;
                    int end = trimmed.LastIndexOf('\'');
                    if (start < end)
                    {
                        string path = trimmed[start..end];
                        libraryPaths.Add(path.Replace("\\\\", "\\"));
                    }
                }
                else if (inSteamSection && trimmed.Contains("\"LibraryFolders\""))
                {
                    inLibraryFolders = true;
                }
                else if (inLibraryFolders && trimmed.Contains('{'))
                {
                    continue;
                }
                else if (inLibraryFolders && trimmed.Contains('}'))
                {
                    break;
                }
                else if (inLibraryFolders && trimmed.StartsWith('"') && trimmed.Contains('"'))
                {
                    int firstQuote = trimmed.IndexOf('"');
                    int secondQuote = trimmed.IndexOf('"', firstQuote + 1);
                    int thirdQuote = trimmed.IndexOf('"', secondQuote + 1);

                    if (thirdQuote > secondQuote)
                    {
                        string path = trimmed[(secondQuote + 1)..thirdQuote];
                        libraryPaths.Add(path.Replace("\\\\", "\\"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"ParseSteamConfigForLibraries - Error parsing config: {ex.Message}");
        }

        return libraryPaths;
    }

    private static string SCBasePath
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
        PluginLog.Debug("SCBasePath - Entry");

        string scp;

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
                        PluginLog.Info($"SCBasePath - Using user-specified path: {scp}");
                        return scp;
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Debug($"SCBasePath - Error reading config: {ex.Message}");
            }
        }

        scp = FindInstallationFromRSILauncher();
        if (!string.IsNullOrEmpty(scp))
        {
            PluginLog.Info($"SCBasePath - Found via RSI Launcher config: {scp}");
            return scp;
        }

        scp = FindLauncherFromRegistry();
        if (!string.IsNullOrEmpty(scp))
        {
            var parentDir = Path.GetDirectoryName(scp);
            if (!string.IsNullOrEmpty(parentDir) && IsValidStarCitizenInstallation(parentDir))
            {
                PluginLog.Info($"SCBasePath - Found via registry: {parentDir}");
                return parentDir;
            }
        }

        scp = FindInstallationFromCommonPaths();
        if (!string.IsNullOrEmpty(scp))
        {
            PluginLog.Info($"SCBasePath - Found via common paths: {scp}");
            return scp;
        }

        scp = FindInstallationFromSteamPaths();
        if (!string.IsNullOrEmpty(scp))
        {
            PluginLog.Info($"SCBasePath - Found via Steam: {scp}");
            return scp;
        }

        scp = FindInstallationFromSteamConfig();
        if (!string.IsNullOrEmpty(scp))
        {
            PluginLog.Info($"SCBasePath - Found via Steam config: {scp}");
            return scp;
        }

        PluginLog.Error("SCBasePath - Could not find Star Citizen installation");
        return "";
    }

    private static bool UsePTU
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
                    PluginLog.Debug($"Error reading UsePTU config: {ex.Message}");
                }
            }

            return TheUser.UsePTU;
        }
    }

    public static string SCClientPath
    {
        get
        {
            string scp = SCBasePath;
            if (string.IsNullOrEmpty(scp)) return "";

            bool usePTU = UsePTU;

            lock (ClientPathCacheLock)
            {
                if (cachedClientPathSet &&
                    cachedClientPathUsePtu == usePTU &&
                    string.Equals(cachedClientBasePath, scp, StringComparison.OrdinalIgnoreCase))
                {
                    return cachedClientPath;
                }

                PluginLog.Debug("SCClientPath - Entry");
                PluginLog.Debug($"Using PTU: {usePTU}");

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

        string rsiStylePath = Path.Combine(scp, "StarCitizen", targetFolder);
        if (Directory.Exists(rsiStylePath) && File.Exists(Path.Combine(rsiStylePath, "Data.p4k")))
        {
            PluginLog.Info($"Using RSI style {targetFolder} installation: {rsiStylePath}");
            return rsiStylePath;
        }

        string directStylePath = Path.Combine(scp, targetFolder);
        if (Directory.Exists(directStylePath) && File.Exists(Path.Combine(directStylePath, "Data.p4k")))
        {
            PluginLog.Info($"Using direct style {targetFolder} installation: {directStylePath}");
            return directStylePath;
        }

        if (usePTU)
        {
            PluginLog.Warn("PTU requested but not found, trying LIVE fallback");

            rsiStylePath = Path.Combine(scp, "StarCitizen", "LIVE");
            if (Directory.Exists(rsiStylePath) && File.Exists(Path.Combine(rsiStylePath, "Data.p4k")))
            {
                PluginLog.Info($"Fallback to RSI style LIVE: {rsiStylePath}");
                return rsiStylePath;
            }

            directStylePath = Path.Combine(scp, "LIVE");
            if (Directory.Exists(directStylePath) && File.Exists(Path.Combine(directStylePath, "Data.p4k")))
            {
                PluginLog.Info($"Fallback to direct style LIVE: {directStylePath}");
                return directStylePath;
            }

            string legacyPtuPath = Path.Combine(scp, "StarCitizenPTU", "LIVE");
            if (Directory.Exists(legacyPtuPath) && File.Exists(Path.Combine(legacyPtuPath, "Data.p4k")))
            {
                PluginLog.Info($"Using legacy PTU: {legacyPtuPath}");
                return legacyPtuPath;
            }
        }

        PluginLog.Error($"SCClientPath - Could not find Star Citizen {targetFolder} installation in: {scp}");
        return "";
    }

    public static string SCClientUSERPath
    {
        get
        {
            string scp = SCClientPath;
            if (string.IsNullOrEmpty(scp)) return "";

            string scpu = Path.Combine(scp, "USER", "Client", "0");
            if (!Directory.Exists(scpu))
            {
                scpu = Path.Combine(scp, "USER");
            }

            if (Directory.Exists(scpu)) return scpu;

            return "";
        }
    }

    public static string SCClientProfilePath
    {
        get
        {
            if (File.Exists("appSettings.config") &&
                ConfigurationManager.GetSection("appSettings") is NameValueCollection appSection)
            {
                if (!string.IsNullOrEmpty(appSection["SCClientProfilePath"]) && 
                    !string.IsNullOrEmpty(Path.GetDirectoryName(appSection["SCClientProfilePath"])))
                {
                    return appSection["SCClientProfilePath"];
                }
            }

            string scp = SCClientUSERPath;
            if (string.IsNullOrEmpty(scp)) return "";

            scp = Path.Combine(scp, "Profiles", "default");

            if (Directory.Exists(scp)) return scp;

            return "";
        }
    }

    public static string ResolveActionMapsPath()
    {
        var profilePath = SCClientProfilePath;
        if (!string.IsNullOrWhiteSpace(profilePath))
        {
            var candidate = Path.Combine(profilePath, "actionmaps.xml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var userRoot = SCClientUSERPath;
        if (!string.IsNullOrWhiteSpace(userRoot) && Directory.Exists(userRoot))
        {
            try
            {
                var candidates = Directory.EnumerateFiles(userRoot, "actionmaps.xml", SearchOption.AllDirectories)
                                          .OrderByDescending(File.GetLastWriteTimeUtc)
                                          .ToArray();

                if (candidates.Length > 0)
                {
                    return candidates[0];
                }
            }
            catch (Exception ex)
            {
                PluginLog.Debug($"ResolveActionMapsPath scan failed: {ex.Message}");
            }
        }

        return "";
    }

    public static string SCData_p4k
    {
        get
        {
            if (File.Exists("appSettings.config") &&
                ConfigurationManager.GetSection("appSettings") is NameValueCollection appSection)
            {
                if (!string.IsNullOrEmpty(appSection["SCData_p4k"]) && File.Exists(appSection["SCData_p4k"]))
                {
                    return appSection["SCData_p4k"];
                }
            }

            string scp = SCClientPath;
            if (string.IsNullOrEmpty(scp)) return "";

            scp = Path.Combine(scp, "Data.p4k");

            if (File.Exists(scp)) return scp;

            return "";
        }
    }

    private static bool ReadBoolAppSetting(string key, bool defaultValue)
    {
        try
        {
            if (File.Exists("appSettings.config") &&
                ConfigurationManager.GetSection("appSettings") is NameValueCollection appSection)
            {
                var raw = appSection[key];
                if (!string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw, out var value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // ignored
        }

        return defaultValue;
    }

    public static bool TreatBlankRebindAsUnbound => ReadBoolAppSetting("TreatBlankRebindAsUnbound", false);

    public static bool SafeUnknownKeyTokens => ReadBoolAppSetting("SafeUnknownKeyTokens", true);

    public static bool EnableMouseOutput => ReadBoolAppSetting("EnableMouseOutput", true);

    public static bool DetailedInputDiagnostics => ReadBoolAppSetting("DetailedInputDiagnostics", false);

    public static bool CoalesceMouseWheel => ReadBoolAppSetting("CoalesceMouseWheel", true);
}
