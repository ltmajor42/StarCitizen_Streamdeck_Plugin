using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using BarRaider.SdTools;
using starcitizen.Core;

namespace starcitizen.Buttons
{
    internal static class FunctionListBuilder
    {
        private static readonly object CacheLock = new object();
        private static int cachedVersion = -1;
        private static JArray cachedFunctions;

        public static JArray BuildFunctionsData(bool includeUnboundActions = true)
        {
            var result = new JArray();
            var bindingService = KeyBindingService.Instance;

            if (bindingService.Reader == null)
            {
                return result;
            }

            var bindingsVersion = bindingService.Version;

            try
            {
                lock (CacheLock)
                {
                    if (cachedFunctions != null && cachedVersion == bindingsVersion)
                    {
                        return (JArray)cachedFunctions.DeepClone();
                    }
                }

                var keyboard = KeyboardLayouts.GetThreadKeyboardLayout();
                CultureInfo culture;

                try
                {
                    culture = new CultureInfo(keyboard.KeyboardId);
                }
                catch
                {
                    culture = new CultureInfo("en-US");
                }

                // IMPORTANT: this plugin action execution sends ONLY the Star Citizen *keyboard* binding.
                // Showing joystick/gamepad-only binds in the dropdown is misleading because they won't execute.
                // Also, we filter out bindings containing unknown tokens (those would otherwise fallback to Escape).
                var actions = bindingService.Reader.GetAllActions().Values
                    .Where(x => !string.IsNullOrWhiteSpace(x.Keyboard))
                    .Where(x => IsExecutableKeyboardBinding(x.Keyboard))
                    .OrderBy(x => x.MapUILabel)
                    .GroupBy(x => x.MapUILabel);

                foreach (var group in actions)
                {
                    var groupObj = new JObject
                    {
                        ["label"] = group.Key,
                        ["options"] = new JArray()
                    };

                    foreach (var action in group.OrderBy(x => x.MapUICategory).ThenBy(x => x.UILabel))
                    {
                        string primaryBinding = "";
                        string bindingType = "";

                        // Keyboard-only (see filter above)
                        var keyString = CommandTools.ConvertKeyStringToLocale(action.Keyboard, culture.Name);
                        primaryBinding = keyString
                            .Replace("Dik", "")
                            .Replace("}{", "+")
                            .Replace("}", "")
                            .Replace("{", "");
                        bindingType = "keyboard";

                        string bindingDisplay = string.IsNullOrWhiteSpace(primaryBinding) ? "" : $" [{primaryBinding}]";
                        string overruleIndicator = action.KeyboardOverRule || action.MouseOverRule ? " *" : "";
                        string uniqueSuffix = "";

                        if (duplicateKeys.Contains(new { action.UILabel, actionInfo.PrimaryBinding, actionInfo.BindingType }))
                        {
                            var actionName = action.Name?.StartsWith($"{action.MapName}-", StringComparison.OrdinalIgnoreCase) == true
                                ? action.Name.Substring(action.MapName.Length + 1)
                                : action.Name;
                            uniqueSuffix = $" ({action.MapName}:{actionName})";
                        }

                        ((JArray)groupObj["options"]).Add(new JObject
                        {
                            ["value"] = action.Name,
                            ["text"] = $"{action.UILabel}{bindingDisplay}{overruleIndicator}{uniqueSuffix}",
                            ["bindingType"] = bindingType,
                            ["searchText"] =
                                $"{action.UILabel.ToLower()} " +
                                $"{action.UIDescription?.ToLower() ?? ""} " +
                                $"{primaryBinding.ToLower()} " +
                                $"{action.Name.ToLower()} " +
                                $"{action.MapName.ToLower()}"
                        });
                    }

                    if (((JArray)groupObj["options"]).Count > 0)
                    {
                        result.Add(groupObj);
                    }
                }

                if (includeUnboundActions)
                {
                    var unboundActions = bindingService.Reader.GetUnboundActions();
                    if (unboundActions.Any())
                    {
                        var unboundGroup = new JObject
                        {
                            ["label"] = "Unbound Actions",
                            ["options"] = new JArray()
                        };

                        foreach (var action in unboundActions.OrderBy(x => x.Value.MapUILabel).ThenBy(x => x.Value.UILabel))
                        {
                            ((JArray)unboundGroup["options"]).Add(new JObject
                            {
                                ["value"] = action.Value.Name,
                                ["text"] = $"{action.Value.UILabel} (unbound)",
                                ["bindingType"] = "unbound",
                                ["searchText"] = $"{action.Value.UILabel.ToLower()} {action.Value.UIDescription?.ToLower() ?? ""}"
                            });
                        }

                        result.Add(unboundGroup);
                    }
                }

                // Surface bindings that exist but contain unknown/unsupported keyboard tokens
                var unknownBindings = bindingService.Reader.GetAllActions().Values
                    .Where(x => !string.IsNullOrWhiteSpace(x.Keyboard))
                    .Where(x => !IsExecutableKeyboardBinding(x.Keyboard))
                    .OrderBy(x => x.MapUILabel)
                    .GroupBy(x => x.MapUILabel);

                foreach (var group in unknownBindings)
                {
                    var groupObj = new JObject
                    {
                        ["label"] = $"{group.Key} (unknown tokens)",
                        ["options"] = new JArray()
                    };

                    foreach (var action in group.OrderBy(x => x.MapUICategory).ThenBy(x => x.UILabel))
                    {
                        var unknownTokens = DescribeUnknownTokens(action.Keyboard);
                        ((JArray)groupObj["options"]).Add(new JObject
                        {
                            ["value"] = action.Name,
                            ["text"] = $"{action.UILabel} [unknown: {unknownTokens}]",
                            ["bindingType"] = "unknown",
                            ["searchText"] =
                                $"{action.UILabel.ToLower()} " +
                                $"{action.UIDescription?.ToLower() ?? ""} " +
                                $"{unknownTokens.ToLower()}"
                        });
                    }

                    if (((JArray)groupObj["options"]).Count > 0)
                    {
                        result.Add(groupObj);
                    }
                }

                lock (CacheLock)
                {
                    cachedFunctions = (JArray)result.DeepClone();
                    cachedVersion = bindingsVersion;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, ex.ToString());
            }

            return result;
        }

        /// <summary>
        /// Returns true only if ALL tokens in a keyboard binding are recognized keyboard tokens.
        /// This prevents unknown tokens (which would otherwise fallback to Escape) from showing in the PI.
        /// </summary>
        private static bool IsExecutableKeyboardBinding(string keyboard)
        {
            if (string.IsNullOrWhiteSpace(keyboard))
            {
                return false;
            }

            var tokens = keyboard.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return false;
            }

            foreach (var raw in tokens)
            {
                var token = raw?.Trim();
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                // Hard exclude mouse tokens in a keyboard field.
                // The action execution path sends keyboard only; mouse tokens would not execute reliably.
                if (IsMouseToken(token))
                {
                    return false;
                }

                if (!CommandTools.TryFromSCKeyboardCmd(token, out _))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsMouseToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            var t = token.Trim().ToLowerInvariant();
            return t == "mouse1" || t == "mouse2" || t == "mouse3" || t == "mouse4" || t == "mouse5" ||
                   t == "mwheelup" || t == "mwheeldown" || t == "mwheelleft" || t == "mwheelright";
        }

        private static string DescribeUnknownTokens(string keyboard)
        {
            if (string.IsNullOrWhiteSpace(keyboard))
            {
                return "unknown";
            }

            var tokens = keyboard.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            var unknowns = tokens
                .Select(t => t?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Where(t => !IsMouseToken(t))
                .Where(t => !CommandTools.TryFromSCKeyboardCmd(t, out _))
                .Distinct()
                .ToArray();

            return unknowns.Length == 0 ? "unknown" : string.Join(", ", unknowns);
        }
    }
}
