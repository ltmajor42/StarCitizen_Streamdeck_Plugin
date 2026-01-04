using BarRaider.SdTools;
using BarRaider.SdTools.Wrappers;

namespace starcitizen.Buttons
{
    [PluginActionId("com.mhwlng.starcitizen.momentary")]
    public class LegacyMomentary(SDConnection connection, InitialPayload payload) : Momentary(connection, payload)
    {
    }
}
