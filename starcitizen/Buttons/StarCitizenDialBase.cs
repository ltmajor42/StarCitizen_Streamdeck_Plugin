using BarRaider.SdTools;

namespace starcitizen.Buttons
{
    /// <summary>
    /// Base class for Star Citizen dial-style (encoder) Stream Deck+ actions.
    /// Provides common initialization and lifecycle management for dial actions.
    /// </summary>
    public abstract class StarCitizenDialBase : EncoderBase
    {
        protected StarCitizenDialBase(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
        }

        public override void Dispose()
        {
        }

        public override void OnTick()
        {
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }
    }
}
