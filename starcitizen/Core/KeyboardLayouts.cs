using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace starcitizen.Core;

/// <summary>
/// Provides keyboard layout detection for the current thread/process.
/// Used to display localized key names in the Property Inspector.
/// </summary>
public class KeyboardLayout(uint id, ushort languageId, ushort keyboardId, string languageName, string keyboardName)
{
    public uint Id { get; } = id;
    public ushort LanguageId { get; } = languageId;
    public ushort KeyboardId { get; } = keyboardId;
    public string LanguageName { get; } = languageName;
    public string KeyboardName { get; } = keyboardName;
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
    public static KeyboardLayout GetThreadKeyboardLayout(int threadId = 0)
    {
        var keyboardLayoutId = (uint)GetKeyboardLayout((uint)threadId);
        return CreateKeyboardLayout(keyboardLayoutId);
    }

    /// <summary>
    /// Gets the keyboard layout for the main thread of a process.
    /// </summary>
    /// <param name="processId">Process ID (0 for current process)</param>
    /// <returns>KeyboardLayout containing layout identifiers</returns>
    public static KeyboardLayout GetProcessKeyboardLayout(int processId = 0)
    {
        var threadId = GetProcessMainThreadId(processId);
        return GetThreadKeyboardLayout(threadId);
    }

    private static int GetProcessMainThreadId(int processId = 0)
    {
        var process = processId == 0 ? Process.GetCurrentProcess() : Process.GetProcessById(processId);
        return process.Threads[0].Id;
    }

    private static KeyboardLayout CreateKeyboardLayout(uint keyboardLayoutId)
    {
        var languageId = (ushort)(keyboardLayoutId & 0xFFFF);
        var keyboardId = (ushort)(keyboardLayoutId >> 16);
        
        // NOTE: Language/Keyboard names are not used; empty strings to avoid CultureInfo overhead
        return new KeyboardLayout(keyboardLayoutId, languageId, keyboardId, "", "");
    }

    // Using DllImport instead of LibraryImport to avoid requiring unsafe blocks
#pragma warning disable SYSLIB1054 // Use LibraryImportAttribute instead of DllImportAttribute
    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);
#pragma warning restore SYSLIB1054
}
