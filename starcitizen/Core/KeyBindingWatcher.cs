using System;
using System.IO;
using System.Threading;

namespace starcitizen.Core;

/// <summary>
/// Watches for changes to the Star Citizen actionmaps.xml file.
/// Uses FileSystemWatcher combined with polling and hash comparison
/// to reliably detect when bindings have changed.
/// </summary>
public class KeyBindingWatcher : FileSystemWatcher
{
    // ============================================================
    // REGION: Events
    // ============================================================
    
    /// <summary>Raised when actionmaps.xml has been modified.</summary>
    public event EventHandler KeyBindingUpdated;

    // ============================================================
    // REGION: Configuration
    // ============================================================
    private const int DebounceDelayMs = 200;
    private const int PollIntervalMs = 1500;

    // ============================================================
    // REGION: State
    // ============================================================
    private readonly Timer debounceTimer;
    private readonly Timer pollTimer;
    private readonly object debounceLock = new();
    private readonly string targetFilePath;
    private FileSignature lastSignature;

    /// <summary>Tracks file identity using multiple attributes for reliable change detection.</summary>
    private class FileSignature
    {
        public DateTime? WriteTimeUtc { get; set; }
        public long? Length { get; set; }
        public string Hash { get; set; }

        public bool Equals(FileSignature other)
        {
            if (other == null) return false;
            return WriteTimeUtc == other.WriteTimeUtc &&
                   Length == other.Length &&
                   string.Equals(Hash, other.Hash, StringComparison.Ordinal);
        }
    }

    // ============================================================
    // REGION: Initialization
    // ============================================================
    public KeyBindingWatcher(string path, string fileName)
    {
        Filter = fileName;
        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size;
        Path = path;
        targetFilePath = System.IO.Path.Combine(path, fileName);
        lastSignature = GetFileSignature();
        debounceTimer = new Timer(_ => RaiseUpdate(), null, Timeout.Infinite, Timeout.Infinite);
        pollTimer = new Timer(_ => PollForChanges(), null, Timeout.Infinite, Timeout.Infinite);
    }

    // ============================================================
    // REGION: Public API
    // ============================================================
    
    /// <summary>Starts watching for file changes.</summary>
    public virtual void StartWatching()
    {
        if (EnableRaisingEvents)
        {
            pollTimer.Change(PollIntervalMs, PollIntervalMs);
            return;
        }

        Changed -= UpdateStatus;
        Changed += UpdateStatus;
        Created -= UpdateStatus;
        Created += UpdateStatus;
        Renamed -= UpdateStatus;
        Renamed += UpdateStatus;

        EnableRaisingEvents = true;
        pollTimer.Change(PollIntervalMs, PollIntervalMs);
    }

    /// <summary>Stops watching for file changes.</summary>
    public virtual void StopWatching()
    {
        try
        {
            lock (debounceLock)
            {
                debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            pollTimer.Change(Timeout.Infinite, Timeout.Infinite);

            if (EnableRaisingEvents)
            {
                Changed -= UpdateStatus;
                Created -= UpdateStatus;
                Renamed -= UpdateStatus;
                EnableRaisingEvents = false;
            }
        }
        catch (Exception e)
        {
            PluginLog.Error($"Error stopping watcher: {e.Message}");
        }
    }

    // ============================================================
    // REGION: Event Handlers
    // ============================================================
    protected void UpdateStatus(object sender, FileSystemEventArgs e)
    {
        TouchLastWriteTimestamp();
        ScheduleUpdate();
    }

    protected void UpdateStatus(object sender, RenamedEventArgs e)
    {
        TouchLastWriteTimestamp();
        ScheduleUpdate();
    }

    // ============================================================
    // REGION: Update Scheduling
    // ============================================================
    private void ScheduleUpdate()
    {
        lock (debounceLock)
        {
            debounceTimer.Change(DebounceDelayMs, Timeout.Infinite);
        }
    }

    private void RaiseUpdate()
    {
        try
        {
            KeyBindingUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            PluginLog.Warn($"Key binding update handler failed: {e.Message}");
        }
    }

    // ============================================================
    // REGION: Polling (Fallback for missed events)
    // ============================================================
    private void PollForChanges()
    {
        try
        {
            var currentSignature = GetFileSignature();

            lock (debounceLock)
            {
                if (currentSignature.Equals(lastSignature)) return;

                lastSignature = currentSignature;
                debounceTimer.Change(DebounceDelayMs, Timeout.Infinite);
            }
        }
        catch (Exception e)
        {
            PluginLog.Warn($"Key binding poll failed: {e.Message}");
        }
    }

    private void TouchLastWriteTimestamp()
    {
        try
        {
            lock (debounceLock)
            {
                lastSignature = GetFileSignature();
            }
        }
        catch (Exception e)
        {
            PluginLog.Warn($"Could not update key binding timestamp: {e.Message}");
        }
    }

    // ============================================================
    // REGION: File Signature Computation
    // ============================================================
    private FileSignature GetFileSignature()
    {
        try
        {
            var info = new FileInfo(targetFilePath);
            if (!info.Exists) return new FileSignature();

            info.Refresh();

            string hash = null;
            try
            {
                using var stream = new FileStream(targetFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sha = System.Security.Cryptography.SHA256.Create();
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
            catch (Exception ex)
            {
                PluginLog.Debug($"Could not hash key binding file: {ex.Message}");
            }

            return new FileSignature
            {
                WriteTimeUtc = info.LastWriteTimeUtc,
                Length = info.Length,
                Hash = hash
            };
        }
        catch (Exception e)
        {
            PluginLog.Warn($"Could not read key binding file info: {e.Message}");
            return lastSignature ?? new FileSignature();
        }
    }

    // ============================================================
    // REGION: Disposal
    // ============================================================
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            debounceTimer.Dispose();
            pollTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
