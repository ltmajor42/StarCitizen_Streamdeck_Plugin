using System;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace starcitizen.Buttons
{
    internal static class StreamDeckEventArgsExtensions
    {
        public static JObject ExtractPayload(this EventArgs eventArgs)
        {
            if (eventArgs == null)
            {
                return null;
            }

            try
            {
                var eventProperty = eventArgs.GetType().GetProperty("Event", BindingFlags.Public | BindingFlags.Instance);
                var eventValue = eventProperty?.GetValue(eventArgs);

                var payloadProperty = eventValue?.GetType().GetProperty("Payload", BindingFlags.Public | BindingFlags.Instance);
                return payloadProperty?.GetValue(eventValue) as JObject;
            }
            catch
            {
                return null;
            }
        }
    }
}
