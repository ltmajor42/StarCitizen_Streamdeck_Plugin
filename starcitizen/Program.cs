using System;
using BarRaider.SdTools;
using starcitizen.Core;

namespace starcitizen
{
    class Program
    {
        static void Main(string[] args)
        {
            PluginLog.Info("Init Star Citizen");

            try
            {
                KeyBindingService.Instance.Initialize();
            }
            catch (Exception ex)
            {
                PluginLog.Fatal($"Failed to initialize Star Citizen plugin: {ex}");
            }

            PluginLog.Info("Finished Init Star Citizen");
            SDWrapper.Run(args);
        }
    }
}
