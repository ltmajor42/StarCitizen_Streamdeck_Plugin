using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using starcitizen.Core;

namespace starcitizen
{
    internal static class MouseTokenHelper
    {
        private static readonly HashSet<string> LoggedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object LoggedTokensLock = new object();

        private static void LogDebugOnce(string key, string message)
        {
            // Only emit these debug messages when detailed diagnostics are explicitly enabled
            if (!SCJMapper_V2.SC.SCPath.DetailedInputDiagnostics) return;

            if (string.IsNullOrEmpty(key)) return;
            lock (LoggedTokensLock)
            {
                if (LoggedTokens.Contains(key)) return;
                LoggedTokens.Add(key);
            }
            try { PluginLog.Debug(message); } catch { }
        }

        private static readonly Dictionary<string, string> CanonicalTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mouse1"] = "mouse1",
            ["mousebutton1"] = "mouse1",
            ["mouse2"] = "mouse2",
            ["mousebutton2"] = "mouse2",
            ["mouse3"] = "mouse3",
            ["mousebutton3"] = "mouse3",
            ["mouse4"] = "mouse4",
            ["mousebutton4"] = "mouse4",
            ["mouse5"] = "mouse5",
            ["mousebutton5"] = "mouse5",
            ["mwheelup"] = "mwheelup",
            ["mousewheelup"] = "mwheelup",
            ["wheelup"] = "mwheelup",
            ["mwheeldown"] = "mwheeldown",
            ["mousewheeldown"] = "mwheeldown",
            ["wheeldown"] = "mwheeldown",
            ["mwheelleft"] = "mwheelleft",
            ["mousewheelleft"] = "mwheelleft",
            ["wheelleft"] = "mwheeleft",
            ["mwheelright"] = "mwheelright",
            ["mousewheelright"] = "mwheelright",
            ["wheelright"] = "mwheelright",
            // Some bindings may be recorded as an undirected mouse wheel movement.
            ["mwheel"] = "mwheel",
            ["mousewheel"] = "mwheel",
            ["wheel"] = "mwheel"
        };

        private static readonly Regex RemoveSeparators = new Regex(@"[ _\-]+", RegexOptions.Compiled);

        // Pattern to match human readable mouse button tokens like "Button 2", "Mouse Button 2", "Button(2)", "Button 2 (mouse)"
        private static readonly Regex ButtonNumberPattern = new Regex(@"\b(?:mouse\s*button|mousebutton|mouse|button|btn)\s*[:\-\s]?\(?\s*(\d+)\s*\)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static bool TryNormalize(string token, out string normalized)
        {
            normalized = string.Empty;
            var cleaned = NormalizeForLookup(token);
            if (string.IsNullOrEmpty(cleaned))
            {
                return false;
            }

            if (CanonicalTokens.TryGetValue(cleaned, out var canonical))
            {
                normalized = canonical;
                return true;
            }

            // Tolerantly accept human-readable "Button 2" / "Mouse Button 2" style tokens and map them to "mouseN"
            try
            {
                // First try the dedicated pattern for a single button
                var m = ButtonNumberPattern.Match(token ?? string.Empty);
                if (m.Success && m.Groups.Count > 1)
                {
                    var num = m.Groups[1].Value;
                    if (!string.IsNullOrEmpty(num))
                    {
                        normalized = "mouse" + num;
                        LogDebugOnce($"norm_human:{token}", $"MouseTokenHelper normalized human-readable token '{token}' -> '{normalized}'");
                        return true;
                    }
                }

                // Fallback: detect composite tokens containing multiple button numbers like "mouse1_2" or "mouse1+2"
                var numMatches = System.Text.RegularExpressions.Regex.Matches(token ?? string.Empty, "\\d+");
                if (numMatches.Count > 0)
                {
                    // Do not treat a bare numeric token (e.g. "1") as a mouse button — those are likely keyboard tokens.
                    if (System.Text.RegularExpressions.Regex.IsMatch((token ?? string.Empty).Trim(), "^\\d+$"))
                    {
                        // Log once and bail out so other systems can interpret as keyboard '1'
                        LogDebugOnce($"skip_numeric:{token}", $"MouseTokenHelper skipped pure-numeric token '{token}' (not mapping to mouse)");
                        return false;
                    }

                    // Only consider numeric tokens for mouse mapping when the original token contains
                    // explicit mouse/button/wheel indicators. This avoids misinterpreting keys like 'f4' or 'np_6'.
                    var loweredToken = (token ?? string.Empty).ToLowerInvariant();
                    if (!(loweredToken.Contains("mouse") || loweredToken.Contains("button") || loweredToken.Contains("btn") || loweredToken.Contains("wheel")))
                    {
                        LogDebugOnce($"skip_indicator:{token}", $"MouseTokenHelper skipped numeric token '{token}' (no mouse/button/wheel indicator)");
                        return false;
                    }

                    var nums = new List<string>();
                    foreach (System.Text.RegularExpressions.Match nm in numMatches)
                    {
                        var val = nm.Value;
                        if (!string.IsNullOrEmpty(val) && !nums.Contains(val)) nums.Add(val);
                    }

                    if (nums.Count == 1)
                    {
                        normalized = "mouse" + nums[0];
                        LogDebugOnce($"norm_num:{token}", $"MouseTokenHelper normalized numeric token '{token}' -> '{normalized}'");
                        return true;
                    }

                    if (nums.Count > 1)
                    {
                        // produce composite normalized token using '+' as separator: e.g. mouse1+mouse2
                        normalized = string.Join("+", nums.ConvertAll(n => "mouse" + n));
                        LogDebugOnce($"norm_comp:{token}", $"MouseTokenHelper normalized composite token '{token}' -> '{normalized}'");
                        return true;
                    }
                }
            }
            catch
            {
                // ignore regex errors and fall through
            }

            return false;
        }

        internal static bool IsMouseLike(string token)
        {
            if (TryNormalize(token, out _))
            {
                return true;
            }

            var cleaned = NormalizeForLookup(token);
            if (string.IsNullOrEmpty(cleaned))
            {
                return false;
            }

            return cleaned.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   cleaned.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeForLookup(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            var lowered = token.Trim().ToLowerInvariant();
            return RemoveSeparators.Replace(lowered, "");
        }
    }
}
