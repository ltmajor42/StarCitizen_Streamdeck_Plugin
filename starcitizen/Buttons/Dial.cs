// File: starcitizen/Buttons/Dial.cs

using System;
using System.Threading;
using System.Threading.Tasks;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Payloads;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using starcitizen.Core;

namespace starcitizen.Buttons
{
    [PluginActionId("com.ltmajor42.starcitizen.dial")]
    public class Dial : StarCitizenDialBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    FunctionCw = string.Empty,
                    FunctionCcw = string.Empty,
                    FunctionPress = string.Empty,
                    FunctionTouchLongPress = string.Empty,
                    FunctionTouchPress = string.Empty,
                    Delay = string.Empty
                };
            }

            [JsonProperty(PropertyName = "functioncw")]
            public string FunctionCw { get; set; }

            [JsonProperty(PropertyName = "functionccw")]
            public string FunctionCcw { get; set; }

            [JsonProperty(PropertyName = "delay")]
            public string Delay { get; set; }

            [JsonProperty(PropertyName = "functionpress")]
            public string FunctionPress { get; set; }

            [JsonProperty(PropertyName = "functiontouchpress")]
            public string FunctionTouchPress { get; set; }

            [JsonProperty(PropertyName = "functiontouchlongpress")]
            public string FunctionTouchLongPress { get; set; }
        }

        private PluginSettings settings;
        private int? _delay = null;

        private readonly KeyBindingService bindingService = KeyBindingService.Instance;

        private CancellationTokenSource cancellationTokenSource;

        // Per-tick queue/worker (pulses keypress for each tick)
        private readonly SemaphoreSlim dialQueueSignal = new SemaphoreSlim(0, int.MaxValue);
        private Task dialQueueWorkerTask;

        private int cwQueuedTicks;
        private int ccwQueuedTicks;

        public Dial(SDConnection connection, InitialPayload payload) : base(connection, payload)
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

            cancellationTokenSource = new CancellationTokenSource();

            dialQueueWorkerTask = Task.Run(() => DialQueueWorkerAsync(cancellationTokenSource.Token));

            Connection.OnPropertyInspectorDidAppear += Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += Connection_OnSendToPlugin;
            bindingService.KeyBindingsLoaded += OnKeyBindingsLoaded;

            UpdatePropertyInspector();
        }

        public override void Dispose()
        {
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();

                // Unblock worker if waiting
                try { dialQueueSignal.Release(); } catch { /* ignored */ }

                try { dialQueueWorkerTask?.Wait(250); } catch { /* ignored */ }

                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }

            dialQueueSignal.Dispose();

            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            bindingService.KeyBindingsLoaded -= OnKeyBindingsLoaded;

            base.Dispose();
        }

        public override void TouchPress(TouchpadPressPayload payload)
        {
            if (bindingService.Reader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            var function = payload.IsLongPress ? settings.FunctionTouchLongPress : settings.FunctionTouchPress;

            if (bindingService.TryGetBinding(function, out var action))
            {
                var key = CommandTools.ConvertKeyString(action.Keyboard);
                StreamDeckCommon.SendKeypress(key, GetPressDurationMs());
            }
        }

        public override void DialDown(DialPayload payload)
        {
            if (bindingService.Reader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            if (bindingService.TryGetBinding(settings.FunctionPress, out var action))
            {
                var key = CommandTools.ConvertKeyString(action.Keyboard);
                StreamDeckCommon.SendKeypressDown(key);
            }
        }

        public override void DialUp(DialPayload payload)
        {
            if (bindingService.Reader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            if (bindingService.TryGetBinding(settings.FunctionPress, out var action))
            {
                var key = CommandTools.ConvertKeyString(action.Keyboard);
                StreamDeckCommon.SendKeypressUp(key);
            }
        }

        public override void DialRotate(DialRotatePayload payload)
        {
            if (bindingService.Reader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            var ticks = payload.Ticks;
            if (ticks == 0)
            {
                return;
            }

            // If user reverses direction quickly, dropping stale queued ticks feels much more responsive.
            if (ticks > 0)
            {
                Interlocked.Exchange(ref ccwQueuedTicks, 0);
                Interlocked.Add(ref cwQueuedTicks, ticks);
                for (var i = 0; i < ticks; i++)
                {
                    dialQueueSignal.Release();
                }
            }
            else
            {
                var abs = -ticks;
                Interlocked.Exchange(ref cwQueuedTicks, 0);
                Interlocked.Add(ref ccwQueuedTicks, abs);
                for (var i = 0; i < abs; i++)
                {
                    dialQueueSignal.Release();
                }
            }
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            BarRaider.SdTools.Tools.AutoPopulateSettings(settings, payload.Settings);
            HandleFileNames();
        }

        private void HandleFileNames()
        {
            _delay = null;

            if (!string.IsNullOrEmpty(settings.Delay))
            {
                if (int.TryParse(settings.Delay, out var delay) && delay > 0)
                {
                    _delay = delay;
                }
            }

            Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
        }

        private async Task DialQueueWorkerAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await dialQueueSignal.WaitAsync(token).ConfigureAwait(false);

                    // Drain quickly (keeps fast spins feeling snappy)
                    while (!token.IsCancellationRequested)
                    {
                        if (TryDequeueTick(out var clockwise))
                        {
                            SendDialTick(clockwise);
                            continue;
                        }

                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"DialQueueWorker crashed: {ex}");
            }
        }

        private bool TryDequeueTick(out bool clockwise)
        {
            // Prefer the direction with pending ticks; order matters for feel.
            if (Volatile.Read(ref cwQueuedTicks) > 0)
            {
                Interlocked.Decrement(ref cwQueuedTicks);
                clockwise = true;
                return true;
            }

            if (Volatile.Read(ref ccwQueuedTicks) > 0)
            {
                Interlocked.Decrement(ref ccwQueuedTicks);
                clockwise = false;
                return true;
            }

            clockwise = true;
            return false;
        }

        private void SendDialTick(bool clockwise)
        {
            var function = clockwise ? settings.FunctionCw : settings.FunctionCcw;

            if (bindingService.TryGetBinding(function, out var action))
            {
                var key = CommandTools.ConvertKeyString(action.Keyboard);
                StreamDeckCommon.SendKeypress(key, GetPressDurationMs());
            }
        }

        private int GetPressDurationMs()
        {
            // Default lower than your previous 40ms to improve “fluid” feel.
            var ms = _delay ?? 20;

            // Too low can be missed by some games; too high feels sluggish.
            if (ms < 5) ms = 5;
            if (ms > 200) ms = 200;

            return ms;
        }

        private void Connection_OnPropertyInspectorDidAppear(object sender, EventArgs e)
        {
            UpdatePropertyInspector();
        }

        private void Connection_OnSendToPlugin(object sender, EventArgs e)
        {
            try
            {
                var payload = e.ExtractPayload();
                if (payload != null && payload.TryGetValue("piEvent", out var piEvent) &&
                    piEvent?.ToString() == "refreshKeybinds")
                {
                    bindingService.QueueReload();
                    return;
                }

                if (payload?["property_inspector"]?.ToString() == "propertyInspectorConnected")
                {
                    UpdatePropertyInspector();
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"Failed processing PI payload: {ex.Message}");
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
