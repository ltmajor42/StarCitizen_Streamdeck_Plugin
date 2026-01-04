using BarRaider.SdTools;
using BarRaider.SdTools.Wrappers;

namespace starcitizen.Buttons
{
    [PluginActionId("com.mhwlng.starcitizen.statememory")]
    public class LegacyStateMemory(SDConnection connection, InitialPayload payload) : StateMemory(connection, payload)
    {
    }
}
