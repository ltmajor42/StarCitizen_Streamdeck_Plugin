using System;
using System.Text.RegularExpressions;
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
    /// Hold Macro button - holds a key down for a configurable duration or until release.
    /// Supports auto-repeat for wheel actions.
    /// </summary>
    /// <remarks>
    /// Use this for charge-up actions or when you need precise hold timing.
    /// Can be configured to release after a set duration or hold until button release.
    /// </remarks>
    [PluginActionId("com.ltmajor42.starcitizen.holdmacro")]
    public class HoldMacroAction : StarCitizenKeypadBase
    {
        #region Settings

        /// <summary>
        /// Settings for HoldMacroAction with duration and release behavior.
        /// Stored as string to handle empty values from PI gracefully.
        /// </summary>
        protected class PluginSettings : PluginSettingsBase
        {
            public static PluginSettings CreateDefaultSettings() => new PluginSettings();

            [JsonProperty(PropertyName = "holdDurationMs")]
            public string HoldDurationMs { get; set; } = "0";

            [JsonProperty(PropertyName = "holdUntilRelease")]
            public bool HoldUntilRelease { get; set; } = true;
        }

        #endregion

        #region Constants

        private const int MaxHoldDurationMs = 60000;
        private const int DefaultHoldDurationMs = 0;
        private const int RepeatInitialDelayMs = 250;
        private const int RepeatIntervalMs = 45;

        #endregion

        #region State

        private PluginSettings settings;
        private CancellationTokenSource autoReleaseToken;
        private CancellationTokenSource repeatToken;
        private string activeKeyInfo;
        private bool isKeyDown;

        // Parsed value
        private int holdDurationMs = DefaultHoldDurationMs;

        #endregion

        #region Initialization

        public HoldMacroAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            settings = PluginSettings.CreateDefaultSettings();

            if (payload.Settings != null && payload.Settings.Count > 0)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseSettings();
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

            if (!TryGetKeyBinding(settings.Function, out var keyInfo))
            {
                PluginLog.Warn("HoldMacroAction: no binding found for selected function");
                return;
            }

            CancelAutoRelease(logCancellation: false);

            isKeyDown = true;
            activeKeyInfo = keyInfo;

            PluginLog.Info($"HoldMacroAction pressed: sending DOWN for '{settings.Function}' (holdUntilRelease={settings.HoldUntilRelease}, duration={holdDurationMs}ms)");

            StreamDeckCommon.SendKeypressDown(keyInfo);
            StartRepeat(keyInfo);
            _ = Connection.SetStateAsync(1);

            // If configured to auto-release, schedule it
            if (!settings.HoldUntilRelease)
            {
                var duration = Math.Clamp(holdDurationMs, 0, MaxHoldDurationMs);
                if (duration > 0)
                {
                    ScheduleAutoRelease(keyInfo, duration);
                }
            }
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (!isKeyDown && autoReleaseToken == null) return;

            CancelAutoRelease(logCancellation: true);
            CancelRepeat();

            if (!string.IsNullOrWhiteSpace(activeKeyInfo))
            {
                PluginLog.Info($"HoldMacroAction released: sending UP for '{settings.Function}'");
                StreamDeckCommon.SendKeypressUp(activeKeyInfo);
            }

            isKeyDown = false;
            activeKeyInfo = null;
            _ = Connection.SetStateAsync(0);
        }

        #endregion

        #region Auto-Release

        private void ScheduleAutoRelease(string keyInfo, int duration)
        {
            CancelAutoRelease(logCancellation: false);
            autoReleaseToken = new CancellationTokenSource();
            var token = autoReleaseToken.Token;

            PluginLog.Info($"HoldMacroAction: scheduling auto-release in {duration}ms");

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(duration, token);
                    if (token.IsCancellationRequested) return;

                    PluginLog.Info($"HoldMacroAction: auto-releasing after {duration}ms");
                    CancelRepeat();
                    StreamDeckCommon.SendKeypressUp(keyInfo);
                }
                catch (TaskCanceledException)
                {
                    PluginLog.Info("HoldMacroAction: auto-release cancelled");
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"HoldMacroAction: error during auto-release: {ex}");
                }
                finally
                {
                    isKeyDown = false;
                    activeKeyInfo = null;
                    _ = Connection.SetStateAsync(0);
                    DisposeAutoReleaseToken();
                }
            }, token);
        }

        private void CancelAutoRelease(bool logCancellation)
        {
            if (autoReleaseToken == null) return;

            if (!autoReleaseToken.IsCancellationRequested)
            {
                autoReleaseToken.Cancel();
                if (logCancellation)
                {
                    PluginLog.Info("HoldMacroAction: cancelled scheduled auto-release");
                }
            }

            DisposeAutoReleaseToken();
        }

        private void DisposeAutoReleaseToken()
        {
            autoReleaseToken?.Dispose();
            autoReleaseToken = null;
        }

        #endregion

        #region Repeat Logic

        private void StartRepeat(string keyInfo)
        {
            if (string.IsNullOrWhiteSpace(keyInfo)) return;

            var containsWheelToken = ContainsMouseWheelToken(keyInfo);

            // Do not repeat non-wheel mouse actions
            if (!containsWheelToken && ContainsMouseToken(keyInfo)) return;

            CancelRepeat();
            repeatToken = new CancellationTokenSource();
            var token = repeatToken.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(RepeatInitialDelayMs, token);

                    while (!token.IsCancellationRequested && isKeyDown)
                    {
                        StreamDeckCommon.SendKeypressDown(keyInfo);
                        await Task.Delay(RepeatIntervalMs, token);
                    }
                }
                catch (TaskCanceledException) { /* Expected */ }
                catch (Exception ex)
                {
                    PluginLog.Error($"HoldMacroAction: repeat loop failed: {ex}");
                }
            }, token);
        }

        private void CancelRepeat()
        {
            if (repeatToken == null) return;

            if (!repeatToken.IsCancellationRequested)
            {
                repeatToken.Cancel();
            }

            repeatToken.Dispose();
            repeatToken = null;
        }

        private bool ContainsMouseToken(string keyInfo)
        {
            var matches = Regex.Matches(keyInfo, CommandTools.REGEX_SUB_COMMAND);
            foreach (Match match in matches)
            {
                var token = match.Value.Replace("{", string.Empty).Replace("}", string.Empty);
                if (MouseTokenHelper.TryNormalize(token, out _)) return true;
            }
            return false;
        }

        private bool ContainsMouseWheelToken(string keyInfo)
        {
            var matches = Regex.Matches(keyInfo, CommandTools.REGEX_SUB_COMMAND);
            foreach (Match match in matches)
            {
                var token = match.Value.Replace("{", string.Empty).Replace("}", string.Empty);
                if (MouseTokenHelper.TryNormalize(token, out var normalized) &&
                    normalized.StartsWith("mwheel", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Settings Management

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseSettings();
            }
        }

        private void ParseSettings()
        {
            // Parse hold duration
            if (!string.IsNullOrWhiteSpace(settings.HoldDurationMs) &&
                int.TryParse(settings.HoldDurationMs, out var parsedDuration) &&
                parsedDuration >= 0)
            {
                holdDurationMs = Math.Min(parsedDuration, MaxHoldDurationMs);
            }
            else
            {
                holdDurationMs = DefaultHoldDurationMs;
            }
        }

        #endregion

        #region Disposal

        public override void Dispose()
        {
            CancelAutoRelease(logCancellation: false);
            CancelRepeat();
            base.Dispose();
        }

        #endregion
    }
}
