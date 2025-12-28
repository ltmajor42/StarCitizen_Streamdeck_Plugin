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
    [PluginActionId("com.mhwlng.starcitizen.dualaction")]
    public class DualAction : StarCitizenKeypadBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    DownFunction = string.Empty,
                    UpFunction = string.Empty
                };
            }

            [JsonProperty(PropertyName = "downFunction")]
            public string DownFunction { get; set; }

            [JsonProperty(PropertyName = "upFunction")]
            public string UpFunction { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "clickSound")]
            public string ClickSoundFilename { get; set; }
        }

        private PluginSettings settings;
        private CachedSound _clickSound;
        private readonly KeyBindingService bindingService = KeyBindingService.Instance;

        public DualAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
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

        public override void KeyPressed(KeyPayload payload)
        {
            if (bindingService.Reader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            SendDownAction();
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

            SendUpAction();
            _ = Connection.SetStateAsync(0);
        }

        private void SendDownAction()
        {
            if (!bindingService.TryGetBinding(settings.DownFunction, out var action))
            {
                return;
            }

            StreamDeckCommon.SendKeypressDown(CommandTools.ConvertKeyString(action.Keyboard));
        }

        private void SendUpAction()
        {
            if (bindingService.TryGetBinding(settings.DownFunction, out var downAction))
            {
                StreamDeckCommon.SendKeypressUp(CommandTools.ConvertKeyString(downAction.Keyboard));
            }

            if (!bindingService.TryGetBinding(settings.UpFunction, out var upAction) || settings.UpFunction == settings.DownFunction)
            {
                return;
            }

            StreamDeckCommon.SendKeypress(CommandTools.ConvertKeyString(upAction.Keyboard), 40);
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Tools.AutoPopulateSettings(settings, payload.Settings);
            LoadClickSound();
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
            if (bindingService.Reader == null)
            {
                return;
            }

            PropertyInspectorMessenger.SendFunctionsAsync(Connection);
        }

        private void LoadClickSound()
        {
            _clickSound = null;

            if (!string.IsNullOrEmpty(settings.ClickSoundFilename) &&
                File.Exists(settings.ClickSoundFilename))
            {
                try
                {
                    _clickSound = new CachedSound(settings.ClickSoundFilename);
                }
                catch
                {
                    settings.ClickSoundFilename = null;
                }
            }
        }

        private void PlayClickSound()
        {
            if (_clickSound == null)
            {
                return;
            }

            try
            {
                AudioPlaybackEngine.Instance.PlaySound(_clickSound);
            }
            catch
            {
                // intentionally ignore
            }
        }

        public override void Dispose()
        {
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            bindingService.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
        }
    }
}
