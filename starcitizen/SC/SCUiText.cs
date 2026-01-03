using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using p4ktest.SC;
using SCJMapper_V2.p4kFile;
using starcitizen.Core;

namespace starcitizen.SC
{
    sealed class SCUiText
    {
        public enum Languages
        {
            profile = 0, // use profile texts
            english // must be the one used in the game assets.. Data\\Localization\\<lang>
        }


        private readonly SCLocale[] m_locales =
        {
            new SCLocale(Languages.profile
                .ToString()), // creates an empty one and will return the default(profile string) later
            new SCLocale(Languages.english.ToString())
        }; // add supported languages

        private Languages m_language = Languages.english;

        // One-time rate limiter: remember which (language,label) failures have already been logged to avoid flooding
        private static readonly ConcurrentDictionary<string, byte> s_loggedFailures = new ConcurrentDictionary<string, byte>();

        /// <summary>
        /// Set the language to be used
        /// </summary>
        public Languages Language
        {
            get => m_language;
            set => m_language = value;
        }

        public IList<string> LanguagesS
        {
            get
            {
                List<string> list = new List<string>();
                foreach (SCLocale l in m_locales)
                {
                    list.Add(l.Language);
                }

                return list;
            }
        }

        // Singleton
        private static readonly Lazy<SCUiText> m_lazy = new Lazy<SCUiText>(() => new SCUiText());

        public static SCUiText Instance
        {
            get => m_lazy.Value;
        }

        /// <summary>
        /// Load all languages from Assets
        ///  like:  dfm_crusader_port_olisar=Port Olisar
        /// </summary>
        public SCUiText()
        {
            PluginLog.Info($"SCUiText initializing - {SCFiles.Instance.LangFiles.Count} language file(s) available");
            
            foreach (string fileKey in SCFiles.Instance.LangFiles)
            {
                string lang = Path.GetFileNameWithoutExtension(fileKey);
                PluginLog.Debug($"SCUiText - Processing language file: {fileKey} -> lang={lang}");
                
                // check if it is a valid language
                if (Enum.TryParse(lang, true, out Languages fileLang))
                {
                    string fContent = SCFiles.Instance.LangFile(fileKey);

                    int entriesLoaded = 0;
                    using TextReader sr = new StringReader(fContent);
                    string line = sr.ReadLine();
                    while (line != null)
                    {
                        int epo = line.IndexOf('=');
                        string tag = "";
                        string content = "";
                        if (epo >= 0)
                        {
                            tag = line.Substring(0, epo);
                            if (line.Length >= (epo + 1))
                            {
                                content = line.Substring(epo + 1);
                            }

                            if (tag.StartsWith("ui_", StringComparison.InvariantCultureIgnoreCase))
                            {
                                // seems all strings we may need are ui_Cxyz
                                m_locales[(int) fileLang].Add("@" + tag, content); // cAT is prepending the tags
                                entriesLoaded++;
                            }
                        }

                        line = sr.ReadLine();
                    } // while
                    
                    PluginLog.Info($"SCUiText - Loaded {entriesLoaded} UI strings for language '{lang}'");
                }
                else
                {
                    PluginLog.Warn($"SCUiText - Unrecognized language: {lang}");
                }
            } // all files
            
            PluginLog.Info($"SCUiText initialization complete - english locale has {m_locales[(int)Languages.english].Count} entries");
        }

        /// <summary>
        /// Returns the content from the UILabel in the set Language
        /// </summary>
        /// <param name="UILabel">The UILabel from defaultProfile</param>
        /// <param name="defaultS">A default string to return if the label cannot be found</param>
        /// <returns>A text string</returns>
        public string Text(string UILabel, string defaultS)
        {
            // Guard against null/empty label to avoid Dictionary operations with null key
            if (string.IsNullOrEmpty(UILabel))
            {
                try
                {
                    string key = $"{m_language}:<empty_label>";
                    if (s_loggedFailures.TryAdd(key, 0))
                    {
                        PluginLog.Warn($"SCLocale - Empty or null UILabel requested for language {m_language}. Returning default.");
                    }
                }
                catch
                {
                    // swallow
                }

                return defaultS;
            }

            try
            {
                string retVal = "";
                if (m_locales[(int) m_language].ContainsKey(UILabel))
                {
                    retVal = m_locales[(int) m_language][UILabel];
                }

                //if ( string.IsNullOrEmpty( retVal ) )
                //  if ( m_locales[(int)Languages.english].ContainsKey( UILabel ) ) {
                //    retVal = m_locales[(int)Languages.english][UILabel]; // fallback to english
                //  }
                if (string.IsNullOrEmpty(retVal))
                    retVal = defaultS; // final fallback to default

                retVal = RemoveControlCharacters(retVal);

                return retVal.Replace("Â", "").Trim();
            }
            catch (Exception ex)
            {
                // One-time rate-limited warn: only log the first time this language+label fails to avoid flooding the log
                try
                {
                    string key = $"{m_language}:{UILabel}";
                    if (s_loggedFailures.TryAdd(key, 0))
                    {
                        // Log as Warn for visibility but avoid repeated entries
                        PluginLog.Warn($"SCLocale - Language not valid ({m_language}) for label '{UILabel}': {ex.Message}");
                    }
                }
                catch
                {
                    // swallow logging errors to avoid cascading failures
                }
            }

            return defaultS;
        }

        private static string RemoveControlCharacters(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var builder = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (!char.IsControl(ch) || ch == '\r' || ch == '\n' || ch == '\t')
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }


    }
}
