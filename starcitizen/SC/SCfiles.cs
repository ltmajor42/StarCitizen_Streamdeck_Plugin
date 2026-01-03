using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text.Json;
using SCJMapper_V2.CryXMLlib;
using SCJMapper_V2.p4kFile;
using starcitizen.Core;
using P4kFileClass = SCJMapper_V2.p4kFile.p4kFile;

namespace starcitizen.SC
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
            
            // Force update if:
            // 1. No valid cache exists (filetype is still Unknown)
            // 2. Cache exists but profile data is empty
            bool cacheInvalid = m_defProfile.Filetype == SCFile.FileType.UnknownFile;
            bool cacheEmpty = m_defProfile.Filetype == SCFile.FileType.DefProfile && 
                              string.IsNullOrEmpty(m_defProfile.Filedata);
            bool forceUpdate = cacheInvalid || cacheEmpty;
            
            if (forceUpdate)
            {
                PluginLog.Warn($"Cache invalid (type={m_defProfile.Filetype}, empty={string.IsNullOrEmpty(m_defProfile.Filedata)}), forcing re-extraction from p4k");
            }
            
            if (!forceUpdate && !NeedsUpdate()) return;

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
            PluginLog.Info($"UpdateDefProfileFile - Data.p4k path: {SCPath.SCData_p4k}");

            if (!File.Exists(SCPath.SCData_p4k))
            {
                PluginLog.Error($"Data.p4k file not found at: {SCPath.SCData_p4k}");
                return;
            }

            try
            {
                var PD = new p4kDirectory();
                var candidates = PD.ScanDirectoryForAllEndsWith(SCPath.SCData_p4k, SCDefaultProfile.DefaultProfileName);

                if (candidates == null || candidates.Count == 0)
                {
                    PluginLog.Error($"No defaultProfile.xml files found in {SCPath.SCData_p4k}");
                    return;
                }

                PluginLog.Info($"Found {candidates.Count} defaultProfile.xml candidate(s):");
                foreach (var c in candidates)
                {
                    bool isCanonical = IsCanonicalDefaultProfilePath(c.Filename);
                    PluginLog.Info($"  - {c.Filename} (size={c.FileSize}, date={c.FileModifyDate:s}, canonical={isCanonical})");
                }

                // Prefer canonical path, then largest file, then most recent
                var p4K = candidates
                    .OrderByDescending(f => IsCanonicalDefaultProfilePath(f.Filename) ? 1 : 0)
                    .ThenByDescending(f => f.FileSize)
                    .ThenByDescending(f => f.FileModifyDate)
                    .FirstOrDefault();

                PluginLog.Info($"Selected: {p4K?.Filename ?? "(none)"}");

                if (p4K == null)
                {
                    PluginLog.Warn("No defaultProfile.xml candidates found in p4k");
                    return;
                }

                byte[] fContent = PD.GetFile(SCPath.SCData_p4k, p4K);
                
                if (fContent == null || fContent.Length == 0)
                {
                    PluginLog.Error($"Failed to extract defaultProfile.xml from p4k (empty content)");
                    return;
                }
                
                PluginLog.Debug($"Extracted defaultProfile.xml: {fContent.Length} bytes, first 8 bytes: {BitConverter.ToString(fContent, 0, Math.Min(8, fContent.Length))}");

                string xmlContent = null;

                // Check if it's binary CryXML (starts with exactly "CryXmlB\0")
                bool isCryXmlBinary = fContent.Length > 8 && 
                    fContent[0] == 'C' && fContent[1] == 'r' && fContent[2] == 'y' && 
                    fContent[3] == 'X' && fContent[4] == 'm' && fContent[5] == 'l' && 
                    fContent[6] == 'B' && fContent[7] == 0;

                // Check if it starts with XML declaration or root element
                bool isPlainXml = fContent.Length > 1 && 
                    (fContent[0] == '<' || 
                     (fContent[0] == 0xEF && fContent[1] == 0xBB && fContent[2] == 0xBF && fContent.Length > 3 && fContent[3] == '<')); // UTF-8 BOM

                PluginLog.Debug($"File format detection: isCryXmlBinary={isCryXmlBinary}, isPlainXml={isPlainXml}");

                if (isCryXmlBinary)
                {
                    PluginLog.Debug("Detected binary CryXML format");
                    
                    // Try CryXmlBinReader first (proven to work in old build)
                    try
                    {
                        var cbr = new CryXmlBinReader();
                        var root = cbr.LoadFromBuffer(fContent, out var readResult);
                        
                        if (readResult == CryXmlBinReader.EResult.Success && root != null)
                        {
                            var tree = new XmlTree();
                            tree.BuildXML(root);
                            xmlContent = tree.XML_string;
                            
                            if (!string.IsNullOrEmpty(xmlContent))
                            {
                                PluginLog.Info($"CryXmlBinReader succeeded: {xmlContent.Length} chars");
                            }
                        }
                        else
                        {
                            PluginLog.Warn($"CryXmlBinReader failed: {cbr.GetErrorDescription()}");
                        }
                    }
                    catch (Exception cryEx)
                    {
                        PluginLog.Warn($"CryXmlBinReader exception: {cryEx.GetType().Name}: {cryEx.Message}");
                    }
                    
                    // Fallback to new simple parser if CryXmlBinReader failed
                    if (string.IsNullOrEmpty(xmlContent))
                    {
                        PluginLog.Info("Trying CryXmlParser fallback...");
                        try
                        {
                            xmlContent = CryXmlParser.Parse(fContent);
                            
                            if (!string.IsNullOrEmpty(xmlContent))
                            {
                                PluginLog.Info($"CryXmlParser succeeded: {xmlContent.Length} chars");
                            }
                        }
                        catch (Exception cryEx)
                        {
                            PluginLog.Warn($"CryXmlParser failed: {cryEx.GetType().Name}: {cryEx.Message}");
                        }
                    }
                    
                    if (string.IsNullOrEmpty(xmlContent))
                    {
                        PluginLog.Error("Both CryXML parsers failed to produce output");
                    }
                }
                else if (isPlainXml)
                {
                    // It's plain XML text (possibly with BOM)
                    PluginLog.Debug("Detected plain XML format");
                    xmlContent = System.Text.Encoding.UTF8.GetString(fContent);
                }
                else
                {
                    // Unknown format - try as plain text anyway
                    PluginLog.Warn($"Unknown file format, attempting to parse as XML");
                    xmlContent = System.Text.Encoding.UTF8.GetString(fContent);
                    
                    // Validate it looks like XML
                    if (!xmlContent.TrimStart().StartsWith("<"))
                    {
                        PluginLog.Error($"Extracted content doesn't appear to be valid XML. First chars: {xmlContent.Substring(0, Math.Min(50, xmlContent.Length))}");
                        xmlContent = null;
                    }
                }

                PluginLog.Debug($"Parsed defaultProfile.xml: {xmlContent?.Length ?? 0} chars");
                
                if (!string.IsNullOrEmpty(xmlContent))
                {
                    m_defProfile.Filetype = SCFile.FileType.DefProfile;
                    m_defProfile.Filename = Path.GetFileName(p4K.Filename);
                    m_defProfile.Filepath = Path.GetDirectoryName(p4K.Filename);
                    m_defProfile.FileDateTime = p4K.FileModifyDate;
                    m_defProfile.Filedata = xmlContent;
                    PluginLog.Info($"Successfully loaded defaultProfile.xml ({xmlContent.Length} chars)");
                }
                else
                {
                    PluginLog.Error("Parsed defaultProfile.xml but content is empty");
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
            var profileName = SCDefaultProfile.DefaultProfileName.ToLowerInvariant();
            
            // Must end with the profile filename
            if (!normalized.EndsWith(profileName)) return false;
            
            // Check for canonical paths (order by preference - newer structure first)
            // Structure 1 (Current): Data\Libs\Config\defaultProfile.xml
            // Structure 2 (Legacy): Data\Libs\Config\Profiles\default\defaultProfile.xml
            // Structure 3 (Very old): data\libs\config\profiles\default\defaultProfile.xml
            return normalized.Contains("\\libs\\config\\defaultprofile.xml") ||
                   normalized.Contains("\\libs\\config\\profiles\\default\\") ||
                   normalized.Contains("\\data\\libs\\config\\");
        }

        private void UpdateLangFiles()
        {
            if (!File.Exists(SCPath.SCData_p4k)) return;

            try
            {
                var PD = new p4kDirectory();
                // Note: p4k paths use forward slashes internally, so search for /global.ini
                var fileList = PD.ScanDirectoryContaining(SCPath.SCData_p4k, "global.ini");
                
                PluginLog.Info($"UpdateLangFiles - Found {fileList?.Count ?? 0} global.ini file(s)");
                
                if (fileList == null || fileList.Count == 0)
                {
                    PluginLog.Warn("UpdateLangFiles - No global.ini files found in p4k");
                    return;
                }
                
                foreach (var file in fileList)
                {
                    // Extract language from path like "Data/Localization/english/global.ini"
                    string dirPath = Path.GetDirectoryName(file.Filename)?.Replace('\\', '/');
                    string lang = dirPath?.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s));
                    
                    PluginLog.Debug($"UpdateLangFiles - Processing: {file.Filename} -> lang={lang}");
                    
                    if (string.IsNullOrEmpty(lang)) continue;
                    if (!Enum.TryParse(lang, true, out SCUiText.Languages fileLang)) continue;

                    byte[] fContent = PD.GetFile(SCPath.SCData_p4k, file);
                    if (fContent == null || fContent.Length == 0)
                    {
                        PluginLog.Warn($"UpdateLangFiles - Empty content for {file.Filename}");
                        continue;
                    }
                    
                    var content = ExtractUiStrings(System.Text.Encoding.UTF8.GetString(fContent));
                    PluginLog.Info($"UpdateLangFiles - Extracted {content.Split('\n').Length} UI strings for {lang}");

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
                var filelist = Directory.EnumerateFiles(p4ktest.SC.TheUser.FileStoreDir, "*.scj").ToList();
                var filesToDelete = new List<string>();
                
                foreach (var file in filelist)
                {
                    var obj = DeserializeFile(file);
                    if (obj == null)
                    {
                        // Mark incompatible cache files for deletion (likely old BinaryFormatter format)
                        filesToDelete.Add(file);
                        continue;
                    }

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
                
                // Clean up incompatible cache files (old BinaryFormatter format from pre-.NET 8)
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        File.Delete(file);
                        PluginLog.Info($"Deleted incompatible cache file: {Path.GetFileName(file)}");
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Warn($"Could not delete incompatible cache file {file}: {ex.Message}");
                    }
                }
                
                if (filesToDelete.Count > 0)
                {
                    PluginLog.Info($"Cleaned up {filesToDelete.Count} incompatible cache file(s). Cache will be rebuilt from p4k.");
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
        // Migrated from BinaryFormatter (deprecated/blocked in .NET 8) to System.Text.Json
        
        private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };
        
        private static void SerializeFile(string path, SCFile obj)
        {
            try
            {
                var json = JsonSerializer.Serialize(obj, s_jsonOptions);
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                
                using (var stream = File.Open(path, FileMode.Create))
                using (var gZip = new GZipStream(stream, CompressionMode.Compress))
                {
                    gZip.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error($"SerializeFile failed for {path}: {ex.Message}");
                throw;
            }
        }

        private static SCFile DeserializeFile(string path)
        {
            try
            {
                using (var stream = File.Open(path, FileMode.Open))
                using (var gZip = new GZipStream(stream, CompressionMode.Decompress))
                using (var memoryStream = new MemoryStream())
                {
                    gZip.CopyTo(memoryStream);
                    var json = System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());
                    return JsonSerializer.Deserialize<SCFile>(json, s_jsonOptions);
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw - caller will handle null gracefully
                PluginLog.Warn($"DeserializeFile failed for {path}: {ex.Message}. Cache will be refreshed.");
                return null;
            }
        }
    }
}
