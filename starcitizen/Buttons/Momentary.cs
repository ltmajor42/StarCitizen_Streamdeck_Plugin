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
    /// Momentary button - sends key down on press, key up on release.
    /// Includes visual state indicator that shows active state briefly after release.
    /// </summary>
    /// <remarks>
    /// Use this for quick-tap actions like firing weapons or activating shields.
    /// The visual feedback helps confirm the action was triggered.
    /// </remarks>
    [PluginActionId("com.ltmajor42.starcitizen.momentary")]
    public class Momentary : StarCitizenKeypadBase
    {
        #region Settings

        /// <summary>
        /// Settings for Momentary button with visual feedback delay.
        /// </summary>
        protected class PluginSettings : PluginSettingsBase
        {
            public static PluginSettings CreateDefaultSettings() => new PluginSettings();

            /// <summary>
            /// Duration in ms to show the active visual state after release.
            /// Stored as string to handle empty values from PI gracefully.
            /// </summary>
            [JsonProperty(PropertyName = "delay")]
            public string Delay { get; set; } = "1000";
        }

        #endregion

        #region Constants

        private const int DefaultVisualDelay = 1000;

        #endregion

        #region State

        private readonly PluginSettings settings;
        private CancellationTokenSource resetToken;
        private int visualSequence;
        private int currentVisualDelay = DefaultVisualDelay;

        #endregion

        #region Initialization

        public Momentary(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            settings = PluginSettings.CreateDefaultSettings();

            if (payload.Settings != null && payload.Settings.Count > 0)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseVisualDelay();
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

            if (TryGetKeyBinding(settings.Function, out var keyInfo))
            {
                StreamDeckCommon.SendKeypressDown(keyInfo);
            }
            else
            {
                PluginLog.Warn($"Momentary action '{settings.Function}' missing keyboard binding");
            }

            PlayClickSound();
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (!EnsureBindingsReady()) return;

            if (TryGetKeyBinding(settings.Function, out var keyInfo))
            {
                StreamDeckCommon.SendKeypressUp(keyInfo);
            }

            TriggerMomentaryVisual(currentVisualDelay);
        }

        #endregion

        #region Visual State Management

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

        #endregion

        #region Settings Management

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseVisualDelay();
                LoadClickSoundFromSettings(settings);
            }
        }

        private void ParseVisualDelay()
        {
            if (!string.IsNullOrWhiteSpace(settings.Delay) && 
                int.TryParse(settings.Delay, out int parsed) && 
                parsed >= 0)
            {
                currentVisualDelay = parsed;
            }
            else
            {
                currentVisualDelay = DefaultVisualDelay;
            }
        }

        #endregion

        #region Disposal

        public override void Dispose()
        {
            resetToken?.Cancel();
            resetToken?.Dispose();
            resetToken = null;
            
            base.Dispose();
        }

        #endregion
    }
}
