// Shared constants of the networking contract (section 1). No UnityEngine, no game types.

namespace PortNet
{
    public static class NetLimits
    {
        public const int DefaultUdpPort = 47000;
        public const int UdpPortRetries = 9;                 // E_BIND: try port+1 .. port+9

        public const int JsonTransferId = -7503;
        public const int MaxText = 65536;                    // text frame, UTF-8 bytes
        public const int MaxBinary = 131072;                 // one content transfer
        public const int MaxGameContent = 131062;            // the game drops larger phone content

        public const int Slots = 6;
        public const float DeathTime = 17f;
        public const float BadConnectionThreshold = 6.5f;
        public const float JoinRetrySeconds = 1.5f;
        public const float UnclaimedAbandonSeconds = 2f;

        public const int MaxBufferedForSend = 1 << 20;       // CanStartSend false at or above 1 MiB queued
        public const int DrainEventBudget = 2000;            // events per Update
        public const int DrainByteBudget = 8 << 20;          // payload bytes per Update

        public const int ReceiveStoreEntries = 256;
        public const long ReceiveStoreBytes = 16L << 20;

        public const int MaxUidBytes = 128;
        public const int QuietZoneModules = 4;
    }

    // IWYK_* result codes the engine reacts to (mirrors include/iwyknet.h; IwykNative.cs is the
    // checked copy, a test keeps these two equal).
    public static class NetCodes
    {
        public const int Ok = 0;
        public const int NotStarted = -1;
        public const int AlreadyStarted = -2;
        public const int Arg = -3;
        public const int NoSession = -4;
        public const int SmallBuf = -5;
        public const int TooBig = -6;
        public const int Bind = -7;
        public const int Cert = -8;
        public const int NoIp = -9;
        public const int Full = -10;
        public const int Internal = -11;

        public const int CloseHost = 1;
        public const int CloseRemote = 2;
        public const int CloseTransport = 3;
        public const int CloseProtocol = 4;
        public const int CloseStopped = 5;

        public const int LogDebug = 0;
        public const int LogInfo = 1;
        public const int LogWarn = 2;
        public const int LogError = 3;
        public const int LogOff = 4;
    }
}
