using BarRaider.SdTools;
using SCJMapper_V2.SC;
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
                var token = key?.Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (IsMouseToken(token))
                {
                    if (SCPath.EnableMouseOutput)
                    {
                        builder.Append('{').Append(NormalizeMouseToken(token)).Append('}');
                    }
                    else if (SCPath.SafeUnknownKeyTokens)
                    {
                        Logger.Instance.LogMessage(TracingLevel.WARN,
                            $"Mouse token '{token}' encountered but EnableMouseOutput=false. Skipping send.");
                    }
                    else
                    {
                        // Legacy behavior (falls back to Escape)
                        builder.Append('{').Append(FromSCKeyboardCmd(token)).Append('}');
                    }

                    continue;
                }

                if (TryFromSCKeyboardCmd(token, out var dxKey))
                {
                    builder.Append('{').Append(dxKey).Append('}');
                }
                else if (SCPath.SafeUnknownKeyTokens)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN,
                        $"Unknown key token '{token}' encountered. Skipping send (SafeUnknownKeyTokens=true).");
                }
                else
                {
                    builder.Append('{').Append(DirectInputKeyCode.DikEscape).Append('}');
                }
            }

            return builder.ToString();
        }

        private static string NormalizeMouseToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return string.Empty;
            return token.Trim().ToLowerInvariant();
        }

        private static bool IsMouseToken(string token)
        {
            var t = NormalizeMouseToken(token);
            return t == "mouse1" || t == "mouse2" || t == "mouse3" || t == "mouse4" || t == "mouse5" ||
                   t == "mwheelup" || t == "mwheeldown" || t == "mwheelleft" || t == "mwheelright";
        }

        private static string MouseTokenToDisplay(string token)
        {
            var t = NormalizeMouseToken(token);

            switch (t)
            {
                case "mouse1": return "Mouse1";
                case "mouse2": return "Mouse2";
                case "mouse3": return "Mouse3";
                case "mouse4": return "Mouse4";
                case "mouse5": return "Mouse5";
                case "mwheelup": return "WheelUp";
                case "mwheeldown": return "WheelDown";
                case "mwheelleft": return "WheelLeft";
                case "mwheelright": return "WheelRight";
                default: return t;
            }
        }

        private static bool TryFromSCKeyboardCmd(string scKey, out DirectInputKeyCode dxKey)
        {
            dxKey = default;

            if (string.IsNullOrWhiteSpace(scKey))
            {
                return false;
            }

            var key = scKey.Trim().ToLowerInvariant();

            switch (key)
            {
                // handle modifiers first
                case "lalt": dxKey = DirectInputKeyCode.DikLalt; return true;
                case "ralt": dxKey = DirectInputKeyCode.DikRalt; return true;
                case "lshift": dxKey = DirectInputKeyCode.DikLshift; return true;
                case "rshift": dxKey = DirectInputKeyCode.DikRshift; return true;
                case "lctrl": dxKey = DirectInputKeyCode.DikLcontrol; return true;
                case "rctrl": dxKey = DirectInputKeyCode.DikRcontrol; return true;

                // function keys first 
                case "f1": dxKey = DirectInputKeyCode.DikF1; return true;
                case "f2": dxKey = DirectInputKeyCode.DikF2; return true;
                case "f3": dxKey = DirectInputKeyCode.DikF3; return true;
                case "f4": dxKey = DirectInputKeyCode.DikF4; return true;
                case "f5": dxKey = DirectInputKeyCode.DikF5; return true;
                case "f6": dxKey = DirectInputKeyCode.DikF6; return true;
                case "f7": dxKey = DirectInputKeyCode.DikF7; return true;
                case "f8": dxKey = DirectInputKeyCode.DikF8; return true;
                case "f9": dxKey = DirectInputKeyCode.DikF9; return true;
                case "f10": dxKey = DirectInputKeyCode.DikF10; return true;
                case "f11": dxKey = DirectInputKeyCode.DikF11; return true;
                case "f12": dxKey = DirectInputKeyCode.DikF12; return true;
                case "f13": dxKey = DirectInputKeyCode.DikF13; return true;
                case "f14": dxKey = DirectInputKeyCode.DikF14; return true;
                case "f15": dxKey = DirectInputKeyCode.DikF15; return true;

                // all keys where the DX name does not match the SC name
                // Numpad
                case "numlock": dxKey = DirectInputKeyCode.DikNumlock; return true;

                case "np_divide": dxKey = DirectInputKeyCode.DikDivide; return true;
                case "np_multiply": dxKey = DirectInputKeyCode.DikMultiply; return true;
                case "np_subtract": dxKey = DirectInputKeyCode.DikSubtract; return true;
                case "np_add": dxKey = DirectInputKeyCode.DikAdd; return true;
                case "np_period": dxKey = DirectInputKeyCode.DikDecimal; return true;
                case "np_enter": dxKey = DirectInputKeyCode.DikNumpadenter; return true;
                case "np_0": dxKey = DirectInputKeyCode.DikNumpad0; return true;
                case "np_1": dxKey = DirectInputKeyCode.DikNumpad1; return true;
                case "np_2": dxKey = DirectInputKeyCode.DikNumpad2; return true;
                case "np_3": dxKey = DirectInputKeyCode.DikNumpad3; return true;
                case "np_4": dxKey = DirectInputKeyCode.DikNumpad4; return true;
                case "np_5": dxKey = DirectInputKeyCode.DikNumpad5; return true;
                case "np_6": dxKey = DirectInputKeyCode.DikNumpad6; return true;
                case "np_7": dxKey = DirectInputKeyCode.DikNumpad7; return true;
                case "np_8": dxKey = DirectInputKeyCode.DikNumpad8; return true;
                case "np_9": dxKey = DirectInputKeyCode.DikNumpad9; return true;

                // Digits
                case "0": dxKey = DirectInputKeyCode.Dik0; return true;
                case "1": dxKey = DirectInputKeyCode.Dik1; return true;
                case "2": dxKey = DirectInputKeyCode.Dik2; return true;
                case "3": dxKey = DirectInputKeyCode.Dik3; return true;
                case "4": dxKey = DirectInputKeyCode.Dik4; return true;
                case "5": dxKey = DirectInputKeyCode.Dik5; return true;
                case "6": dxKey = DirectInputKeyCode.Dik6; return true;
                case "7": dxKey = DirectInputKeyCode.Dik7; return true;
                case "8": dxKey = DirectInputKeyCode.Dik8; return true;
                case "9": dxKey = DirectInputKeyCode.Dik9; return true;

                // navigation
                case "insert": dxKey = DirectInputKeyCode.DikInsert; return true;
                case "home": dxKey = DirectInputKeyCode.DikHome; return true;
                case "delete": dxKey = DirectInputKeyCode.DikDelete; return true;
                case "end": dxKey = DirectInputKeyCode.DikEnd; return true;
                case "pgup": dxKey = DirectInputKeyCode.DikPageUp; return true;
                case "pgdown": dxKey = DirectInputKeyCode.DikPageDown; return true;
                case "pgdn": dxKey = DirectInputKeyCode.DikPageDown; return true;
                case "print": dxKey = DirectInputKeyCode.DikPrintscreen; return true;
                case "scrolllock": dxKey = DirectInputKeyCode.DikScroll; return true;
                case "pause": dxKey = DirectInputKeyCode.DikPause; return true;

                // Arrows
                case "up": dxKey = DirectInputKeyCode.DikUp; return true;
                case "down": dxKey = DirectInputKeyCode.DikDown; return true;
                case "left": dxKey = DirectInputKeyCode.DikLeft; return true;
                case "right": dxKey = DirectInputKeyCode.DikRight; return true;

                // non letters
                case "escape": dxKey = DirectInputKeyCode.DikEscape; return true;
                case "minus": dxKey = DirectInputKeyCode.DikMinus; return true;
                case "equals": dxKey = DirectInputKeyCode.DikEquals; return true;
                case "grave": dxKey = DirectInputKeyCode.DikGrave; return true;
                case "underline": dxKey = DirectInputKeyCode.DikUnderline; return true;
                case "backspace": dxKey = DirectInputKeyCode.DikBackspace; return true;
                case "tab": dxKey = DirectInputKeyCode.DikTab; return true;
                case "lbracket": dxKey = DirectInputKeyCode.DikLbracket; return true;
                case "rbracket": dxKey = DirectInputKeyCode.DikRbracket; return true;
                case "enter": dxKey = DirectInputKeyCode.DikReturn; return true;
                case "capslock": dxKey = DirectInputKeyCode.DikCapital; return true;
                case "colon": dxKey = DirectInputKeyCode.DikColon; return true;
                case "backslash": dxKey = DirectInputKeyCode.DikBackslash; return true;
                case "comma": dxKey = DirectInputKeyCode.DikComma; return true;
                case "period": dxKey = DirectInputKeyCode.DikPeriod; return true;
                case "slash": dxKey = DirectInputKeyCode.DikSlash; return true;
                case "space": dxKey = DirectInputKeyCode.DikSpace; return true;
                case "semicolon": dxKey = DirectInputKeyCode.DikSemicolon; return true;
                case "apostrophe": dxKey = DirectInputKeyCode.DikApostrophe; return true;
            }

            // all where the lowercase DX name matches the SC name
            var letter = "Dik" + key.ToUpperInvariant();
            return Enum.TryParse(letter, out dxKey);
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
                    if (SCPath.SafeUnknownKeyTokens)
                    {
                        builder.Append('{').Append(token).Append('}');
                        continue;
                    }

                    // Legacy fallback
                    dikKey = DirectInputKeyCode.DikEscape;
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

        private static DirectInputKeyCode FromSCKeyboardCmd(string scKey)
        {
            switch (scKey)
            {
                // handle modifiers first
                case "lalt": return DirectInputKeyCode.DikLalt;
                case "ralt": return DirectInputKeyCode.DikRalt;
                case "lshift": return DirectInputKeyCode.DikLshift;
                case "rshift": return DirectInputKeyCode.DikRshift;
                case "lctrl": return DirectInputKeyCode.DikLcontrol;
                case "rctrl": return DirectInputKeyCode.DikRcontrol;

                // function keys first 
                case "f1": return DirectInputKeyCode.DikF1;
                case "f2": return DirectInputKeyCode.DikF2;
                case "f3": return DirectInputKeyCode.DikF3;
                case "f4": return DirectInputKeyCode.DikF4;
                case "f5": return DirectInputKeyCode.DikF5;
                case "f6": return DirectInputKeyCode.DikF6;
                case "f7": return DirectInputKeyCode.DikF7;
                case "f8": return DirectInputKeyCode.DikF8;
                case "f9": return DirectInputKeyCode.DikF9;
                case "f10": return DirectInputKeyCode.DikF10;
                case "f11": return DirectInputKeyCode.DikF11;
                case "f12": return DirectInputKeyCode.DikF12;
                case "f13": return DirectInputKeyCode.DikF13;
                case "f14": return DirectInputKeyCode.DikF14;
                case "f15": return DirectInputKeyCode.DikF15;

                // all keys where the DX name does not match the SC name
                // Numpad
                case "numlock": return DirectInputKeyCode.DikNumlock;

                case "np_divide": return DirectInputKeyCode.DikDivide;
                case "np_multiply": return DirectInputKeyCode.DikMultiply;
                case "np_subtract": return DirectInputKeyCode.DikSubtract;
                case "np_add": return DirectInputKeyCode.DikAdd;
                case "np_period": return DirectInputKeyCode.DikDecimal;
                case "np_enter": return DirectInputKeyCode.DikNumpadenter;
                case "np_0": return DirectInputKeyCode.DikNumpad0;
                case "np_1": return DirectInputKeyCode.DikNumpad1;
                case "np_2": return DirectInputKeyCode.DikNumpad2;
                case "np_3": return DirectInputKeyCode.DikNumpad3;
                case "np_4": return DirectInputKeyCode.DikNumpad4;
                case "np_5": return DirectInputKeyCode.DikNumpad5;
                case "np_6": return DirectInputKeyCode.DikNumpad6;
                case "np_7": return DirectInputKeyCode.DikNumpad7;
                case "np_8": return DirectInputKeyCode.DikNumpad8;
                case "np_9": return DirectInputKeyCode.DikNumpad9;
                // Digits
                case "0": return DirectInputKeyCode.Dik0;
                case "1": return DirectInputKeyCode.Dik1;
                case "2": return DirectInputKeyCode.Dik2;
                case "3": return DirectInputKeyCode.Dik3;
                case "4": return DirectInputKeyCode.Dik4;
                case "5": return DirectInputKeyCode.Dik5;
                case "6": return DirectInputKeyCode.Dik6;
                case "7": return DirectInputKeyCode.Dik7;
                case "8": return DirectInputKeyCode.Dik8;
                case "9": return DirectInputKeyCode.Dik9;
                // navigation
                case "insert": return DirectInputKeyCode.DikInsert;
                case "home": return DirectInputKeyCode.DikHome;
                case "delete": return DirectInputKeyCode.DikDelete;
                case "end": return DirectInputKeyCode.DikEnd;
                case "pgup": return DirectInputKeyCode.DikPageUp;
                case "pgdown": return DirectInputKeyCode.DikPageDown;
                case "pgdn": return DirectInputKeyCode.DikPageDown;
                case "print": return DirectInputKeyCode.DikPrintscreen;
                case "scrolllock": return DirectInputKeyCode.DikScroll;
                case "pause": return DirectInputKeyCode.DikPause;
                // Arrows
                case "up": return DirectInputKeyCode.DikUp;
                case "down": return DirectInputKeyCode.DikDown;
                case "left": return DirectInputKeyCode.DikLeft;
                case "right": return DirectInputKeyCode.DikRight;
                // non letters
                case "escape": return DirectInputKeyCode.DikEscape;
                case "minus": return DirectInputKeyCode.DikMinus;
                case "equals": return DirectInputKeyCode.DikEquals;
                case "grave": return DirectInputKeyCode.DikGrave;
                case "underline": return DirectInputKeyCode.DikUnderline;
                case "backspace": return DirectInputKeyCode.DikBackspace;
                case "tab": return DirectInputKeyCode.DikTab;
                case "lbracket": return DirectInputKeyCode.DikLbracket;
                case "rbracket": return DirectInputKeyCode.DikRbracket;
                case "enter": return DirectInputKeyCode.DikReturn;
                case "capslock": return DirectInputKeyCode.DikCapital;
                case "colon": return DirectInputKeyCode.DikColon;
                case "backslash": return DirectInputKeyCode.DikBackslash;
                case "comma": return DirectInputKeyCode.DikComma;
                case "period": return DirectInputKeyCode.DikPeriod;
                case "slash": return DirectInputKeyCode.DikSlash;
                case "space": return DirectInputKeyCode.DikSpace;
                case "semicolon": return DirectInputKeyCode.DikSemicolon;
                case "apostrophe": return DirectInputKeyCode.DikApostrophe;

                // all where the lowercase DX name matches the SC name
                default:
                    var letter = "Dik" + scKey.ToUpperInvariant();
                    if (Enum.TryParse(letter, out DirectInputKeyCode dxKey))
                    {
                        return dxKey;
                    }
                    else
                    {
                        return DirectInputKeyCode.DikEscape;
                    }
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
