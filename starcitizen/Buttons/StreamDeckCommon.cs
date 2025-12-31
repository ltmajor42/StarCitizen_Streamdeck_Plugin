using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WindowsInput;
using WindowsInput.Native;
using BarRaider.SdTools;
using SCJMapper_V2.SC;

namespace starcitizen.Buttons
{
    static class StreamDeckCommon
    {
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern int GetWindowThreadProcessId(IntPtr handleWindow, out int lpdwProcessID);

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern IntPtr GetKeyboardLayout(int WindowsThreadProcessID);

        private static Dictionary<string,string> _lastStatus = new Dictionary<string, string>();
        
        public static bool ForceStop = false;

        private static readonly Regex SubCommandRegex = new Regex(CommandTools.REGEX_SUB_COMMAND, RegexOptions.Compiled);

        private static bool IsMouseToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            var t = token.Trim().ToLowerInvariant();
            return t == "mouse1" || t == "mouse2" || t == "mouse3" || t == "mouse4" || t == "mouse5" ||
                   t == "mwheelup" || t == "mwheeldown" || t == "mwheelleft" || t == "mwheelright";
        }

        private static bool IsModifierKey(DirectInputKeyCode code)
        {
            return code == DirectInputKeyCode.DikLalt || code == DirectInputKeyCode.DikRalt ||
                   code == DirectInputKeyCode.DikLcontrol || code == DirectInputKeyCode.DikRcontrol ||
                   code == DirectInputKeyCode.DikLshift || code == DirectInputKeyCode.DikRshift;
        }

        private static bool TryHandleMouseMacro(string macroText, int delay, bool isDown, bool isUp)
        {
            if (!SCPath.EnableMouseOutput)
            {
                return false;
            }

            // quick check
            if (macroText?.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) < 0 &&
                macroText?.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            try
            {
                var matches = SubCommandRegex.Matches(macroText);
                if (matches.Count == 0)
                {
                    return false;
                }

                var modifiers = new List<DirectInputKeyCode>();
                var otherKeys = new List<DirectInputKeyCode>();
                string mouseToken = null;

                foreach (Match match in matches)
                {
                    var token = match.Value.Replace("{", "").Replace("}", "").Trim();
                    if (string.IsNullOrEmpty(token)) continue;

                    if (IsMouseToken(token))
                    {
                        // only one mouse action per macro is expected; if multiple exist, we apply the last.
                        mouseToken = token.Trim().ToLowerInvariant();
                        continue;
                    }

                    if (Enum.TryParse(token, true, out DirectInputKeyCode dxKey))
                    {
                        if (IsModifierKey(dxKey)) modifiers.Add(dxKey);
                        else otherKeys.Add(dxKey);
                    }
                }

                if (string.IsNullOrEmpty(mouseToken))
                {
                    return false;
                }

                var iis = new InputSimulator();

                // press modifiers
                foreach (var mod in modifiers)
                {
                    iis.Keyboard.KeyDown(mod);
                }

                // press any other keys that might be included (rare)
                foreach (var key in otherKeys)
                {
                    iis.Keyboard.DelayedKeyPress(key, delay);
                }

                // mouse action
                if (!isUp)
                {
                    switch (mouseToken)
                    {
                        case "mouse1":
                            if (isDown) iis.Mouse.LeftButtonDown(); else iis.Mouse.LeftButtonClick();
                            break;
                        case "mouse2":
                            if (isDown) iis.Mouse.RightButtonDown(); else iis.Mouse.RightButtonClick();
                            break;
                        case "mouse3":
                            if (isDown) iis.Mouse.MiddleButtonDown(); else iis.Mouse.MiddleButtonClick();
                            break;
                        case "mouse4":
                            if (isDown) iis.Mouse.XButtonDown(1); else iis.Mouse.XButtonClick(1);
                            break;
                        case "mouse5":
                            if (isDown) iis.Mouse.XButtonDown(2); else iis.Mouse.XButtonClick(2);
                            break;
                        case "mwheelup":
                            iis.Mouse.VerticalScroll(1);
                            break;
                        case "mwheeldown":
                            iis.Mouse.VerticalScroll(-1);
                            break;
                        case "mwheelleft":
                            iis.Mouse.HorizontalScroll(-1);
                            break;
                        case "mwheelright":
                            iis.Mouse.HorizontalScroll(1);
                            break;
                    }
                }
                else
                {
                    switch (mouseToken)
                    {
                        case "mouse1": iis.Mouse.LeftButtonUp(); break;
                        case "mouse2": iis.Mouse.RightButtonUp(); break;
                        case "mouse3": iis.Mouse.MiddleButtonUp(); break;
                        case "mouse4": iis.Mouse.XButtonUp(1); break;
                        case "mouse5": iis.Mouse.XButtonUp(2); break;
                    }
                }

                // release modifiers
                for (int i = modifiers.Count - 1; i >= 0; i--)
                {
                    iis.Keyboard.KeyUp(modifiers[i]);
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"TryHandleMouseMacro failed: {ex.Message}");
                return false;
            }
        }
        private static void SendInput(string inputText, int delay)
        {
            var text = inputText;

            for (var idx = 0; idx < text.Length && !ForceStop; idx++)
            {
                var macro = CommandTools.ExtractMacro(text, idx);
                idx += macro.Length - 1;
                macro = macro.Substring(1, macro.Length - 2);

                HandleMacro(macro, delay);
            }
        }
        private static void SendInputDown(string inputText)
        {
            var text = inputText;

            for (var idx = 0; idx < text.Length && !ForceStop; idx++)
            {
                var macro = CommandTools.ExtractMacro(text, idx);
                idx += macro.Length - 1;
                macro = macro.Substring(1, macro.Length - 2);

                HandleMacroDown(macro);
            }
        }

        private static void SendInputUp(string inputText)
        {
            var text = inputText;

            for (var idx = 0; idx < text.Length && !ForceStop; idx++)
            {
                var macro = CommandTools.ExtractMacro(text, idx);
                idx += macro.Length - 1;
                macro = macro.Substring(1, macro.Length - 2);

                HandleMacroUp(macro);
            }
        }

        private static void HandleMacro(string macro, int delay)
        {
            if (TryHandleMouseMacro(macro, delay, isDown: false, isUp: false))
            {
                return;
            }

            var keyStrokes = CommandTools.ExtractKeyStrokes(macro);

            // Actually initiate the keystrokes
            if (keyStrokes.Count > 0)
            {
                var iis = new InputSimulator();
                var keyCode = keyStrokes.Last();
                keyStrokes.Remove(keyCode);

                if (keyStrokes.Count > 0)
                {
                    //iis.Keyboard.ModifiedKeyStroke(keyStrokes.Select(ks => ks).ToArray(), keyCode);

                    iis.Keyboard.DelayedModifiedKeyStroke(keyStrokes.Select(ks => ks), keyCode, delay);

                }
                else // Single Keycode
                {
                    //iis.Keyboard.KeyPress(keyCode);

                    iis.Keyboard.DelayedKeyPress(keyCode, delay);
                }
            }
        }

        private static void HandleMacroDown(string macro)
        {
            if (TryHandleMouseMacro(macro, delay: 0, isDown: true, isUp: false))
            {
                return;
            }

            var keyStrokes = CommandTools.ExtractKeyStrokes(macro);

            // Actually initiate the keystrokes
            if (keyStrokes.Count > 0)
            {
                var iis = new InputSimulator();
                var keyCode = keyStrokes.Last();
                keyStrokes.Remove(keyCode);

                if (keyStrokes.Count > 0)
                {
                    iis.Keyboard.ModifiedKeyStrokeDown(keyStrokes.Select(ks => ks), keyCode);

                }
                else // Single Keycode
                {
                    iis.Keyboard.DelayedKeyPressDown(keyCode);
                }
            }
        }


        private static void HandleMacroUp(string macro)
        {
            if (TryHandleMouseMacro(macro, delay: 0, isDown: false, isUp: true))
            {
                return;
            }

            var keyStrokes = CommandTools.ExtractKeyStrokes(macro);

            // Actually initiate the keystrokes
            if (keyStrokes.Count > 0)
            {
                var iis = new InputSimulator();
                var keyCode = keyStrokes.Last();
                keyStrokes.Remove(keyCode);

                if (keyStrokes.Count > 0)
                {
                    iis.Keyboard.ModifiedKeyStrokeUp(keyStrokes.Select(ks => ks), keyCode);

                }
                else // Single Keycode
                {
                    iis.Keyboard.DelayedKeyPressUp(keyCode);
                }
            }
        }

        public static void SendKeypress(string keyInfo, int delay)
        {
            if (!string.IsNullOrEmpty(keyInfo))
            {
                SendInput("{" + keyInfo + "}", delay);

                //Thread.Sleep(delay);

            }
        }

        public static void SendKeypressDown(string keyInfo)
        {
            if (!string.IsNullOrEmpty(keyInfo))
            {
                SendInputDown("{" + keyInfo + "}");
            }
        }


        public static void SendKeypressUp(string keyInfo)
        {
            if (!string.IsNullOrEmpty(keyInfo))
            {
                SendInputUp("{" + keyInfo + "}");
            }
        }



    }
}
