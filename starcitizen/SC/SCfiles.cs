using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO.Compression;
using SCJMapper_V2.CryXMLlib;
using SCJMapper_V2.p4kFile;
using starcitizen.Core;

namespace SCJMapper_V2.SC
{
    /// <summary>
    /// Manages Star Citizen asset files extracted from Data.p4k.
    /// Caches extracted files locally to avoid re-parsing the p4k on each startup.
    /// Tracks file dates to update only when the p4k changes.
    /// </summary>
    class SCFiles
    {
        // ============================================================
        // REGION: Singleton
        // ============================================================
        private static readonly Lazy<SCFiles> m_lazy = new Lazy<SCFiles>(() => new SCFiles());
        public static SCFiles Instance => m_lazy.Value;

        // ============================================================
        // REGION: State
        // ============================================================
        private SCFile m_pakFile;          // Reference to p4k for tracking file date
        private SCFile m_defProfile;       // Cached defaultProfile.xml
        private Dictionary<string, SCFile> m_langFiles;  // Cached language files

        // ============================================================
        // REGION: Initialization
        // ============================================================
        private SCFiles()
        {
            LoadPack();
        }

        // ============================================================
        // REGION: Public API
        // ============================================================
        public string DefaultProfile => m_defProfile.Filedata;

        public IList<string> LangFiles => m_langFiles.Keys.ToList();

        public string LangFile(string filekey) =>
            m_langFiles.TryGetValue(filekey, out var file) ? file.Filedata : "";

        /// <summary>
        /// Loads cached files and updates from p4k if needed.
        /// Call this at startup to ensure assets are current.
        /// </summary>
        public void UpdatePack()
        {
            LoadPack();
            if (!NeedsUpdate()) return;

            UpdatePakFile();
            UpdateDefProfileFile();
            UpdateLangFiles();
            SavePack();
        }

        // ============================================================
        // REGION: P4K File Updates
        // ============================================================
        private void UpdatePakFile()
        {
            if (!File.Exists(SCPath.SCData_p4k)) return;

            m_pakFile.Filetype = SCFile.FileType.PakFile;
            m_pakFile.Filename = Path.GetFileName(SCPath.SCData_p4k);
            m_pakFile.Filepath = Path.GetDirectoryName(SCPath.SCData_p4k);
            m_pakFile.FileDateTime = File.GetLastWriteTime(SCPath.SCData_p4k);
            m_pakFile.Filedata = "DUMMY CONTENT ONLY";
        }

        private void UpdateDefProfileFile()
        {
            PluginLog.Info(SCPath.SCData_p4k);

            if (!File.Exists(SCPath.SCData_p4k)) return;

            try
            {
                var PD = new p4kDirectory();
                var candidates = PD.ScanDirectoryForAllEndsWith(SCPath.SCData_p4k, SCDefaultProfile.DefaultProfileName);

                p4kFile.p4kFile p4K = null;
                if (candidates != null && candidates.Count > 0)
                {
                    foreach (var c in candidates)
                    {
                        PluginLog.Debug($"defaultProfile candidate: {c.Filename} (size={c.FileSize}, date={c.FileModifyDate:s})");
                    }

                    // Prefer canonical path, then largest file, then most recent
                    p4K = candidates
                        .OrderByDescending(f => IsCanonicalDefaultProfilePath(f.Filename) ? 1 : 0)
                        .ThenByDescending(f => f.FileSize)
                        .ThenByDescending(f => f.FileModifyDate)
                        .FirstOrDefault();

                    PluginLog.Info($"defaultProfile.xml candidates: {candidates.Count}, chosen: {p4K?.Filename ?? "(none)"}");
                }

                if (p4K == null) return;

                byte[] fContent = PD.GetFile(SCPath.SCData_p4k, p4K);

                // Parse binary XML
                var cbr = new CryXmlBinReader();
                var ROOT = cbr.LoadFromBuffer(fContent, out var readResult);
                
                if (readResult == CryXmlBinReader.EResult.Success)
                {
                    var tree = new XmlTree();
                    tree.BuildXML(ROOT);
                    
                    m_defProfile.Filetype = SCFile.FileType.DefProfile;
                    m_defProfile.Filename = Path.GetFileName(p4K.Filename);
                    m_defProfile.Filepath = Path.GetDirectoryName(p4K.Filename);
                    m_defProfile.FileDateTime = p4K.FileModifyDate;
                    m_defProfile.Filedata = tree.XML_string;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Fatal($"UpdateDefProfileFile - Unexpected {ex}");
            }
        }

        private static bool IsCanonicalDefaultProfilePath(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return false;

            var normalized = filename.Replace('/', '\\').ToLowerInvariant();
            return normalized.Contains("\\data\\libs\\config\\profiles\\default\\") &&
                   normalized.EndsWith("\\" + SCDefaultProfile.DefaultProfileName.ToLowerInvariant());
        }

        private void UpdateLangFiles()
        {
            if (!File.Exists(SCPath.SCData_p4k)) return;

            try
            {
                var PD = new p4kDirectory();
                var fileList = PD.ScanDirectoryContaining(SCPath.SCData_p4k, @"\\global.ini");
                
                foreach (var file in fileList)
                {
                    string lang = Path.GetFileNameWithoutExtension(Path.GetDirectoryName(file.Filename));
                    if (!Enum.TryParse(lang, out SCUiText.Languages fileLang)) continue;

                    byte[] fContent = PD.GetFile(SCPath.SCData_p4k, file);
                    var content = ExtractUiStrings(System.Text.Encoding.UTF8.GetString(fContent));

                    var obj = new SCFile
                    {
                        Filetype = SCFile.FileType.LangFile,
                        Filename = lang.ToLowerInvariant(),
                        Filepath = Path.GetDirectoryName(file.Filename),
                        FileDateTime = file.FileModifyDate,
                        Filedata = content
                    };

                    // Replace existing entry
                    m_langFiles[obj.Filename] = obj;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Fatal($"UpdateLangFiles - Unexpected {ex}");
            }
        }

        /// <summary>Extracts only ui_ prefixed strings from language file content.</summary>
        private static string ExtractUiStrings(string content)
        {
            var builder = new System.Text.StringBuilder();
            using (var sr = new StringReader(content))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    int epo = line.IndexOf('=');
                    if (epo >= 0)
                    {
                        var tag = line.Substring(0, epo);
                        if (tag.StartsWith("ui_", StringComparison.InvariantCultureIgnoreCase))
                        {
                            builder.AppendLine(line);
                        }
                    }
                }
            }
            return builder.ToString();
        }

        // ============================================================
        // REGION: Cache Persistence
        // ============================================================
        private void LoadPack()
        {
            m_pakFile = new SCFile();
            m_defProfile = new SCFile();
            m_langFiles = new Dictionary<string, SCFile>();

            if (!Directory.Exists(p4ktest.SC.TheUser.FileStoreDir)) return;

            try
            {
                var filelist = Directory.EnumerateFiles(p4ktest.SC.TheUser.FileStoreDir, "*.scj");
                foreach (var file in filelist)
                {
                    var obj = DeserializeFile(file);
                    if (obj == null) continue;

                    switch (obj.Filetype)
                    {
                        case SCFile.FileType.PakFile:
                            m_pakFile = obj;
                            break;
                        case SCFile.FileType.DefProfile:
                            m_defProfile = obj;
                            break;
                        case SCFile.FileType.LangFile:
                            m_langFiles[obj.Filename] = obj;
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                PluginLog.Fatal($"LoadPack - deserialization error: {e}");
            }
        }

        private void SavePack()
        {
            if (m_pakFile.Filetype != SCFile.FileType.PakFile) return;

            try
            {
                if (!Directory.Exists(p4ktest.SC.TheUser.FileStoreDir))
                    Directory.CreateDirectory(p4ktest.SC.TheUser.FileStoreDir);
            }
            catch (Exception e)
            {
                PluginLog.Fatal($"SavePack - create dir error: {e}");
                return;
            }

            try
            {
                // Save p4k reference
                SerializeFile(Path.Combine(p4ktest.SC.TheUser.FileStoreDir, m_pakFile.Filename + ".scj"), m_pakFile);

                // Save default profile
                if (m_defProfile.Filetype == SCFile.FileType.DefProfile)
                {
                    SerializeFile(Path.Combine(p4ktest.SC.TheUser.FileStoreDir, m_defProfile.Filename + ".scj"), m_defProfile);
                    File.WriteAllText(Path.Combine(p4ktest.SC.TheUser.FileStoreDir, m_defProfile.Filename), m_defProfile.Filedata);
                }

                // Save language files
                foreach (var kv in m_langFiles)
                {
                    if (kv.Value.Filetype == SCFile.FileType.LangFile)
                    {
                        SerializeFile(Path.Combine(p4ktest.SC.TheUser.FileStoreDir, kv.Value.Filename + ".scj"), kv.Value);
                    }
                }
            }
            catch (Exception e)
            {
                PluginLog.Fatal($"SavePack - serialization error: {e}");
            }
        }

        /// <summary>Checks if p4k has changed since last cache.</summary>
        private bool NeedsUpdate()
        {
            if (m_pakFile.Filetype != SCFile.FileType.PakFile) return true;
            if (!File.Exists(SCPath.SCData_p4k)) return false;

            var dateTime = File.GetLastWriteTime(SCPath.SCData_p4k);
            var needsUpdate = dateTime > m_pakFile.FileDateTime;
            PluginLog.Info($"{SCPath.SCData_p4k} needs update: {needsUpdate}");
            return needsUpdate;
        }

        // ============================================================
        // REGION: Serialization Helpers
        // ============================================================
        // NOTE: BinaryFormatter is deprecated in .NET 5+ due to security concerns, but is 
        // retained here for backward compatibility with existing .scj cache files. This is 
        // a local cache of trusted game data, not untrusted user input, so the risk is minimal.
        
        private static void SerializeFile(string path, SCFile obj)
        {
            using (var stream = File.Open(path, FileMode.Create))
            using (var gZip = new GZipStream(stream, CompressionMode.Compress))
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(gZip, obj);
            }
        }

        private static SCFile DeserializeFile(string path)
        {
            using (var stream = File.Open(path, FileMode.Open))
            using (var gZip = new GZipStream(stream, CompressionMode.Decompress))
            {
                var formatter = new BinaryFormatter();
                return (SCFile)formatter.Deserialize(gZip);
            }
        }
    }
}
