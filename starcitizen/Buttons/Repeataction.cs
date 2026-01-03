using System;
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
    /// Repeat Action button - repeatedly sends a keybind while held.
    /// Useful for actions that need rapid repeated input.
    /// </summary>
    [PluginActionId("com.ltmajor42.starcitizen.holdrepeat")]
    public class Repeataction : StarCitizenKeypadBase
    {
        // ============================================================
        // REGION: Settings
        // ============================================================
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings() => new PluginSettings
            {
                Function = string.Empty,
                RepeatRate = 100
            };

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [JsonProperty(PropertyName = "repeatRate")]
            public int RepeatRate { get; set; }
        }

        // ============================================================
        // REGION: State
        // ============================================================
        private readonly PluginSettings settings;
        private CancellationTokenSource repeatToken;
        private int currentRepeatRate = 100;
        private bool isRepeating;
        private readonly KeyBindingService bindingService = KeyBindingService.Instance;

        // ============================================================
        // REGION: Initialization
        // ============================================================
        public Repeataction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            settings = PluginSettings.CreateDefaultSettings();

            if (payload.Settings != null && payload.Settings.Count > 0)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseRepeatRate(payload.Settings);
            }
            else
            {
                _ = Connection.SetSettingsAsync(JObject.FromObject(settings));
            }

            Connection.OnPropertyInspectorDidAppear += Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += Connection_OnSendToPlugin;
            bindingService.KeyBindingsLoaded += OnKeyBindingsLoaded;

            UpdatePropertyInspector();
        }

        // ============================================================
        // REGION: Key Events
        // ============================================================
        public override void KeyPressed(KeyPayload payload)
        {
            if (bindingService.Reader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            if (payload?.Settings != null) ParseRepeatRate(payload.Settings);

            if (!bindingService.TryGetBinding(settings.Function, out var action)) return;

            var keyInfo = CommandTools.ConvertKeyString(action.Keyboard);
            if (string.IsNullOrWhiteSpace(keyInfo)) return;

            StopRepeater();
            repeatToken = new CancellationTokenSource();
            isRepeating = true;

            _ = Connection.SetStateAsync(1);
            _ = Task.Run(() => RepeatWhileHeldAsync(keyInfo, currentRepeatRate, repeatToken.Token));
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (!isRepeating) return;

            StopRepeater();
            _ = Connection.SetStateAsync(0);
        }

        // ============================================================
        // REGION: Repeat Logic
        // ============================================================
        private async Task RepeatWhileHeldAsync(string keyInfo, int repeatRate, CancellationToken token)
        {
            SendSingleKeypress(keyInfo);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Math.Max(1, repeatRate), token);
                }
                catch (TaskCanceledException) { break; }

                if (token.IsCancellationRequested) break;

                SendSingleKeypress(keyInfo);
            }
        }

        private void SendSingleKeypress(string keyInfo)
        {
            try
            {
                StreamDeckCommon.SendKeypress(keyInfo, 40);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed to send repeat keypress: {ex}");
            }
        }

        private void StopRepeater()
        {
            if (repeatToken != null)
            {
                try { repeatToken.Cancel(); } catch { }
                repeatToken.Dispose();
                repeatToken = null;
            }
            isRepeating = false;
        }

        // ============================================================
        // REGION: Settings Management
        // ============================================================
        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseRepeatRate(payload.Settings);
            }
        }

        private void ParseRepeatRate(JObject settingsObj)
        {
            if (settingsObj != null &&
                settingsObj.TryGetValue("repeatRate", out var rateToken) &&
                int.TryParse(rateToken.ToString(), out var parsedRate))
            {
                currentRepeatRate = Math.Max(1, parsedRate);
            }
            else
            {
                currentRepeatRate = Math.Max(1, settings.RepeatRate);
            }
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
            repeatToken?.Cancel();
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            bindingService.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
        }
    }
}
