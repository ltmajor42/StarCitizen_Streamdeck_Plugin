using BarRaider.SdTools;
using BarRaider.SdTools.Wrappers;

namespace starcitizen.Buttons
{
    [PluginActionId("com.mhwlng.starcitizen.holdrepeat")]
    public class LegacyRepeataction(SDConnection connection, InitialPayload payload) : Repeataction(connection, payload)
    {
    }
}
