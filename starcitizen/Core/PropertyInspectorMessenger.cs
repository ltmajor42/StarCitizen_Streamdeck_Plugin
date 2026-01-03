using System;
using System.Threading.Tasks;
using BarRaider.SdTools;
using Newtonsoft.Json.Linq;
using starcitizen.Buttons;

namespace starcitizen.Core
{
    internal static class PropertyInspectorMessenger
    {
        /// <summary>
        /// Sends the function list to the Property Inspector.
        /// If bindings aren't loaded yet, sends a "loading" state instead.
        /// </summary>
        public static Task SendFunctionsAsync(ISDConnection connection)
        {
            if (connection == null)
            {
                PluginLog.Warn("SendFunctionsAsync: connection is null");
                return Task.CompletedTask;
            }

            try
            {
                var bindingService = KeyBindingService.Instance;
                
                // If bindings aren't loaded yet, send loading state
                if (bindingService.Reader == null)
                {
                    PluginLog.Info("SendFunctionsAsync: Bindings not loaded yet, sending loading state");
                    var loadingPayload = new JObject
                    {
                        ["functionsLoaded"] = false,
                        ["loading"] = true,
                        ["message"] = "Loading Star Citizen keybinds..."
                    };
                    return connection.SendToPropertyInspectorAsync(loadingPayload);
                }

                PluginLog.Info("SendFunctionsAsync: Building functions data...");
                var functionsData = FunctionListBuilder.BuildFunctionsData();
                PluginLog.Info($"SendFunctionsAsync: Built {functionsData?.Count ?? 0} function groups");
                
                var payload = new JObject
                {
                    ["functionsLoaded"] = true,
                    ["functions"] = functionsData
                };

                PluginLog.Info("SendFunctionsAsync: Sending to Property Inspector...");
                return connection.SendToPropertyInspectorAsync(payload);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to send functions to PI: {ex}");
                return Task.CompletedTask;
            }
        }
    }
}
