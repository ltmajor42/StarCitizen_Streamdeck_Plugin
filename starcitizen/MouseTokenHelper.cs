using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using starcitizen.Core;

namespace starcitizen
{
    /// <summary>
    /// Helper for normalizing and validating mouse token strings from Star Citizen bindings.
    /// Handles various formats like "mouse1", "mousebutton1", "Button 2", "mwheelup", etc.
    /// </summary>
    internal static class MouseTokenHelper
    {
        // ============================================================
        // REGION: Logging Deduplication
        // ============================================================
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

        // ============================================================
        // REGION: Token Normalization Maps
        // ============================================================
        /// <summary>Maps various mouse token formats to canonical forms.</summary>
        private static readonly Dictionary<string, string> CanonicalTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Button aliases
            ["mouse1"] = "mouse1", ["mousebutton1"] = "mouse1",
            ["mouse2"] = "mouse2", ["mousebutton2"] = "mouse2",
            ["mouse3"] = "mouse3", ["mousebutton3"] = "mouse3",
            ["mouse4"] = "mouse4", ["mousebutton4"] = "mouse4",
            ["mouse5"] = "mouse5", ["mousebutton5"] = "mouse5",
            
            // Wheel aliases
            ["mwheelup"] = "mwheelup", ["mousewheelup"] = "mwheelup", ["wheelup"] = "mwheelup",
            ["mwheeldown"] = "mwheeldown", ["mousewheeldown"] = "mwheeldown", ["wheeldown"] = "mwheeldown",
            ["mwheelleft"] = "mwheelleft", ["mousewheelleft"] = "mwheelleft", ["wheelleft"] = "mwheeleft",
            ["mwheelright"] = "mwheelright", ["mousewheelright"] = "mwheelright", ["wheelright"] = "mwheelright",
            ["mwheel"] = "mwheel", ["mousewheel"] = "mwheel", ["wheel"] = "mwheel"
        };

        private static readonly Regex RemoveSeparators = new Regex(@"[ _\-]+", RegexOptions.Compiled);
        private static readonly Regex ButtonNumberPattern = new Regex(
            @"\b(?:mouse\s*button|mousebutton|mouse|button|btn)\s*[:\-\s]?\(?\s*(\d+)\s*\)?\b", 
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ============================================================
        // REGION: Public API
        // ============================================================
        
        /// <summary>
        /// Attempts to normalize a mouse token to its canonical form.
        /// </summary>
        /// <param name="token">Raw token from binding (e.g., "mousebutton1", "Button 2")</param>
        /// <param name="normalized">Canonical form (e.g., "mouse1")</param>
        /// <returns>True if token is a recognized mouse token</returns>
        internal static bool TryNormalize(string token, out string normalized)
        {
            normalized = string.Empty;
            var cleaned = NormalizeForLookup(token);
            if (string.IsNullOrEmpty(cleaned)) return false;

            // Direct lookup
            if (CanonicalTokens.TryGetValue(cleaned, out var canonical))
            {
                normalized = canonical;
                return true;
            }

            // Try human-readable formats like "Button 2"
            return TryParseHumanReadableToken(token, out normalized);
        }

        /// <summary>
        /// Returns true if the token looks like a mouse token (even if not fully normalized).
        /// </summary>
        internal static bool IsMouseLike(string token)
        {
            if (TryNormalize(token, out _)) return true;

            var cleaned = NormalizeForLookup(token);
            if (string.IsNullOrEmpty(cleaned)) return false;

            return cleaned.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   cleaned.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ============================================================
        // REGION: Parsing Helpers
        // ============================================================
        private static bool TryParseHumanReadableToken(string token, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrEmpty(token)) return false;

            try
            {
                // Try dedicated pattern for single button
                var m = ButtonNumberPattern.Match(token);
                if (m.Success && m.Groups.Count > 1)
                {
                    var num = m.Groups[1].Value;
                    if (!string.IsNullOrEmpty(num))
                    {
                        normalized = "mouse" + num;
                        LogDebugOnce($"norm_human:{token}", $"MouseTokenHelper normalized '{token}' -> '{normalized}'");
                        return true;
                    }
                }

                // Fallback: detect composite tokens like "mouse1_2" or numeric extraction
                return TryParseNumericToken(token, out normalized);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseNumericToken(string token, out string normalized)
        {
            normalized = string.Empty;

            var numMatches = Regex.Matches(token ?? string.Empty, "\\d+");
            if (numMatches.Count == 0) return false;

            // Skip pure numeric tokens (likely keyboard keys like "1")
            if (Regex.IsMatch((token ?? string.Empty).Trim(), "^\\d+$"))
            {
                LogDebugOnce($"skip_numeric:{token}", $"MouseTokenHelper skipped pure-numeric '{token}'");
                return false;
            }

            // Only consider when explicit mouse/button/wheel indicator present
            var lowered = (token ?? string.Empty).ToLowerInvariant();
            if (!(lowered.Contains("mouse") || lowered.Contains("button") || lowered.Contains("btn") || lowered.Contains("wheel")))
            {
                LogDebugOnce($"skip_indicator:{token}", $"MouseTokenHelper skipped '{token}' (no mouse indicator)");
                return false;
            }

            var nums = new List<string>();
            foreach (Match nm in numMatches)
            {
                var val = nm.Value;
                if (!string.IsNullOrEmpty(val) && !nums.Contains(val)) nums.Add(val);
            }

            if (nums.Count == 1)
            {
                normalized = "mouse" + nums[0];
                LogDebugOnce($"norm_num:{token}", $"MouseTokenHelper normalized '{token}' -> '{normalized}'");
                return true;
            }

            if (nums.Count > 1)
            {
                // Composite: e.g., "mouse1+mouse2"
                normalized = string.Join("+", nums.ConvertAll(n => "mouse" + n));
                LogDebugOnce($"norm_comp:{token}", $"MouseTokenHelper normalized composite '{token}' -> '{normalized}'");
                return true;
            }

            return false;
        }

        private static string NormalizeForLookup(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return string.Empty;
            var lowered = token.Trim().ToLowerInvariant();
            return RemoveSeparators.Replace(lowered, "");
        }
    }
}
