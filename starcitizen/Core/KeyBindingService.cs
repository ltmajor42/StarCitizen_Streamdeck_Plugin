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
            StopWatcher();

            try
            {
                PluginLog.Info("Loading Star Citizen key bindings...");
                var profile = SCDefaultProfile.DefaultProfile();
                var actionmaps = SCDefaultProfile.ActionMaps();

                var reader = new DProfileReader();
                reader.fromXML(profile);

                if (!string.IsNullOrEmpty(actionmaps))
                {
                    reader.fromActionProfile(actionmaps);
                }

                reader.Actions();
                reader.CreateCsv(enableCsvExport);

                Reader = reader;

                MonitorProfileDirectory();

                Interlocked.Increment(ref bindingsVersion);
                KeyBindingsLoaded?.Invoke(this, EventArgs.Empty);
                PluginLog.Info("Key bindings loaded - notifying buttons");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error loading key bindings: {ex.Message}");
            }
        }

        private void MonitorProfileDirectory()
        {
            var profilePath = SCPath.SCClientProfilePath;
            if (string.IsNullOrEmpty(profilePath) || !Directory.Exists(profilePath))
            {
                PluginLog.Warn("Could not find profile directory to monitor for changes");
                return;
            }

            PluginLog.Info($"Monitoring key binding file at: {profilePath}\\actionmaps.xml");
            watcher = new KeyBindingWatcher(profilePath, "actionmaps.xml");
            watcher.KeyBindingUpdated += (sender, args) => QueueReload();
            watcher.StartWatching();
        }

        private void StopWatcher()
        {
            if (watcher == null)
            {
                return;
            }

            try
            {
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
    }
}
