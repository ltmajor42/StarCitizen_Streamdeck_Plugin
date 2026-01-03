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
    /// <summary>
    /// Utility class for converting Star Citizen key binding strings to DirectInput keycodes
    /// and handling macro extraction for keyboard/mouse input simulation.
    /// </summary>
    internal static class CommandTools
    {
        // ============================================================
        // REGION: Constants and Configuration
        // ============================================================
        internal const char MACRO_START_CHAR = '{';
        internal const string MACRO_END = "}}";
        internal const string REGEX_MACRO = @"^\{(\{[^\{\}]+\})+\}$";
        internal const string REGEX_SUB_COMMAND = @"(\{[^\{\}]+\})";

        /// <summary>Enable verbose logging via appSettings key 'VerboseConvertLogging'</summary>
        private static readonly bool s_verbose = ReadBoolAppSetting("VerboseConvertLogging", false);
        
        /// <summary>Expose verbose flag for other modules to gate debug logs</summary>
        public static bool Verbose => s_verbose;

        // ============================================================
        // REGION: Precompiled Regex (performance optimization)
        // ============================================================
        private static readonly Regex s_regexMacro = new Regex(REGEX_MACRO, RegexOptions.Compiled);
        private static readonly Regex s_regexSubCommand = new Regex(REGEX_SUB_COMMAND, RegexOptions.Compiled);

        // ============================================================
        // REGION: SC Token to DirectInput Mapping
        // ============================================================
        /// <summary>
        /// Fast lookup map for SC key tokens to DirectInputKeyCode.
        /// Avoids large switch statements and string allocations.
        /// </summary>
        private static readonly Dictionary<string, DirectInputKeyCode> s_scToDxMap = CreateScToDxMap();

        private static Dictionary<string, DirectInputKeyCode> CreateScToDxMap()
        {
            var map = new Dictionary<string, DirectInputKeyCode>(StringComparer.OrdinalIgnoreCase)
            {
                // Modifiers
                ["lalt"] = DirectInputKeyCode.DikLalt,
                ["ralt"] = DirectInputKeyCode.DikRalt,
                ["lshift"] = DirectInputKeyCode.DikLshift,
                ["rshift"] = DirectInputKeyCode.DikRshift,
                ["lctrl"] = DirectInputKeyCode.DikLcontrol,
                ["rctrl"] = DirectInputKeyCode.DikRcontrol,

                // Function keys
                ["f1"] = DirectInputKeyCode.DikF1, ["f2"] = DirectInputKeyCode.DikF2, ["f3"] = DirectInputKeyCode.DikF3,
                ["f4"] = DirectInputKeyCode.DikF4, ["f5"] = DirectInputKeyCode.DikF5, ["f6"] = DirectInputKeyCode.DikF6,
                ["f7"] = DirectInputKeyCode.DikF7, ["f8"] = DirectInputKeyCode.DikF8, ["f9"] = DirectInputKeyCode.DikF9,
                ["f10"] = DirectInputKeyCode.DikF10, ["f11"] = DirectInputKeyCode.DikF11, ["f12"] = DirectInputKeyCode.DikF12,
                ["f13"] = DirectInputKeyCode.DikF13, ["f14"] = DirectInputKeyCode.DikF14, ["f15"] = DirectInputKeyCode.DikF15,

                // Numpad
                ["numlock"] = DirectInputKeyCode.DikNumlock,
                ["np_divide"] = DirectInputKeyCode.DikDivide, ["np_multiply"] = DirectInputKeyCode.DikMultiply,
                ["np_subtract"] = DirectInputKeyCode.DikSubtract, ["np_add"] = DirectInputKeyCode.DikAdd,
                ["np_period"] = DirectInputKeyCode.DikDecimal, ["np_enter"] = DirectInputKeyCode.DikNumpadenter,
                ["np_0"] = DirectInputKeyCode.DikNumpad0, ["np_1"] = DirectInputKeyCode.DikNumpad1, ["np_2"] = DirectInputKeyCode.DikNumpad2,
                ["np_3"] = DirectInputKeyCode.DikNumpad3, ["np_4"] = DirectInputKeyCode.DikNumpad4, ["np_5"] = DirectInputKeyCode.DikNumpad5,
                ["np_6"] = DirectInputKeyCode.DikNumpad6, ["np_7"] = DirectInputKeyCode.DikNumpad7, ["np_8"] = DirectInputKeyCode.DikNumpad8,
                ["np_9"] = DirectInputKeyCode.DikNumpad9,

                // Digits
                ["0"] = DirectInputKeyCode.Dik0, ["1"] = DirectInputKeyCode.Dik1, ["2"] = DirectInputKeyCode.Dik2,
                ["3"] = DirectInputKeyCode.Dik3, ["4"] = DirectInputKeyCode.Dik4, ["5"] = DirectInputKeyCode.Dik5,
                ["6"] = DirectInputKeyCode.Dik6, ["7"] = DirectInputKeyCode.Dik7, ["8"] = DirectInputKeyCode.Dik8,
                ["9"] = DirectInputKeyCode.Dik9,

                // Navigation
                ["insert"] = DirectInputKeyCode.DikInsert, ["home"] = DirectInputKeyCode.DikHome, ["delete"] = DirectInputKeyCode.DikDelete,
                ["end"] = DirectInputKeyCode.DikEnd, ["pgup"] = DirectInputKeyCode.DikPageUp, ["pgdown"] = DirectInputKeyCode.DikPageDown,
                ["pgdn"] = DirectInputKeyCode.DikPageDown, ["print"] = DirectInputKeyCode.DikPrintscreen, ["scrolllock"] = DirectInputKeyCode.DikScroll,
                ["pause"] = DirectInputKeyCode.DikPause,

                // Arrows
                ["up"] = DirectInputKeyCode.DikUp, ["down"] = DirectInputKeyCode.DikDown, 
                ["left"] = DirectInputKeyCode.DikLeft, ["right"] = DirectInputKeyCode.DikRight,

                // Punctuation and special keys
                ["escape"] = DirectInputKeyCode.DikEscape, ["minus"] = DirectInputKeyCode.DikMinus, ["equals"] = DirectInputKeyCode.DikEquals,
                ["grave"] = DirectInputKeyCode.DikGrave, ["underline"] = DirectInputKeyCode.DikUnderline, ["backspace"] = DirectInputKeyCode.DikBackspace,
                ["tab"] = DirectInputKeyCode.DikTab, ["lbracket"] = DirectInputKeyCode.DikLbracket, ["rbracket"] = DirectInputKeyCode.DikRbracket,
                ["enter"] = DirectInputKeyCode.DikReturn, ["capslock"] = DirectInputKeyCode.DikCapital, ["colon"] = DirectInputKeyCode.DikColon,
                ["backslash"] = DirectInputKeyCode.DikBackslash, ["comma"] = DirectInputKeyCode.DikComma, ["period"] = DirectInputKeyCode.DikPeriod,
                ["slash"] = DirectInputKeyCode.DikSlash, ["space"] = DirectInputKeyCode.DikSpace, ["semicolon"] = DirectInputKeyCode.DikSemicolon,
                ["apostrophe"] = DirectInputKeyCode.DikApostrophe
            };
            return map;
        }

        // ============================================================
        // REGION: Conversion Cache (performance optimization)
        // ============================================================
        /// <summary>Memoization cache for ConvertKeyString to reduce repeated parsing</summary>
        private const int MAX_CONVERT_CACHE_ENTRIES = 1024;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> s_convertCache = 
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private static readonly System.Collections.Concurrent.ConcurrentQueue<string> s_convertCacheOrder = 
            new System.Collections.Concurrent.ConcurrentQueue<string>();

        // ============================================================
        // REGION: Public Key Conversion Methods
        // ============================================================
        
        /// <summary>
        /// Converts a Star Citizen keyboard binding string (e.g., "lalt+f") to a macro format
        /// suitable for InputSimulator (e.g., "{DikLalt}{DikF}").
        /// </summary>
        /// <param name="keyboard">The SC binding string (plus-separated tokens)</param>
        /// <returns>Macro format string, or empty if no valid tokens found</returns>
        public static string ConvertKeyString(string keyboard)
        {
            if (string.IsNullOrWhiteSpace(keyboard))
            {
                PluginLog.Warn("ConvertKeyString called with empty binding. Skipping.");
                return string.Empty;
            }

            if (s_verbose) PluginLog.Debug($"ConvertKeyString input: {keyboard}");

            // Fast path: return cached conversion
            if (s_convertCache.TryGetValue(keyboard, out var cached))
            {
                if (s_verbose) PluginLog.Debug($"ConvertKeyString cache hit: {keyboard} -> {cached}");
                return cached;
            }

            var keys = keyboard.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (keys.Length == 0)
            {
                PluginLog.Warn("ConvertKeyString: no usable tokens after splitting. Skipping.");
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var key in keys)
            {
                var token = key?.Trim();
                if (string.IsNullOrEmpty(token)) continue;

                // Check if it's a mouse token
                if (MouseTokenHelper.TryNormalize(token, out var normalizedMouseToken))
                {
                    if (SCPath.EnableMouseOutput)
                    {
                        builder.Append('{').Append(normalizedMouseToken).Append('}');
                        if (s_verbose) PluginLog.Debug($"ConvertKeyString: mouse token '{token}' -> '{normalizedMouseToken}'");
                    }
                    else
                    {
                        PluginLog.Warn($"Mouse token '{token}' found but EnableMouseOutput=false. Skipping.");
                    }
                    continue;
                }

                // Try keyboard mapping
                if (TryFromSCKeyboardCmd(token, out var dxKey))
                {
                    builder.Append('{').Append(dxKey).Append('}');
                    if (s_verbose) PluginLog.Debug($"ConvertKeyString: keyboard token '{token}' -> '{dxKey}'");
                }
                else
                {
                    PluginLog.Warn($"Unknown key token '{token}'. Skipping.");
                }
            }

            var result = builder.ToString();
            
            // Cache result with simple LRU eviction
            s_convertCache[keyboard] = result;
            s_convertCacheOrder.Enqueue(keyboard);
            EvictOldCacheEntries();

            if (s_verbose) PluginLog.Debug($"ConvertKeyString output: {result}");
            return result;
        }

        /// <summary>
        /// Converts a keyboard binding to localized display format for the Property Inspector.
        /// Shows user-friendly key names based on the current keyboard layout.
        /// </summary>
        public static string ConvertKeyStringToLocale(string keyboard, string language)
        {
            if (string.IsNullOrWhiteSpace(keyboard))
            {
                PluginLog.Warn("ConvertKeyStringToLocale called with empty binding. Skipping.");
                return string.Empty;
            }

            var keys = keyboard.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (keys.Length == 0)
            {
                PluginLog.Warn("ConvertKeyStringToLocale: no usable tokens after splitting. Skipping.");
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var key in keys)
            {
                var token = key?.Trim();
                if (string.IsNullOrEmpty(token)) continue;

                // Handle mouse tokens for display
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

                // Apply locale-specific key mappings
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

        /// <summary>
        /// Attempts to map a Star Citizen key token to its DirectInput keycode.
        /// Used to validate bindings before display in the Property Inspector.
        /// </summary>
        internal static bool TryFromSCKeyboardCmd(string scKey, out DirectInputKeyCode dxKey)
        {
            dxKey = default;
            var key = scKey?.Trim();
            if (string.IsNullOrWhiteSpace(key)) return false;

            // Fast dictionary lookup (case-insensitive)
            if (s_scToDxMap.TryGetValue(key, out var found))
            {
                dxKey = found;
                return true;
            }

            // Fallback: try to match by DirectInput enum naming (for letter keys)
            var letter = "Dik" + key.ToUpperInvariant();
            return Enum.TryParse(letter, out dxKey);
        }

        // ============================================================
        // REGION: Macro Extraction
        // ============================================================
        
        /// <summary>
        /// Extracts a macro from text at the specified position.
        /// Macros are formatted as {{key1}{key2}...}.
        /// </summary>
        internal static string ExtractMacro(string text, int position)
        {
            try
            {
                if (string.IsNullOrEmpty(text) || position < 0 || position >= text.Length) return null;
                if (text[position] != MACRO_START_CHAR) return null;

                int end = text.IndexOf(MACRO_END, position, StringComparison.Ordinal);
                if (end < 0) return null;

                int macroLen = end - position + MACRO_END.Length;
                if (macroLen <= 0) return null;

                var macro = text.Substring(position, macroLen);

                // Verify at least one inner subcommand exists
                if (s_regexSubCommand.Matches(macro).Count == 0) return null;

                return macro;
            }
            catch (Exception ex)
            {
                PluginLog.Fatal($"ExtractMacro Exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Extracts individual keystrokes from a macro string.
        /// Returns a list of DirectInputKeyCode values for input simulation.
        /// </summary>
        internal static List<DirectInputKeyCode> ExtractKeyStrokes(string macroText)
        {
            var keyStrokes = new List<DirectInputKeyCode>();

            try
            {
                var matches = s_regexSubCommand.Matches(macroText);
                if (matches.Count > 0)
                {
                    foreach (Match match in matches)
                    {
                        var inner = match.Value.Replace("{", "").Replace("}", "");

                        // Try parse as-is (handles 'DikA', 'DikReturn', 'Space', etc.)
                        if (Enum.TryParse<DirectInputKeyCode>(inner, true, out var stroke))
                        {
                            keyStrokes.Add(stroke);
                            continue;
                        }

                        // Try with 'Dik' prefix
                        if (!inner.StartsWith("Dik", StringComparison.OrdinalIgnoreCase))
                        {
                            var prefixed = "Dik" + inner;
                            if (Enum.TryParse<DirectInputKeyCode>(prefixed, true, out stroke))
                            {
                                keyStrokes.Add(stroke);
                                continue;
                            }
                        }

                        if (s_verbose) PluginLog.Debug($"ExtractKeyStrokes: unknown token '{inner}'");
                    }
                }
                else
                {
                    // Fallback: manual scan for {token} substrings
                    keyStrokes.AddRange(ExtractKeyStrokesFallback(macroText));
                }
            }
            catch (Exception ex)
            {
                PluginLog.Fatal($"ExtractKeyStrokes Exception: {ex}");
            }

            return keyStrokes;
        }

        // ============================================================
        // REGION: Private Helper Methods
        // ============================================================
        
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

        private static void EvictOldCacheEntries()
        {
            try
            {
                while (s_convertCache.Count > MAX_CONVERT_CACHE_ENTRIES && s_convertCacheOrder.TryDequeue(out var oldest))
                {
                    s_convertCache.TryRemove(oldest, out _);
                }
            }
            catch
            {
                // Cache eviction should never throw; swallow to avoid affecting runtime
            }
        }

        private static List<DirectInputKeyCode> ExtractKeyStrokesFallback(string macroText)
        {
            var keyStrokes = new List<DirectInputKeyCode>();
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
                    if (Enum.TryParse<DirectInputKeyCode>(inner, true, out var stroke) ||
                        Enum.TryParse<DirectInputKeyCode>("Dik" + inner, true, out stroke))
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
            catch { /* ignored */ }
            return defaultValue;
        }

        // ============================================================
        // REGION: Locale-Specific Key Mappings
        // ============================================================
        /// <summary>
        /// Per-language mappings for scancode to display token.
        /// Only entries that differ from the enum name are listed.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<DirectInputKeyCode, string>> s_localeMaps = 
            new Dictionary<string, Dictionary<DirectInputKeyCode, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-GB"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik`", [DirectInputKeyCode.DikMinus] = "Dik-",
                [DirectInputKeyCode.DikEquals] = "Dik=", [DirectInputKeyCode.DikLbracket] = "Dik[",
                [DirectInputKeyCode.DikRbracket] = "Dik]", [DirectInputKeyCode.DikBackslash] = "Dik#",
                [DirectInputKeyCode.DikSemicolon] = "Dik:", [DirectInputKeyCode.DikApostrophe] = "Dik'",
                [DirectInputKeyCode.DikComma] = "Dik,", [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik/",
            },
            ["de-CH"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik§", [DirectInputKeyCode.DikMinus] = "Dik'",
                [DirectInputKeyCode.DikEquals] = "Dik^", [DirectInputKeyCode.DikY] = "DikZ",
                [DirectInputKeyCode.DikLbracket] = "DikÜ", [DirectInputKeyCode.DikRbracket] = "Dik¨",
                [DirectInputKeyCode.DikBackslash] = "Dik$", [DirectInputKeyCode.DikSemicolon] = "DikÖ",
                [DirectInputKeyCode.DikApostrophe] = "DikÄ", [DirectInputKeyCode.DikZ] = "DikY",
                [DirectInputKeyCode.DikComma] = "Dik,", [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik-",
            },
            ["es-ES"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dikº", [DirectInputKeyCode.DikMinus] = "Dik'",
                [DirectInputKeyCode.DikEquals] = "Dik¡", [DirectInputKeyCode.DikLbracket] = "Dik`",
                [DirectInputKeyCode.DikRbracket] = "Dik+",
                [DirectInputKeyCode.DikBackslash] = "Dikç", [DirectInputKeyCode.DikSemicolon] = "Dikñ",
                [DirectInputKeyCode.DikApostrophe] = "Dik´", [DirectInputKeyCode.DikComma] = "Dik,",
                [DirectInputKeyCode.DikPeriod] = "Dik.", [DirectInputKeyCode.DikSlash] = "Dik-",
            },
            ["da-DK"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik½", [DirectInputKeyCode.DikMinus] = "Dik+",
                [DirectInputKeyCode.DikEquals] = "Dik´", [DirectInputKeyCode.DikLbracket] = "DikÅ",
                [DirectInputKeyCode.DikRbracket] = "Dik¨", [DirectInputKeyCode.DikBackslash] = "Dik'",
                [DirectInputKeyCode.DikSemicolon] = "DikÆ", [DirectInputKeyCode.DikApostrophe] = "DikØ",
                [DirectInputKeyCode.DikComma] = "Dik,", [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik-",
            },
            ["it-IT"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik\\", [DirectInputKeyCode.DikMinus] = "Dik'",
                [DirectInputKeyCode.DikEquals] = "DikÌ", [DirectInputKeyCode.DikLbracket] = "DikÈ",
                [DirectInputKeyCode.DikRbracket] = "Dik+", [DirectInputKeyCode.DikBackslash] = "DikÙ",
                [DirectInputKeyCode.DikSemicolon] = "DikÒ", [DirectInputKeyCode.DikApostrophe] = "DikÀ",
                [DirectInputKeyCode.DikComma] = "Dik,", [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik-",
            },
            ["pt-PT"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik\\", [DirectInputKeyCode.DikMinus] = "Dik'",
                [DirectInputKeyCode.DikEquals] = "Dik«", [DirectInputKeyCode.DikLbracket] = "Dik+",
                [DirectInputKeyCode.DikRbracket] = "Dik´", [DirectInputKeyCode.DikBackslash] = "Dik~",
                [DirectInputKeyCode.DikSemicolon] = "DikÇ", [DirectInputKeyCode.DikApostrophe] = "Dikº",
                [DirectInputKeyCode.DikComma] = "Dik,", [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik-",
            },
            ["de-DE"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik^", [DirectInputKeyCode.DikMinus] = "Dikß",
                [DirectInputKeyCode.DikEquals] = "Dik´", [DirectInputKeyCode.DikY] = "DikZ",
                [DirectInputKeyCode.DikLbracket] = "DikÜ", [DirectInputKeyCode.DikRbracket] = "Dik+",
                [DirectInputKeyCode.DikBackslash] = "Dik#", [DirectInputKeyCode.DikSemicolon] = "DikÖ",
                [DirectInputKeyCode.DikApostrophe] = "DikÄ", [DirectInputKeyCode.DikZ] = "DikY",
                [DirectInputKeyCode.DikComma] = "Dik,", [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik-",
            },
            ["fr-FR"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik²",
                [DirectInputKeyCode.Dik1] = "Dik&", [DirectInputKeyCode.Dik2] = "DikÉ",
                [DirectInputKeyCode.Dik3] = "Dik\"", [DirectInputKeyCode.Dik4] = "Dik'",
                [DirectInputKeyCode.Dik5] = "Dik(",
                [DirectInputKeyCode.Dik6] = "Dik-",
                [DirectInputKeyCode.Dik7] = "DikÈ", [DirectInputKeyCode.Dik8] = "Dik_",
                [DirectInputKeyCode.Dik9] = "DikÇ", [DirectInputKeyCode.Dik0] = "DikÀ",
                [DirectInputKeyCode.DikMinus] = "Dik)", [DirectInputKeyCode.DikEquals] = "Dik=",
                [DirectInputKeyCode.DikQ] = "DikA", [DirectInputKeyCode.DikW] = "DikZ",
                [DirectInputKeyCode.DikLbracket] = "Dik^", [DirectInputKeyCode.DikRbracket] = "Dik$",
                [DirectInputKeyCode.DikBackslash] = "Dik*", [DirectInputKeyCode.DikA] = "DikQ",
                [DirectInputKeyCode.DikSemicolon] = "DikM", [DirectInputKeyCode.DikApostrophe] = "DikÙ",
                [DirectInputKeyCode.DikZ] = "DikW", [DirectInputKeyCode.DikM] = "Dik,",
                [DirectInputKeyCode.DikComma] = "Dik;", [DirectInputKeyCode.DikPeriod] = "Dik:",
                [DirectInputKeyCode.DikSlash] = "Dik!",
            },
            ["default"] = new Dictionary<DirectInputKeyCode, string>
            {
                [DirectInputKeyCode.DikGrave] = "Dik`", [DirectInputKeyCode.DikMinus] = "Dik-",
                [DirectInputKeyCode.DikEquals] = "Dik=", [DirectInputKeyCode.DikLbracket] = "Dik[",
                [DirectInputKeyCode.DikRbracket] = "Dik]", [DirectInputKeyCode.DikBackslash] = "Dik\\",
                [DirectInputKeyCode.DikSemicolon] = "Dik:", [DirectInputKeyCode.DikApostrophe] = "Dik'",
                [DirectInputKeyCode.DikComma] = "Dik,", [DirectInputKeyCode.DikPeriod] = "Dik.",
                [DirectInputKeyCode.DikSlash] = "Dik/",
            }
        };
    }
}
