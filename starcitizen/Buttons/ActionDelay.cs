// File: Buttons/ActionDelay.cs

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
    /// Action Delay button - delayed execution with cancel window.
    /// Tap to start countdown (blinking), tap again to cancel, executes after delay.
    /// </summary>
    /// <remarks>
    /// Use this for safer actions that need confirmation time, like ejection
    /// or self-destruct. The blinking provides visual feedback during countdown.
    /// </remarks>
    [PluginActionId("com.ltmajor42.starcitizen.actiondelay")]
    public class ActionDelay : StarCitizenKeypadBase
    {
        #region Enums

        private enum DelayState
        {
            Idle,
            Pending,
            Confirm
        }

        #endregion

        #region Settings

        /// <summary>
        /// Settings for ActionDelay with timing and blink configuration.
        /// Stored as strings to handle empty values from PI gracefully.
        /// </summary>
        protected class PluginSettings : PluginSettingsBase
        {
            public static PluginSettings CreateDefaultSettings() => new PluginSettings();

            [JsonProperty(PropertyName = "executionDelayMs")]
            public string ExecutionDelayMs { get; set; } = "800";

            [JsonProperty(PropertyName = "confirmationDurationMs")]
            public string ConfirmationDurationMs { get; set; } = "500";

            [JsonProperty(PropertyName = "blinkRateMs")]
            public string BlinkRateMs { get; set; } = "300";

            [JsonProperty(PropertyName = "holdToCancel")]
            public bool HoldToCancel { get; set; } = true;
        }

        #endregion

        #region Constants

        private const int MinExecutionDelay = 100;
        private const int MaxExecutionDelay = 5000;
        private const int DefaultExecutionDelay = 800;
        private const int MinConfirmationDuration = 100;
        private const int MaxConfirmationDuration = 3000;
        private const int DefaultConfirmationDuration = 500;
        private const int MinBlinkRate = 100;
        private const int MaxBlinkRate = 1000;
        private const int DefaultBlinkRate = 300;
        private const int DefaultKeypressDelay = 40;

        #endregion

        #region State

        private readonly object stateLock = new();
        private PluginSettings settings;
        private DelayState currentState = DelayState.Idle;
        private CancellationTokenSource pendingCts;
        private CancellationTokenSource confirmCts;
        private Timer blinkTimer;
        private int blinkGuard;
        private uint blinkState;

        // Parsed values
        private int executionDelayMs = DefaultExecutionDelay;
        private int confirmationDurationMs = DefaultConfirmationDuration;
        private int blinkRateMs = DefaultBlinkRate;

        #endregion

        #region Initialization

        public ActionDelay(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            settings = PluginSettings.CreateDefaultSettings();

            if (payload.Settings != null && payload.Settings.Count > 0)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseAndClampSettings();
                LoadClickSoundFromSettings(settings);
            }
            else
            {
                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
            }

            WirePropertyInspectorEvents();
            SendFunctionsToPropertyInspector();
        }

        #endregion

        #region Key Events

        public override void KeyPressed(KeyPayload payload)
        {
            // Intentionally empty: We use KeyReleased for "tap once to start, tap again to cancel".
        }

        public override void KeyReleased(KeyPayload payload)
        {
            DelayState state;
            lock (stateLock)
            {
                state = currentState;
            }

            if (state == DelayState.Pending)
            {
                if (settings.HoldToCancel)
                {
                    ResetToIdle();
                }
                return;
            }

            if (state != DelayState.Idle) return;

            if (!EnsureBindingsReady()) return;

            if (payload?.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseAndClampSettings();
            }

            if (string.IsNullOrWhiteSpace(settings.Function)) return;

            BeginPending();
        }

        #endregion

        #region Pending State Management

        private void BeginPending()
        {
            lock (stateLock)
            {
                currentState = DelayState.Pending;
            }

            blinkState = 0u;
            _ = Connection.SetStateAsync(0u);

            StartBlinking();
            StartPendingDelay();
        }

        private void StartPendingDelay()
        {
            CancelPendingDelay();

            pendingCts = new CancellationTokenSource();
            var token = pendingCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(executionDelayMs, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested) return;

                await ExecuteNowAsync();
            });
        }

        private async Task ExecuteNowAsync()
        {
            StopBlinking();

            lock (stateLock)
            {
                if (currentState != DelayState.Pending) return;
            }

            if (!EnsureBindingsReady())
            {
                ResetToIdle();
                return;
            }

            if (!TryGetKeyBinding(settings.Function, out var keyInfo))
            {
                ResetToIdle();
                return;
            }

            try
            {
                StreamDeckCommon.SendKeypress(keyInfo, DefaultKeypressDelay);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Delayed keypress failed: {ex}");
                ResetToIdle();
                return;
            }

            PlayClickSound();

            lock (stateLock)
            {
                currentState = DelayState.Confirm;
            }

            await Connection.SetStateAsync(1u);
            StartConfirmTimer();
        }

        private void StartConfirmTimer()
        {
            CancelConfirmTimer();

            confirmCts = new CancellationTokenSource();
            var token = confirmCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(confirmationDurationMs, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested) return;

                ResetToIdle();
            });
        }

        #endregion

        #region Blinking

        private void StartBlinking()
        {
            StopBlinking();
            blinkGuard = 0;

            blinkTimer = new Timer(_ =>
            {
                if (Interlocked.Exchange(ref blinkGuard, 1) == 1) return;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        lock (stateLock)
                        {
                            if (currentState != DelayState.Pending) return;
                        }

                        blinkState = blinkState == 0u ? 1u : 0u;
                        await Connection.SetStateAsync(blinkState);
                    }
                    catch { /* ignore transient SD connection errors */ }
                    finally
                    {
                        Interlocked.Exchange(ref blinkGuard, 0);
                    }
                });
            }, null, 0, blinkRateMs);
        }

        private void StopBlinking()
        {
            blinkTimer?.Dispose();
            blinkTimer = null;
            blinkGuard = 0;

            lock (stateLock)
            {
                if (currentState == DelayState.Pending)
                {
                    blinkState = 0u;
                    _ = Connection.SetStateAsync(0u);
                }
            }
        }

        #endregion

        #region Reset and Cleanup

        private void ResetToIdle(bool resetState = true)
        {
            CancelPendingDelay();
            CancelConfirmTimer();

            blinkTimer?.Dispose();
            blinkTimer = null;
            blinkGuard = 0;
            blinkState = 0u;

            lock (stateLock)
            {
                currentState = DelayState.Idle;
            }

            if (resetState)
            {
                _ = Connection.SetStateAsync(0u);
            }
        }

        private void CancelPendingDelay()
        {
            if (pendingCts == null) return;

            try { pendingCts.Cancel(); } catch { }
            finally
            {
                pendingCts.Dispose();
                pendingCts = null;
            }
        }

        private void CancelConfirmTimer()
        {
            if (confirmCts == null) return;

            try { confirmCts.Cancel(); } catch { }
            finally
            {
                confirmCts.Dispose();
                confirmCts = null;
            }
        }

        #endregion

        #region Settings Management

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseAndClampSettings();
                LoadClickSoundFromSettings(settings);
            }

            ResetToIdle();
        }

        private void ParseAndClampSettings()
        {
            // Parse execution delay
            if (!string.IsNullOrWhiteSpace(settings.ExecutionDelayMs) &&
                int.TryParse(settings.ExecutionDelayMs, out var parsedExec))
            {
                executionDelayMs = Math.Clamp(parsedExec, MinExecutionDelay, MaxExecutionDelay);
            }
            else
            {
                executionDelayMs = DefaultExecutionDelay;
            }

            // Parse confirmation duration
            if (!string.IsNullOrWhiteSpace(settings.ConfirmationDurationMs) &&
                int.TryParse(settings.ConfirmationDurationMs, out var parsedConfirm))
            {
                confirmationDurationMs = Math.Clamp(parsedConfirm, MinConfirmationDuration, MaxConfirmationDuration);
            }
            else
            {
                confirmationDurationMs = DefaultConfirmationDuration;
            }

            // Parse blink rate
            if (!string.IsNullOrWhiteSpace(settings.BlinkRateMs) &&
                int.TryParse(settings.BlinkRateMs, out var parsedBlink))
            {
                blinkRateMs = Math.Clamp(parsedBlink, MinBlinkRate, MaxBlinkRate);
            }
            else
            {
                blinkRateMs = DefaultBlinkRate;
            }
        }

        #endregion

        #region Disposal

        public override void Dispose()
        {
            ResetToIdle(false);
            base.Dispose();
        }

        #endregion
    }
}
