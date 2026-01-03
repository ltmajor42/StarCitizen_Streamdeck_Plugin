using BarRaider.SdTools;
using BarRaider.SdTools.Wrappers;

namespace starcitizen.Buttons
{
    /// <summary>
    /// Legacy Action Key for backward compatibility with old mhwlng profiles.
    /// This class inherits all behavior from ActionKey but registers with the old UUID.
    /// Hidden from the action list but existing buttons on old profiles will still work.
    /// </summary>
    [PluginActionId("com.mhwlng.starcitizen.static")]
    public class LegacyActionKey : ActionKey
    {
        public LegacyActionKey(SDConnection connection, InitialPayload payload) 
            : base(connection, payload)
        {
        }
    }
}
