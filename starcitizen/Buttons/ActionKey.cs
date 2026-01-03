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
    /// Action Key button - sends a Star Citizen keybinding while held.
    /// Key down on press, key up on release (hold behavior).
    /// </summary>
    [PluginActionId("com.ltmajor42.starcitizen.static")]
    public class ActionKey : StarCitizenKeypadBase
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
        private readonly KeyBindingService bindingService = KeyBindingService.Instance;

        // ============================================================
        // REGION: Initialization
        // ============================================================
        public ActionKey(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                settings = PluginSettings.CreateDefaultSettings();
                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
            }
            else
            {
                settings = payload.Settings.ToObject<PluginSettings>();
                HandleFileNames();
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

            if (bindingService.TryGetBinding(settings.Function, out var action))
            {
                var keyInfo = CommandTools.ConvertKeyString(action.Keyboard);
                PluginLog.Info(keyInfo);
                StreamDeckCommon.SendKeypressDown(keyInfo);
            }

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

            if (bindingService.TryGetBinding(settings.Function, out var action))
            {
                var keyInfo = CommandTools.ConvertKeyString(action.Keyboard);
                PluginLog.Info(keyInfo);
                StreamDeckCommon.SendKeypressUp(keyInfo);
            }
        }

        // ============================================================
        // REGION: Settings Management
        // ============================================================
        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            PluginLog.Info($"ReceivedSettings - Function: {payload.Settings?["function"]?.ToString() ?? "null"}");
            Tools.AutoPopulateSettings(settings, payload.Settings);
            PluginLog.Info($"After AutoPopulateSettings - Function: {settings.Function ?? "null"}");
            HandleFileNames();
        }

        private void HandleFileNames()
        {
            _clickSound = null;
            if (File.Exists(settings.ClickSoundFilename))
            {
                try
                {
                    _clickSound = new CachedSound(settings.ClickSoundFilename);
                }
                catch (Exception ex)
                {
                    PluginLog.Fatal($"CachedSound: {settings.ClickSoundFilename} {ex}");
                    _clickSound = null;
                    settings.ClickSoundFilename = null;
                }
            }

            Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
        }

        private void PlayClickSound()
        {
            if (_clickSound == null) return;

            try
            {
                AudioPlaybackEngine.Instance.PlaySound(_clickSound);
            }
            catch (Exception ex)
            {
                PluginLog.Fatal($"PlaySound: {ex}");
            }
        }

        // ============================================================
        // REGION: Property Inspector
        // ============================================================
        private void Connection_OnPropertyInspectorDidAppear(object sender, EventArgs e)
        {
            PluginLog.Info("Property Inspector appeared, sending functions data");
            UpdatePropertyInspector();
        }

        private void Connection_OnSendToPlugin(object sender, EventArgs e)
        {
            JObject payload = null;
            try { payload = e.ExtractPayload(); }
            catch (Exception ex)
            {
                PluginLog.Error($"Error processing PI payload: {ex.Message}");
            }

            if (payload != null)
            {
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
                    PluginLog.Info("Property Inspector connected, sending functions data");
                    UpdatePropertyInspector();
                }
            }
        }

        private void OnKeyBindingsLoaded(object sender, EventArgs e)
        {
            UpdatePropertyInspector();
        }

        private void UpdatePropertyInspector()
        {
            try
            {
                if (bindingService.Reader == null)
                {
                    PluginLog.Warn("dpReader is null, cannot update Property Inspector");
                    return;
                }

                PropertyInspectorMessenger.SendFunctionsAsync(Connection);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to update Property Inspector: {ex.Message}");
            }
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
