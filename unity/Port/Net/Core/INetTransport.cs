// Transport seam between the engine and the native library (contract 4.3), plus the hooks the
// game adapter implements (contract 4.5).

namespace PortNet
{
    public enum NetEventKind
    {
        None = 0,
        Opened = 1,
        Text = 2,
        Binary = 3,
        Closed = 4,
        Log = 5
    }

    public struct NetEvent
    {
        public NetEventKind Kind;
        public int Session;
        public int TransferId;
        public int Code;
        public byte[] Data;
    }

    public interface INetTransport
    {
        int Start(int udpPort, string lanIp, string certPath);  // IWYK_* result codes
        void Stop();
        void SetLogLevel(int level);
        bool Poll(out NetEvent ev);                             // false when empty; Data is a fresh array
        int SendText(int session, byte[] utf8, int len);
        int SendBinary(int session, int transferId, byte[] data, int len);
        int Close(int session);
        int Buffered(int session);
        string Endpoint { get; }                                // null when not started
        string JoinUrl(string baseUrl);                         // null on error
        bool Qr(string text, out byte[] modules, out int side);
        string LastError { get; }
    }

    public enum JoinOutcome
    {
        Pending,
        Joined,
        Refused
    }

    public struct JoinDecision
    {
        public JoinOutcome Outcome;
        public int RefuseReason;

        public JoinDecision(JoinOutcome outcome, int refuseReason = 0)
        {
            Outcome = outcome;
            RefuseReason = refuseReason;
        }

        public static JoinDecision Joined => new JoinDecision(JoinOutcome.Joined);
        public static JoinDecision Pending => new JoinDecision(JoinOutcome.Pending);
        public static JoinDecision Refused(int reason) => new JoinDecision(JoinOutcome.Refused, reason);
    }

    // Asked when a session says hello and again every 1.5 s while the answer is Pending. May call
    // back into the engine (ConnectClient, IsPeerConnected, ...).
    public interface INetJoinPolicy
    {
        JoinDecision OnJoinRequest(NetAddr addr, string uid);
    }

    public interface INetEvents
    {
        void OnReceived(NetAddr addr, int transferId);
        void OnReceiveActivity(NetAddr addr);
        void OnSendActivity(NetAddr addr);
        void OnSendComplete(NetAddr addr, int outerTransferId);
        void OnHeartStopped(NetAddr addr);
    }

    public interface INetLog
    {
        void Info(string m);
        void Warn(string m);
        void Error(string m);
    }
}
