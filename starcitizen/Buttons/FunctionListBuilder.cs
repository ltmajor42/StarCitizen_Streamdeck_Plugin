using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
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
                    // Compute duplicate keys using the same normalization that will be shown to the user
                    var duplicateKeyStrings = group
                        .Select(a =>
                        {
                            var bindingInfo = GetBindingInfo(a.Keyboard, culture.Name);
                            return $"{a.UILabel}||{bindingInfo.PrimaryBinding}||{bindingInfo.BindingType}";
                        })
                        .GroupBy(k => k)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key);

                    var duplicateKeys = new HashSet<string>(duplicateKeyStrings, StringComparer.OrdinalIgnoreCase);

                    var groupObj = new JObject
                    {
                        ["label"] = group.Key,
                        ["options"] = new JArray()
                    };

                    // Track seen option identity to avoid adding exact duplicates to the dropdown
                    var seenOptionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var action in group.OrderBy(x => x.MapUICategory).ThenBy(x => x.UILabel))
                    {
                        string primaryBinding = "";
                        string bindingType = "";

                        var bindingInfo = GetBindingInfo(action.Keyboard, culture.Name);
                        primaryBinding = bindingInfo.PrimaryBinding;
                        bindingType = bindingInfo.BindingType;

                        string bindingDisplay = string.IsNullOrWhiteSpace(primaryBinding) ? "" : $" [{primaryBinding}]";
                        string overruleIndicator = action.KeyboardOverRule || action.MouseOverRule ? " *" : "";
                        string uniqueSuffix = "";

                        var duplicateKeyObj = $"{action.UILabel}||{primaryBinding}||{bindingType}";

                        if (duplicateKeys.Contains(duplicateKeyObj))
                        {
                            var actionName = action.Name?.StartsWith($"{action.MapName}-", StringComparison.OrdinalIgnoreCase) == true
                                ? action.Name.Substring(action.MapName.Length + 1)
                                : action.Name;
                            uniqueSuffix = $" ({action.MapName}:{actionName})";
                        }

                        var optionText = $"{action.UILabel}{bindingDisplay}{overruleIndicator}{uniqueSuffix}";

                        // Build a stable key for de-duplication. If another action would produce the same
                        // displayed text and represent the same binding type and primary binding, skip adding it.
                        var optionKey = $"{action.UILabel}||{primaryBinding}||{bindingType}";
                        if (seenOptionKeys.Contains(optionKey))
                        {
                            // skip duplicate option to avoid duplicates in the PI dropdown
                            continue;
                        }

                        seenOptionKeys.Add(optionKey);

                        ((JArray)groupObj["options"]).Add(new JObject
                        {
                            ["value"] = action.Name,
                            ["text"] = optionText,
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
        /// Returns true only if ALL tokens in a keyboard binding are recognized keyboard tokens or mouse tokens.
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

            var foundValidToken = false;
            foreach (var raw in tokens)
            {
                var token = raw?.Trim();
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (IsMouseToken(token))
                {
                    foundValidToken = true;
                    continue;
                }

                if (!CommandTools.TryFromSCKeyboardCmd(token, out _))
                {
                    return false;
                }

                foundValidToken = true;
            }

            return foundValidToken;
        }

        private static (string PrimaryBinding, string BindingType) GetBindingInfo(string keyboard, string cultureName)
        {
            var keyString = CommandTools.ConvertKeyStringToLocale(keyboard, cultureName);
            var primaryBinding = keyString
                .Replace("Dik", "")
                .Replace("}{", "+")
                .Replace("}", "")
                .Replace("{", "");

            var bindingType = ContainsMouseToken(keyboard) ? "mouse" : "keyboard";

            return (primaryBinding, bindingType);
        }

        private static bool IsMouseToken(string token) =>
            MouseTokenHelper.TryNormalize(token, out _) || MouseTokenHelper.IsMouseLike(token);

        private static bool ContainsMouseToken(string binding)
        {
            if (string.IsNullOrWhiteSpace(binding))
            {
                return false;
            }

            var tokens = binding.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (IsMouseToken(token))
                {
                    return true;
                }
            }

            return false;
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
                .Where(t => !MouseTokenHelper.IsMouseLike(t))
                .Where(t => !CommandTools.TryFromSCKeyboardCmd(t, out _))
                .Distinct()
                .ToArray();

            return unknowns.Length == 0 ? "unknown" : string.Join(", ", unknowns);
        }
    }
}
