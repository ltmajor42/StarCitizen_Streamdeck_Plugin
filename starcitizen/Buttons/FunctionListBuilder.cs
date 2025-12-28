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
        private static readonly InputBindingResolver BindingResolver = new InputBindingResolver();
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

                    foreach (var action in group.OrderBy(x => x.MapUICategory).ThenBy(x => x.UILabel))
                    {
                        var resolved = BindingResolver.Resolve(action, culture);
                        if (resolved.BindingType == InputBindingType.None)
                        {
                            continue;
                        }
                        string primaryBinding = resolved.BindingDisplay ?? "";
                        string bindingType = resolved.BindingType.ToString().ToLowerInvariant();

                        string bindingDisplay = string.IsNullOrWhiteSpace(primaryBinding) ? "" : $" [{primaryBinding}]";
                        string overruleIndicator = action.KeyboardOverRule || action.MouseOverRule ? " *" : "";

                        ((JArray)groupObj["options"]).Add(new JObject
                        {
                            ["value"] = action.Name,
                            ["text"] = $"{action.UILabel}{bindingDisplay}{overruleIndicator}",
                            ["bindingType"] = bindingType,
                            ["searchText"] =
                                $"{action.UILabel.ToLower()} " +
                                $"{action.UIDescription?.ToLower() ?? ""} " +
                                $"{primaryBinding.ToLower()}"
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
