using System;
using System.IO;
using System.Threading;
using BarRaider.SdTools;

namespace starcitizen.Core
{
    public class KeyBindingWatcher : FileSystemWatcher
    {
        public event EventHandler KeyBindingUpdated;

        private readonly Timer debounceTimer;
        private readonly Timer pollTimer;
        private readonly object debounceLock = new object();
        private readonly string targetFilePath;
        private DateTime? lastWriteTimeUtc;
        private const int DebounceDelayMs = 200;
        private const int PollIntervalMs = 1500;

        public KeyBindingWatcher(string path, string fileName)
        {
            Filter = fileName;
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size;
            Path = path;
            targetFilePath = System.IO.Path.Combine(path, fileName);
            lastWriteTimeUtc = GetFileWriteTimeUtc();
            debounceTimer = new Timer(_ => RaiseUpdate(), null, Timeout.Infinite, Timeout.Infinite);
            pollTimer = new Timer(_ => PollForChanges(), null, Timeout.Infinite, Timeout.Infinite);
        }

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
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error while stopping Status watcher: {e.Message}");
                Logger.Instance.LogMessage(TracingLevel.INFO, e.StackTrace);
            }
        }

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

        private void ScheduleUpdate()
        {
            lock (debounceLock)
            {
                debounceTimer.Change(DebounceDelayMs, Timeout.Infinite);
            }
        }

        private void RaiseUpdate()
        {
            KeyBindingUpdated?.Invoke(this, EventArgs.Empty);
        }

        private void PollForChanges()
        {
            try
            {
                var currentWriteTime = GetFileWriteTimeUtc();

                lock (debounceLock)
                {
                    if (currentWriteTime == lastWriteTimeUtc)
                    {
                        return;
                    }

                    lastWriteTimeUtc = currentWriteTime;
                    debounceTimer.Change(DebounceDelayMs, Timeout.Infinite);
                }
            }
            catch (Exception e)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"Key binding poll failed: {e.Message}");
            }
        }

        private void TouchLastWriteTimestamp()
        {
            try
            {
                lock (debounceLock)
                {
                    lastWriteTimeUtc = GetFileWriteTimeUtc();
                }
            }
            catch (Exception e)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"Could not update key binding timestamp: {e.Message}");
            }
        }

        private DateTime? GetFileWriteTimeUtc()
        {
            var info = new FileInfo(targetFilePath);
            if (!info.Exists)
            {
                return null;
            }

            info.Refresh();
            return info.LastWriteTimeUtc;
        }

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
}
