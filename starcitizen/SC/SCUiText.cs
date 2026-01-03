using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SCJMapper_V2.p4kFile;
using starcitizen.Core;

namespace starcitizen.SC;

sealed class SCUiText
{
    public enum Languages
    {
        profile = 0,
        english
    }

    private readonly SCLocale[] m_locales =
    [
        new(Languages.profile.ToString()),
        new(Languages.english.ToString())
    ];

    private Languages m_language = Languages.english;

    private static readonly ConcurrentDictionary<string, byte> s_loggedFailures = new();

    public Languages Language
    {
        get => m_language;
        set => m_language = value;
    }

    public IList<string> LanguagesS
    {
        get
        {
            List<string> list = [];
            foreach (SCLocale l in m_locales)
            {
                list.Add(l.Language);
            }
            return list;
        }
    }

    private static readonly Lazy<SCUiText> m_lazy = new(() => new SCUiText());

    public static SCUiText Instance => m_lazy.Value;

    public SCUiText()
    {
        PluginLog.Info($"SCUiText initializing - {SCFiles.Instance.LangFiles.Count} language file(s) available");
        
        foreach (string fileKey in SCFiles.Instance.LangFiles)
        {
            string lang = Path.GetFileNameWithoutExtension(fileKey);
            PluginLog.Debug($"SCUiText - Processing language file: {fileKey} -> lang={lang}");
            
            if (Enum.TryParse(lang, true, out Languages fileLang))
            {
                string fContent = SCFiles.Instance.LangFile(fileKey);

                int entriesLoaded = 0;
                using StringReader sr = new(fContent);
                string line = sr.ReadLine();
                while (line != null)
                {
                    int epo = line.IndexOf('=');
                    string tag = "";
                    string content = "";
                    if (epo >= 0)
                    {
                        tag = line[..epo];
                        if (line.Length >= (epo + 1))
                        {
                            content = line[(epo + 1)..];
                        }

                        if (tag.StartsWith("ui_", StringComparison.InvariantCultureIgnoreCase))
                        {
                            m_locales[(int)fileLang].Add("@" + tag, content);
                            entriesLoaded++;
                        }
                    }

                    line = sr.ReadLine();
                }
                
                PluginLog.Info($"SCUiText - Loaded {entriesLoaded} UI strings for language '{lang}'");
            }
            else
            {
                PluginLog.Warn($"SCUiText - Unrecognized language: {lang}");
            }
        }
        
        PluginLog.Info($"SCUiText initialization complete - english locale has {m_locales[(int)Languages.english].Count} entries");
    }

    public string Text(string UILabel, string defaultS)
    {
        if (string.IsNullOrWhiteSpace(UILabel))
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
            if (m_locales[(int)m_language].TryGetValue(UILabel, out var value))
            {
                retVal = value;
            }

            if (string.IsNullOrEmpty(retVal))
                retVal = defaultS;

            retVal = RemoveControlCharacters(retVal);

            return retVal.Replace("Â", "").Trim();
        }
        catch (Exception ex)
        {
            try
            {
                string key = $"{m_language}:{UILabel}";
                if (s_loggedFailures.TryAdd(key, 0))
                {
                    PluginLog.Warn($"SCLocale - Language not valid ({m_language}) for label '{UILabel}': {ex.Message}");
                }
            }
            catch
            {
                // swallow logging errors
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
