using System;
using System.Globalization;
using System.Text.RegularExpressions;
using BarRaider.SdTools;
using SCJMapper_V2.SC;
using starcitizen.Core;

namespace starcitizen.Buttons
{
    internal enum InputBindingType
    {
        None,
        Keyboard,
        Joystick,
        Mouse,
        Gamepad
    }

    internal enum JoystickBindingKind
    {
        Unknown,
        Button,
        Hat,
        Axis
    }

    internal sealed class JoystickBinding
    {
        public string RawValue { get; set; }
        public string DeviceInstanceName { get; set; }
        public uint? DeviceId { get; set; }
        public JoystickBindingKind Kind { get; set; }
        public int? ButtonNumber { get; set; }

        public string Describe()
        {
            if (Kind == JoystickBindingKind.Button && ButtonNumber.HasValue)
            {
                return $"{DeviceLabel()}Button {ButtonNumber.Value}";
            }

            return $"{DeviceLabel()}{RawValue}";
        }

        private string DeviceLabel()
        {
            if (!string.IsNullOrWhiteSpace(DeviceInstanceName))
            {
                return $"[{DeviceInstanceName}] ";
            }

            if (DeviceId.HasValue)
            {
                return $"[js{DeviceId.Value}] ";
            }

            return string.Empty;
        }

        public static bool TryParse(string raw, string deviceOverride, out JoystickBinding binding)
        {
            binding = new JoystickBinding
            {
                RawValue = raw?.Trim(),
            };

            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var cleaned = raw.Trim();
            var match = Regex.Match(cleaned, @"^(js(?<id>\d+)[_\-])?(?<control>.+)$", RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var idGroup = match.Groups["id"].Value;
                if (!string.IsNullOrWhiteSpace(idGroup) && uint.TryParse(idGroup, out var deviceId))
                {
                    binding.DeviceId = deviceId;
                }

                cleaned = match.Groups["control"].Value;
            }

            var deviceLabel = deviceOverride?.Trim();

            if (!string.IsNullOrWhiteSpace(deviceLabel))
            {
                if (uint.TryParse(deviceLabel, out var overrideId) && !binding.DeviceId.HasValue)
                {
                    binding.DeviceId = overrideId;
                }
                else
                {
                    binding.DeviceInstanceName = deviceLabel;
                }
            }

            var control = cleaned.ToLowerInvariant();

            var buttonMatch = Regex.Match(control, @"button[_\-]?(?<num>\d+)", RegexOptions.IgnoreCase);
            if (buttonMatch.Success && int.TryParse(buttonMatch.Groups["num"].Value, out var buttonNumber))
            {
                binding.Kind = JoystickBindingKind.Button;
                binding.ButtonNumber = buttonNumber;
                return true;
            }

            if (Regex.IsMatch(control, @"hat", RegexOptions.IgnoreCase))
            {
                binding.Kind = JoystickBindingKind.Hat;
                return true;
            }

            if (Regex.IsMatch(control, @"axis", RegexOptions.IgnoreCase))
            {
                binding.Kind = JoystickBindingKind.Axis;
                return true;
            }

            return false;
        }
    }

    internal sealed class ResolvedBinding
    {
        public static ResolvedBinding None { get; } = new ResolvedBinding { BindingType = InputBindingType.None };

        public InputBindingType BindingType { get; set; }
        public string BindingDisplay { get; set; }
        public string KeyboardBinding { get; set; }
        public string KeyboardMacro { get; set; }
        public string RawJoystick { get; set; }
        public JoystickBinding JoystickBinding { get; set; }
    }

    /// <summary>
    /// Chooses the best usable binding for an action. Preference order:
    /// keyboard, joystick, mouse, then gamepad.
    /// </summary>
    internal sealed class InputBindingResolver
    {
        public ResolvedBinding Resolve(DProfileReader.Action action, CultureInfo culture = null)
        {
            if (action == null)
            {
                return ResolvedBinding.None;
            }

            var hasJoystickOverride = !string.IsNullOrWhiteSpace(action.JoystickOverRule);
            var hasKeyboardOverride = action.KeyboardOverRule;
            var hasMouseOverride = action.MouseOverRule;

            if (hasJoystickOverride)
            {
                var joystickOverride = BuildJoystickBinding(action);
                if (joystickOverride != null)
                {
                    return joystickOverride;
                }
            }

            if (hasKeyboardOverride)
            {
                var keyboardOverride = BuildKeyboardBinding(action, culture);
                if (keyboardOverride != null)
                {
                    return keyboardOverride;
                }
            }

            if (hasMouseOverride)
            {
                var mouseOverride = BuildMouseBinding(action);
                if (mouseOverride != null)
                {
                    return mouseOverride;
                }
            }

            var keyboardBinding = BuildKeyboardBinding(action, culture);
            if (keyboardBinding != null)
            {
                return keyboardBinding;
            }

            var joystickBinding = BuildJoystickBinding(action);
            if (joystickBinding != null)
            {
                return joystickBinding;
            }

            if (!string.IsNullOrWhiteSpace(action.Mouse))
            {
                return BuildMouseBinding(action);
            }

            if (!string.IsNullOrWhiteSpace(action.Gamepad))
            {
                return BuildGamepadBinding(action);
            }

            return ResolvedBinding.None;
        }

        private ResolvedBinding BuildKeyboardBinding(DProfileReader.Action action, CultureInfo culture)
        {
            if (string.IsNullOrWhiteSpace(action.Keyboard))
            {
                return null;
            }

            var macro = CommandTools.ConvertKeyString(action.Keyboard);
            if (string.IsNullOrWhiteSpace(macro))
            {
                PluginLog.Warn($"Keyboard binding '{action.Keyboard}' could not be converted for action '{action.Name}'.");
                return null;
            }

            var display = macro;
            if (culture != null)
            {
                display = CommandTools.ConvertKeyStringToLocale(action.Keyboard, culture.Name)
                    .Replace("Dik", "")
                    .Replace("}{", "+")
                    .Replace("}", "")
                    .Replace("{", "");
            }

            return new ResolvedBinding
            {
                BindingType = InputBindingType.Keyboard,
                KeyboardBinding = action.Keyboard,
                KeyboardMacro = macro,
                BindingDisplay = display
            };
        }

        private ResolvedBinding BuildJoystickBinding(DProfileReader.Action action)
        {
            if (string.IsNullOrWhiteSpace(action.Joystick))
            {
                return null;
            }

            if (!JoystickBinding.TryParse(action.Joystick, action.JoystickOverRule, out var parsed))
            {
                PluginLog.Warn($"Unsupported joystick binding format '{action.Joystick}' for action '{action.Name}'.");
                return null;
            }

            return new ResolvedBinding
            {
                BindingType = InputBindingType.Joystick,
                RawJoystick = action.Joystick,
                JoystickBinding = parsed,
                BindingDisplay = parsed.Describe()
            };
        }

        private static ResolvedBinding BuildMouseBinding(DProfileReader.Action action)
        {
            if (string.IsNullOrWhiteSpace(action.Mouse))
            {
                return null;
            }

            return new ResolvedBinding
            {
                BindingType = InputBindingType.Mouse,
                BindingDisplay = action.Mouse
            };
        }

        private static ResolvedBinding BuildGamepadBinding(DProfileReader.Action action)
        {
            if (string.IsNullOrWhiteSpace(action.Gamepad))
            {
                return null;
            }

            return new ResolvedBinding
            {
                BindingType = InputBindingType.Gamepad,
                BindingDisplay = action.Gamepad
            };
        }
    }
}
