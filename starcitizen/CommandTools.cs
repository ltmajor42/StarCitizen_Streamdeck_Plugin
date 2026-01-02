using BarRaider.SdTools;
using SCJMapper_V2.SC;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using WindowsInput.Native;
using starcitizen.Core;
using System.Configuration;

namespace starcitizen
{
    internal static class CommandTools
    {
        internal const char MACRO_START_CHAR = '{';
        internal const string MACRO_END = "}}";
        internal const string REGEX_MACRO = @"^\{(\{[^\{\}]+\})+\}$";
        internal const string REGEX_SUB_COMMAND = @"(\{[^\{\}]+\})";

        // Enable verbose convert logging via appSettings key 'VerboseConvertLogging' (true|false)
        private static readonly bool s_verbose = ReadBoolAppSetting("VerboseConvertLogging", false);

        // Expose verbose flag for other modules (e.g., StreamDeckCommon) to gate debug logs
        public static bool Verbose => s_verbose;

        // Precompiled regex instances for performance
        private static readonly System.Text.RegularExpressions.Regex s_regexMacro = new System.Text.RegularExpressions.Regex(REGEX_MACRO, RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex s_regexSubCommand = new System.Text.RegularExpressions.Regex(REGEX_SUB_COMMAND, RegexOptions.Compiled);

        // Fast lookup map for SC key tokens to DirectInputKeyCode to avoid big switch and allocations
        private static readonly Dictionary<string, DirectInputKeyCode> s_scToDxMap = CreateScToDxMap();

        private static Dictionary<string, DirectInputKeyCode> CreateScToDxMap()
        {
            var map = new Dictionary<string, DirectInputKeyCode>(StringComparer.OrdinalIgnoreCase);
            // modifiers
            map["lalt"] = DirectInputKeyCode.DikLalt;
            map["ralt"] = DirectInputKeyCode.DikRalt;
            map["lshift"] = DirectInputKeyCode.DikLshift;
            map["rshift"] = DirectInputKeyCode.DikRshift;
            map["lctrl"] = DirectInputKeyCode.DikLcontrol;
            map["rctrl"] = DirectInputKeyCode.DikRcontrol;

            // function keys
            map["f1"] = DirectInputKeyCode.DikF1; map["f2"] = DirectInputKeyCode.DikF2; map["f3"] = DirectInputKeyCode.DikF3;
            map["f4"] = DirectInputKeyCode.DikF4; map["f5"] = DirectInputKeyCode.DikF5; map["f6"] = DirectInputKeyCode.DikF6;
            map["f7"] = DirectInputKeyCode.DikF7; map["f8"] = DirectInputKeyCode.DikF8; map["f9"] = DirectInputKeyCode.DikF9;
            map["f10"] = DirectInputKeyCode.DikF10; map["f11"] = DirectInputKeyCode.DikF11; map["f12"] = DirectInputKeyCode.DikF12;
            map["f13"] = DirectInputKeyCode.DikF13; map["f14"] = DirectInputKeyCode.DikF14; map["f15"] = DirectInputKeyCode.DikF15;

            // numpad
            map["numlock"] = DirectInputKeyCode.DikNumlock;
            map["np_divide"] = DirectInputKeyCode.DikDivide; map["np_multiply"] = DirectInputKeyCode.DikMultiply;
            map["np_subtract"] = DirectInputKeyCode.DikSubtract; map["np_add"] = DirectInputKeyCode.DikAdd;
            map["np_period"] = DirectInputKeyCode.DikDecimal; map["np_enter"] = DirectInputKeyCode.DikNumpadenter;
            map["np_0"] = DirectInputKeyCode.DikNumpad0; map["np_1"] = DirectInputKeyCode.DikNumpad1; map["np_2"] = DirectInputKeyCode.DikNumpad2;
            map["np_3"] = DirectInputKeyCode.DikNumpad3; map["np_4"] = DirectInputKeyCode.DikNumpad4; map["np_5"] = DirectInputKeyCode.DikNumpad5;
            map["np_6"] = DirectInputKeyCode.DikNumpad6; map["np_7"] = DirectInputKeyCode.DikNumpad7; map["np_8"] = DirectInputKeyCode.DikNumpad8;
            map["np_9"] = DirectInputKeyCode.DikNumpad9;

            // digits
            map["0"] = DirectInputKeyCode.Dik0; map["1"] = DirectInputKeyCode.Dik1; map["2"] = DirectInputKeyCode.Dik2;
            map["3"] = DirectInputKeyCode.Dik3; map["4"] = DirectInputKeyCode.Dik4; map["5"] = DirectInputKeyCode.Dik5;
            map["6"] = DirectInputKeyCode.Dik6; map["7"] = DirectInputKeyCode.Dik7; map["8"] = DirectInputKeyCode.Dik8;
            map["9"] = DirectInputKeyCode.Dik9;

            // navigation
            map["insert"] = DirectInputKeyCode.DikInsert; map["home"] = DirectInputKeyCode.DikHome; map["delete"] = DirectInputKeyCode.DikDelete;
            map["end"] = DirectInputKeyCode.DikEnd; map["pgup"] = DirectInputKeyCode.DikPageUp; map["pgdown"] = DirectInputKeyCode.DikPageDown;
            map["pgdn"] = DirectInputKeyCode.DikPageDown; map["print"] = DirectInputKeyCode.DikPrintscreen; map["scrolllock"] = DirectInputKeyCode.DikScroll;
            map["pause"] = DirectInputKeyCode.DikPause;

            // arrows
            map["up"] = DirectInputKeyCode.DikUp; map["down"] = DirectInputKeyCode.DikDown; map["left"] = DirectInputKeyCode.DikLeft; map["right"] = DirectInputKeyCode.DikRight;

            // non-letters / punctuation
            map["escape"] = DirectInputKeyCode.DikEscape; map["minus"] = DirectInputKeyCode.DikMinus; map["equals"] = DirectInputKeyCode.DikEquals;
            map["grave"] = DirectInputKeyCode.DikGrave; map["underline"] = DirectInputKeyCode.DikUnderline; map["backspace"] = DirectInputKeyCode.DikBackspace;
            map["tab"] = DirectInputKeyCode.DikTab; map["lbracket"] = DirectInputKeyCode.DikLbracket; map["rbracket"] = DirectInputKeyCode.DikRbracket;
            map["enter"] = DirectInputKeyCode.DikReturn; map["capslock"] = DirectInputKeyCode.DikCapital; map["colon"] = DirectInputKeyCode.DikColon;
            map["backslash"] = DirectInputKeyCode.DikBackslash; map["comma"] = DirectInputKeyCode.DikComma; map["period"] = DirectInputKeyCode.DikPeriod;
            map["slash"] = DirectInputKeyCode.DikSlash; map["space"] = DirectInputKeyCode.DikSpace; map["semicolon"] = DirectInputKeyCode.DikSemicolon;
            map["apostrophe"] = DirectInputKeyCode.DikApostrophe;

            return map;
        }

        // Memoization cache for ConvertKeyString to reduce repeated parsing of identical bindings
        private const int MAX_CONVERT_CACHE_ENTRIES = 1024;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> s_convertCache = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private static readonly System.Collections.Concurrent.ConcurrentQueue<string> s_convertCacheOrder = new System.Collections.Concurrent.ConcurrentQueue<string>();

        public static string ConvertKeyString(string keyboard)
        {
            if (string.IsNullOrWhiteSpace(keyboard))
            {
                PluginLog.Warn("ConvertKeyString called with an empty keyboard binding. Skipping send.");
                return string.Empty;
            }

            if (s_verbose)
            {
                PluginLog.Debug($"ConvertKeyString input: {keyboard}");
            }

            // Fast path: cached conversion
            if (s_convertCache.TryGetValue(keyboard, out var cached))
            {
                if (s_verbose) PluginLog.Debug($"ConvertKeyString cache hit: {keyboard} -> {cached}");
                return cached;
            }

            var keys = keyboard.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);

            if (keys.Length == 0)
            {
                PluginLog.Warn("ConvertKeyString received no usable key tokens after splitting. Skipping send.");
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var key in keys)
            {
                var token = key?.Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (MouseTokenHelper.TryNormalize(token, out var normalizedMouseToken))
                {
                    if (SCPath.EnableMouseOutput)
                    {
                        builder.Append('{').Append(normalizedMouseToken).Append('}');
                        if (s_verbose) PluginLog.Debug($"ConvertKeyString: mouse token '{token}' -> '{normalizedMouseToken}'");
                    }
                    else
                    {
                        PluginLog.Warn($"Mouse token '{token}' encountered but EnableMouseOutput=false. Skipping send.");
                    }

                    continue;
                }

                if (TryFromSCKeyboardCmd(token, out var dxKey))
                {
                    builder.Append('{').Append(dxKey).Append('}');
                    if (s_verbose) PluginLog.Debug($"ConvertKeyString: keyboard token '{token}' -> '{dxKey}'");
                }
                else
                {
                    PluginLog.Warn($"Unknown key token '{token}' encountered. Skipping send.");
                }
            }

            var result = builder.ToString();
            // Insert into cache and record insertion order for simple eviction
            s_convertCache[keyboard] = result;
            s_convertCacheOrder.Enqueue(keyboard);

            // Evict oldest entries if we exceeded capacity
            try
            {
                while (s_convertCache.Count > MAX_CONVERT_CACHE_ENTRIES && s_convertCacheOrder.TryDequeue(out var oldest))
                {
                    s_convertCache.TryRemove(oldest, out _);
                }
            }
            catch
            {
                // cache eviction should never throw; swallow to avoid affecting runtime
            }

            if (s_verbose) PluginLog.Debug($"ConvertKeyString output: {result}");
            return result;
        }

        private static bool IsMouseToken(string token) => MouseTokenHelper.TryNormalize(token, out _);

        private static string MouseTokenToDisplay(string token) =>
            MouseTokenHelper.TryNormalize(token, out var normalized)
                ? normalized switch
                {
                    "mouse1" => "Mouse1",
                    "mouse2" => "Mouse2",
                    "mouse3" => "Mouse3",
                    "mouse4" => "Mouse4",
                    "mouse5" => "Mouse5",
                    "mwheelup" => "WheelUp",
                    "mwheeldown" => "WheelDown",
                    "mwheelleft" => "WheelLeft",
                    "mwheelright" => "WheelRight",
                    "mwheel" => "Wheel",
                    _ => normalized
                }
                : token?.Trim();

        // NOTE: Behavior is unchanged. Visibility is internal so UI code can validate bindings
        // and avoid showing non-executable options (e.g., joystick-only or unknown tokens).
        internal static bool TryFromSCKeyboardCmd(string scKey, out DirectInputKeyCode dxKey)
        {
            dxKey = default;

            var key = scKey?.Trim();
            if (string.IsNullOrWhiteSpace(key)) return false;

            // Dictionary lookup is case-insensitive
            if (s_scToDxMap.TryGetValue(key, out var found))
            {
                dxKey = found;
                return true;
            }

            // Fallback: attempt match by DX enum naming (letters)
            var letter = "Dik" + key.ToUpperInvariant();
            return Enum.TryParse(letter, out dxKey);
        }
        
        public static string ConvertKeyStringToLocale(string keyboard, string language)
        {
            if (string.IsNullOrWhiteSpace(keyboard))
            {
                PluginLog.Warn("ConvertKeyStringToLocale called with an empty keyboard binding. Skipping send.");
                return string.Empty;
            }

            var keys = keyboard.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);

            if (keys.Length == 0)
            {
                PluginLog.Warn("ConvertKeyStringToLocale received no usable key tokens after splitting. Skipping send.");
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var key in keys)
            {
                var token = key?.Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                // If a mouse token leaked into a keyboard field, prefer displaying it rather than mapping to Escape.
                if (IsMouseToken(token))
                {
                    builder.Append('{').Append(MouseTokenToDisplay(token)).Append('}');
                    continue;
                }

                if (!TryFromSCKeyboardCmd(token, out var dikKey))
                {
                    builder.Append('{').Append($"unknown:{token}").Append('}');
                    continue;
                }

                var dikKeyOut = dikKey.ToString();

                // Locale lookup tables for keys that differ by keyboard layout
                if (!s_localeMaps.TryGetValue(language, out var localeMap))
                {
                    localeMap = s_localeMaps["default"];
                }

                if (localeMap != null && localeMap.TryGetValue(dikKey, out var mappedOut))
                {
                    dikKeyOut = mappedOut;
                }

                builder.Append('{').Append(dikKeyOut).Append('}');
            }

            return builder.ToString();
        }

        private static DirectInputKeyCode FromSCKeyboardCmd(string scKey)
        {
            // Legacy helper: map SC tokens to DirectInput codes.
            // Note: callers still validate tokens; unknown tokens return default and must be handled by the caller.
            if (string.IsNullOrWhiteSpace(scKey)) return default;
            var key = scKey.Trim();

            if (s_scToDxMap.TryGetValue(key, out var dxKey))
            {
                return dxKey;
            }

            // Fallback to enum by name
            var letter = "Dik" + key.ToUpperInvariant();
            if (Enum.TryParse(letter, out dxKey)) return dxKey;
            return default;
        }

        internal static string ExtractMacro(string text, int position)
        {
            try
            {
                if (string.IsNullOrEmpty(text) || position < 0 || position >= text.Length) return null;
                if (text[position] != MACRO_START_CHAR) return null;

                // Find the closing '}}' sequence starting from position
                int end = text.IndexOf(MACRO_END, position, StringComparison.Ordinal);
                if (end < 0) return null;

                int macroLen = end - position + MACRO_END.Length;
                if (macroLen <= 0) return null;

                var macro = text.Substring(position, macroLen);

                // Ensure there is at least one inner subcommand of the form '{...}' inside
                var subMatches = s_regexSubCommand.Matches(macro);
                if (subMatches.Count == 0) return null;

                return macro;
            }
            catch (Exception ex)
            {
                PluginLog.Fatal($"ExtractMacro Exception: {ex}");
            }

            return null;
        }

        internal static List<DirectInputKeyCode> ExtractKeyStrokes(string macroText)
        {
            var keyStrokes = new List<DirectInputKeyCode>();

            try
            {
                var matches = s_regexSubCommand.Matches(macroText);
                if (matches.Count > 0)
                {
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        // Extract inner token without braces preserving case
                        var inner = match.Value.Replace("{", "").Replace("}", "");

                        DirectInputKeyCode stroke;

                        // Try parse the token as-is (handles values like 'DikA' or 'DikReturn' or 'Space')
                        if (Enum.TryParse<DirectInputKeyCode>(inner, true, out stroke))
                        {
                            keyStrokes.Add(stroke);
                            continue;
                        }

                        // If token doesn't start with 'Dik', try with 'Dik' prefix
                        if (!inner.StartsWith("Dik", StringComparison.OrdinalIgnoreCase))
                        {
                            var prefixed = "Dik" + inner;
                            if (Enum.TryParse<DirectInputKeyCode>(prefixed, true, out stroke))
                            {
                                keyStrokes.Add(stroke);
                                continue;
                            }
                        }

                        // unknown token: skip and log once if verbose
                        if (s_verbose) PluginLog.Debug($"ExtractKeyStrokes: unknown token '{inner}'");
                    }
                }
                else
                {
                    // Fallback: manual scan for {token} substrings in case regex didn't match
                    int i = 0;
                    while (i < macroText.Length)
                    {
                        int start = macroText.IndexOf('{', i);
                        if (start < 0) break;
                        int end = macroText.IndexOf('}', start + 1);
                        if (end <= start) break;
                        var inner = macroText.Substring(start + 1, end - start - 1).Trim();
                        if (!string.IsNullOrEmpty(inner))
                        {
                            if (Enum.TryParse<DirectInputKeyCode>(inner, true, out var stroke) 
                                || Enum.TryParse<DirectInputKeyCode>("Dik" + inner, true, out stroke))
                            {
                                keyStrokes.Add(stroke);
                            }
                            else if (s_verbose)
                            {
                                PluginLog.Debug($"ExtractKeyStrokes (fallback): unknown token '{inner}'");
                            }
                        }

                        i = end + 1;
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Fatal($"ExtractKeyStrokes Exception: {ex}");
            }

            return keyStrokes;
        }

        private static bool ReadBoolAppSetting(string key, bool defaultValue)
        {
            try
            {
                var raw = ConfigurationManager.AppSettings[key];
                if (!string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw, out var value))
                {
                    return value;
                }
            }
            catch
            {
                // ignored
            }

            return defaultValue;
        }

        // Per-language mappings for scancode -> output token. Only entries that differ from the enum name are listed.
        private static readonly Dictionary<string, Dictionary<DirectInputKeyCode, string>> s_localeMaps = new Dictionary<string, Dictionary<DirectInputKeyCode, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-GB"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik`",
                [DirectInputKeyCode.DikMinus] = "Dik-",
                [DirectInputKeyCode.DikEquals] = "Dik=",
                [DirectInputKeyCode.DikLbracket] = "Dik[",
                [DirectInputKeyCode.DikRbracket] = "Dik]",
                [DirectInputKeyCode.DikBackslash] = "Dik#",
                [DirectInputKeyCode.DikSemicolon] = "Dik:",
                [DirectInputKeyCode.DikApostrophe] = "Dik'",
                [DirectInputKeyCode.DikComma] = "Dik,",
                [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik/",
            },
            ["de-CH"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik§",
                [DirectInputKeyCode.DikMinus] = "Dik'",
                [DirectInputKeyCode.DikEquals] = "Dik^",
                [DirectInputKeyCode.DikY] = "DikZ",
                [DirectInputKeyCode.DikLbracket] = "DikÜ",
                [DirectInputKeyCode.DikRbracket] = "Dik¨",
                [DirectInputKeyCode.DikBackslash] = "Dik$",
                [DirectInputKeyCode.DikSemicolon] = "DikÖ",
                [DirectInputKeyCode.DikApostrophe] = "DikÄ",
                [DirectInputKeyCode.DikZ] = "DikY",
                [DirectInputKeyCode.DikComma] = "Dik,",
                [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik-",
            },
            ["es-ES"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dikº",
                [DirectInputKeyCode.DikMinus] = "Dik'",
                [DirectInputKeyCode.DikEquals] = "Dik¡",
                [DirectInputKeyCode.DikLbracket] = "Dik`",
                [DirectInputKeyCode.DikRbracket] = "Dik+",
                [DirectInputKeyCode.DikBackslash] = "Dikç",
                [DirectInputKeyCode.DikSemicolon] = "Dikñ",
                [DirectInputKeyCode.DikApostrophe] = "Dik´",
                [DirectInputKeyCode.DikComma] = "Dik,",
                [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik-",
            },
            ["da-DK"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik½",
                [DirectInputKeyCode.DikMinus] = "Dik+",
                [DirectInputKeyCode.DikEquals] = "Dik´",
                [DirectInputKeyCode.DikLbracket] = "DikÅ",
                [DirectInputKeyCode.DikRbracket] = "Dik¨",
                [DirectInputKeyCode.DikBackslash] = "Dik'",
                [DirectInputKeyCode.DikSemicolon] = "DikÆ",
                [DirectInputKeyCode.DikApostrophe] = "DikØ",
                [DirectInputKeyCode.DikComma] = "Dik,",
                [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik-",
            },
            ["it-IT"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik\\",
                [DirectInputKeyCode.DikMinus] = "Dik'",
                [DirectInputKeyCode.DikEquals] = "DikÌ",
                [DirectInputKeyCode.DikLbracket] = "DikÈ",
                [DirectInputKeyCode.DikRbracket] = "Dik+",
                [DirectInputKeyCode.DikBackslash] = "DikÙ",
                [DirectInputKeyCode.DikSemicolon] = "DikÒ",
                [DirectInputKeyCode.DikApostrophe] = "DikÀ",
                [DirectInputKeyCode.DikComma] = "Dik,",
                [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik-",
            },
            ["pt-PT"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik\\",
                [DirectInputKeyCode.DikMinus] = "Dik'",
                [DirectInputKeyCode.DikEquals] = "Dik«",
                [DirectInputKeyCode.DikLbracket] = "Dik+",
                [DirectInputKeyCode.DikRbracket] = "Dik´",
                [DirectInputKeyCode.DikBackslash] = "Dik~",
                [DirectInputKeyCode.DikSemicolon] = "DikÇ",
                [DirectInputKeyCode.DikApostrophe] = "Dikº",
                [DirectInputKeyCode.DikComma] = "Dik,",
                [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik-",
            },
            ["de-DE"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik^",
                [DirectInputKeyCode.DikMinus] = "Dikß",
                [DirectInputKeyCode.DikEquals] = "Dik´",
                [DirectInputKeyCode.DikY] = "DikZ",
                [DirectInputKeyCode.DikLbracket] = "DikÜ",
                [DirectInputKeyCode.DikRbracket] = "Dik+",
                [DirectInputKeyCode.DikBackslash] = "Dik#",
                [DirectInputKeyCode.DikSemicolon] = "DikÖ",
                [DirectInputKeyCode.DikApostrophe] = "DikÄ",
                [DirectInputKeyCode.DikZ] = "DikY",
                [DirectInputKeyCode.DikComma] = "Dik,",
                [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik-",
            },
            ["fr-FR"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik²",
                [DirectInputKeyCode.Dik1] = "Dik&",
                [DirectInputKeyCode.Dik2] = "DikÉ",
                [DirectInputKeyCode.Dik3] = "Dik\"",
                [DirectInputKeyCode.Dik4] = "Dik'",
                [DirectInputKeyCode.Dik5] = "Dik(",
                [DirectInputKeyCode.Dik6] = "Dik-",
                [DirectInputKeyCode.Dik7] = "DikÈ",
                [DirectInputKeyCode.Dik8] = "Dik_",
                [DirectInputKeyCode.Dik9] = "DikÇ",
                [DirectInputKeyCode.Dik0] = "DikÀ",
                [DirectInputKeyCode.DikMinus] = "Dik)",
                [DirectInputKeyCode.DikEquals] = "Dik=",
                [DirectInputKeyCode.DikQ] = "DikA",
                [DirectInputKeyCode.DikW] = "DikZ",
                [DirectInputKeyCode.DikLbracket] = "Dik^",
                [DirectInputKeyCode.DikRbracket] = "Dik$",
                [DirectInputKeyCode.DikBackslash] = "Dik*",
                [DirectInputKeyCode.DikA] = "DikQ",
                [DirectInputKeyCode.DikSemicolon] = "DikM",
                [DirectInputKeyCode.DikApostrophe] = "DikÙ",
                [DirectInputKeyCode.DikZ] = "DikW",
                [DirectInputKeyCode.DikM] = "Dik,",
                [DirectInputKeyCode.DikComma] = "Dik;",
                [DirectInputKeyCode.DikPeriod] = "Dik:",
                [DirectInputKeyCode.DikSlash] = "Dik!",
            },
            ["default"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik`",
                [DirectInputKeyCode.DikMinus] = "Dik-",
                [DirectInputKeyCode.DikEquals] = "Dik=",
                [DirectInputKeyCode.DikLbracket] = "Dik[",
                [DirectInputKeyCode.DikRbracket] = "Dik]",
                [DirectInputKeyCode.DikBackslash] = "Dik\\",
                [DirectInputKeyCode.DikSemicolon] = "Dik:",
                [DirectInputKeyCode.DikApostrophe] = "Dik'",
                [DirectInputKeyCode.DikComma] = "Dik,",
                [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik/",
            }
        };
    }
}
