using System;
using BarRaider.SdTools;
using starcitizen.Core;

namespace starcitizen
{
    class Program
    {
        static void Main(string[] args)
        {
            PluginLog.Info("========================================");
            PluginLog.Info("Star Citizen Stream Deck Plugin Starting");
            PluginLog.Info("========================================");

            // Register shutdown handlers for proper cleanup
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += OnCancelKeyPress;

            try
            {
                KeyBindingService.Instance.Initialize();
                PluginLog.Info("KeyBindingService initialized successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Fatal($"Failed to initialize Star Citizen plugin: {ex}");
            }

            PluginLog.Info("Handing control to Stream Deck SDK");
            SDWrapper.Run(args);

            // Cleanup after SDWrapper.Run() returns
            Shutdown();
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            PluginLog.Info("Process exit signal received");
            Shutdown();
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            PluginLog.Info("Cancel key press received");
            e.Cancel = true; // Allow graceful shutdown
            Shutdown();
        }

        private static bool _shutdownCalled;
        private static readonly object _shutdownLock = new();

        private static void Shutdown()
        {
            lock (_shutdownLock)
            {
                if (_shutdownCalled) return;
                _shutdownCalled = true;
            }

            PluginLog.Info("========================================");
            PluginLog.Info("Star Citizen Stream Deck Plugin Shutting Down");
            PluginLog.Info("========================================");

            try
            {
                // Dispose KeyBindingService (stops file watcher, disposes FifoExecution)
                PluginLog.Info("Disposing KeyBindingService...");
                KeyBindingService.Instance.Dispose();
                PluginLog.Info("KeyBindingService disposed");

                // Dispose AudioPlaybackEngine
                PluginLog.Info("Disposing AudioPlaybackEngine...");
                AudioPlaybackEngine.Instance.Dispose();
                PluginLog.Info("AudioPlaybackEngine disposed");

                PluginLog.Info("Shutdown complete - all resources released");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error during shutdown: {ex.Message}");
            }

            PluginLog.Info("========================================");
            PluginLog.Info("Goodbye!");
            PluginLog.Info("========================================");

            // Final flush of log
            PluginLog.Shutdown();
        }
    }
}
