using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BarRaider.SdTools;
using starcitizen.Core;

namespace starcitizen.Buttons
{
    internal sealed class JoystickInputSender : IDisposable
    {
        private const uint DefaultDeviceId = 1;

        private static readonly Lazy<JoystickInputSender> LazyInstance =
            new Lazy<JoystickInputSender>(() => new JoystickInputSender());

        private readonly object sync = new object();
        private bool availabilityChecked;
        private bool available;
        private uint activeDeviceId = DefaultDeviceId;
        private bool disposed;

        private JoystickInputSender()
        {
        }

        public static JoystickInputSender Instance => LazyInstance.Value;

        public bool IsAvailable
        {
            get
            {
                EnsureAvailability();
                return available;
            }
        }

        public bool TrySendButtonPulse(JoystickBinding binding, int durationMs, string functionName)
        {
            if (!EnsureDevice(binding, functionName))
            {
                return false;
            }

            if (!TrySendButtonDown(binding))
            {
                return false;
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(Math.Max(1, durationMs));
                TrySendButtonUp(binding);
            });

            return true;
        }

        public bool TrySendButtonDown(JoystickBinding binding)
        {
            if (!EnsureDevice(binding, binding?.RawValue))
            {
                return false;
            }

            return SetBtn(true, activeDeviceId, (uint)(binding.ButtonNumber ?? 1));
        }

        public bool TrySendButtonUp(JoystickBinding binding)
        {
            if (!EnsureDevice(binding, binding?.RawValue))
            {
                return false;
            }

            return SetBtn(false, activeDeviceId, (uint)(binding.ButtonNumber ?? 1));
        }

        private bool EnsureDevice(JoystickBinding binding, string context)
        {
            if (binding == null)
            {
                PluginLog.Warn($"Joystick binding was null for '{context}', skipping send.");
                return false;
            }

            if (binding.Kind != JoystickBindingKind.Button || !binding.ButtonNumber.HasValue)
            {
                PluginLog.Warn($"Joystick binding '{binding.RawValue}' is not a simple button. Emulation supports buttons only.");
                return false;
            }

            if (!EnsureAvailability())
            {
                return false;
            }

            var targetDeviceId = binding.DeviceId ?? DefaultDeviceId;

            lock (sync)
            {
                if (activeDeviceId != targetDeviceId)
                {
                    ReleaseDevice(activeDeviceId);
                    activeDeviceId = targetDeviceId;
                }

                var status = GetVJDStatus(activeDeviceId);
                if (status == VjdStat.VJD_STAT_OWN || status == VjdStat.VJD_STAT_FREE)
                {
                    if (AcquireVJD(activeDeviceId))
                    {
                        return true;
                    }

                    PluginLog.Warn($"Failed to acquire virtual joystick {activeDeviceId} for '{context}'. Status: {status}.");
                    return false;
                }

                PluginLog.Warn($"Virtual joystick {activeDeviceId} not ready (status {status}); skipping '{context}'.");
                return false;
            }
        }

        private bool EnsureAvailability()
        {
            if (availabilityChecked)
            {
                return available;
            }

            availabilityChecked = true;

            try
            {
                available = vJoyEnabled();
                if (!available)
                {
                    PluginLog.Warn("vJoy driver not detected. Joystick emulation is disabled.");
                }
            }
            catch (DllNotFoundException)
            {
                available = false;
                PluginLog.Warn("vJoyInterface.dll not found. Install vJoy to enable joystick emulation.");
            }
            catch (Exception ex)
            {
                available = false;
                PluginLog.Warn($"Unable to initialize joystick emulation: {ex.Message}");
            }

            return available;
        }

        private void ReleaseDevice(uint deviceId)
        {
            try
            {
                RelinquishVJD(deviceId);
            }
            catch
            {
                // Ignore release failures
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            ReleaseDevice(activeDeviceId);
        }

        private enum VjdStat
        {
            VJD_STAT_OWN = 0,
            VJD_STAT_FREE = 1,
            VJD_STAT_BUSY = 2,
            VJD_STAT_MISS = 3,
            VJD_STAT_UNKN = 4
        }

        [DllImport("vJoyInterface.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool vJoyEnabled();

        [DllImport("vJoyInterface.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern VjdStat GetVJDStatus(uint rId);

        [DllImport("vJoyInterface.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool AcquireVJD(uint rId);

        [DllImport("vJoyInterface.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void RelinquishVJD(uint rId);

        [DllImport("vJoyInterface.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SetBtn(bool value, uint rId, uint nBtn);
    }
}
