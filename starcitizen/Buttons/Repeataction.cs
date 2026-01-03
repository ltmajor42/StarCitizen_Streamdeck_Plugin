using System;
using System.Threading;
using System.Threading.Tasks;
using BarRaider.SdTools;
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
    /// <remarks>
    /// Use this for power management or other actions that benefit
    /// from rapid repeated input while the button is held.
    /// </remarks>
    [PluginActionId("com.ltmajor42.starcitizen.holdrepeat")]
    public class Repeataction : StarCitizenKeypadBase
    {
        #region Settings

        /// <summary>
        /// Settings for Repeataction with configurable repeat rate.
        /// Stored as string to handle empty values from PI gracefully.
        /// </summary>
        protected class PluginSettings : PluginSettingsBase
        {
            public static PluginSettings CreateDefaultSettings() => new PluginSettings();

            [JsonProperty(PropertyName = "repeatRate")]
            public string RepeatRate { get; set; } = "100";
        }

        #endregion

        #region Constants

        private const int MinRepeatRate = 1;
        private const int DefaultRepeatRate = 100;
        private const int DefaultKeypressDelay = 40;

        #endregion

        #region State

        private readonly PluginSettings settings;
        private CancellationTokenSource repeatToken;
        private int currentRepeatRate = DefaultRepeatRate;
        private bool isRepeating;

        #endregion

        #region Initialization

        public Repeataction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            settings = PluginSettings.CreateDefaultSettings();

            if (payload.Settings != null && payload.Settings.Count > 0)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseRepeatRate();
            }
            else
            {
                _ = Connection.SetSettingsAsync(JObject.FromObject(settings));
            }

            WirePropertyInspectorEvents();
            SendFunctionsToPropertyInspector();
        }

        #endregion

        #region Key Events

        public override void KeyPressed(KeyPayload payload)
        {
            if (!EnsureBindingsReady()) return;

            if (!TryGetKeyBinding(settings.Function, out var keyInfo)) return;

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

        #endregion

        #region Repeat Logic

        private async Task RepeatWhileHeldAsync(string keyInfo, int repeatRate, CancellationToken token)
        {
            SendSingleKeypress(keyInfo);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Math.Max(MinRepeatRate, repeatRate), token);
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
                StreamDeckCommon.SendKeypress(keyInfo, DefaultKeypressDelay);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to send repeat keypress: {ex}");
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

        #endregion

        #region Settings Management

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseRepeatRate();
            }
        }

        private void ParseRepeatRate()
        {
            if (!string.IsNullOrWhiteSpace(settings.RepeatRate) && 
                int.TryParse(settings.RepeatRate, out var parsedRate) &&
                parsedRate >= MinRepeatRate)
            {
                currentRepeatRate = parsedRate;
            }
            else
            {
                currentRepeatRate = DefaultRepeatRate;
            }
        }

        #endregion

        #region Disposal

        public override void Dispose()
        {
            StopRepeater();
            base.Dispose();
        }

        #endregion
    }
}
