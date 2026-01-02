using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using WindowsInput.Native;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SCJMapper_V2.SC;
using starcitizen.Core;

// ReSharper disable StringLiteralTypo

namespace starcitizen.Buttons
{

    [PluginActionId("com.ltmajor42.starcitizen.static")]
    public class ActionKey : StarCitizenKeypadBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                var instance = new PluginSettings
                {
                    Function = string.Empty,
                };

                return instance;
            }

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "clickSound")]
            public string ClickSoundFilename { get; set; }

        }


        private readonly PluginSettings settings;
         private CachedSound _clickSound = null;
         private readonly KeyBindingService bindingService = KeyBindingService.Instance;

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

            // Subscribe to Property Inspector events
            Connection.OnPropertyInspectorDidAppear += Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += Connection_OnSendToPlugin;

            // Subscribe to key bindings loaded event
            bindingService.KeyBindingsLoaded += OnKeyBindingsLoaded;

            // Send functions data immediately if PI is already open
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

            if (bindingService.TryGetBinding(settings.Function, out var action))
            {
                var keyInfo = CommandTools.ConvertKeyString(action.Keyboard);
                PluginLog.Info(keyInfo);

                StreamDeckCommon.SendKeypressDown(keyInfo);
            }

            if (_clickSound != null)
            {
                try
                {
                    AudioPlaybackEngine.Instance.PlaySound(_clickSound);
                }
                catch (Exception ex)
                {
                    PluginLog.Fatal($"PlaySound: {ex}");
                }

            }

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


        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            PluginLog.Info($"ReceivedSettings - Function: {payload.Settings?["function"]?.ToString() ?? "null"}");

            // New in StreamDeck-Tools v2.0:
            BarRaider.SdTools.Tools.AutoPopulateSettings(settings, payload.Settings);
            
            PluginLog.Info($"After AutoPopulateSettings - Function: {settings.Function ?? "null"}");
            
            HandleFileNames();
        }

        private void Connection_OnPropertyInspectorDidAppear(object sender, EventArgs e)
        {
            PluginLog.Info("Property Inspector appeared, sending functions data");
            UpdatePropertyInspector();
        }

        private void Connection_OnSendToPlugin(object sender, EventArgs e)
        {
            // Check if the Property Inspector is sending a log message
            JObject payload = null;
            try
            {
                payload = e.ExtractPayload();
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error processing PI payload: {ex.Message}");
            }

            if (payload != null)
            {
                if (payload != null && payload.ContainsKey("jslog"))
                {
                    var logMessage = payload["jslog"]?.ToString();
                    PluginLog.Info($"[JS-PI] {logMessage}");
                    return; // Handled, exit early
                }
            }

            // Check if the Property Inspector is sending a connection message
            string propertyInspectorStatus = null;
            try
            {

                if (payload != null && payload.ContainsKey("property_inspector"))
                {
                    propertyInspectorStatus = payload["property_inspector"]?.ToString();
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            if (propertyInspectorStatus == "propertyInspectorConnected")
            {
                PluginLog.Info("Property Inspector connected message received, sending functions data");
                UpdatePropertyInspector();
            }
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

        private void OnKeyBindingsLoaded(object sender, EventArgs e)
        {
            // Update Property Inspector when key bindings are loaded
            UpdatePropertyInspector();
        }

        public override void Dispose()
        {
            // Unsubscribe from events
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            bindingService.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
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

    }
}
