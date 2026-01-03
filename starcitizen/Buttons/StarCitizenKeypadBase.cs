using BarRaider.SdTools;

namespace starcitizen.Buttons
{
    /// <summary>
    /// Base class for Star Citizen keypad-style Stream Deck buttons.
    /// Provides common initialization and lifecycle management for keypad actions.
    /// </summary>
    public abstract class StarCitizenKeypadBase : KeypadBase
    {
        protected StarCitizenKeypadBase(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
        }

        public override void Dispose()
        {
        }

        public override void KeyReleased(KeyPayload payload) { }

        public override void OnTick()
        {
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }
    }
}
