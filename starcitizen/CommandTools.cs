using BarRaider.SdTools;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using WindowsInput.Native;

namespace starcitizen
{
    internal static class CommandTools
    {
        internal const char MACRO_START_CHAR = '{';
        internal const string MACRO_END = "}}";
        internal const string REGEX_MACRO = @"^\{(\{[^\{\}]+\})+\}$";
        internal const string REGEX_SUB_COMMAND = @"(\{[^\{\}]+\})";

        public static string ConvertKeyString(string keyboard)
        {
            if (string.IsNullOrWhiteSpace(keyboard))
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, "ConvertKeyString called with an empty keyboard binding. Skipping send.");
                return string.Empty;
            }

            var keys = keyboard.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);

            if (keys.Length == 0)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, "ConvertKeyString received no usable key tokens after splitting. Skipping send.");
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var key in keys)
            {
                if (!TryGetDirectInputKey(key.Trim(), out var dikKey))
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"ConvertKeyString received unknown key '{key}'. Skipping send.");
                    return string.Empty;
                }

                builder.Append('{').Append(dikKey).Append('}');
            }

            return builder.ToString();
        }
        
        public static string ConvertKeyStringToLocale(string keyboard, string language)
        {
            if (string.IsNullOrWhiteSpace(keyboard))
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, "ConvertKeyStringToLocale called with an empty keyboard binding. Skipping send.");
                return string.Empty;
            }

            var keys = keyboard.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);

            if (keys.Length == 0)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, "ConvertKeyStringToLocale received no usable key tokens after splitting. Skipping send.");
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var key in keys)
            {
                if (!TryGetDirectInputKey(key.Trim(), out var dikKey))
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"ConvertKeyStringToLocale received unknown key '{key}'. Skipping send.");
                    return string.Empty;
                }

                var dikKeyOut = dikKey.ToString();

                switch (language)
                {
                    case "en-GB":
                        // http://kbdlayout.info/kbduk/shiftstates+scancodes/base

                        switch (dikKey)
                        {
                            // FIRST ROW
                            case DirectInputKeyCode.DikGrave:

                                dikKeyOut = "Dik`";
                                break;

                            case DirectInputKeyCode.DikMinus:
                                dikKeyOut = "Dik-";
                                break;

                            case DirectInputKeyCode.DikEquals:
                                dikKeyOut = "Dik=";
                                break;

                            // SECOND ROW 

                            case DirectInputKeyCode.DikLbracket:
                                dikKeyOut = "Dik[";
                                break;

                            case DirectInputKeyCode.DikRbracket:
                                dikKeyOut = "Dik]";
                                break;

                            case DirectInputKeyCode.DikBackslash:
                                dikKeyOut = "Dik#";
                                break;

                            // THIRD ROW
                            case DirectInputKeyCode.DikSemicolon:
                                dikKeyOut = "Dik:";
                                break;

                            case DirectInputKeyCode.DikApostrophe:
                                dikKeyOut = "Dik'";
                                break;

                            // FOURTH ROW

                            case DirectInputKeyCode.DikComma:
                                dikKeyOut = "Dik,";
                                break;

                            case DirectInputKeyCode.DikPeriod:
                                dikKeyOut = "Dik.";
                                break;

                            case DirectInputKeyCode.DikSlash:
                                dikKeyOut = "Dik/";
                                break;

                        }
                        break;

                    case "de-CH":

                        // http://kbdlayout.info/kbdsg/shiftstates+scancodes/base

                switch (dikKey)
                {
                    // FIRST ROW
                    case DirectInputKeyCode.DikGrave:
                        dikKeyOut = "Dik§";
                        break;

                    case DirectInputKeyCode.DikMinus:
                        dikKeyOut = "Dik'";
                        break;

                    case DirectInputKeyCode.DikEquals:
                        dikKeyOut = "Dik^";
                        break;

                    // SECOND ROW 
                    case DirectInputKeyCode.DikY:
                        dikKeyOut = "DikZ";
                        break;

                    case DirectInputKeyCode.DikLbracket:
                        dikKeyOut = "DikÜ";
                        break;

                    case DirectInputKeyCode.DikRbracket:
                        dikKeyOut = "Dik¨";
                        break;

                    case DirectInputKeyCode.DikBackslash:
                        dikKeyOut = "Dik$";
                        break;

                    // THIRD ROW
                    case DirectInputKeyCode.DikSemicolon:
                        dikKeyOut = "DikÖ";
                        break;

                    case DirectInputKeyCode.DikApostrophe:
                        dikKeyOut = "DikÄ";
                        break;

                    // FOURTH ROW
                    case DirectInputKeyCode.DikZ:
                        dikKeyOut = "DikY";
                        break;

                    case DirectInputKeyCode.DikComma:
                        dikKeyOut = "Dik,";
                        break;

                    case DirectInputKeyCode.DikPeriod:
                        dikKeyOut = "Dik.";
                        break;

                    case DirectInputKeyCode.DikSlash:
                        dikKeyOut = "Dik-";
                        break;

                }
                break;


                    case "es-ES":

                        // http://kbdlayout.info/kbdsp/shiftstates+scancodes/base
                        
                        switch (dikKey)
                        {
                            // FIRST ROW
                            case DirectInputKeyCode.DikGrave:
                                dikKeyOut = "Dikº";
                                break;

                            case DirectInputKeyCode.DikMinus:
                                dikKeyOut = "Dik'";
                                break;

                            case DirectInputKeyCode.DikEquals:
                                dikKeyOut = "Dik¡";
                                break;

                            // SECOND ROW 

                            case DirectInputKeyCode.DikLbracket:
                                dikKeyOut = "Dik`";
                                break;

                            case DirectInputKeyCode.DikRbracket:
                                dikKeyOut = "Dik+";
                                break;

                            case DirectInputKeyCode.DikBackslash:
                                dikKeyOut = "Dikç";
                                break;

                            // THIRD ROW
                            case DirectInputKeyCode.DikSemicolon:
                                dikKeyOut = "Dikñ";
                                break;

                            case DirectInputKeyCode.DikApostrophe:
                                dikKeyOut = "Dik´";
                                break;

                            // FOURTH ROW

                            case DirectInputKeyCode.DikComma:
                                dikKeyOut = "Dik,";
                                break;

                            case DirectInputKeyCode.DikPeriod:
                                dikKeyOut = "Dik.";
                                break;

                            case DirectInputKeyCode.DikSlash:
                                dikKeyOut = "Dik-";
                                break;

                        }
                        break;

                    case "da-DK":

                        // http://kbdlayout.info/kbdda/shiftstates+scancodes/base

                        switch (dikKey)
                        {
                            // FIRST ROW
                            case DirectInputKeyCode.DikGrave:
                                dikKeyOut = "Dik½";
                                break;

                            case DirectInputKeyCode.DikMinus:
                                dikKeyOut = "Dik+";
                                break;

                            case DirectInputKeyCode.DikEquals:
                                dikKeyOut = "Dik´";
                                break;


                            // SECOND ROW 
                            case DirectInputKeyCode.DikLbracket:
                                dikKeyOut = "DikÅ";
                                break;

                            case DirectInputKeyCode.DikRbracket:
                                dikKeyOut = "Dik¨";
                                break;

                            case DirectInputKeyCode.DikBackslash:
                                dikKeyOut = "Dik'";
                                break;


                            // THIRD ROW
                            case DirectInputKeyCode.DikSemicolon:
                                dikKeyOut = "DikÆ";
                                break;

                            case DirectInputKeyCode.DikApostrophe:
                                dikKeyOut = "DikØ";
                                break;

                            // FOURTH ROW

                            case DirectInputKeyCode.DikComma:
                                dikKeyOut = "Dik,";
                                break;

                            case DirectInputKeyCode.DikPeriod:
                                dikKeyOut = "Dik.";
                                break;

                            case DirectInputKeyCode.DikSlash:
                                dikKeyOut = "Dik-";
                                break;

                        }
                        break;

                    case "it-IT":

                        // http://kbdlayout.info/kbdit/shiftstates+scancodes/base

                        switch (dikKey)
                        {
                            // FIRST ROW
                            case DirectInputKeyCode.DikGrave:
                                dikKeyOut = "Dik\\";
                                break;

                            case DirectInputKeyCode.DikMinus:
                                dikKeyOut = "Dik'";
                                break;

                            case DirectInputKeyCode.DikEquals:
                                dikKeyOut = "DikÌ";
                                break;

                            // SECOND ROW 
                            case DirectInputKeyCode.DikLbracket:
                                dikKeyOut = "DikÈ";
                                break;

                            case DirectInputKeyCode.DikRbracket:
                                dikKeyOut = "Dik+";
                                break;

                            case DirectInputKeyCode.DikBackslash:
                                dikKeyOut = "DikÙ";
                                break;


                            // THIRD ROW
                            case DirectInputKeyCode.DikSemicolon:
                                dikKeyOut = "DikÒ";
                                break;

                            case DirectInputKeyCode.DikApostrophe:
                                dikKeyOut = "DikÀ";
                                break;


                            // FOURTH ROW

                            case DirectInputKeyCode.DikComma:
                                dikKeyOut = "Dik,";
                                break;

                            case DirectInputKeyCode.DikPeriod:
                                dikKeyOut = "Dik.";
                                break;

                            case DirectInputKeyCode.DikSlash:
                                dikKeyOut = "Dik-";
                                break;

                        }
                        break;

                    case "pt-PT":

                        // http://kbdlayout.info/kbdpo/shiftstates+scancodes/base

                        switch (dikKey)
                        {
                            // FIRST ROW
                            case DirectInputKeyCode.DikGrave:
                                dikKeyOut = "Dik\\";
                                break;

                            case DirectInputKeyCode.DikMinus:
                                dikKeyOut = "Dik'";
                                break;

                            case DirectInputKeyCode.DikEquals:
                                dikKeyOut = "Dik«";
                                break;

                            // SECOND ROW 
                            case DirectInputKeyCode.DikLbracket:
                                dikKeyOut = "Dik+";
                                break;

                            case DirectInputKeyCode.DikRbracket:
                                dikKeyOut = "Dik´";
                                break;

                            case DirectInputKeyCode.DikBackslash:
                                dikKeyOut = "Dik~";
                                break;

                            // THIRD ROW
                            case DirectInputKeyCode.DikSemicolon:
                                dikKeyOut = "DikÇ";
                                break;

                            case DirectInputKeyCode.DikApostrophe:
                                dikKeyOut = "Dikº";
                                break;

                            // FOURTH ROW

                            case DirectInputKeyCode.DikComma:
                                dikKeyOut = "Dik,";
                                break;

                            case DirectInputKeyCode.DikPeriod:
                                dikKeyOut = "Dik.";
                                break;

                            case DirectInputKeyCode.DikSlash:
                                dikKeyOut = "Dik-";
                                break;

                        }
                        break;


                    case "de-DE":
                        // http://kbdlayout.info/kbdgr/shiftstates+scancodes/base

                        switch (dikKey)
                        {
                            // FIRST ROW
                            case DirectInputKeyCode.DikGrave:
                                dikKeyOut = "Dik^";
                                break;

                            case DirectInputKeyCode.DikMinus:
                                dikKeyOut = "Dikß";
                                break;

                            case DirectInputKeyCode.DikEquals:
                                dikKeyOut = "Dik´";
                                break;

                            // SECOND ROW 
                            case DirectInputKeyCode.DikY:
                                dikKeyOut = "DikZ";
                                break;

                            case DirectInputKeyCode.DikLbracket:
                                dikKeyOut = "DikÜ";
                                break;

                            case DirectInputKeyCode.DikRbracket:
                                dikKeyOut = "Dik+";
                                break;

                            case DirectInputKeyCode.DikBackslash:
                                dikKeyOut = "Dik#";
                                break;

                            // THIRD ROW
                            case DirectInputKeyCode.DikSemicolon:
                                dikKeyOut = "DikÖ";
                                break;

                            case DirectInputKeyCode.DikApostrophe:
                                dikKeyOut = "DikÄ";
                                break;

                            // FOURTH ROW
                            case DirectInputKeyCode.DikZ:
                                dikKeyOut = "DikY";
                                break;

                            case DirectInputKeyCode.DikComma:
                                dikKeyOut = "Dik,";
                                break;

                            case DirectInputKeyCode.DikPeriod:
                                dikKeyOut = "Dik.";
                                break;

                            case DirectInputKeyCode.DikSlash:
                                dikKeyOut = "Dik-";
                                break;
                        }

                        break;
                    case "fr-FR":
                        // http://kbdlayout.info/kbdfr/shiftstates+scancodes/base
                        switch (dikKey)
                        {
                            // FIRST ROW
                            case DirectInputKeyCode.DikGrave:
                                dikKeyOut = "Dik²";
                                break;

                            case DirectInputKeyCode.Dik1:
                                dikKeyOut = "Dik&";
                                break;

                            case DirectInputKeyCode.Dik2:
                                dikKeyOut = "DikÉ";
                                break;

                            case DirectInputKeyCode.Dik3:
                                dikKeyOut = "Dik\"";
                                break;

                            case DirectInputKeyCode.Dik4:
                                dikKeyOut = "Dik'";
                                break;

                            case DirectInputKeyCode.Dik5:
                                dikKeyOut = "Dik(";
                                break;

                            case DirectInputKeyCode.Dik6:
                                dikKeyOut = "Dik-";
                                break;

                            case DirectInputKeyCode.Dik7:
                                dikKeyOut = "DikÈ";
                                break;

                            case DirectInputKeyCode.Dik8:
                                dikKeyOut = "Dik_";
                                break;

                            case DirectInputKeyCode.Dik9:
                                dikKeyOut = "DikÇ";
                                break;

                            case DirectInputKeyCode.Dik0:
                                dikKeyOut = "DikÀ";
                                break;

                            case DirectInputKeyCode.DikMinus:
                                dikKeyOut = "Dik)";
                                break;

                            case DirectInputKeyCode.DikEquals:
                                dikKeyOut = "Dik=";
                                break;

                            // SECOND ROW
                            case DirectInputKeyCode.DikQ:
                                dikKeyOut = "DikA";
                                break;

                            case DirectInputKeyCode.DikW:
                                dikKeyOut = "DikZ";
                                break;

                            case DirectInputKeyCode.DikLbracket:
                                dikKeyOut = "Dik^";
                                break;

                            case DirectInputKeyCode.DikRbracket:
                                dikKeyOut = "Dik$";
                                break;

                            case DirectInputKeyCode.DikBackslash:
                                dikKeyOut = "Dik*";
                                break;

                            // THIRD ROW
                            case DirectInputKeyCode.DikA:
                                dikKeyOut = "DikQ";
                                break;

                            case DirectInputKeyCode.DikSemicolon:
                                dikKeyOut = "DikM";
                                break;

                            case DirectInputKeyCode.DikApostrophe:
                                dikKeyOut = "DikÙ";
                                break;

                            // FOURTH ROW
                            case DirectInputKeyCode.DikZ:
                                dikKeyOut = "DikW";
                                break;

                            case DirectInputKeyCode.DikM:
                                dikKeyOut = "Dik,";
                                break;

                            case DirectInputKeyCode.DikComma:
                                dikKeyOut = "Dik;";
                                break;

                            case DirectInputKeyCode.DikPeriod:
                                dikKeyOut = "Dik:";
                                break;

                            case DirectInputKeyCode.DikSlash:
                                dikKeyOut = "Dik!";
                                break;

                        }

                        break;
                    default:

                        switch (dikKey)
                        {
                            // FIRST ROW
                            case DirectInputKeyCode.DikGrave:

                                dikKeyOut = "Dik`";
                                break;

                            case DirectInputKeyCode.DikMinus:
                                dikKeyOut = "Dik-";
                                break;

                            case DirectInputKeyCode.DikEquals:
                                dikKeyOut = "Dik=";
                                break;

                            // SECOND ROW 

                            case DirectInputKeyCode.DikLbracket:
                                dikKeyOut = "Dik[";
                                break;

                            case DirectInputKeyCode.DikRbracket:
                                dikKeyOut = "Dik]";
                                break;

                            case DirectInputKeyCode.DikBackslash:
                                dikKeyOut = "Dik\\";
                                break;

                            // THIRD ROW
                            case DirectInputKeyCode.DikSemicolon:
                                dikKeyOut = "Dik:";
                                break;

                            case DirectInputKeyCode.DikApostrophe:
                                dikKeyOut = "Dik'";
                                break;

                            // FOURTH ROW

                            case DirectInputKeyCode.DikComma:
                                dikKeyOut = "Dik,";
                                break;

                            case DirectInputKeyCode.DikPeriod:
                                dikKeyOut = "Dik.";
                                break;

                            case DirectInputKeyCode.DikSlash:
                                dikKeyOut = "Dik/";
                                break;
                        }

                        break;
                }

                builder.Append('{').Append(dikKeyOut).Append('}');
            }

            return builder.ToString();
        }

        private static bool TryGetDirectInputKey(string scKey, out DirectInputKeyCode dikKey)
        {
            switch (scKey)
            {
                // handle modifiers first
                case "lalt": dikKey = DirectInputKeyCode.DikLalt; return true;
                case "ralt": dikKey = DirectInputKeyCode.DikRalt; return true;
                case "lshift": dikKey = DirectInputKeyCode.DikLshift; return true;
                case "rshift": dikKey = DirectInputKeyCode.DikRshift; return true;
                case "lctrl": dikKey = DirectInputKeyCode.DikLcontrol; return true;
                case "rctrl": dikKey = DirectInputKeyCode.DikRcontrol; return true;

                // function keys first 
                case "f1": dikKey = DirectInputKeyCode.DikF1; return true;
                case "f2": dikKey = DirectInputKeyCode.DikF2; return true;
                case "f3": dikKey = DirectInputKeyCode.DikF3; return true;
                case "f4": dikKey = DirectInputKeyCode.DikF4; return true;
                case "f5": dikKey = DirectInputKeyCode.DikF5; return true;
                case "f6": dikKey = DirectInputKeyCode.DikF6; return true;
                case "f7": dikKey = DirectInputKeyCode.DikF7; return true;
                case "f8": dikKey = DirectInputKeyCode.DikF8; return true;
                case "f9": dikKey = DirectInputKeyCode.DikF9; return true;
                case "f10": dikKey = DirectInputKeyCode.DikF10; return true;
                case "f11": dikKey = DirectInputKeyCode.DikF11; return true;
                case "f12": dikKey = DirectInputKeyCode.DikF12; return true;
                case "f13": dikKey = DirectInputKeyCode.DikF13; return true;
                case "f14": dikKey = DirectInputKeyCode.DikF14; return true;
                case "f15": dikKey = DirectInputKeyCode.DikF15; return true;

                // all keys where the DX name does not match the SC name
                // Numpad
                case "numlock": dikKey = DirectInputKeyCode.DikNumlock; return true;

                case "np_divide": dikKey = DirectInputKeyCode.DikDivide; return true;
                case "np_multiply": dikKey = DirectInputKeyCode.DikMultiply; return true;
                case "np_subtract": dikKey = DirectInputKeyCode.DikSubtract; return true;
                case "np_add": dikKey = DirectInputKeyCode.DikAdd; return true;
                case "np_period": dikKey = DirectInputKeyCode.DikDecimal; return true;
                case "np_enter": dikKey = DirectInputKeyCode.DikNumpadenter; return true;
                case "np_0": dikKey = DirectInputKeyCode.DikNumpad0; return true;
                case "np_1": dikKey = DirectInputKeyCode.DikNumpad1; return true;
                case "np_2": dikKey = DirectInputKeyCode.DikNumpad2; return true;
                case "np_3": dikKey = DirectInputKeyCode.DikNumpad3; return true;
                case "np_4": dikKey = DirectInputKeyCode.DikNumpad4; return true;
                case "np_5": dikKey = DirectInputKeyCode.DikNumpad5; return true;
                case "np_6": dikKey = DirectInputKeyCode.DikNumpad6; return true;
                case "np_7": dikKey = DirectInputKeyCode.DikNumpad7; return true;
                case "np_8": dikKey = DirectInputKeyCode.DikNumpad8; return true;
                case "np_9": dikKey = DirectInputKeyCode.DikNumpad9; return true;
                // Digits
                case "0": dikKey = DirectInputKeyCode.Dik0; return true;
                case "1": dikKey = DirectInputKeyCode.Dik1; return true;
                case "2": dikKey = DirectInputKeyCode.Dik2; return true;
                case "3": dikKey = DirectInputKeyCode.Dik3; return true;
                case "4": dikKey = DirectInputKeyCode.Dik4; return true;
                case "5": dikKey = DirectInputKeyCode.Dik5; return true;
                case "6": dikKey = DirectInputKeyCode.Dik6; return true;
                case "7": dikKey = DirectInputKeyCode.Dik7; return true;
                case "8": dikKey = DirectInputKeyCode.Dik8; return true;
                case "9": dikKey = DirectInputKeyCode.Dik9; return true;
                // navigation
                case "insert": dikKey = DirectInputKeyCode.DikInsert; return true;
                case "home": dikKey = DirectInputKeyCode.DikHome; return true;
                case "delete": dikKey = DirectInputKeyCode.DikDelete; return true;
                case "end": dikKey = DirectInputKeyCode.DikEnd; return true;
                case "pgup": dikKey = DirectInputKeyCode.DikPageUp; return true;
                case "pgdown": dikKey = DirectInputKeyCode.DikPageDown; return true;
                case "pgdn": dikKey = DirectInputKeyCode.DikPageDown; return true;
                case "print": dikKey = DirectInputKeyCode.DikPrintscreen; return true;
                case "scrolllock": dikKey = DirectInputKeyCode.DikScroll; return true;
                case "pause": dikKey = DirectInputKeyCode.DikPause; return true;
                // Arrows
                case "up": dikKey = DirectInputKeyCode.DikUp; return true;
                case "down": dikKey = DirectInputKeyCode.DikDown; return true;
                case "left": dikKey = DirectInputKeyCode.DikLeft; return true;
                case "right": dikKey = DirectInputKeyCode.DikRight; return true;
                // non letters
                case "escape": dikKey = DirectInputKeyCode.DikEscape; return true;
                case "minus": dikKey = DirectInputKeyCode.DikMinus; return true;
                case "equals": dikKey = DirectInputKeyCode.DikEquals; return true;
                case "grave": dikKey = DirectInputKeyCode.DikGrave; return true;
                case "underline": dikKey = DirectInputKeyCode.DikUnderline; return true;
                case "backspace": dikKey = DirectInputKeyCode.DikBackspace; return true;
                case "tab": dikKey = DirectInputKeyCode.DikTab; return true;
                case "lbracket": dikKey = DirectInputKeyCode.DikLbracket; return true;
                case "rbracket": dikKey = DirectInputKeyCode.DikRbracket; return true;
                case "enter": dikKey = DirectInputKeyCode.DikReturn; return true;
                case "capslock": dikKey = DirectInputKeyCode.DikCapital; return true;
                case "colon": dikKey = DirectInputKeyCode.DikColon; return true;
                case "backslash": dikKey = DirectInputKeyCode.DikBackslash; return true;
                case "comma": dikKey = DirectInputKeyCode.DikComma; return true;
                case "period": dikKey = DirectInputKeyCode.DikPeriod; return true;
                case "slash": dikKey = DirectInputKeyCode.DikSlash; return true;
                case "space": dikKey = DirectInputKeyCode.DikSpace; return true;
                case "semicolon": dikKey = DirectInputKeyCode.DikSemicolon; return true;
                case "apostrophe": dikKey = DirectInputKeyCode.DikApostrophe; return true;

                // all where the lowercase DX name matches the SC name
                default:
                    var letter = "Dik" + scKey.ToUpperInvariant();
                    if (Enum.TryParse(letter, out DirectInputKeyCode dxKey))
                    {
                        dikKey = dxKey;
                        return true;
                    }

                    dikKey = default;
                    return false;
            }

        }


        internal static string ExtractMacro(string text, int position)
        {
            try
            {
                var endPosition = text.IndexOf(MACRO_END, position);

                // Found an end, let's verify it's actually a macro
                if (endPosition > position)
                {
                    // Use Regex to verify it's really a macro
                    var match = Regex.Match(text.Substring(position, endPosition - position + MACRO_END.Length), REGEX_MACRO);
                    if (match.Length > 0)
                    {
                        return match.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.FATAL, $"ExtractMacro Exception: {ex}");
            }

            return null;
        }

        internal static List<DirectInputKeyCode> ExtractKeyStrokes(string macroText)
        {
            var keyStrokes = new List<DirectInputKeyCode>();

            try
            {
                var matches = Regex.Matches(macroText, REGEX_SUB_COMMAND);
                foreach (var match in matches)
                {
                    var matchText = match.ToString().ToUpperInvariant().Replace("{", "").Replace("}", "");

                    var stroke = (DirectInputKeyCode)Enum.Parse(typeof(DirectInputKeyCode), matchText, true);

                    keyStrokes.Add(stroke);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.FATAL, $"ExtractKeyStrokes Exception: {ex}");
            }

            return keyStrokes;
        }
    }
}
