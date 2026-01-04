using BarRaider.SdTools;
using BarRaider.SdTools.Wrappers;

namespace starcitizen.Buttons
{
    [PluginActionId("com.mhwlng.starcitizen.dial")]
    public class LegacyDial(SDConnection connection, InitialPayload payload) : Dial(connection, payload)
    {
    }
}
