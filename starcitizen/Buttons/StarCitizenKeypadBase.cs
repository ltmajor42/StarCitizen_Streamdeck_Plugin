using System;
using System.IO;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using starcitizen.Core;

namespace starcitizen.Buttons
{
    #region Settings Interfaces

    /// <summary>
    /// Interface for action settings that support a single function binding.
    /// </summary>
    public interface IFunctionSettings
    {
        string Function { get; set; }
    }

    /// <summary>
    /// Interface for action settings that support click sound playback.
    /// </summary>
    public interface ISoundSettings
    {
        string ClickSoundFilename { get; set; }
    }

    #endregion

    #region Base Settings Class

    /// <summary>
    /// Base settings class providing common properties for Star Citizen actions.
    /// Inherit from this to get Function and ClickSound support with proper JSON serialization.
    /// </summary>
    public abstract class PluginSettingsBase : IFunctionSettings, ISoundSettings
    {
        [JsonProperty(PropertyName = "function")]
        public virtual string Function { get; set; } = string.Empty;

        [FilenameProperty]
        [JsonProperty(PropertyName = "clickSound")]
        public virtual string ClickSoundFilename { get; set; }
    }

    #endregion

    /// <summary>
    /// Base class for Star Citizen keypad-style Stream Deck buttons.
    /// Provides common initialization, Property Inspector communication,
    /// sound playback, and key binding access for all keypad actions.
    /// </summary>
    /// <remarks>
    /// Derived classes should:
    /// - Override InitializeSettings() to load their specific settings
    /// - Override KeyPressed() to implement press behavior
    /// - Optionally override KeyReleased() for release behavior
    /// - Call base.Dispose() in their Dispose() override
    /// </remarks>
    public abstract class StarCitizenKeypadBase : KeypadBase
    {
        #region Services

        /// <summary>
        /// Shared KeyBindingService instance for all button actions.
        /// Provides thread-safe access to Star Citizen key bindings.
        /// </summary>
        protected readonly KeyBindingService BindingService = KeyBindingService.Instance;

        #endregion

        #region State

        private CachedSound _cachedClickSound;
        private string _lastSoundPath;
        private bool _eventsWired;
        private bool _disposed;

        #endregion

        #region Constructor

        protected StarCitizenKeypadBase(SDConnection connection, InitialPayload payload) 
            : base(connection, payload)
        {
        }

        #endregion

        #region Property Inspector Infrastructure

        /// <summary>
        /// Wires up standard Property Inspector event handlers.
        /// Call this in derived class constructors after loading settings.
        /// </summary>
        protected void WirePropertyInspectorEvents()
        {
            if (_eventsWired) return;
            
            Connection.OnPropertyInspectorDidAppear += OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += OnSendToPlugin;
            BindingService.KeyBindingsLoaded += OnKeyBindingsLoaded;
            _eventsWired = true;
        }

        /// <summary>
        /// Unwires Property Inspector event handlers.
        /// Called automatically by Dispose(), but can be called manually if needed.
        /// </summary>
        protected void UnwirePropertyInspectorEvents()
        {
            if (!_eventsWired) return;
            
            Connection.OnPropertyInspectorDidAppear -= OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= OnSendToPlugin;
            BindingService.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            _eventsWired = false;
        }

        /// <summary>
        /// Sends function list to Property Inspector.
        /// Uses PropertyInspectorMessenger which handles loading state automatically.
        /// </summary>
        protected virtual void SendFunctionsToPropertyInspector()
        {
            try
            {
                PropertyInspectorMessenger.SendFunctionsAsync(Connection);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to send functions to PI: {ex.Message}");
            }
        }

        private void OnPropertyInspectorDidAppear(object sender, EventArgs e)
        {
            PluginLog.Info($"{GetType().Name}: Property Inspector appeared");
            SendFunctionsToPropertyInspector();
        }

        private void OnSendToPlugin(object sender, EventArgs e)
        {
            try
            {
                var payload = e.ExtractPayload();
                if (payload == null) return;

                // Handle JS logging from PI
                if (payload.ContainsKey("jslog"))
                {
                    PluginLog.Info($"[JS-PI] {payload["jslog"]}");
                    return;
                }

                // Handle PI connection message
                if (payload.ContainsKey("property_inspector") &&
                    payload["property_inspector"]?.ToString() == "propertyInspectorConnected")
                {
                    PluginLog.Info($"{GetType().Name}: PI connected, sending functions");
                    SendFunctionsToPropertyInspector();
                    return;
                }

                // Handle explicit function request
                if (payload["requestFunctions"]?.Value<bool>() == true)
                {
                    SendFunctionsToPropertyInspector();
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warn($"Error processing PI payload: {ex.Message}");
            }
        }

        private void OnKeyBindingsLoaded(object sender, EventArgs e)
        {
            SendFunctionsToPropertyInspector();
        }

        #endregion

        #region Sound Playback Infrastructure

        /// <summary>
        /// Loads a click sound from the specified path.
        /// Caches the sound to avoid repeated file reads.
        /// </summary>
        /// <param name="soundPath">Path to the audio file, or null to clear</param>
        /// <returns>True if sound was loaded successfully</returns>
        protected bool LoadClickSound(string soundPath)
        {
            // Same path = already loaded
            if (string.Equals(_lastSoundPath, soundPath, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedClickSound != null;
            }

            _cachedClickSound = null;
            _lastSoundPath = soundPath;

            if (string.IsNullOrEmpty(soundPath) || !File.Exists(soundPath))
            {
                return false;
            }

            try
            {
                _cachedClickSound = new CachedSound(soundPath);
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to load sound '{soundPath}': {ex.Message}");
                _cachedClickSound = null;
                return false;
            }
        }

        /// <summary>
        /// Loads sound from settings that implement ISoundSettings.
        /// Clears the filename in settings if loading fails.
        /// </summary>
        /// <typeparam name="T">Settings type implementing ISoundSettings</typeparam>
        /// <param name="settings">Settings instance to load from</param>
        protected void LoadClickSoundFromSettings<T>(T settings) where T : ISoundSettings
        {
            if (settings == null) return;

            if (!LoadClickSound(settings.ClickSoundFilename))
            {
                settings.ClickSoundFilename = null;
            }
        }

        /// <summary>
        /// Plays the cached click sound if available.
        /// </summary>
        protected void PlayClickSound()
        {
            if (_cachedClickSound == null) return;

            try
            {
                AudioPlaybackEngine.Instance.PlaySound(_cachedClickSound);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to play click sound: {ex.Message}");
            }
        }

        /// <summary>
        /// Plays a specific cached sound if available.
        /// </summary>
        protected void PlaySound(CachedSound sound)
        {
            if (sound == null) return;

            try
            {
                AudioPlaybackEngine.Instance.PlaySound(sound);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to play sound: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads a sound from file path, returning the CachedSound or null.
        /// Useful for actions with multiple sounds.
        /// </summary>
        protected CachedSound TryLoadSound(string filePath, out string normalizedPath)
        {
            normalizedPath = filePath;

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                normalizedPath = null;
                return null;
            }

            try
            {
                return new CachedSound(filePath);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to load sound '{filePath}': {ex.Message}");
                normalizedPath = null;
                return null;
            }
        }

        #endregion

        #region Key Binding Helpers

        /// <summary>
        /// Safely attempts to get and convert a key binding for the specified function.
        /// </summary>
        /// <param name="functionName">The SC function name (e.g., "spaceship_flight-v_pitch")</param>
        /// <param name="keyInfo">Output: converted key string for input simulation</param>
        /// <returns>True if binding was found and converted</returns>
        protected bool TryGetKeyBinding(string functionName, out string keyInfo)
        {
            keyInfo = null;

            if (string.IsNullOrWhiteSpace(functionName))
            {
                return false;
            }

            if (!BindingService.TryGetBinding(functionName, out var action))
            {
                PluginLog.Debug($"No binding found for '{functionName}'");
                return false;
            }

            if (string.IsNullOrWhiteSpace(action.Keyboard))
            {
                PluginLog.Debug($"Binding '{functionName}' has no keyboard mapping");
                return false;
            }

            keyInfo = CommandTools.ConvertKeyString(action.Keyboard);
            return !string.IsNullOrWhiteSpace(keyInfo);
        }

        /// <summary>
        /// Checks if bindings are ready and sets ForceStop appropriately.
        /// </summary>
        /// <returns>True if bindings are loaded and ready</returns>
        protected bool EnsureBindingsReady()
        {
            if (BindingService.Reader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return false;
            }

            StreamDeckCommon.ForceStop = false;
            return true;
        }

        #endregion

        #region Lifecycle

        public override void KeyReleased(KeyPayload payload) 
        { 
            // Default: no-op. Override in derived classes as needed.
        }

        public override void OnTick()
        {
            // Default: no-op. Override in derived classes as needed.
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) 
        { 
            // Default: no-op. Override in derived classes as needed.
        }

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            UnwirePropertyInspectorEvents();
            _cachedClickSound = null;
        }

        #endregion
    }
}
