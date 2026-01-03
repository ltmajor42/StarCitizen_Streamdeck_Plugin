using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using WindowsInput;
using WindowsInput.Native;
using BarRaider.SdTools;
using starcitizen.SC;
using starcitizen.Core;
using System.Diagnostics;

namespace starcitizen.Buttons
{
    /// <summary>
    /// Common utilities for Stream Deck button input handling.
    /// Provides keyboard and mouse input simulation for Star Citizen bindings.
    /// </summary>
    internal static partial class StreamDeckCommon
    {
        // ============================================================
        // REGION: Win32 Imports for Window Focus Management
        // ============================================================
#pragma warning disable SYSLIB1054 // Use LibraryImportAttribute - keeping DllImport for compatibility with out parameters
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern int GetWindowThreadProcessId(IntPtr handleWindow, out int lpdwProcessID);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);
#pragma warning restore SYSLIB1054

        // ============================================================
        // REGION: State and Configuration
        // ============================================================
        public static bool ForceStop = false;

        // Reusable separator array for string splitting
        private static readonly char[] PlusSeparator = ['+'];

        // Generated regex for sub-command extraction
        [GeneratedRegex(CommandTools.REGEX_SUB_COMMAND)]
        private static partial Regex SubCommandRegex();

        private static readonly InputSimulator s_inputSimulator = new();
        private static readonly AsyncLocal<string> CurrentCorrelation = new();

        // Mouse wheel coalescing state
        private static readonly object _wheelLock = new();
        private static int _wheelAccum = 0;
        private static DateTime _wheelLastLog = DateTime.MinValue;

        private static string CorrelationOrDash => CurrentCorrelation.Value ?? "-";

        // ============================================================
        // REGION: Public API - Keypress Methods
        // ============================================================
        
        /// <summary>Sends a complete keypress (down + delay + up).</summary>
        public static void SendKeypress(string keyInfo, int delay)
        {
            if (string.IsNullOrEmpty(keyInfo)) return;

            CurrentCorrelation.Value = Guid.NewGuid().ToString("N");
            PluginLog.Info($"[{CorrelationOrDash}] SendKeypress: {keyInfo}, delay={delay}, fg={GetForegroundInfo()}");
            try
            {
                SendInput("{" + keyInfo + "}", delay);
            }
            finally
            {
                CurrentCorrelation.Value = null;
            }
        }

        /// <summary>Sends key down events only (for hold behavior).</summary>
        public static void SendKeypressDown(string keyInfo)
        {
            if (string.IsNullOrEmpty(keyInfo)) return;

            CurrentCorrelation.Value = Guid.NewGuid().ToString("N");
            PluginLog.Info($"[{CorrelationOrDash}] SendKeypressDown: {keyInfo}, fg={GetForegroundInfo()}");
            try
            {
                SendInputDown("{" + keyInfo + "}");
            }
            finally
            {
                CurrentCorrelation.Value = null;
            }
        }

        /// <summary>Sends key up events only (to release held keys).</summary>
        public static void SendKeypressUp(string keyInfo)
        {
            if (string.IsNullOrEmpty(keyInfo)) return;

            CurrentCorrelation.Value = Guid.NewGuid().ToString("N");
            PluginLog.Info($"[{CorrelationOrDash}] SendKeypressUp: {keyInfo}, fg={GetForegroundInfo()}");
            try
            {
                SendInputUp("{" + keyInfo + "}");
            }
            finally
            {
                CurrentCorrelation.Value = null;
            }
        }

        // ============================================================
        // REGION: Input Processing
        // ============================================================
        private static void SendInput(string inputText, int delay)
        {
            PluginLog.Debug($"[{CorrelationOrDash}] SendInput: {inputText}, delay={delay}, fg={GetForegroundInfo()}");
            TryEnsureTargetWindowFocused();
            ProcessMacros(inputText, (macro) => HandleMacro(macro, delay));
        }

        private static void SendInputDown(string inputText)
        {
            PluginLog.Debug($"[{CorrelationOrDash}] SendInputDown: {inputText}, fg={GetForegroundInfo()}");
            TryEnsureTargetWindowFocused();
            ProcessMacros(inputText, HandleMacroDown);
        }

        private static void SendInputUp(string inputText)
        {
            PluginLog.Debug($"[{CorrelationOrDash}] SendInputUp: {inputText}, fg={GetForegroundInfo()}");
            TryEnsureTargetWindowFocused();
            ProcessMacros(inputText, HandleMacroUp);
        }

        /// <summary>Extracts and processes macros from input text.</summary>
        private static void ProcessMacros(string text, Action<string> handler)
        {
            for (var idx = 0; idx < text.Length && !ForceStop; idx++)
            {
                var macro = CommandTools.ExtractMacro(text, idx);
                if (string.IsNullOrEmpty(macro))
                {
                    if (CommandTools.Verbose) 
                        PluginLog.Debug($"[{CorrelationOrDash}] ProcessMacros: no macro at pos {idx}");
                    continue;
                }

                idx += macro.Length - 1;
                macro = macro[1..^1];
                PluginLog.Debug($"[{CorrelationOrDash}] ProcessMacros handling: {macro}");
                handler(macro);
            }
        }

        // ============================================================
        // REGION: Macro Handlers
        // ============================================================
        private static void HandleMacro(string macro, int delay)
        {
            if (TryHandleMouseMacro(macro, delay, isDown: false, isUp: false)) return;

            var keyStrokes = CommandTools.ExtractKeyStrokes(macro);
            if (SCPath.DetailedInputDiagnostics)
                PluginLog.Debug($"[{CorrelationOrDash}] HandleMacro: {macro}, keyStrokes={keyStrokes.Count}, delay={delay}");

            if (keyStrokes.Count == 0)
            {
                PluginLog.Debug($"[{CorrelationOrDash}] HandleMacro: no keystrokes to send");
                return;
            }

            SendKeyStrokesWithRetry(keyStrokes, delay, SendMode.FullPress);
        }

        private static void HandleMacroDown(string macro)
        {
            if (TryHandleMouseMacro(macro, delay: 0, isDown: true, isUp: false)) return;

            var keyStrokes = CommandTools.ExtractKeyStrokes(macro);
            PluginLog.Debug($"[{CorrelationOrDash}] HandleMacroDown: {macro}, keyStrokes={keyStrokes.Count}");

            if (keyStrokes.Count == 0) return;

            SendKeyStrokesWithRetry(keyStrokes, 0, SendMode.KeyDown);
        }

        private static void HandleMacroUp(string macro)
        {
            if (TryHandleMouseMacro(macro, delay: 0, isDown: false, isUp: true)) return;

            var keyStrokes = CommandTools.ExtractKeyStrokes(macro);
            PluginLog.Debug($"[{CorrelationOrDash}] HandleMacroUp: {macro}, keyStrokes={keyStrokes.Count}");

            if (keyStrokes.Count == 0) return;

            SendKeyStrokesWithRetry(keyStrokes, 0, SendMode.KeyUp);
        }

        // ============================================================
        // REGION: Keystroke Sending
        // ============================================================
        private enum SendMode { FullPress, KeyDown, KeyUp }

        private static void SendKeyStrokesWithRetry(List<DirectInputKeyCode> keyStrokes, int delay, SendMode mode)
        {
            if (SCPath.DetailedInputDiagnostics)
            {
                PluginLog.Debug($"[{CorrelationOrDash}] Sending keystrokes [{mode}]: [{string.Join(",", keyStrokes)}] delay={delay}");
            }

            try
            {
                SendKeyStrokesCore(s_inputSimulator, keyStrokes, delay, mode);
            }
            catch (Exception ex)
            {
                PluginLog.Warn($"[{CorrelationOrDash}] Keystroke send failed: {ex}. Retrying with fresh simulator.");
                try
                {
                    SendKeyStrokesCore(new InputSimulator(), [.. keyStrokes], delay, mode);
                }
                catch (Exception ex2)
                {
                    PluginLog.Error($"[{CorrelationOrDash}] Keystroke retry failed: {ex2}");
                }
            }
        }

        private static void SendKeyStrokesCore(InputSimulator iis, List<DirectInputKeyCode> keyStrokes, int delay, SendMode mode)
        {
            var keyCode = keyStrokes.Last();
            keyStrokes.Remove(keyCode);

            switch (mode)
            {
                case SendMode.FullPress:
                    if (keyStrokes.Count > 0)
                        iis.Keyboard.DelayedModifiedKeyStroke(keyStrokes, keyCode, delay);
                    else
                        iis.Keyboard.DelayedKeyPress(keyCode, delay);
                    break;

                case SendMode.KeyDown:
                    if (keyStrokes.Count > 0)
                        iis.Keyboard.ModifiedKeyStrokeDown(keyStrokes, keyCode);
                    else
                        iis.Keyboard.DelayedKeyPressDown(keyCode);
                    break;

                case SendMode.KeyUp:
                    if (keyStrokes.Count > 0)
                        iis.Keyboard.ModifiedKeyStrokeUp(keyStrokes, keyCode);
                    else
                        iis.Keyboard.DelayedKeyPressUp(keyCode);
                    break;
            }
        }

        // ============================================================
        // REGION: Mouse Handling
        // ============================================================
        private static bool TryHandleMouseMacro(string macroText, int delay, bool isDown, bool isUp)
        {
            if (!SCPath.EnableMouseOutput) return false;

            // Quick check for mouse tokens
            if (macroText?.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) < 0 &&
                macroText?.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            try
            {
                var matches = SubCommandRegex().Matches(macroText);
                if (matches.Count == 0) return false;

                var modifiers = new List<DirectInputKeyCode>();
                var otherKeys = new List<DirectInputKeyCode>();
                string mouseToken = null;

                foreach (Match match in matches)
                {
                    var token = match.Value.Replace("{", "").Replace("}", "").Trim();
                    if (string.IsNullOrEmpty(token)) continue;

                    if (MouseTokenHelper.TryNormalize(token, out var normalizedMouse))
                    {
                        mouseToken = normalizedMouse;
                        continue;
                    }

                    if (Enum.TryParse(token, true, out DirectInputKeyCode dxKey))
                    {
                        if (IsModifierKey(dxKey)) modifiers.Add(dxKey);
                        else otherKeys.Add(dxKey);
                    }
                }

                if (string.IsNullOrEmpty(mouseToken)) return false;

                if (SCPath.DetailedInputDiagnostics)
                    PluginLog.Debug($"[{CorrelationOrDash}] MouseMacro: token={mouseToken}, mods=[{string.Join(",", modifiers)}], isDown={isDown}, isUp={isUp}");

                var iis = s_inputSimulator;

                // Press modifiers
                foreach (var mod in modifiers) iis.Keyboard.KeyDown(mod);

                // Press any other keys
                foreach (var key in otherKeys) iis.Keyboard.DelayedKeyPress(key, delay);

                // Handle mouse action
                ExecuteMouseAction(iis, mouseToken, isDown, isUp);

                // Release modifiers in reverse order
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

        private static void ExecuteMouseAction(InputSimulator iis, string mouseToken, bool isDown, bool isUp)
        {
            // Handle composite tokens like "mouse1+mouse2"
            var parts = mouseToken.Contains('+') 
                ? mouseToken.Split(PlusSeparator, StringSplitOptions.RemoveEmptyEntries)
                : [mouseToken];

            foreach (var part in parts)
            {
                if (isUp)
                {
                    ExecuteMouseButtonUp(iis, part);
                }
                else
                {
                    ExecuteMouseButtonAction(iis, part, isDown);
                }

                if (parts.Length > 1) Thread.Sleep(8); // Gap between composite actions
            }
        }

        private static void ExecuteMouseButtonAction(InputSimulator iis, string token, bool isDown)
        {
            switch (token)
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
                    HandleMouseWheel(iis, 1);
                    break;
                case "mwheeldown":
                    HandleMouseWheel(iis, -1);
                    break;
                case "mwheelleft":
                    _ = iis.Mouse.HorizontalScroll(-1);
                    break;
                case "mwheelright":
                    _ = iis.Mouse.HorizontalScroll(1);
                    break;
            }
        }

        private static void ExecuteMouseButtonUp(InputSimulator iis, string token)
        {
            switch (token)
            {
                case "mouse1": iis.Mouse.LeftButtonUp(); break;
                case "mouse2": iis.Mouse.RightButtonUp(); break;
                case "mouse3": iis.Mouse.MiddleButtonUp(); break;
                case "mouse4": iis.Mouse.XButtonUp(1); break;
                case "mouse5": iis.Mouse.XButtonUp(2); break;
                // Wheel tokens have no "up" state
            }
        }

        private static void HandleMouseWheel(InputSimulator iis, int amount)
        {
            if (SCPath.CoalesceMouseWheel)
            {
                lock (_wheelLock)
                {
                    _wheelAccum += amount;
                    var now = DateTime.UtcNow;
                    if ((now - _wheelLastLog).TotalMilliseconds >= 60)
                    {
                        if (_wheelAccum != 0)
                        {
                            PluginLog.Debug($"[{CorrelationOrDash}] Mouse: VerticalScroll({_wheelAccum}) (coalesced)");
                            _ = iis.Mouse.VerticalScroll(_wheelAccum);
                        }
                        _wheelAccum = 0;
                        _wheelLastLog = now;
                    }
                }
            }
            else
            {
                if (SCPath.DetailedInputDiagnostics)
                    PluginLog.Debug($"[{CorrelationOrDash}] Mouse: VerticalScroll({amount})");
                _ = iis.Mouse.VerticalScroll(amount);
            }
        }

        private static bool IsModifierKey(DirectInputKeyCode code) =>
            code == DirectInputKeyCode.DikLalt || code == DirectInputKeyCode.DikRalt ||
            code == DirectInputKeyCode.DikLcontrol || code == DirectInputKeyCode.DikRcontrol ||
            code == DirectInputKeyCode.DikLshift || code == DirectInputKeyCode.DikRshift;

        // ============================================================
        // REGION: Window Focus Management
        // ============================================================
        private static void TryEnsureTargetWindowFocused()
        {
            try { EnsureTargetWindowFocused(); }
            catch (Exception ex) { PluginLog.Debug($"[{CorrelationOrDash}] EnsureTargetWindowFocused failed: {ex.Message}"); }
        }

        private static string GetForegroundInfo()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return "hwnd=0";
                _ = GetWindowThreadProcessId(hwnd, out var pid);
                string proc;
                try { proc = Process.GetProcessById(pid).ProcessName; }
                catch { proc = pid.ToString(); }
                return $"hwnd=0x{hwnd.ToInt64():X},pid={pid},proc={proc}";
            }
            catch { return "hwnd=?"; }
        }

        private static void EnsureTargetWindowFocused()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return;

                _ = GetWindowThreadProcessId(hwnd, out var pid);
                try
                {
                    var p = Process.GetProcessById(pid);
                    if (p?.ProcessName?.IndexOf("StreamDeck", StringComparison.OrdinalIgnoreCase) < 0) return;

                    // Prefer Star Citizen if running
                    var sc = Process.GetProcessesByName("StarCitizen").FirstOrDefault() 
                          ?? Process.GetProcessesByName("StarCitizen64").FirstOrDefault();
                    if (sc?.MainWindowHandle != IntPtr.Zero)
                    {
                        SetForegroundWindow(sc.MainWindowHandle);
                        return;
                    }

                    // Try previous window in z-order
                    const uint GW_HWNDPREV = 3;
                    var prev = GetWindow(hwnd, GW_HWNDPREV);
                    for (int i = 0; i < 8 && prev != IntPtr.Zero; i++)
                    {
                        if (IsWindowVisible(prev))
                        {
                            _ = GetWindowThreadProcessId(prev, out var prevPid);
                            try
                            {
                                var prevProc = Process.GetProcessById(prevPid);
                                if (prevProc?.ProcessName?.IndexOf("StreamDeck", StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    SetForegroundWindow(prev);
                                    return;
                                }
                            }
                            catch { }
                        }
                        prev = GetWindow(prev, GW_HWNDPREV);
                    }

                    // Fallback: any other visible window
                    var candidate = Process.GetProcesses()
                        .Where(pr => pr != null && pr.Id != pid && pr.MainWindowHandle != IntPtr.Zero)
                        .Where(pr => !string.IsNullOrWhiteSpace(pr.MainWindowTitle))
                        .Where(pr => pr.ProcessName?.IndexOf("StreamDeck", StringComparison.OrdinalIgnoreCase) < 0)
                        .OrderByDescending(pr => pr.StartTime)
                        .FirstOrDefault();

                    if (candidate?.MainWindowHandle != IntPtr.Zero)
                        SetForegroundWindow(candidate.MainWindowHandle);
                }
                catch { }
            }
            catch { }
        }
    }
}
