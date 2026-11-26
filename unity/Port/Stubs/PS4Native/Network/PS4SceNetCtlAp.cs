// Managed stand-in for the PS4SceNetCtlAp native plugin, which ran the
// console's Wi-Fi Direct access point that phones joined to play.
//
// Names and signatures follow the PS4 binding. On desktop there is no access
// point: starting it fails immediately, it never becomes active, and no
// callbacks are pending, so PS4Wifi stays in its inactive state and restores
// dialog opening right away.

namespace Wish.Core
{
    public class PS4SceNetCtlAp
    {
        public static int Initialize()
        {
            return PS4NetworkOffline.ErrorNotAvailable;
        }

        public static int StartWifi()
        {
            return PS4NetworkOffline.ErrorNotAvailable;
        }

        // Reports whether a pending start request finished; none ever does.
        public static bool Tick()
        {
            return false;
        }

        public static string GetPS4IPAddress()
        {
            return string.Empty;
        }

        public static string GetWifiPassword()
        {
            return string.Empty;
        }

        public static string GetSSID()
        {
            return string.Empty;
        }

        // Polls for native callbacks; there are none, so nothing was dispatched.
        public static int CheckCallback()
        {
            return 0;
        }

        public static int Stop()
        {
            return PS4NetworkOffline.ErrorNotAvailable;
        }

        public static bool GetIsActive()
        {
            return false;
        }
    }
}
