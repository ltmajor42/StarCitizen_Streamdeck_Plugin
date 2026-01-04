using BarRaider.SdTools;
using BarRaider.SdTools.Wrappers;

namespace starcitizen.Buttons
{
    [PluginActionId("com.mhwlng.starcitizen.dualaction")]
    public class LegacyDualAction(SDConnection connection, InitialPayload payload) : DualAction(connection, payload)
    {
    }
}
