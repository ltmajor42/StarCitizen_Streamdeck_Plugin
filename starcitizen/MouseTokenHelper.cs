using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace starcitizen
{
    internal static class MouseTokenHelper
    {
        private static readonly Dictionary<string, string> CanonicalTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mouse1"] = "mouse1",
            ["mouse2"] = "mouse2",
            ["mouse3"] = "mouse3",
            ["mouse4"] = "mouse4",
            ["mouse5"] = "mouse5",
            ["mwheelup"] = "mwheelup",
            ["mousewheelup"] = "mwheelup",
            ["wheelup"] = "mwheelup",
            ["mwheeldown"] = "mwheeldown",
            ["mousewheeldown"] = "mwheeldown",
            ["wheeldown"] = "mwheeldown",
            ["mwheelleft"] = "mwheelleft",
            ["mousewheelleft"] = "mwheelleft",
            ["wheelleft"] = "mwheelleft",
            ["mwheelright"] = "mwheelright",
            ["mousewheelright"] = "mwheelright",
            ["wheelright"] = "mwheelright",
            // Some bindings may be recorded as an undirected mouse wheel movement.
            ["mwheel"] = "mwheel",
            ["mousewheel"] = "mwheel",
            ["wheel"] = "mwheel"
        };

        private static readonly Regex RemoveSeparators = new Regex(@"[ _\-]+", RegexOptions.Compiled);

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

            return cleaned.Contains("mouse", StringComparison.OrdinalIgnoreCase) ||
                   cleaned.Contains("wheel", StringComparison.OrdinalIgnoreCase);
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
