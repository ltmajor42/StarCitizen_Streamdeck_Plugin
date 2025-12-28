using System;
using System.Threading.Tasks;
using BarRaider.SdTools;
using Newtonsoft.Json.Linq;
using starcitizen.Buttons;

namespace starcitizen.Core
{
    internal static class PropertyInspectorMessenger
    {
        public static Task SendFunctionsAsync(ISDConnection connection)
        {
            if (connection == null)
            {
                return Task.CompletedTask;
            }

            try
            {
                var functionsData = FunctionListBuilder.BuildFunctionsData();
                var payload = new JObject
                {
                    ["functionsLoaded"] = true,
                    ["functions"] = functionsData
                };

                return connection.SendToPropertyInspectorAsync(payload);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to send functions to PI: {ex.Message}");
                return Task.CompletedTask;
            }
        }
    }
}
