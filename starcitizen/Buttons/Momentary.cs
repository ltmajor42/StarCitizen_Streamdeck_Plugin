using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using starcitizen.Core;

namespace starcitizen.Buttons
{
    /// <summary>
    /// Momentary button - sends key down on press, key up on release.
    /// Includes visual state indicator that shows active state briefly after release.
    /// </summary>
    [PluginActionId("com.ltmajor42.starcitizen.momentary")]
    public class Momentary : StarCitizenKeypadBase
    {
        // ============================================================
        // REGION: Settings
        // ============================================================
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings() => new PluginSettings { Function = string.Empty };

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "clickSound")]
            public string ClickSoundFilename { get; set; }
        }

        // ============================================================
        // REGION: State
        // ============================================================
        private readonly PluginSettings settings;
        private CachedSound _clickSound;
        private CancellationTokenSource resetToken;
        private int visualSequence;
        private readonly KeyBindingService bindingService = KeyBindingService.Instance;
        private int currentDelay = 1000;  // Visual delay in ms

        // ============================================================
        // REGION: Initialization
        // ============================================================
        public Momentary(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            settings = PluginSettings.CreateDefaultSettings();

            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseDelay(payload.Settings);
            }

            Connection.OnPropertyInspectorDidAppear += Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += Connection_OnSendToPlugin;
            bindingService.KeyBindingsLoaded += OnKeyBindingsLoaded;

            LoadClickSound();
            UpdatePropertyInspector();
        }

        // ============================================================
        // REGION: Key Events
        // ============================================================
        public override void KeyPressed(KeyPayload payload)
        {
            if (bindingService.Reader == null) return;

            if (bindingService.TryGetBinding(settings.Function, out var action))
            {
                var keyString = CommandTools.ConvertKeyString(action.Keyboard);
                if (!string.IsNullOrEmpty(keyString))
                {
                    StreamDeckCommon.SendKeypressDown(keyString);
                }
                else
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, 
                        $"Momentary action '{settings.Function}' missing keyboard binding");
                }
            }

            PlayClickSound();
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (bindingService.Reader == null) return;

            if (bindingService.TryGetBinding(settings.Function, out var action))
            {
                var keyString = CommandTools.ConvertKeyString(action.Keyboard);
                if (!string.IsNullOrEmpty(keyString))
                {
                    StreamDeckCommon.SendKeypressUp(keyString);
                }
            }

            // Prefer live payload value if available
            var delayToUse = currentDelay;
            if (payload?.Settings != null &&
                payload.Settings.TryGetValue("delay", out var delayToken) &&
                int.TryParse(delayToken.ToString(), out int liveDelay))
            {
                delayToUse = liveDelay;
                currentDelay = liveDelay;
            }

            TriggerMomentaryVisual(delayToUse);
        }

        // ============================================================
        // REGION: Visual State Management
        // ============================================================
        private void TriggerMomentaryVisual(int delay)
        {
            resetToken?.Cancel();
            resetToken = new CancellationTokenSource();
            var sequence = Interlocked.Increment(ref visualSequence);
            _ = RunMomentaryVisualAsync(delay, resetToken.Token, sequence);
        }

        private async Task RunMomentaryVisualAsync(int delay, CancellationToken token, int sequence)
        {
            await Connection.SetStateAsync(1);  // Active state

            try
            {
                await Task.Delay(Math.Max(0, delay), token);
            }
            catch (TaskCanceledException) { }
            finally
            {
                // Only the latest sequence reverts to idle
                if (sequence == visualSequence)
                {
                    await Connection.SetStateAsync(0);
                }
            }
        }

        // ============================================================
        // REGION: Settings Management
        // ============================================================
        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseDelay(payload.Settings);
            }
            LoadClickSound();
        }

        private void ParseDelay(JObject settingsObj)
        {
            if (settingsObj.TryGetValue("delay", out var delayToken) &&
                int.TryParse(delayToken.ToString(), out int parsed))
            {
                currentDelay = parsed;
            }
        }

        private void LoadClickSound()
        {
            _clickSound = null;
            if (!string.IsNullOrEmpty(settings.ClickSoundFilename) && File.Exists(settings.ClickSoundFilename))
            {
                try { _clickSound = new CachedSound(settings.ClickSoundFilename); }
                catch { settings.ClickSoundFilename = null; }
            }
        }

        private void PlayClickSound()
        {
            if (_clickSound == null) return;
            try { AudioPlaybackEngine.Instance.PlaySound(_clickSound); }
            catch { }
        }

        // ============================================================
        // REGION: Property Inspector
        // ============================================================
        private void Connection_OnPropertyInspectorDidAppear(object sender, EventArgs e) => UpdatePropertyInspector();

        private void Connection_OnSendToPlugin(object sender, EventArgs e)
        {
            try
            {
                var payload = e.ExtractPayload();
                if (payload?["property_inspector"]?.ToString() == "propertyInspectorConnected")
                    UpdatePropertyInspector();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"Failed processing PI payload: {ex.Message}");
            }
        }

        private void OnKeyBindingsLoaded(object sender, EventArgs e) => UpdatePropertyInspector();

        private void UpdatePropertyInspector()
        {
            if (bindingService.Reader == null) return;
            PropertyInspectorMessenger.SendFunctionsAsync(Connection);
        }

        // ============================================================
        // REGION: Disposal
        // ============================================================
        public override void Dispose()
        {
            resetToken?.Cancel();
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            bindingService.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
        }
    }
}
