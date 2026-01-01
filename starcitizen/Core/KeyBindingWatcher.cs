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
        private readonly object debounceLock = new object();
        private const int DebounceDelayMs = 200;

        public KeyBindingWatcher(string path, string fileName)
        {
            Filter = fileName;
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size;
            Path = path;
            debounceTimer = new Timer(_ => RaiseUpdate(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public virtual void StartWatching()
        {
            if (EnableRaisingEvents)
            {
                return;
            }

            Changed -= UpdateStatus;
            Changed += UpdateStatus;
            Created -= UpdateStatus;
            Created += UpdateStatus;
            Renamed -= UpdateStatus;
            Renamed += UpdateStatus;

            EnableRaisingEvents = true;
        }

        public virtual void StopWatching()
        {
            try
            {
                lock (debounceLock)
                {
                    debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }

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
            ScheduleUpdate();
        }

        protected void UpdateStatus(object sender, RenamedEventArgs e)
        {
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                debounceTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
