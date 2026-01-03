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

                // Diagnostic: collect stats to help debug missing mouse bindings
                var allActionsList = bindingService.Reader.GetAllActions().Values.ToList();
                var totalActions = allActionsList.Count;
                var hasKeyboardCount = allActionsList.Count(a => !string.IsNullOrWhiteSpace(a.Keyboard));
                var hasMouseCount = allActionsList.Count(a => !string.IsNullOrWhiteSpace(a.Mouse));
                var mouseLikeInKeyboard = allActionsList.Count(a => !string.IsNullOrWhiteSpace(a.Keyboard) && MouseTokenHelper.IsMouseLike(a.Keyboard));
                var executableCount = allActionsList.Count(a => !string.IsNullOrWhiteSpace(a.Keyboard) && IsExecutableKeyboardBinding(a.Keyboard));
                PluginLog.Debug($"FunctionListBuilder: totalActions={totalActions}, hasKeyboard={hasKeyboardCount}, hasMouse={hasMouseCount}, mouseLikeInKeyboard={mouseLikeInKeyboard}, executable={executableCount}");

                // Get all actions grouped by MapUILabel (fallback to MapName if missing)
                var allActions = allActionsList
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.MapUILabel) ? (x.MapName ?? "Other") : x.MapUILabel);

                foreach (var group in allActions)
                {
                    var groupObj = BuildFullActionGroup(group, culture, includeUnboundActions);
                    if (((JArray)groupObj["options"]).Count > 0)
                    {
                        result.Add(groupObj);
                    }
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

        // New: Build group with both bound and unbound actions
        private static JObject BuildFullActionGroup(IGrouping<string, DProfileReader.Action> group, CultureInfo culture, bool includeUnboundActions)
        {
            var duplicateKeys = ComputeDuplicateKeys(group, culture.Name);
            var seenOptionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var groupObj = new JObject
            {
                ["label"] = string.IsNullOrWhiteSpace(group.Key) ? "Other" : group.Key,
                ["options"] = new JArray()
            };

            foreach (var action in group.OrderBy(x => x.MapUICategory).ThenBy(x => x.UILabel ?? x.Name))
            {
                // Use fallback for label
                var label = string.IsNullOrWhiteSpace(action.UILabel) ? action.Name : action.UILabel;

                // Bound action
                if (!string.IsNullOrWhiteSpace(action.Keyboard) && IsExecutableKeyboardBinding(action.Keyboard))
                {
                    var bindingInfo = GetBindingInfo(action.Keyboard, culture.Name);
                    var optionKey = $"{label}||{bindingInfo.PrimaryBinding}||{bindingInfo.BindingType}";
                    if (seenOptionKeys.Contains(optionKey)) continue;
                    seenOptionKeys.Add(optionKey);
                    var optionText = FormatOptionTextWithFallback(action, bindingInfo, duplicateKeys.Contains(optionKey));
                    ((JArray)groupObj["options"]).Add(new JObject
                    {
                        ["value"] = action.Name,
                        ["text"] = optionText,
                        ["bindingType"] = bindingInfo.BindingType,
                        ["searchText"] = BuildSearchTextWithFallback(action, bindingInfo.PrimaryBinding)
                    });
                }
                // Unbound action
                else if (includeUnboundActions)
                {
                    var optionKey = $"{label}||unbound";
                    if (seenOptionKeys.Contains(optionKey)) continue;
                    seenOptionKeys.Add(optionKey);
                    ((JArray)groupObj["options"]).Add(new JObject
                    {
                        ["value"] = action.Name,
                        ["text"] = $"{label} (unbound)",
                        ["bindingType"] = "unbound",
                        ["searchText"] = $"{label.ToLower()} {action.UIDescription?.ToLower() ?? ""}"
                    });
                }
            }

            return groupObj;
        }

        // Fallback for option text
        private static string FormatOptionTextWithFallback(DProfileReader.Action action, (string PrimaryBinding, string BindingType) bindingInfo, bool isDuplicate)
        {
            var label = string.IsNullOrWhiteSpace(action.UILabel) ? action.Name : action.UILabel;
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

            return $"{label}{bindingDisplay}{overruleIndicator}{uniqueSuffix}";
        }

        // Fallback for search text
        private static string BuildSearchTextWithFallback(DProfileReader.Action action, string primaryBinding)
        {
            var label = string.IsNullOrWhiteSpace(action.UILabel) ? action.Name : action.UILabel;
            return $"{label.ToLower()} {action.UIDescription?.ToLower() ?? ""} {primaryBinding.ToLower()} {action.Name.ToLower()} {action.MapName.ToLower()}";
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

        // Helper: Split tokens on both '+' and 'and' (case-insensitive, with/without spaces)
        private static string[] SplitBindingTokens(string binding)
        {
            if (string.IsNullOrWhiteSpace(binding)) return [];
            // Split on + or 'and' (with optional spaces and parentheses)
            return System.Text.RegularExpressions.Regex.Split(binding, @"\s*(\+|and)\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Where(t => !string.IsNullOrWhiteSpace(t) && !string.Equals(t, "+", StringComparison.OrdinalIgnoreCase) && !string.Equals(t, "and", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static (string PrimaryBinding, string BindingType) GetBindingInfo(string keyboard, string cultureName)
        {
            if (string.IsNullOrWhiteSpace(keyboard))
            {
                return (string.Empty, "keyboard");
            }

            // Build display string by processing each token
            var tokens = SplitBindingTokens(keyboard);
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

            var tokens = SplitBindingTokens(keyboard);
            if (tokens.Length <= 0) return false;

            // Accept if ALL tokens are valid keyboard OR valid mouse tokens
            foreach (var rawToken in tokens)
            {
                var token = rawToken?.Trim();
                if (string.IsNullOrWhiteSpace(token)) continue;

                // Accept if token is a valid mouse token
                if (MouseTokenHelper.TryNormalize(token, out _))
                {
                    continue;
                }

                // Accept if token is a valid keyboard command
                if (CommandTools.TryFromSCKeyboardCmd(token, out _))
                {
                    continue;
                }

                // If neither, it's not executable
                return false;
            }

            return true;
        }

        private static string DescribeUnknownTokens(string keyboard)
        {
            if (string.IsNullOrWhiteSpace(keyboard)) return "";

            var tokens = SplitBindingTokens(keyboard);
            var unknownTokens = new List<string>();

            foreach (var rawToken in tokens)
            {
                var token = rawToken?.Trim();
                if (string.IsNullOrWhiteSpace(token)) continue;

                // Mouse tokens are ignored here - they are either normalized or considered valid by default
                if (MouseTokenHelper.IsMouseLike(token)) continue;

                // Unknown keyboard command - add to list
                if (!CommandTools.TryFromSCKeyboardCmd(token, out _))
                {
                    unknownTokens.Add(token);
                }
            }

            // Join unknown tokens with commas for display
            return string.Join(", ", unknownTokens);
        }
    }
}
