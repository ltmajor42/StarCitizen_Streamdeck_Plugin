// File: Buttons/StateMemory.cs
// UUID: com.mhwlng.starcitizen.statememory
using System;
using System.IO;
using System.Threading;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SCJMapper_V2.SC;
using starcitizen.Core;

namespace starcitizen.Buttons
{
    [PluginActionId("com.mhwlng.starcitizen.statememory")]
    public class StateMemory : StarCitizenKeypadBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    Function = string.Empty,
                    StateOn = false,
                    SoftSyncLongPress = true,
                    LongPressMs = 750,
                    KeypressDelayMs = 40
                };
            }

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [JsonProperty(PropertyName = "stateOn")]
            public bool StateOn { get; set; }

            [JsonProperty(PropertyName = "softSyncLongPress")]
            public bool SoftSyncLongPress { get; set; }

            [JsonProperty(PropertyName = "longPressMs")]
            public int LongPressMs { get; set; }

            [JsonProperty(PropertyName = "keypressDelayMs")]
            public int KeypressDelayMs { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "shortPressSound")]
            public string ShortPressSoundFilename { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "longPressSound")]
            public string LongPressSoundFilename { get; set; }
        }

        private PluginSettings settings;
        private CachedSound shortPressSound;
        private CachedSound longPressSound;
        private DateTime? pressStartUtc;

        private int inFlight;
        private readonly KeyBindingService bindingService = KeyBindingService.Instance;

        public StateMemory(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            settings = PluginSettings.CreateDefaultSettings();

            if (payload != null && payload.Settings != null && payload.Settings.Count > 0)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
            }

            NormalizeDefaults();
            LoadSounds();

            Connection.OnPropertyInspectorDidAppear += Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += Connection_OnSendToPlugin;
            bindingService.KeyBindingsLoaded += OnKeyBindingsLoaded;

            ApplyVisualState();
            UpdatePropertyInspector();

            Connection.SetSettingsAsync(JObject.FromObject(settings));
        }

        public override void KeyPressed(KeyPayload payload)
        {
            pressStartUtc = DateTime.UtcNow;
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (Interlocked.Exchange(ref inFlight, 1) == 1)
            {
                return;
            }

            try
            {
                if (bindingService.Reader == null)
                {
                    StreamDeckCommon.ForceStop = true;
                    return;
                }

                StreamDeckCommon.ForceStop = false;

                var isLongPress = IsLongPress();

                // Long press = indicator only (no key sent)
                if (settings.SoftSyncLongPress && isLongPress)
                {
                    settings.StateOn = !settings.StateOn;
                    ApplyVisualState();
                    Connection.SetSettingsAsync(JObject.FromObject(settings));
                    PlayLongPressSound();
                    return;
                }

                // Short press = send key + flip indicator
                SafeSendBoundKeypress();

                settings.StateOn = !settings.StateOn;
                ApplyVisualState();
                Connection.SetSettingsAsync(JObject.FromObject(settings));
                PlayShortPressSound();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, "StateMemory KeyReleased failed: " + ex);
            }
            finally
            {
                pressStartUtc = null;
                Interlocked.Exchange(ref inFlight, 0);
            }
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload == null || payload.Settings == null) return;

            Tools.AutoPopulateSettings(settings, payload.Settings);
            NormalizeDefaults();
            LoadSounds();
            ApplyVisualState();
        }

        public override void Dispose()
        {
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            bindingService.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
        }

        private void NormalizeDefaults()
        {
            if (settings.LongPressMs <= 0) settings.LongPressMs = 750;
            if (settings.KeypressDelayMs < 0) settings.KeypressDelayMs = 0;

            if (settings.Function == null) settings.Function = string.Empty;
        }

        private bool IsLongPress()
        {
            if (!pressStartUtc.HasValue) return false;

            var ms = (DateTime.UtcNow - pressStartUtc.Value).TotalMilliseconds;
            return ms >= Math.Max(0, settings.LongPressMs);
        }

        private async void ApplyVisualState()
        {
            try
            {
                await Connection.SetStateAsync(settings.StateOn ? 1u : 0u);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, "StateMemory SetStateAsync failed: " + ex);
            }
        }

        private void SafeSendBoundKeypress()
        {
            try
            {
                if (bindingService.Reader == null) return;
                if (string.IsNullOrWhiteSpace(settings.Function)) return;

                var binding = bindingService.Reader.GetBinding(settings.Function);
                var keyboard = binding != null ? binding.Keyboard : null;

                if (string.IsNullOrWhiteSpace(keyboard)) return;

                var converted = CommandTools.ConvertKeyString(keyboard);
                if (string.IsNullOrWhiteSpace(converted)) return;

                StreamDeckCommon.SendKeypress(converted, settings.KeypressDelayMs);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, "StateMemory SendKeypress failed: " + ex);
            }
        }

        private void LoadSounds()
        {
            string normalizedShort;
            shortPressSound = TryLoadSound(settings.ShortPressSoundFilename, out normalizedShort);
            settings.ShortPressSoundFilename = normalizedShort;

            string normalizedLong;
            longPressSound = TryLoadSound(settings.LongPressSoundFilename, out normalizedLong);
            settings.LongPressSoundFilename = normalizedLong;
        }

        private CachedSound TryLoadSound(string filename, out string normalizedFilename)
        {
            normalizedFilename = filename;

            if (string.IsNullOrEmpty(filename) || !File.Exists(filename))
            {
                normalizedFilename = null;
                return null;
            }

            try
            {
                return new CachedSound(filename);
            }
            catch
            {
                normalizedFilename = null;
                return null;
            }
        }

        private void PlayShortPressSound()
        {
            PlaySound(shortPressSound);
        }

        private void PlayLongPressSound()
        {
            PlaySound(longPressSound ?? shortPressSound);
        }

        private void PlaySound(CachedSound sound)
        {
            if (sound == null) return;

            try
            {
                AudioPlaybackEngine.Instance.PlaySound(sound);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, "StateMemory PlaySound failed: " + ex);
            }
        }

        private void Connection_OnPropertyInspectorDidAppear(object sender, EventArgs e)
        {
            UpdatePropertyInspector();
        }

        private void Connection_OnSendToPlugin(object sender, EventArgs e)
        {
            var payload = e.ExtractPayload();

            if (payload?["property_inspector"]?.ToString() == "propertyInspectorConnected")
            {
                UpdatePropertyInspector();
            }
        }

        private void OnKeyBindingsLoaded(object sender, EventArgs e)
        {
            UpdatePropertyInspector();
        }

        private void UpdatePropertyInspector()
        {
            if (bindingService.Reader == null) return;

            PropertyInspectorMessenger.SendFunctionsAsync(Connection);
        }
    }
}
