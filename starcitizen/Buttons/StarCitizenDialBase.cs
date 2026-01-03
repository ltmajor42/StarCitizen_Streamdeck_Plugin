using BarRaider.SdTools;
using System;

namespace starcitizen.Buttons;

/// <summary>
/// Base class for Star Citizen dial-style (encoder) Stream Deck+ actions.
/// Provides common initialization and lifecycle management for dial actions.
/// </summary>
public abstract class StarCitizenDialBase(SDConnection connection, InitialPayload payload) 
    : EncoderBase(connection, payload)
{
    public override void Dispose() => GC.SuppressFinalize(this);
    public override void OnTick() { }
    public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }
}
