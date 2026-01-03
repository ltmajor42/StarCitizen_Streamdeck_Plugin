using System;
using System.Threading.Tasks;
using BarRaider.SdTools;
using Newtonsoft.Json.Linq;
using starcitizen.Buttons;

namespace starcitizen.Core;

/// <summary>
/// Handles communication with Property Inspector for sending function lists and state updates.
/// Provides a centralized way to send data to any action's Property Inspector.
/// </summary>
internal static class PropertyInspectorMessenger
{
    /// <summary>
    /// Sends the function list to the Property Inspector.
    /// If bindings aren't loaded yet, sends a "loading" state instead.
    /// </summary>
    /// <param name="connection">The Stream Deck connection for the action.</param>
    /// <returns>A task representing the async operation.</returns>
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
                PluginLog.Debug("SendFunctionsAsync: Bindings not loaded yet, sending loading state");
                var loadingPayload = new JObject
                {
                    ["functionsLoaded"] = false,
                    ["loading"] = true,
                    ["message"] = "Loading Star Citizen keybinds..."
                };
                return connection.SendToPropertyInspectorAsync(loadingPayload);
            }

            PluginLog.Debug("SendFunctionsAsync: Building functions data...");
            var functionsData = FunctionListBuilder.BuildFunctionsData();
            PluginLog.Debug($"SendFunctionsAsync: Built {functionsData?.Count ?? 0} function groups");
            
            var payload = new JObject
            {
                ["functionsLoaded"] = true,
                ["functions"] = functionsData
            };

            PluginLog.Debug("SendFunctionsAsync: Sending to Property Inspector...");
            return connection.SendToPropertyInspectorAsync(payload);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to send functions to PI: {ex}");
            return Task.CompletedTask;
        }
    }
}
