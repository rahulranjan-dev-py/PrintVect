using System;
using PrintVect.App.Client;
using PrintVect.App.Host;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;

namespace PrintVect.App.Forms
{
    /// <summary>Saves the office PIN from Settings or the Turn on sharing window: it guards this PC's printers and goes with every job it sends.</summary>
    internal static class PinSetting
    {
        public static bool Save(ConfigStore store, AppConfig config, HostController host, ClientController client, string pin)
        {
            config.Pin = pin ?? "";
            host.ApplyPin(config.Pin);
            client.ApplyPin(config.Pin);
            Log.Info("Office PIN " + (config.Pin.Length == 0 ? "removed" : "set (" + config.Pin.Length + " characters)") + ".");
            try
            {
                store.Save(config);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not save " + store.Path, ex);
                return false;
            }
        }
    }
}
