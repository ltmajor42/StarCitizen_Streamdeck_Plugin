using System;
using System.Configuration;
using System.IO;
using System.Threading;
using BarRaider.SdTools;
using p4ktest.SC;
using SCJMapper_V2.SC;

namespace starcitizen.Core
{
    /// <summary>
    /// Centralized management of Star Citizen key bindings.
    /// Handles loading, caching, and watching the profile for changes.
    /// All button actions read bindings from this service instead of touching the loader directly.
    /// </summary>
    /// <remarks>
    /// This is a singleton service that:
    /// - Loads bindings from defaultProfile.xml (base layer from Data.p4k)
    /// - Applies actionmaps.xml overrides (user customizations)
    /// - Watches for actionmaps.xml changes and triggers reload
    /// - Raises KeyBindingsLoaded event when bindings are refreshed
    /// </remarks>
    public sealed class KeyBindingService : IDisposable
    {
        // ============================================================
        // REGION: Singleton Instance
        // ============================================================
        public static KeyBindingService Instance { get; } = new KeyBindingService();

        // ============================================================
        // REGION: Thread Safety and State
        // ============================================================
        private readonly FifoExecution loadQueue = new FifoExecution();
        private readonly object syncLock = new object();

        private KeyBindingWatcher watcher;
        private bool disposed;
        private bool initialized;
        private bool enableCsvExport;
        private int bindingsVersion;

        // ============================================================
        // REGION: Public API
        // ============================================================
        
        /// <summary>Raised when key bindings are loaded or reloaded.</summary>
        public event EventHandler KeyBindingsLoaded;

        /// <summary>The current binding reader. May be null if not yet initialized.</summary>
        public DProfileReader Reader { get; private set; }

        /// <summary>Incremented each time bindings are successfully loaded. Used for cache invalidation.</summary>
        public int Version => bindingsVersion;

        /// <summary>
        /// Initializes the service. Must be called once at plugin startup.
        /// Triggers initial binding load and starts file watching.
        /// </summary>
        public void Initialize()
        {
            lock (syncLock)
            {
                if (initialized) return;

                enableCsvExport = ReadCsvFlagFromConfig();
                initialized = true;
            }

            PluginLog.Info($"CSV export setting: {(enableCsvExport ? "enabled" : "disabled")}");
            SCFiles.Instance.UpdatePack();
            QueueReload();
        }

        /// <summary>Queues a reload of bindings on a background thread.</summary>
        public void QueueReload()
        {
            loadQueue.QueueUserWorkItem(_ => LoadBindings(), null);
        }

        /// <summary>
        /// Attempts to retrieve a binding by function name.
        /// </summary>
        /// <param name="functionName">The function name (e.g., "spaceship_flight-v_pitch")</param>
        /// <param name="action">The action containing binding info if found</param>
        /// <returns>True if binding exists, false otherwise</returns>
        public bool TryGetBinding(string functionName, out DProfileReader.Action action)
        {
            action = null;
            var reader = Reader;
            if (reader == null || string.IsNullOrWhiteSpace(functionName)) return false;

            action = reader.GetBinding(functionName);
            return action != null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            StopWatcher();
        }

        // ============================================================
        // REGION: Binding Loading
        // ============================================================
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

                // Build a fresh reader (don't overwrite current Reader unless successful)
                var newReader = new DProfileReader();
                newReader.FromXML(profile);

                // Apply actionmaps.xml with retry (handles SC writing/flush timing)
                if (!TryApplyActionMapsWithRetry(newReader))
                {
                    PluginLog.Warn("Failed to apply actionmaps.xml after retries. Keeping previous bindings.");
                    return;
                }

                newReader.Actions();
                newReader.CreateCsv(enableCsvExport);

                // Success => swap reader reference
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

        /// <summary>
        /// Attempts to apply actionmaps.xml with exponential backoff retry.
        /// Handles race conditions when SC is still writing the file.
        /// </summary>
        private static bool TryApplyActionMapsWithRetry(DProfileReader reader, int maxAttempts = 8, int baseDelayMs = 180)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var actionmaps = SCDefaultProfile.ActionMaps(out var actionmapsPath);
                if (string.IsNullOrEmpty(actionmaps))
                {
                    PluginLog.Warn($"actionmaps.xml missing or empty at '{actionmapsPath ?? "(unknown)"}'. Keeping previous bindings.");
                    return true; // No actionmaps = nothing to apply (not an error)
                }

                try
                {
                    reader.FromActionProfile(actionmaps);
                    return true; // success
                }
                catch (Exception ex)
                {
                    PluginLog.Warn($"actionmaps.xml parse failed (attempt {attempt}/{maxAttempts}). Retrying... {ex.Message}");
                    Thread.Sleep(baseDelayMs * attempt); // exponential backoff
                }
            }
            return false;
        }

        // ============================================================
        // REGION: File Watching
        // ============================================================
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
                    if (!watcher.EnableRaisingEvents) watcher.StartWatching();
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
            PluginLog.Warn($"Key binding watcher error: {e.GetException()?.Message ?? "unknown"}. Restarting watcher.");
            MonitorProfileDirectory(forceRestart: true);
        }

        private void StopWatcher()
        {
            lock (syncLock) { StopWatcherInternal(); }
        }

        private void StopWatcherInternal()
        {
            if (watcher == null) return;

            try
            {
                watcher.KeyBindingUpdated -= Watcher_KeyBindingUpdated;
                watcher.Error -= Watcher_OnError;
                watcher.StopWatching();
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error stopping watcher: {ex.Message}");
            }
            finally
            {
                watcher.Dispose();
                watcher = null;
            }
        }

        // ============================================================
        // REGION: Configuration Helpers
        // ============================================================
        private static bool ReadCsvFlagFromConfig()
        {
            try
            {
                var csvSetting = ConfigurationManager.AppSettings["EnableCsvExport"];
                if (bool.TryParse(csvSetting, out var parsed)) return parsed;
            }
            catch (Exception ex)
            {
                PluginLog.Warn($"Could not read CSV export setting, defaulting to disabled. {ex.Message}");
            }
            return false;
        }
    }
}
