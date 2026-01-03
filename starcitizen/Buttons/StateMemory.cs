// File: Buttons/StateMemory.cs
// UUID: com.ltmajor42.starcitizen.statememory
using System;
using System.Threading;
using BarRaider.SdTools;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using starcitizen.Audio;
using starcitizen.Core;

namespace starcitizen.Buttons
{
    /// <summary>
    /// State Memory button - a toggle with soft sync capability.
    /// Short press: sends keybind + toggles state.
    /// Long press: toggles state only (manual resync without sending key).
    /// </summary>
    /// <remarks>
    /// Use this for toggle actions like landing gear, lights, or VTOL.
    /// Long-press allows manual resync when state gets out of sync with the game.
    /// </remarks>
    [PluginActionId("com.ltmajor42.starcitizen.statememory")]
    public class StateMemory : StarCitizenKeypadBase
    {
        #region Settings

        /// <summary>
        /// Settings for StateMemory with dual sound and long-press configuration.
        /// Numeric values stored as strings to handle empty values from PI gracefully.
        /// </summary>
        protected class PluginSettings : PluginSettingsBase
        {
            public static PluginSettings CreateDefaultSettings() => new();

            [JsonProperty(PropertyName = "stateOn")]
            public bool StateOn { get; set; }

            [JsonProperty(PropertyName = "softSyncLongPress")]
            public bool SoftSyncLongPress { get; set; } = true;

            [JsonProperty(PropertyName = "longPressMs")]
            public string LongPressMs { get; set; } = "750";

            [JsonProperty(PropertyName = "keypressDelayMs")]
            public string KeypressDelayMs { get; set; } = "40";

            [FilenameProperty]
            [JsonProperty(PropertyName = "shortPressSound")]
            public string ShortPressSoundFilename { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "longPressSound")]
            public string LongPressSoundFilename { get; set; }
        }

        #endregion

        #region Constants

        private const int DefaultLongPressMs = 750;
        private const int DefaultKeypressDelayMs = 40;

        #endregion

        #region State

        private readonly PluginSettings settings;
        private CachedSound shortPressSound;
        private CachedSound longPressSound;
        private DateTime? pressStartUtc;
        private int inFlight;

        // Parsed values
        private int longPressMs = DefaultLongPressMs;
        private int keypressDelayMs = DefaultKeypressDelayMs;

        #endregion

        #region Initialization

        public StateMemory(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            settings = PluginSettings.CreateDefaultSettings();

            if (payload?.Settings != null && payload.Settings.Count > 0)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
            }

            ParseSettings();
            LoadSounds();
            ApplyVisualState();

            WirePropertyInspectorEvents();
            SendFunctionsToPropertyInspector();
            Connection.SetSettingsAsync(JObject.FromObject(settings));
        }

        #endregion

        #region Key Events

        public override void KeyPressed(KeyPayload payload)
        {
            pressStartUtc = DateTime.UtcNow;
        }

        public override void KeyReleased(KeyPayload payload)
        {
            // Prevent concurrent execution
            if (Interlocked.Exchange(ref inFlight, 1) == 1) return;

            try
            {
                if (!EnsureBindingsReady()) return;

                var isLongPress = IsLongPress();

                // Long press = indicator only (no key sent)
                if (settings.SoftSyncLongPress && isLongPress)
                {
                    settings.StateOn = !settings.StateOn;
                    ApplyVisualState();
                    Connection.SetSettingsAsync(JObject.FromObject(settings));
                    PlaySound(longPressSound ?? shortPressSound);
                    return;
                }

                // Short press = send key + flip indicator
                SendBoundKeypress();
                settings.StateOn = !settings.StateOn;
                ApplyVisualState();
                Connection.SetSettingsAsync(JObject.FromObject(settings));
                PlaySound(shortPressSound);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"StateMemory KeyReleased failed: {ex}");
            }
            finally
            {
                pressStartUtc = null;
                Interlocked.Exchange(ref inFlight, 0);
            }
        }

        #endregion

        #region Settings Management

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload?.Settings == null) return;

            Tools.AutoPopulateSettings(settings, payload.Settings);
            ParseSettings();
            LoadSounds();
            ApplyVisualState();
        }

        private void ParseSettings()
        {
            // Parse long press duration
            if (!string.IsNullOrWhiteSpace(settings.LongPressMs) &&
                int.TryParse(settings.LongPressMs, out var parsedLong) &&
                parsedLong > 0)
            {
                longPressMs = parsedLong;
            }
            else
            {
                longPressMs = DefaultLongPressMs;
            }

            // Parse keypress delay
            if (!string.IsNullOrWhiteSpace(settings.KeypressDelayMs) &&
                int.TryParse(settings.KeypressDelayMs, out var parsedDelay) &&
                parsedDelay >= 0)
            {
                keypressDelayMs = parsedDelay;
            }
            else
            {
                keypressDelayMs = DefaultKeypressDelayMs;
            }

            settings.Function ??= string.Empty;
        }

        #endregion

        #region Key Handling

        private bool IsLongPress()
        {
            if (!pressStartUtc.HasValue) return false;
            var ms = (DateTime.UtcNow - pressStartUtc.Value).TotalMilliseconds;
            return ms >= Math.Max(0, longPressMs);
        }

        private async void ApplyVisualState()
        {
            try
            {
                await Connection.SetStateAsync(settings.StateOn ? 1u : 0u);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"StateMemory SetStateAsync failed: {ex}");
            }
        }

        private void SendBoundKeypress()
        {
            if (!TryGetKeyBinding(settings.Function, out var keyInfo)) return;
            StreamDeckCommon.SendKeypress(keyInfo, keypressDelayMs);
        }

        #endregion

        #region Sound Management

        private void LoadSounds()
        {
            shortPressSound = TryLoadSound(settings.ShortPressSoundFilename, out var normalizedShort);
            settings.ShortPressSoundFilename = normalizedShort;

            longPressSound = TryLoadSound(settings.LongPressSoundFilename, out var normalizedLong);
            settings.LongPressSoundFilename = normalizedLong;
        }

        #endregion

        #region Disposal

        public override void Dispose()
        {
            shortPressSound = null;
            longPressSound = null;
            
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
