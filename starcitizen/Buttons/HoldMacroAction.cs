using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using starcitizen;
using starcitizen.Core;

namespace starcitizen.Buttons
{
    [PluginActionId("com.ltmajor42.starcitizen.holdmacro")]
    public class HoldMacroAction : StarCitizenKeypadBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    Function = string.Empty,
                    HoldDurationMs = 0,
                    HoldUntilRelease = true
                };
            }

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [JsonProperty(PropertyName = "holdDurationMs")]
            public int HoldDurationMs { get; set; }

            [JsonProperty(PropertyName = "holdUntilRelease")]
            public bool HoldUntilRelease { get; set; }
        }

        private const int MaxHoldDurationMs = 60000;
        private const int RepeatInitialDelayMs = 250;
        private const int RepeatIntervalMs = 45;

        private PluginSettings settings;
        private readonly KeyBindingService bindingService = KeyBindingService.Instance;
        private CancellationTokenSource autoReleaseToken;
        private CancellationTokenSource repeatToken;
        private string activeKeyInfo;
        private bool activeKeyHasMouseToken;
        private bool isKeyDown;

        public HoldMacroAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            settings = PluginSettings.CreateDefaultSettings();

            if (payload.Settings != null && payload.Settings.Count > 0)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ClampHoldDuration();
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

        public override void KeyPressed(KeyPayload payload)
        {
            if (bindingService.Reader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            RefreshRuntimeSettings(payload?.Settings);

            if (!bindingService.TryGetBinding(settings.Function, out var action))
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, "HoldMacroAction: no binding found for selected function");
                return;
            }

            var keyInfo = CommandTools.ConvertKeyString(action.Keyboard);
            if (string.IsNullOrWhiteSpace(keyInfo))
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"HoldMacroAction: selected function '{settings.Function}' has no keyboard/mouse token to send");
                return;
            }

            CancelAutoRelease(false);

            isKeyDown = true;
            activeKeyInfo = keyInfo;
            activeKeyHasMouseToken = ContainsMouseToken(keyInfo);

            Logger.Instance.LogMessage(TracingLevel.INFO, $"HoldMacroAction pressed: sending DOWN for '{settings.Function}' (holdUntilRelease={settings.HoldUntilRelease}, duration={settings.HoldDurationMs}ms)");

            StreamDeckCommon.SendKeypressDown(keyInfo);
            StartRepeat(keyInfo);
            _ = Connection.SetStateAsync(1);

            if (!settings.HoldUntilRelease)
            {
                var duration = Math.Max(0, Math.Min(settings.HoldDurationMs, MaxHoldDurationMs));
                if (duration > 0)
                {
                    ScheduleAutoRelease(keyInfo, duration);
                }
            }
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (!isKeyDown && autoReleaseToken == null)
            {
                return;
            }

            CancelAutoRelease(true);
            CancelRepeat();

            if (!string.IsNullOrWhiteSpace(activeKeyInfo))
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"HoldMacroAction released: sending UP for '{settings.Function}'");
                StreamDeckCommon.SendKeypressUp(activeKeyInfo);
            }

            isKeyDown = false;
            activeKeyInfo = null;

            _ = Connection.SetStateAsync(0);
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ClampHoldDuration();
            }
        }

        public override void Dispose()
        {
            CancelAutoRelease(false);
            CancelRepeat();
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            bindingService.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
        }

        private void ScheduleAutoRelease(string keyInfo, int duration)
        {
            CancelAutoRelease(false);
            autoReleaseToken = new CancellationTokenSource();
            var token = autoReleaseToken.Token;

            Logger.Instance.LogMessage(TracingLevel.INFO, $"HoldMacroAction: scheduling auto-release in {duration}ms for '{settings.Function}'");

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(duration, token);

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    Logger.Instance.LogMessage(TracingLevel.INFO, $"HoldMacroAction: auto-releasing '{settings.Function}' after {duration}ms");
                    CancelRepeat();
                    StreamDeckCommon.SendKeypressUp(keyInfo);
                }
                catch (TaskCanceledException)
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, "HoldMacroAction: auto-release cancelled before completion");
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"HoldMacroAction: error during auto-release: {ex}");
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
            if (autoReleaseToken == null)
            {
                return;
            }

            if (!autoReleaseToken.IsCancellationRequested)
            {
                autoReleaseToken.Cancel();

                if (logCancellation)
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, "HoldMacroAction: cancelled scheduled auto-release due to button release");
                }
            }

            DisposeAutoReleaseToken();
        }

        private void DisposeAutoReleaseToken()
        {
            autoReleaseToken?.Dispose();
            autoReleaseToken = null;
        }

        private void StartRepeat(string keyInfo)
        {
            if (string.IsNullOrWhiteSpace(keyInfo) || ContainsMouseToken(keyInfo))
            {
                return;
            }

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
                catch (TaskCanceledException)
                {
                    // Expected when the button is released or the repeat is cancelled.
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"HoldMacroAction: repeat loop failed: {ex}");
                }
            }, token);
        }

        private void CancelRepeat()
        {
            if (repeatToken == null)
            {
                return;
            }

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
                if (MouseTokenHelper.TryNormalize(token, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private void ClampHoldDuration()
        {
            settings.HoldDurationMs = Math.Max(0, Math.Min(settings.HoldDurationMs, MaxHoldDurationMs));
        }

        private void RefreshRuntimeSettings(JObject settingsObj)
        {
            if (settingsObj == null)
            {
                return;
            }

            if (settingsObj.TryGetValue("holdDurationMs", out var durationToken) &&
                int.TryParse(durationToken.ToString(), out var parsedDuration))
            {
                settings.HoldDurationMs = Math.Max(0, Math.Min(parsedDuration, MaxHoldDurationMs));
            }

            if (settingsObj.TryGetValue("holdUntilRelease", out var holdUntilReleaseToken) &&
                bool.TryParse(holdUntilReleaseToken.ToString(), out var holdUntilRelease))
            {
                settings.HoldUntilRelease = holdUntilRelease;
            }
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
    }
}
