using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace starcitizen.Core
{
    /// <summary>
    /// Provides keyboard layout detection for the current thread/process.
    /// Used to display localized key names in the Property Inspector.
    /// </summary>
    public class KeyboardLayout
    {
        public UInt32 Id { get; }
        public UInt16 LanguageId { get; }
        public UInt16 KeyboardId { get; }
        public String LanguageName { get; }
        public String KeyboardName { get; }

        internal KeyboardLayout(UInt32 id, UInt16 languageId, UInt16 keyboardId, String languageName, String keyboardName)
        {
            Id = id;
            LanguageId = languageId;
            KeyboardId = keyboardId;
            LanguageName = languageName;
            KeyboardName = keyboardName;
        }
    }

    /// <summary>
    /// Static helper for detecting the current keyboard layout.
    /// Provides thread-level keyboard layout detection for key binding display.
    /// </summary>
    public static class KeyboardLayouts
    {
        /// <summary>
        /// Gets the keyboard layout for the current thread (or specified thread).
        /// </summary>
        /// <param name="threadId">Thread ID (0 for current thread)</param>
        /// <returns>KeyboardLayout containing layout identifiers</returns>
        public static KeyboardLayout GetThreadKeyboardLayout(Int32 threadId = 0)
        {
            var keyboardLayoutId = (UInt32)GetKeyboardLayout((UInt32)threadId);
            return CreateKeyboardLayout(keyboardLayoutId);
        }

        /// <summary>
        /// Gets the keyboard layout for the main thread of a process.
        /// </summary>
        /// <param name="processId">Process ID (0 for current process)</param>
        /// <returns>KeyboardLayout containing layout identifiers</returns>
        public static KeyboardLayout GetProcessKeyboardLayout(Int32 processId = 0)
        {
            var threadId = GetProcessMainThreadId(processId);
            return GetThreadKeyboardLayout(threadId);
        }

        private static Int32 GetProcessMainThreadId(Int32 processId = 0)
        {
            var process = 0 == processId ? Process.GetCurrentProcess() : Process.GetProcessById(processId);
            return process.Threads[0].Id;
        }

        private static KeyboardLayout CreateKeyboardLayout(UInt32 keyboardLayoutId)
        {
            var languageId = (UInt16)(keyboardLayoutId & 0xFFFF);
            var keyboardId = (UInt16)(keyboardLayoutId >> 16);
            
            // NOTE: Language/Keyboard names are not used; empty strings to avoid CultureInfo overhead
            return new KeyboardLayout(keyboardLayoutId, languageId, keyboardId, "", "");
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(UInt32 idThread);
    }
}
