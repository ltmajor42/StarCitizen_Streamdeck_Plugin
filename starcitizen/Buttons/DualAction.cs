using BarRaider.SdTools;
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
    /// <remarks>
    /// Use this for two-stage workflows, like opening a menu on press
    /// and confirming/closing on release.
    /// </remarks>
    [PluginActionId("com.ltmajor42.starcitizen.dualaction")]
    public class DualAction : StarCitizenKeypadBase
    {
        #region Settings

        /// <summary>
        /// Settings for DualAction with separate press and release functions.
        /// </summary>
        protected class PluginSettings : ISoundSettings
        {
            public static PluginSettings CreateDefaultSettings() => new();

            [JsonProperty(PropertyName = "downFunction")]
            public string DownFunction { get; set; } = string.Empty;

            [JsonProperty(PropertyName = "upFunction")]
            public string UpFunction { get; set; } = string.Empty;

            [FilenameProperty]
            [JsonProperty(PropertyName = "clickSound")]
            public string ClickSoundFilename { get; set; }
        }

        #endregion

        #region State

        private readonly PluginSettings settings;

        #endregion

        #region Initialization

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
                LoadClickSoundFromSettings(settings);
            }

            WirePropertyInspectorEvents();
            SendFunctionsToPropertyInspector();
        }

        #endregion

        #region Key Events

        public override void KeyPressed(KeyPayload payload)
        {
            if (!EnsureBindingsReady()) return;

            SendAction(settings.DownFunction);
            _ = Connection.SetStateAsync(1);
            PlayClickSound();
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (!EnsureBindingsReady()) return;

            SendAction(settings.UpFunction);
            _ = Connection.SetStateAsync(0);
        }

        private void SendAction(string function)
        {
            if (!TryGetKeyBinding(function, out var keyInfo)) return;
            StreamDeckCommon.SendKeypress(keyInfo, 40);
        }

        #endregion

        #region Settings Management

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Tools.AutoPopulateSettings(settings, payload.Settings);
            LoadClickSoundFromSettings(settings);
        }

        #endregion
    }
}
