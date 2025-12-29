using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using BarRaider.SdTools;
using starcitizen.Core;
using SCJMapper_V2.SC;

namespace starcitizen.Buttons
{
    internal static class FunctionListBuilder
    {
        private static readonly object CacheLock = new object();
        private static int cachedVersion = -1;
        private static JArray cachedFunctions;

        private static bool TryGetPrimaryBinding(DProfileReader.Action action, CultureInfo culture, out string binding, out string bindingType)
        {
            binding = string.Empty;
            bindingType = string.Empty;

            if (!string.IsNullOrWhiteSpace(action.Keyboard))
            {
                var keyString = CommandTools.ConvertKeyStringToLocale(action.Keyboard, culture.Name);
                binding = keyString
                    .Replace("Dik", "")
                    .Replace("}{", "+")
                    .Replace("}", "")
                    .Replace("{", "");
                bindingType = "keyboard";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(action.Mouse))
            {
                binding = action.Mouse;
                bindingType = "mouse";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(action.Joystick))
            {
                binding = action.Joystick;
                bindingType = "joystick";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(action.Gamepad))
            {
                binding = action.Gamepad;
                bindingType = "gamepad";
                return true;
            }

            return false;
        }

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

                var actions = bindingService.Reader.GetAllActions().Values
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Keyboard) ||
                        !string.IsNullOrWhiteSpace(x.Mouse) ||
                        !string.IsNullOrWhiteSpace(x.Joystick) ||
                        !string.IsNullOrWhiteSpace(x.Gamepad))
                    .OrderBy(x => x.MapUILabel)
                    .GroupBy(x => x.MapUILabel);

                foreach (var group in actions)
                {
                    var groupObj = new JObject
                    {
                        ["label"] = group.Key,
                        ["options"] = new JArray()
                    };

                    var orderedGroup = group
                        .OrderByDescending(x => !string.IsNullOrWhiteSpace(x.Keyboard))
                        .ThenBy(x => x.MapUICategory)
                        .ThenBy(x => x.UILabel)
                        .ToList();

                    var actionWithBindings = orderedGroup
                        .Select(action =>
                        {
                            TryGetPrimaryBinding(action, culture, out var primaryBinding, out var bindingType);
                            return new
                            {
                                Action = action,
                                PrimaryBinding = primaryBinding,
                                BindingType = bindingType
                            };
                        })
                        .ToList();

                    var duplicateKeys = actionWithBindings
                        .GroupBy(x => new { x.Action.UILabel, x.PrimaryBinding, x.BindingType })
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key)
                        .ToHashSet();

                    foreach (var actionInfo in actionWithBindings)
                    {
                        var action = actionInfo.Action;
                        var primaryBinding = actionInfo.PrimaryBinding;
                        var bindingType = actionInfo.BindingType;

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
    }
}
