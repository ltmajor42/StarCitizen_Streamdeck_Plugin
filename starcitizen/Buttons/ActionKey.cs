using BarRaider.SdTools;
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
    /// <remarks>
    /// Use this for actions that require holding, like boost or afterburner.
    /// The key remains pressed as long as the Stream Deck button is held.
    /// </remarks>
    [PluginActionId("com.ltmajor42.starcitizen.static")]
    public class ActionKey : StarCitizenKeypadBase
    {
        #region Settings

        /// <summary>
        /// Settings for ActionKey button.
        /// </summary>
        protected class PluginSettings : PluginSettingsBase
        {
            public static PluginSettings CreateDefaultSettings() => new PluginSettings();
        }

        #endregion

        #region State

        private readonly PluginSettings settings;

        #endregion

        #region Initialization

        public ActionKey(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            // Load or create settings
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                settings = PluginSettings.CreateDefaultSettings();
                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
            }
            else
            {
                settings = payload.Settings.ToObject<PluginSettings>();
                LoadClickSoundFromSettings(settings);
            }

            // Wire up PI events and send initial data
            WirePropertyInspectorEvents();
            SendFunctionsToPropertyInspector();
        }

        #endregion

        #region Key Events

        public override void KeyPressed(KeyPayload payload)
        {
            if (!EnsureBindingsReady()) return;

            if (TryGetKeyBinding(settings.Function, out var keyInfo))
            {
                PluginLog.Info($"ActionKey pressed: {keyInfo}");
                StreamDeckCommon.SendKeypressDown(keyInfo);
            }

            PlayClickSound();
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (!EnsureBindingsReady()) return;

            if (TryGetKeyBinding(settings.Function, out var keyInfo))
            {
                PluginLog.Info($"ActionKey released: {keyInfo}");
                StreamDeckCommon.SendKeypressUp(keyInfo);
            }
        }

        #endregion

        #region Settings Management

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            PluginLog.Info($"ReceivedSettings - Function: {payload.Settings?["function"]?.ToString() ?? "null"}");
            Tools.AutoPopulateSettings(settings, payload.Settings);
            PluginLog.Info($"After AutoPopulateSettings - Function: {settings.Function ?? "null"}");
            
            LoadClickSoundFromSettings(settings);
            Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
        }

        #endregion

        #region Disposal

        public override void Dispose()
        {
            base.Dispose();
        }

        #endregion
    }
}
