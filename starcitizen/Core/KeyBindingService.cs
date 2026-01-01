using System;
using System.Configuration;
using System.IO;
using System.Threading;
using BarRaider.SdTools;
using p4ktest.SC;
using starcitizen.SC;

namespace starcitizen.Core
{
    /// <summary>
    /// Centralized management of Star Citizen key bindings, including loading,
    /// caching, and watching the profile for changes. All actions read bindings
    /// from this service instead of touching the loader directly.
    /// </summary>
    public sealed class KeyBindingService : IDisposable
    {
        private readonly FifoExecution loadQueue = new FifoExecution();
        private readonly object syncLock = new object();

        private KeyBindingWatcher watcher;
        private bool disposed;
        private bool initialized;
        private bool enableCsvExport;
        private int bindingsVersion;

        public event EventHandler KeyBindingsLoaded;

        public static KeyBindingService Instance { get; } = new KeyBindingService();

        public DProfileReader Reader { get; private set; }

        public int Version => bindingsVersion;

        public void Initialize()
        {
            lock (syncLock)
            {
                if (initialized)
                {
                    return;
                }

                enableCsvExport = ReadCsvFlagFromConfig();
                initialized = true;
            }

            PluginLog.Info($"CSV export setting: {(enableCsvExport ? "enabled" : "disabled")}");
            SCFiles.Instance.UpdatePack();

            QueueReload();
        }

        public void QueueReload()
        {
            loadQueue.QueueUserWorkItem(_ => LoadBindings(), null);
        }

        public bool TryGetBinding(string functionName, out DProfileReader.Action action)
        {
            action = null;

            var reader = Reader;
            if (reader == null || string.IsNullOrWhiteSpace(functionName))
            {
                return false;
            }

            action = reader.GetBinding(functionName);
            return action != null;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopWatcher();
        }

        private void LoadBindings()
        {
            try
            {
                PluginLog.Info("Loading Star Citizen key bindings...");

                var profile = SCDefaultProfile.DefaultProfile();
                if (string.IsNullOrEmpty(profile))
                {
                    PluginLog.Warn("Default profile is empty. Keeping previous bindings.");
                    return;
                }

                // Build a fresh reader first (do NOT overwrite current Reader unless successful)
                var newReader = new DProfileReader();
                newReader.fromXML(profile);

                // Apply actionmaps.xml with retry (handles SC writing/flush timing)
                if (!TryApplyActionMapsWithRetry(newReader))
                {
                    PluginLog.Warn("Failed to apply actionmaps.xml after retries. Keeping previous bindings.");
                    return;
                }

                newReader.Actions();
                newReader.CreateCsv(enableCsvExport);

                // Success => swap
                Reader = newReader;

                Interlocked.Increment(ref bindingsVersion);
                KeyBindingsLoaded?.Invoke(this, EventArgs.Empty);
                PluginLog.Info("Key bindings loaded - notifying buttons");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error loading key bindings: {ex}");
            }
            finally
            {
                // Ensure watcher stays active for automatic updates
                MonitorProfileDirectory();
            }
        }

        private static bool TryApplyActionMapsWithRetry(DProfileReader reader, int maxAttempts = 8, int baseDelayMs = 180)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var actionmaps = SCDefaultProfile.ActionMaps(out var actionmapsPath);
                if (string.IsNullOrEmpty(actionmaps))
                {
                    // No actionmaps.xml (or empty) => nothing to apply
                    PluginLog.Warn($"actionmaps.xml missing or empty at '{actionmapsPath ?? "(unknown)"}'. Keeping previous bindings.");
                    return true;
                }

                try
                {
                    reader.fromActionProfile(actionmaps);
                    return true; // success
                }
                catch (Exception ex)
                {
                    // Common when SC is still flushing the file (partial/incomplete XML)
                    PluginLog.Warn($"actionmaps.xml parse failed (attempt {attempt}/{maxAttempts}). Retrying... {ex.Message}");
                    Thread.Sleep(baseDelayMs * attempt); // small backoff
                }
            }

            return false;
        }

        private void MonitorProfileDirectory(bool forceRestart = false)
        {
            var profilePath = SCPath.SCClientProfilePath;
            if (string.IsNullOrEmpty(profilePath) || !Directory.Exists(profilePath))
            {
                PluginLog.Warn("Could not find profile directory to monitor for changes");
                return;
            }

            lock (syncLock)
            {
                if (!forceRestart && watcher != null)
                {
                    if (!watcher.EnableRaisingEvents)
                    {
                        watcher.StartWatching();
                    }

                    return;
                }

                StopWatcherInternal();

                PluginLog.Info($"Monitoring key binding file at: {profilePath}\\actionmaps.xml");
                watcher = new KeyBindingWatcher(profilePath, "actionmaps.xml");
                watcher.KeyBindingUpdated += Watcher_KeyBindingUpdated;
                watcher.Error += Watcher_OnError;
                watcher.StartWatching();
            }
        }

        private void Watcher_KeyBindingUpdated(object sender, EventArgs e)
        {
            QueueReload();
        }

        private void Watcher_OnError(object sender, ErrorEventArgs e)
        {
            PluginLog.Warn($"Key binding watcher encountered an error: {e.GetException()?.Message ?? "unknown"}. Restarting watcher.");
            MonitorProfileDirectory(forceRestart: true);
        }

        private void StopWatcher()
        {
            lock (syncLock)
            {
                StopWatcherInternal();
            }
        }

        private static bool ReadCsvFlagFromConfig()
        {
            try
            {
                var csvSetting = ConfigurationManager.AppSettings["EnableCsvExport"];
                if (bool.TryParse(csvSetting, out var parsed))
                {
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warn($"Could not read CSV export setting, defaulting to disabled. {ex.Message}");
            }

            return false;
        }

        private void StopWatcherInternal()
        {
            if (watcher == null)
            {
                return;
            }

            try
            {
                watcher.KeyBindingUpdated -= Watcher_KeyBindingUpdated;
                watcher.Error -= Watcher_OnError;
                watcher.StopWatching();
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error while stopping watcher: {ex.Message}");
            }
            finally
            {
                watcher.Dispose();
                watcher = null;
            }
        }
    }
}
