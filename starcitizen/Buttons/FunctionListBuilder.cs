using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using BarRaider.SdTools;
using starcitizen.Core;
using starcitizen.SC;

namespace starcitizen.Buttons
{
    /// <summary>
    /// Builds the function list data for Property Inspector dropdowns.
    /// Caches results by binding version to avoid rebuilding on every PI open.
    /// </summary>
    internal static class FunctionListBuilder
    {
        // ============================================================
        // REGION: Cache
        // ============================================================
        private static readonly object CacheLock = new();
        private static int cachedVersion = -1;
        private static JArray cachedFunctions;

        // Reusable separator array for string splitting
        private static readonly char[] PlusSeparator = ['+'];

        // ============================================================
        // REGION: Public API
        // ============================================================
        
        /// <summary>
        /// Builds the function list data structure for Property Inspector dropdowns.
        /// Results are cached by binding version.
        /// </summary>
        /// <param name="includeUnboundActions">Whether to include actions without bindings</param>
        /// <returns>JArray of function groups with options</returns>
        public static JArray BuildFunctionsData(bool includeUnboundActions = true)
        {
            var bindingService = KeyBindingService.Instance;
            if (bindingService.Reader == null) return [];

            var bindingsVersion = bindingService.Version;

            // Check cache
            lock (CacheLock)
            {
                if (cachedFunctions != null && cachedVersion == bindingsVersion)
                {
                    return (JArray)cachedFunctions.DeepClone();
                }
            }

            var result = BuildFunctionsDataCore(bindingService, includeUnboundActions);

            // Update cache
            lock (CacheLock)
            {
                cachedFunctions = (JArray)result.DeepClone();
                cachedVersion = bindingsVersion;
            }

            return result;
        }

        // ============================================================
        // REGION: Core Builder
        // ============================================================
        private static JArray BuildFunctionsDataCore(KeyBindingService bindingService, bool includeUnboundActions)
        {
            var result = new JArray();

            try
            {
                var culture = GetCurrentCulture();

                // Build main action groups (executable keyboard bindings)
                var executableActions = bindingService.Reader.GetAllActions().Values
                    .Where(x => !string.IsNullOrWhiteSpace(x.Keyboard))
                    .Where(x => IsExecutableKeyboardBinding(x.Keyboard))
                    .OrderBy(x => x.MapUILabel)
                    .GroupBy(x => x.MapUILabel);

                foreach (var group in executableActions)
                {
                    var groupObj = BuildActionGroup(group, culture);
                    if (((JArray)groupObj["options"]).Count > 0)
                    {
                        result.Add(groupObj);
                    }
                }

                // Add unbound actions group
                if (includeUnboundActions)
                {
                    var unboundGroup = BuildUnboundActionsGroup(bindingService);
                    if (unboundGroup != null) result.Add(unboundGroup);
                }

                // Add unknown token groups (for visibility)
                AddUnknownTokenGroups(result, bindingService);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, ex.ToString());
            }

            return result;
        }

        // ============================================================
        // REGION: Group Builders
        // ============================================================
        private static JObject BuildActionGroup(IGrouping<string, DProfileReader.Action> group, CultureInfo culture)
        {
            // Pre-compute duplicates for this group
            var duplicateKeys = ComputeDuplicateKeys(group, culture.Name);
            var seenOptionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var groupObj = new JObject
            {
                ["label"] = group.Key,
                ["options"] = new JArray()
            };

            foreach (var action in group.OrderBy(x => x.MapUICategory).ThenBy(x => x.UILabel))
            {
                var bindingInfo = GetBindingInfo(action.Keyboard, culture.Name);
                var optionKey = $"{action.UILabel}||{bindingInfo.PrimaryBinding}||{bindingInfo.BindingType}";

                // Skip duplicates
                if (seenOptionKeys.Contains(optionKey)) continue;
                seenOptionKeys.Add(optionKey);

                var optionText = FormatOptionText(action, bindingInfo, duplicateKeys.Contains(optionKey));

                ((JArray)groupObj["options"]).Add(new JObject
                {
                    ["value"] = action.Name,
                    ["text"] = optionText,
                    ["bindingType"] = bindingInfo.BindingType,
                    ["searchText"] = BuildSearchText(action, bindingInfo.PrimaryBinding)
                });
            }

            return groupObj;
        }

        private static JObject BuildUnboundActionsGroup(KeyBindingService bindingService)
        {
            var unboundActions = bindingService.Reader.GetUnboundActions();
            if (unboundActions.Count == 0) return null;

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

            return unboundGroup;
        }

        private static void AddUnknownTokenGroups(JArray result, KeyBindingService bindingService)
        {
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
                        ["searchText"] = $"{action.UILabel.ToLower()} {action.UIDescription?.ToLower() ?? ""} {unknownTokens.ToLower()}"
                    });
                }

                if (((JArray)groupObj["options"]).Count > 0)
                {
                    result.Add(groupObj);
                }
            }
        }

        // ============================================================
        // REGION: Helper Methods
        // ============================================================
        private static CultureInfo GetCurrentCulture()
        {
            try
            {
                var keyboard = KeyboardLayouts.GetThreadKeyboardLayout();
                return new CultureInfo(keyboard.KeyboardId);
            }
            catch
            {
                return new CultureInfo("en-US");
            }
        }

        private static HashSet<string> ComputeDuplicateKeys(IGrouping<string, DProfileReader.Action> group, string cultureName)
        {
            return new HashSet<string>(
                group.Select(a => 
                {
                    var (primaryBinding, bindingType) = GetBindingInfo(a.Keyboard, cultureName);
                    return $"{a.UILabel}||{primaryBinding}||{bindingType}";
                })
                .GroupBy(k => k)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);
        }

        private static string FormatOptionText(DProfileReader.Action action, (string PrimaryBinding, string BindingType) bindingInfo, bool isDuplicate)
        {
            var bindingDisplay = string.IsNullOrWhiteSpace(bindingInfo.PrimaryBinding) ? "" : $" [{bindingInfo.PrimaryBinding}]";
            var overruleIndicator = action.KeyboardOverRule || action.MouseOverRule ? " *" : "";
            var uniqueSuffix = "";

            if (isDuplicate)
            {
                var actionName = action.Name?.StartsWith($"{action.MapName}-", StringComparison.OrdinalIgnoreCase) == true
                    ? action.Name[(action.MapName.Length + 1)..]
                    : action.Name;
                uniqueSuffix = $" ({action.MapName}:{actionName})";
            }

            return $"{action.UILabel}{bindingDisplay}{overruleIndicator}{uniqueSuffix}";
        }

        private static string BuildSearchText(DProfileReader.Action action, string primaryBinding)
        {
            return $"{action.UILabel.ToLower()} {action.UIDescription?.ToLower() ?? ""} {primaryBinding.ToLower()} {action.Name.ToLower()} {action.MapName.ToLower()}";
        }

        private static (string PrimaryBinding, string BindingType) GetBindingInfo(string keyboard, string cultureName)
        {
            if (string.IsNullOrWhiteSpace(keyboard))
            {
                return (string.Empty, "keyboard");
            }

            // Build display string by processing each token
            var tokens = keyboard.Split(PlusSeparator, StringSplitOptions.RemoveEmptyEntries);
            var displayParts = new List<string>();
            var hasMouseToken = false;

            foreach (var rawToken in tokens)
            {
                var token = rawToken?.Trim();
                if (string.IsNullOrWhiteSpace(token)) continue;

                // Check for mouse tokens first
                if (MouseTokenHelper.TryNormalize(token, out var normalizedMouse))
                {
                    displayParts.Add(FormatMouseTokenForDisplay(normalizedMouse));
                    hasMouseToken = true;
                    continue;
                }

                if (MouseTokenHelper.IsMouseLike(token))
                {
                    // It's mouse-like but couldn't normalize - show as-is
                    displayParts.Add(token);
                    hasMouseToken = true;
                    continue;
                }

                // Try keyboard mapping
                if (CommandTools.TryFromSCKeyboardCmd(token, out var dikKey))
                {
                    displayParts.Add(FormatKeyForDisplay(dikKey, cultureName));
                }
                else
                {
                    // Unknown token - show as-is
                    displayParts.Add(token);
                }
            }

            var primaryBinding = string.Join("+", displayParts);
            var bindingType = hasMouseToken ? "mouse" : "keyboard";
            return (primaryBinding, bindingType);
        }

        private static string FormatMouseTokenForDisplay(string normalizedMouse)
        {
            return normalizedMouse switch
            {
                "mouse1" => "Mouse1",
                "mouse2" => "Mouse2",
                "mouse3" => "Mouse3",
                "mouse4" => "Mouse4",
                "mouse5" => "Mouse5",
                "mwheelup" => "WheelUp",
                "mwheeldown" => "WheelDown",
                "mwheelleft" => "WheelLeft",
                "mwheelright" => "WheelRight",
                "mwheel" => "Wheel",
                _ => normalizedMouse
            };
        }

        private static string FormatKeyForDisplay(WindowsInput.Native.DirectInputKeyCode dikKey, string cultureName)
        {
            var dikKeyOut = dikKey.ToString();

            // Remove "Dik" prefix for cleaner display
            if (dikKeyOut.StartsWith("Dik", StringComparison.OrdinalIgnoreCase))
            {
                dikKeyOut = dikKeyOut.Substring(3);
            }

            return dikKeyOut;
        }

        // ============================================================
        // REGION: Token Validation
        // ============================================================
        
        /// <summary>
        /// Returns true only if ALL tokens in a keyboard binding are recognized.
        /// Prevents unknown tokens (which would fallback to Escape) from showing.
        /// </summary>
        private static bool IsExecutableKeyboardBinding(string keyboard)
        {
            if (string.IsNullOrWhiteSpace(keyboard)) return false;

            var tokens = keyboard.Split(PlusSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return false;

            var foundValidToken = false;
            foreach (var raw in tokens)
            {
                var token = raw?.Trim();
                if (string.IsNullOrWhiteSpace(token)) continue;

                if (IsMouseToken(token))
                {
                    foundValidToken = true;
                    continue;
                }

                if (!CommandTools.TryFromSCKeyboardCmd(token, out _)) return false;
                foundValidToken = true;
            }

            return foundValidToken;
        }

        private static bool IsMouseToken(string token) =>
            MouseTokenHelper.TryNormalize(token, out _) || MouseTokenHelper.IsMouseLike(token);

        private static bool ContainsMouseToken(string binding)
        {
            if (string.IsNullOrWhiteSpace(binding)) return false;

            return binding.Split(PlusSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Any(IsMouseToken);
        }

        private static string DescribeUnknownTokens(string keyboard)
        {
            if (string.IsNullOrWhiteSpace(keyboard)) return "unknown";

            var unknowns = keyboard.Split(PlusSeparator, StringSplitOptions.RemoveEmptyEntries)
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
