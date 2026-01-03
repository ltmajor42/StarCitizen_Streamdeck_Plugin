using BarRaider.SdTools;
using System;

namespace starcitizen.Buttons;

/// <summary>
/// Cosmetic-only key that displays an image but performs no action.
/// Use for visual separators, headers, or branding tiles.
/// </summary>
[PluginActionId("com.ltmajor42.starcitizen.cosmetickey")]
public class CosmeticKey(SDConnection connection, InitialPayload payload) : KeypadBase(connection, payload)
{
    public override void KeyPressed(KeyPayload payload) { }
    public override void KeyReleased(KeyPayload payload) { }
    public override void OnTick() { }
    public override void ReceivedSettings(ReceivedSettingsPayload payload) { }
    public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }
    public override void Dispose() => GC.SuppressFinalize(this);
}
