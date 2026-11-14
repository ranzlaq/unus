using System;
using Windows.Devices.WiFiDirect;

namespace Unus.Windows.Services;

internal static class WiFiDirectApiProbe
{
    public static string Run()
    {
        try
        {
            var publisher = new WiFiDirectAdvertisementPublisher();

            return
                "Wi-Fi Direct API activation succeeded!\n\n" +
                $"Initial publisher status: {publisher.Status}";
        }
        catch (Exception ex)
        {
            return
                "Wi-Fi Direct API activation failed.\n\n" +
                $"{ex.GetType().FullName}\n{ex.Message}";
        }
    }
}