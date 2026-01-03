using System.Collections.Generic;

namespace starcitizen.SC;

/// <summary>
/// Language-specific key-value dictionary for UI text localization.
/// </summary>
class SCLocale(string lang) : Dictionary<string, string>
{
    /// <summary>
    /// The language code for debugging purposes.
    /// </summary>
    public string Language { get; set; } = lang;
}
