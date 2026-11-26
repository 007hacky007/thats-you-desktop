// Managed stand-in for the PS4SceNetCtl native plugin (network control:
// connection state, NAT/STUN info, link statistics).
//
// Type, member and enum names and numeric values follow the PS4 binding so
// the game code compiles unchanged. The stand-in reports that the console
// network is disconnected and never raises a state-change event, which makes
// ConnectionState.GetOnlineAllowed() false and keeps every online flow idle.

using System;

public class PS4SceNetCtl
{
    public struct InfoStats
    {
        public long txBytes;
        public long rxBytes;
    }

    public struct SceNetCtlNatInfo
    {
        public uint size;
        public int stunStatus;
        public int natType;
        public int mappedAddr;
    }

    public enum StunStatus
    {
        SCE_NET_CTL_NATINFO_STUN_UNCHECKED = 0,
        SCE_NET_CTL_NATINFO_STUN_FAILED = 1,
        SCE_NET_CTL_NATINFO_STUN_OK = 2
    }

    public enum NatType
    {
        Unknown = 0,
        SCE_NET_CTL_NATINFO_NAT_TYPE_1 = 1,
        SCE_NET_CTL_NATINFO_NAT_TYPE_2 = 2,
        SCE_NET_CTL_NATINFO_NAT_TYPE_3 = 3
    }

    public struct NetCtlEvent
    {
        public int event_type;
        public int errorCode;
    }

    public enum Device
    {
        SCE_NET_CTL_DEVICE_WIRED = 0,
        SCE_NET_CTL_DEVICE_WIRELESS = 1
    }

    public enum NetCtlState
    {
        Unknown = -1,
        SCE_NET_CTL_EVENT_TYPE_DISCONNECTED = 1,
        SCE_NET_CTL_EVENT_TYPE_DISCONNECT_REQ_FINISHED = 2,
        SCE_NET_CTL_EVENT_TYPE_IPOBTAINED = 3
    }

    // No wireless link: zero signal.
    public static int GetRSSIPercentage()
    {
        return 0;
    }

    public static bool GetNetInfoStats(out InfoStats stats)
    {
        stats = default(InfoStats);
        return false;
    }

    public static void ShowNetStat()
    {
    }

    public static void ShowIfConfig()
    {
    }

    // NAT detection is impossible offline; callers map a failure to STUN failed.
    public static int GetNatInfo(IntPtr natInfo)
    {
        return PS4NetworkOffline.ErrorNotAvailable;
    }

    public static int GetDevice(ref uint device)
    {
        return PS4NetworkOffline.ErrorNotAvailable;
    }

    // State-change queue: always empty, never overflowed.
    public static bool NetCtlStates_GetAreAny()
    {
        return false;
    }

    public static bool NetCtlStates_Get(IntPtr outItem)
    {
        return false;
    }

    public static bool NetCtlStates_GetLostFlag()
    {
        return false;
    }

    public static void NetCtlStates_ClearLostFlag()
    {
    }

    // The query itself succeeds and truthfully reports "not connected".
    public static int GetState(ref int state)
    {
        state = (int)NetCtlState.SCE_NET_CTL_EVENT_TYPE_DISCONNECTED;
        return 0;
    }

    // Polls for native callbacks; there are none, so nothing was dispatched.
    public static int CheckCallback()
    {
        return 0;
    }

    public static int RegisterCallback()
    {
        return PS4NetworkOffline.ErrorNotAvailable;
    }

    public static int UnregisterCallback()
    {
        return PS4NetworkOffline.ErrorNotAvailable;
    }

    // No disconnect reason is available.
    public static int GetResult(int eventType, ref int errorCode)
    {
        return PS4NetworkOffline.ErrorNotAvailable;
    }
}
