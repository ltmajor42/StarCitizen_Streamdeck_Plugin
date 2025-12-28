// File: Buttons/CosmeticKey.cs
using BarRaider.SdTools;

namespace starcitizen.Buttons
{
    [PluginActionId("com.mhwlng.starcitizen.cosmetickey")]
    public class CosmeticKey : KeypadBase
    {
        public CosmeticKey(SDConnection connection, InitialPayload payload) : base(connection, payload) { }

        public override void KeyPressed(KeyPayload payload) { }
        public override void KeyReleased(KeyPayload payload) { }
        public override void OnTick() { }
        public override void ReceivedSettings(ReceivedSettingsPayload payload) { }
        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }
        public override void Dispose() { }
    }
}
