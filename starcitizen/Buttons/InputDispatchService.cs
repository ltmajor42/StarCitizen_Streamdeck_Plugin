using BarRaider.SdTools;
using starcitizen.Core;

namespace starcitizen.Buttons
{
    internal static class InputDispatchService
    {
        private static readonly InputBindingResolver Resolver = new InputBindingResolver();
        private static readonly JoystickInputSender JoystickSender = JoystickInputSender.Instance;

        public static bool TryResolveBinding(KeyBindingService bindingService, string functionName, out ResolvedBinding binding)
        {
            binding = ResolvedBinding.None;

            if (bindingService == null || bindingService.Reader == null || string.IsNullOrWhiteSpace(functionName))
            {
                return false;
            }

            if (!bindingService.TryGetBinding(functionName, out var action))
            {
                return false;
            }

            binding = Resolver.Resolve(action);
            if (binding.BindingType == InputBindingType.None)
            {
                return false;
            }

            if (binding.BindingType == InputBindingType.Joystick &&
                (binding.JoystickBinding == null || binding.JoystickBinding.Kind != JoystickBindingKind.Button))
            {
                PluginLog.Warn($"Joystick binding '{binding.RawJoystick ?? binding.BindingDisplay}' for '{functionName}' is not a supported button mapping.");
                return false;
            }

            return true;
        }

        public static bool TrySendPress(ResolvedBinding binding, int durationMs, string contextName)
        {
            switch (binding.BindingType)
            {
                case InputBindingType.Keyboard:
                    return SendKeyboardPress(binding, durationMs);
                case InputBindingType.Joystick:
                    return JoystickSender.TrySendButtonPulse(binding.JoystickBinding, durationMs, contextName);
                default:
                    PluginLog.Warn($"No supported binding found for '{contextName}'.");
                    return false;
            }
        }

        public static bool TrySendDown(ResolvedBinding binding, string contextName)
        {
            switch (binding.BindingType)
            {
                case InputBindingType.Keyboard:
                    return SendKeyboardDown(binding);
                case InputBindingType.Joystick:
                    return JoystickSender.TrySendButtonDown(binding.JoystickBinding);
                default:
                    PluginLog.Warn($"No supported binding found for '{contextName}'.");
                    return false;
            }
        }

        public static bool TrySendUp(ResolvedBinding binding, string contextName)
        {
            switch (binding.BindingType)
            {
                case InputBindingType.Keyboard:
                    return SendKeyboardUp(binding);
                case InputBindingType.Joystick:
                    return JoystickSender.TrySendButtonUp(binding.JoystickBinding);
                default:
                    PluginLog.Warn($"No supported binding found for '{contextName}'.");
                    return false;
            }
        }

        private static bool SendKeyboardPress(ResolvedBinding binding, int durationMs)
        {
            if (string.IsNullOrWhiteSpace(binding.KeyboardMacro))
            {
                return false;
            }

            StreamDeckCommon.SendKeypress(binding.KeyboardMacro, durationMs);
            return true;
        }

        private static bool SendKeyboardDown(ResolvedBinding binding)
        {
            if (string.IsNullOrWhiteSpace(binding.KeyboardMacro))
            {
                return false;
            }

            StreamDeckCommon.SendKeypressDown(binding.KeyboardMacro);
            return true;
        }

        private static bool SendKeyboardUp(ResolvedBinding binding)
        {
            if (string.IsNullOrWhiteSpace(binding.KeyboardMacro))
            {
                return false;
            }

            StreamDeckCommon.SendKeypressUp(binding.KeyboardMacro);
            return true;
        }
    }
}
