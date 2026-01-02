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
using starcitizen.Core;
using System.Diagnostics;

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

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // (no diagnostic P/Invoke here)
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static readonly Dictionary<string,string> _lastStatus = new Dictionary<string, string>();
        
        public static bool ForceStop = false;

        private static readonly Regex SubCommandRegex = new Regex(CommandTools.REGEX_SUB_COMMAND, RegexOptions.Compiled);

        // Shared InputSimulator instance to avoid repeated allocations
        private static readonly InputSimulator s_inputSimulator = new InputSimulator();

        // Correlation id for send operations (per logical keypress)
        private static readonly AsyncLocal<string> CurrentCorrelation = new AsyncLocal<string>();

        private static string CorrelationOrDash => CurrentCorrelation.Value ?? "-";

        private static string GetForegroundInfo()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return "hwnd=0";
                GetWindowThreadProcessId(hwnd, out var pid);
                string proc = "";
                try
                {
                    var p = Process.GetProcessById(pid);
                    proc = p.ProcessName;
                }
                catch { proc = pid.ToString(); }

                return $"hwnd=0x{hwnd.ToInt64():X},pid={pid},proc={proc}";
            }
            catch
            {
                return "hwnd=?";
            }
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

                    if (MouseTokenHelper.TryNormalize(token, out var normalizedMouse))
                    {
                        // only one mouse action per macro is expected; if multiple exist, we apply the last.
                        mouseToken = normalizedMouse;
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

                if (SCPath.DetailedInputDiagnostics)
                {
                    PluginLog.Debug($"[{CorrelationOrDash}] TryHandleMouseMacro: mouseToken={mouseToken}, modifiers=[{string.Join(",", modifiers)}], other=[{string.Join(",", otherKeys)}], isDown={isDown}, isUp={isUp}, delay={delay}");
                }

                var iis = s_inputSimulator;

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
                    // Handle composite mouse tokens like "mouse1+mouse2"
                    if (mouseToken.Contains("+"))
                    {
                        var parts = mouseToken.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            switch (part)
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
                                    if (SCPath.CoalesceMouseWheel) _coalesceWheel(part, 1, CorrelationOrDash, iis); else iis.Mouse.VerticalScroll(1);
                                    break;
                                case "mwheeldown":
                                    if (SCPath.CoalesceMouseWheel) _coalesceWheel(part, -1, CorrelationOrDash, iis); else iis.Mouse.VerticalScroll(-1);
                                    break;
                                case "mwheelleft": iis.Mouse.HorizontalScroll(-1); break;
                                case "mwheelright": iis.Mouse.HorizontalScroll(1); break;
                                default:
                                    // unknown part - ignore
                                    break;
                            }
                            // small gap between composite actions to improve recognition
                            Thread.Sleep(8);
                        }
                    }
                    else
                    {
                        switch (mouseToken)
                        {
                            case "mouse1":
                                if (isDown)
                                {
                                    if (SCPath.DetailedInputDiagnostics) PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: LeftButtonDown");
                                    iis.Mouse.LeftButtonDown();
                                }
                                else
                                {
                                    if (SCPath.DetailedInputDiagnostics) PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: LeftButtonClick");
                                    iis.Mouse.LeftButtonClick();
                                }
                                break;
                            case "mouse2":
                                if (isDown)
                                {
                                    PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: RightButtonDown");
                                    iis.Mouse.RightButtonDown();
                                }
                                else
                                {
                                    PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: RightButtonClick");
                                    iis.Mouse.RightButtonClick();
                                }
                                break;
                            case "mouse3":
                                if (isDown)
                                {
                                    PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: MiddleButtonDown");
                                    iis.Mouse.MiddleButtonDown();
                                }
                                else
                                {
                                    PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: MiddleButtonClick");
                                    iis.Mouse.MiddleButtonClick();
                                }
                                break;
                            case "mouse4":
                                if (isDown)
                                {
                                    PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: XButtonDown(1)");
                                    iis.Mouse.XButtonDown(1);
                                }
                                else
                                {
                                    PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: XButtonClick(1)");
                                    iis.Mouse.XButtonClick(1);
                                }
                                break;
                            case "mouse5":
                                if (isDown)
                                {
                                    PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: XButtonDown(2)");
                                    iis.Mouse.XButtonDown(2);
                                }
                                else
                                {
                                    PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: XButtonClick(2)");
                                    iis.Mouse.XButtonClick(2);
                                }
                                break;
                            case "mwheelup":
                                if (SCPath.CoalesceMouseWheel)
                                {
                                    _coalesceWheel(mouseToken, 1, CorrelationOrDash, iis);
                                }
                                else
                                {
                                    if (SCPath.DetailedInputDiagnostics) PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: VerticalScroll(1)");
                                    iis.Mouse.VerticalScroll(1);
                                }
                                break;
                            case "mwheeldown":
                                if (SCPath.CoalesceMouseWheel)
                                {
                                    _coalesceWheel(mouseToken, -1, CorrelationOrDash, iis);
                                }
                                else
                                {
                                    if (SCPath.DetailedInputDiagnostics) PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: VerticalScroll(-1)");
                                    iis.Mouse.VerticalScroll(-1);
                                }
                                break;
                            case "mwheelleft":
                                PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: HorizontalScroll(-1)");
                                iis.Mouse.HorizontalScroll(-1);
                                break;
                            case "mwheelright":
                                PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: HorizontalScroll(1)");
                                iis.Mouse.HorizontalScroll(1);
                                break;
                        }
                    }
                }
                else
                {
                    // handle mouse up; support composite tokens
                    if (mouseToken.Contains("+"))
                    {
                        var parts = mouseToken.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            switch (part)
                            {
                                case "mouse1": iis.Mouse.LeftButtonUp(); break;
                                case "mouse2": iis.Mouse.RightButtonUp(); break;
                                case "mouse3": iis.Mouse.MiddleButtonUp(); break;
                                case "mouse4": iis.Mouse.XButtonUp(1); break;
                                case "mouse5": iis.Mouse.XButtonUp(2); break;
                            }
                            Thread.Sleep(4);
                        }
                    }
                    else
                    {
                        switch (mouseToken)
                        {
                            case "mouse1": PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: LeftButtonUp"); iis.Mouse.LeftButtonUp(); break;
                            case "mouse2": PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: RightButtonUp"); iis.Mouse.RightButtonUp(); break;
                            case "mouse3": PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: MiddleButtonUp"); iis.Mouse.MiddleButtonUp(); break;
                            case "mouse4": PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: XButtonUp(1)"); iis.Mouse.XButtonUp(1); break;
                            case "mouse5": PluginLog.Debug($"[{CorrelationOrDash}] Mouse action: XButtonUp(2)"); iis.Mouse.XButtonUp(2); break;
                        }
                    }
                }

                // release modifiers
                for (int i = modifiers.Count - 1; i >= 0; i--)
                {
                    PluginLog.Debug($"[{CorrelationOrDash}] Releasing modifier: {modifiers[i]}");
                    iis.Keyboard.KeyUp(modifiers[i]);
                }

                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Warn($"[{CorrelationOrDash}] TryHandleMouseMacro failed: {ex.Message}");
                return false;
            }
        }
        private static void SendInput(string inputText, int delay)
        {
            PluginLog.Debug($"[{CorrelationOrDash}] SendInput called with: {inputText}, delay={delay}, fg={GetForegroundInfo()}");

            // If Stream Deck is currently the foreground window, attempt to focus a better target (prefer Star Citizen)
            try
            {
                EnsureTargetWindowFocused();
            }
            catch (Exception ex)
            {
                PluginLog.Debug($"[{CorrelationOrDash}] EnsureTargetWindowFocused failed: {ex.Message}");
            }

            var text = inputText;

            for (var idx = 0; idx < text.Length && !ForceStop; idx++)
            {
                var macro = CommandTools.ExtractMacro(text, idx);
                if (string.IsNullOrEmpty(macro))
                {
                    // No macro at this position; continue to next character
                    if (CommandTools.Verbose) PluginLog.Debug($"[{CorrelationOrDash}] SendInput: no macro at pos {idx} in '{text}'");
                    continue;
                }

                idx += macro.Length - 1;
                macro = macro.Substring(1, macro.Length - 2);

                PluginLog.Debug($"[{CorrelationOrDash}] SendInput handling macro: {macro}");
                HandleMacro(macro, delay);
            }
        }
        private static void SendInputDown(string inputText)
        {
            PluginLog.Debug($"[{CorrelationOrDash}] SendInputDown called with: {inputText}, fg={GetForegroundInfo()}");

            try { EnsureTargetWindowFocused(); } catch (Exception ex) { PluginLog.Debug($"[{CorrelationOrDash}] EnsureTargetWindowFocused failed: {ex.Message}"); }

            var text = inputText;

            for (var idx = 0; idx < text.Length && !ForceStop; idx++)
            {
                var macro = CommandTools.ExtractMacro(text, idx);
                if (string.IsNullOrEmpty(macro))
                {
                    if (CommandTools.Verbose) PluginLog.Debug($"[{CorrelationOrDash}] SendInputDown: no macro at pos {idx} in '{text}'");
                    continue;
                }

                idx += macro.Length - 1;
                macro = macro.Substring(1, macro.Length - 2);

                PluginLog.Debug($"[{CorrelationOrDash}] SendInputDown handling macro: {macro}");
                HandleMacroDown(macro);
            }
        }

        private static void SendInputUp(string inputText)
        {
            PluginLog.Debug($"[{CorrelationOrDash}] SendInputUp called with: {inputText}, fg={GetForegroundInfo()}");

            try { EnsureTargetWindowFocused(); } catch (Exception ex) { PluginLog.Debug($"[{CorrelationOrDash}] EnsureTargetWindowFocused failed: {ex.Message}"); }

            var text = inputText;

            for (var idx = 0; idx < text.Length && !ForceStop; idx++)
            {
                var macro = CommandTools.ExtractMacro(text, idx);
                if (string.IsNullOrEmpty(macro))
                {
                    if (CommandTools.Verbose) PluginLog.Debug($"[{CorrelationOrDash}] SendInputUp: no macro at pos {idx} in '{text}'");
                    continue;
                }

                idx += macro.Length - 1;
                macro = macro.Substring(1, macro.Length - 2);

                PluginLog.Debug($"[{CorrelationOrDash}] SendInputUp handling macro: {macro}");
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

            if (SCPath.DetailedInputDiagnostics) PluginLog.Debug($"[{CorrelationOrDash}] HandleMacro: macro={macro}, keyStrokesCount={keyStrokes.Count}, delay={delay}");

            // Actually initiate the keystrokes
            if (keyStrokes.Count > 0)
            {
                if (SCPath.DetailedInputDiagnostics)
                {
                    PluginLog.Debug($"[{CorrelationOrDash}] Sending keystrokes: [{string.Join(",", keyStrokes)}] delay={delay}");
                    try
                    {
                        var details = keyStrokes.Select(ks => ks + "(" + ((int)ks) + ")");
                        PluginLog.Debug($"[{CorrelationOrDash}] Keystroke details: [{string.Join(",", details)}]");
                    }
                    catch { }
                }

                void DoSend(InputSimulator i)
                {
                    var keyCode = keyStrokes.Last();
                    keyStrokes.Remove(keyCode);

                    if (keyStrokes.Count > 0)
                    {
                        i.Keyboard.DelayedModifiedKeyStroke(keyStrokes.Select(ks => ks), keyCode, delay);
                    }
                    else // Single Keycode
                    {
                        i.Keyboard.DelayedKeyPress(keyCode, delay);
                    }
                }

                try
                {
                    DoSend(s_inputSimulator);
                }
                catch (Exception ex)
                {
                    PluginLog.Warn($"[{CorrelationOrDash}] HandleMacro send failed with shared simulator: {ex}. Retrying with fresh simulator.");
                    try
                    {
                        var fresh = new InputSimulator();
                        DoSend(fresh);
                    }
                    catch (Exception ex2)
                    {
                        PluginLog.Error($"[{CorrelationOrDash}] HandleMacro retry failed: {ex2}");
                    }
                }
            }
            else
            {
                PluginLog.Debug($"[{CorrelationOrDash}] HandleMacro: no keystrokes found to send.");
            }
        }

        private static void HandleMacroDown(string macro)
        {
            if (TryHandleMouseMacro(macro, delay: 0, isDown: true, isUp: false))
            {
                return;
            }

            var keyStrokes = CommandTools.ExtractKeyStrokes(macro);

            PluginLog.Debug($"[{CorrelationOrDash}] HandleMacroDown: macro={macro}, keyStrokesCount={keyStrokes.Count}");

            // Actually initiate the keystrokes
            if (keyStrokes.Count > 0)
            {
                PluginLog.Debug($"[{CorrelationOrDash}] Sending keystrokes down: [{string.Join(",", keyStrokes)}]");

                try
                {
                    var iis = s_inputSimulator;
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
                catch (Exception ex)
                {
                    PluginLog.Warn($"[{CorrelationOrDash}] HandleMacroDown failed with shared simulator: {ex}. Retrying with fresh simulator.");
                    try
                    {
                        var fresh = new InputSimulator();
                        var keyCode = keyStrokes.Last();
                        keyStrokes.Remove(keyCode);
                        if (keyStrokes.Count > 0)
                        {
                            fresh.Keyboard.ModifiedKeyStrokeDown(keyStrokes.Select(ks => ks), keyCode);
                        }
                        else
                        {
                            fresh.Keyboard.DelayedKeyPressDown(keyCode);
                        }
                    }
                    catch (Exception ex2)
                    {
                        PluginLog.Error($"[{CorrelationOrDash}] HandleMacroDown retry failed: {ex2}");
                    }
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

            PluginLog.Debug($"[{CorrelationOrDash}] HandleMacroUp: macro={macro}, keyStrokesCount={keyStrokes.Count}");

            // Actually initiate the keystrokes
            if (keyStrokes.Count > 0)
            {
                PluginLog.Debug($"[{CorrelationOrDash}] Sending keystrokes up: [{string.Join(",", keyStrokes)}]");

                try
                {
                    var iis = s_inputSimulator;
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
                catch (Exception ex)
                {
                    PluginLog.Warn($"[{CorrelationOrDash}] HandleMacroUp failed with shared simulator: {ex}. Retrying with fresh simulator.");
                    try
                    {
                        var fresh = new InputSimulator();
                        var keyCode = keyStrokes.Last();
                        keyStrokes.Remove(keyCode);
                        if (keyStrokes.Count > 0)
                        {
                            fresh.Keyboard.ModifiedKeyStrokeUp(keyStrokes.Select(ks => ks), keyCode);
                        }
                        else
                        {
                            fresh.Keyboard.DelayedKeyPressUp(keyCode);
                        }
                    }
                    catch (Exception ex2)
                    {
                        PluginLog.Error($"[{CorrelationOrDash}] HandleMacroUp retry failed: {ex2}");
                    }
                }
            }
        }

        public static void SendKeypress(string keyInfo, int delay)
        {
            if (!string.IsNullOrEmpty(keyInfo))
            {
                // create a correlation id for this send operation
                CurrentCorrelation.Value = Guid.NewGuid().ToString("N");
                PluginLog.Info($"[{CorrelationOrDash}] SendKeypress requested: {keyInfo}, delay={delay}, fg={GetForegroundInfo()}");
                try
                {
                    SendInput("{" + keyInfo + "}", delay);
                }
                finally
                {
                    // clear correlation after send (guaranteed)
                    CurrentCorrelation.Value = null;
                }
            }
        }

        public static void SendKeypressDown(string keyInfo)
        {
            if (!string.IsNullOrEmpty(keyInfo))
            {
                CurrentCorrelation.Value = Guid.NewGuid().ToString("N");
                PluginLog.Info($"[{CorrelationOrDash}] SendKeypressDown requested: {keyInfo}, fg={GetForegroundInfo()}");
                try
                {
                    SendInputDown("{" + keyInfo + "}");
                }
                finally
                {
                    CurrentCorrelation.Value = null;
                }
            }
        }


        public static void SendKeypressUp(string keyInfo)
        {
            if (!string.IsNullOrEmpty(keyInfo))
            {
                CurrentCorrelation.Value = Guid.NewGuid().ToString("N");
                PluginLog.Info($"[{CorrelationOrDash}] SendKeypressUp requested: {keyInfo}, fg={GetForegroundInfo()}");
                try
                {
                    SendInputUp("{" + keyInfo + "}");
                }
                finally
                {
                    CurrentCorrelation.Value = null;
                }
            }
        }

        // Simple wheel coalescing implementation
        private static readonly object _wheelLock = new object();
        private static int _wheelAccum = 0;
        private static DateTime _wheelLastLog = DateTime.MinValue;

        private static void _coalesceWheel(string token, int amount, string corr, InputSimulator iis)
        {
            lock (_wheelLock)
            {
                _wheelAccum += amount;
                var now = DateTime.UtcNow;
                // if last log older than 60ms, flush now
                if ((now - _wheelLastLog).TotalMilliseconds >= 60)
                {
                    if (_wheelAccum != 0)
                    {
                        PluginLog.Debug($"[{corr}] Mouse action: VerticalScroll({_wheelAccum}) (coalesced)");
                        iis.Mouse.VerticalScroll(_wheelAccum);
                    }
                    _wheelAccum = 0;
                    _wheelLastLog = now;
                }
            }
        }

        private static void EnsureTargetWindowFocused()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return;

                // If Stream Deck is foreground, try to find any other top-level window to focus (WordPad, Notepad, game, etc.)
                GetWindowThreadProcessId(hwnd, out var pid);
                try
                {
                    var p = Process.GetProcessById(pid);
                    if (p != null && p.ProcessName?.IndexOf("StreamDeck", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Prefer StarCitizen if present
                        var sc = Process.GetProcessesByName("StarCitizen").FirstOrDefault() ?? Process.GetProcessesByName("StarCitizen64").FirstOrDefault();
                        if (sc != null && sc.MainWindowHandle != IntPtr.Zero)
                        {
                            SetForegroundWindow(sc.MainWindowHandle);
                            return;
                        }

                        // Try to focus the previous window in z-order (likely the app the user was in)
                        const uint GW_HWNDPREV = 3;
                        var prev = GetWindow(hwnd, GW_HWNDPREV);
                        for (int i = 0; i < 8 && prev != IntPtr.Zero; i++)
                        {
                            try
                            {
                                if (prev != IntPtr.Zero && IsWindowVisible(prev))
                                {
                                    GetWindowThreadProcessId(prev, out var prevPid);
                                    try
                                    {
                                        var prevProc = Process.GetProcessById(prevPid);
                                        if (prevProc != null && prevProc.ProcessName?.IndexOf("StreamDeck", StringComparison.OrdinalIgnoreCase) < 0)
                                        {
                                            SetForegroundWindow(prev);
                                            return;
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }

                            prev = GetWindow(prev, GW_HWNDPREV);
                        }

                        // Fallback: pick any other visible top-level window (exclude system processes and StreamDeck)
                        var candidate = Process.GetProcesses()
                            .Where(pr => pr != null && pr.Id != pid && pr.MainWindowHandle != IntPtr.Zero)
                            .Where(pr => !string.IsNullOrWhiteSpace(pr.MainWindowTitle))
                            .Where(pr => pr.ProcessName?.IndexOf("StreamDeck", StringComparison.OrdinalIgnoreCase) < 0)
                            .OrderByDescending(pr => pr.StartTime)
                            .FirstOrDefault();

                        if (candidate != null && candidate.MainWindowHandle != IntPtr.Zero)
                        {
                            SetForegroundWindow(candidate.MainWindowHandle);
                        }
                    }
                }
                catch
                {
                    // ignore any process access issues
                }
            }
            catch
            {
                // swallow to avoid affecting send path
            }
        }
    }
}
