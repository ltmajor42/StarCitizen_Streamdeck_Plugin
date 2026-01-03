using System;
using System.IO;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using starcitizen.Core;

namespace starcitizen.Buttons
{
    /// <summary>
    /// Dual Action button - sends different keybindings on press vs release.
    /// Press triggers Action A, Release triggers Action B.
    /// </summary>
    [PluginActionId("com.ltmajor42.starcitizen.dualaction")]
    public class DualAction : StarCitizenKeypadBase
    {
        // ============================================================
        // REGION: Settings
        // ============================================================
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings() => new PluginSettings
            {
                DownFunction = string.Empty,
                UpFunction = string.Empty
            };

            [JsonProperty(PropertyName = "downFunction")]
            public string DownFunction { get; set; }

            [JsonProperty(PropertyName = "upFunction")]
            public string UpFunction { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "clickSound")]
            public string ClickSoundFilename { get; set; }
        }

        // ============================================================
        // REGION: State
        // ============================================================
        private readonly PluginSettings settings;
        private CachedSound _clickSound;
        private readonly KeyBindingService bindingService = KeyBindingService.Instance;

        // ============================================================
        // REGION: Initialization
        // ============================================================
        public DualAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                settings = PluginSettings.CreateDefaultSettings();
                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
            }
            else
            {
                settings = payload.Settings.ToObject<PluginSettings>();
                LoadClickSound();
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
            SendAction(settings.DownFunction);
            _ = Connection.SetStateAsync(1);
            PlayClickSound();
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (bindingService.Reader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;
            SendAction(settings.UpFunction);
            _ = Connection.SetStateAsync(0);
        }

        private void SendAction(string function)
        {
            if (!bindingService.TryGetBinding(function, out var action)) return;

            var converted = CommandTools.ConvertKeyString(action.Keyboard);
            if (!string.IsNullOrWhiteSpace(converted))
            {
                StreamDeckCommon.SendKeypress(converted, 40);
            }
        }

        // ============================================================
        // REGION: Settings Management
        // ============================================================
        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Tools.AutoPopulateSettings(settings, payload.Settings);
            LoadClickSound();
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
            catch { /* intentionally ignore */ }
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
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            bindingService.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
        }
    }
}
