// Compile-only stand-ins for Unity's legacy (RakNet based, pre-UNET) networking API,
// which Unity removed in 2018.2.
//
// Why this exists:
// - The game ships the PlayMaker runtime and its stock "Network" action set. PlayMaker's
//   core (PlayMakerFSM, FsmEventData) and those actions reference NetworkView, NetworkPlayer,
//   BitStream, MasterServer and friends, so they no longer compile against Unity 6.
// - The game itself never networked through this API. Local multiplayer goes through the
//   PS4 RUDP/UDP layer, which is replaced separately by the port. None of the legacy network
//   PlayMaker actions (Network*, MasterServer*, SendRemoteEvent*, GetRemoteEvent*) appear in
//   any scene, prefab or asset of the game.
//
// Behaviour: inert. Every query reports "not connected" (no connections, no hosts, peer type
// Disconnected), every command is a no-op that reports failure where the original API
// returned an error code, and Unity never sends the legacy network messages
// (OnPlayerConnected, OnSerializeNetworkView, ...) because nothing raises them.
// Only the type and member names the game's code uses are provided.

using System;

namespace UnityEngine
{
    public enum RPCMode
    {
        Server = 0,
        Others = 1,
        OthersBuffered = 5,
        All = 2,
        AllBuffered = 6
    }

    public enum NetworkDisconnection
    {
        LostConnection = 11,
        Disconnected = 12
    }

    public enum NetworkConnectionError
    {
        NoError = 0,
        RSAPublicKeyMismatch = 21,
        InvalidPassword = 23,
        ConnectionFailed = 15,
        TooManyConnectedPlayers = 18,
        ConnectionBanned = 22,
        AlreadyConnectedToServer = 16,
        AlreadyConnectedToAnotherServer = -1,
        CreateSocketOrThreadFailure = -2,
        IncorrectParameters = -3,
        EmptyConnectTarget = -4,
        InternalDirectConnectFailed = -5,
        NATTargetNotConnected = 69,
        NATTargetConnectionLost = 71,
        NATPunchthroughFailed = 73
    }

    public enum MasterServerEvent
    {
        RegistrationFailedGameName = 0,
        RegistrationFailedGameType = 1,
        RegistrationFailedNoServer = 2,
        RegistrationSucceeded = 3,
        HostListReceived = 4
    }

    public enum NetworkLogLevel
    {
        Off = 0,
        Informational = 1,
        Full = 3
    }

    public enum NetworkPeerType
    {
        Disconnected = 0,
        Server = 1,
        Client = 2,
        Connecting = 3
    }

    public enum NetworkStateSynchronization
    {
        Off = 0,
        ReliableDeltaCompressed = 1,
        Unreliable = 2
    }

    /// <summary>Marks a method as callable through NetworkView.RPC. Has no effect here.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RPCAttribute : Attribute
    {
    }

    public struct NetworkPlayer : IEquatable<NetworkPlayer>
    {
        private readonly int m_Index;

        public string ipAddress { get { return string.Empty; } }
        public int port { get { return 0; } }
        public string guid { get { return string.Empty; } }
        public string externalIP { get { return string.Empty; } }
        public int externalPort { get { return 0; } }

        public bool Equals(NetworkPlayer other) { return m_Index == other.m_Index; }
        public override bool Equals(object obj) { return obj is NetworkPlayer && Equals((NetworkPlayer)obj); }
        public override int GetHashCode() { return m_Index; }
        public override string ToString() { return m_Index.ToString(); }

        public static bool operator ==(NetworkPlayer a, NetworkPlayer b) { return a.m_Index == b.m_Index; }
        public static bool operator !=(NetworkPlayer a, NetworkPlayer b) { return a.m_Index != b.m_Index; }
    }

    public struct NetworkViewID : IEquatable<NetworkViewID>
    {
        private readonly int m_Id;

        public static NetworkViewID unassigned { get { return default(NetworkViewID); } }
        public bool isMine { get { return true; } }
        public NetworkPlayer owner { get { return default(NetworkPlayer); } }

        public bool Equals(NetworkViewID other) { return m_Id == other.m_Id; }
        public override bool Equals(object obj) { return obj is NetworkViewID && Equals((NetworkViewID)obj); }
        public override int GetHashCode() { return m_Id; }
        public override string ToString() { return "ViewID " + m_Id; }

        public static bool operator ==(NetworkViewID a, NetworkViewID b) { return a.m_Id == b.m_Id; }
        public static bool operator !=(NetworkViewID a, NetworkViewID b) { return a.m_Id != b.m_Id; }
    }

    public struct NetworkMessageInfo
    {
        public double timestamp { get { return 0.0; } }
        public NetworkPlayer sender { get { return default(NetworkPlayer); } }
        public NetworkView networkView { get { return null; } }
    }

    /// <summary>
    /// Stand-in for the removed NetworkView component. Never synchronises or sends anything.
    /// With no network session, every view is owned locally, so isMine is true.
    /// </summary>
    public class NetworkView : MonoBehaviour
    {
        public Component observed;
        public NetworkStateSynchronization stateSynchronization = NetworkStateSynchronization.Off;
        public NetworkViewID viewID;
        public int group;

        public bool isMine { get { return true; } }
        public NetworkPlayer owner { get { return default(NetworkPlayer); } }

        public void RPC(string name, RPCMode mode, params object[] args) { }
        public void RPC(string name, NetworkPlayer target, params object[] args) { }
        public bool SetScope(NetworkPlayer player, bool relevancy) { return false; }

        public static NetworkView Find(NetworkViewID viewID) { return null; }
    }

    /// <summary>Stand-in for the removed BitStream. Serialize leaves every value unchanged.</summary>
    public class BitStream
    {
        public bool isReading { get { return false; } }
        public bool isWriting { get { return false; } }

        public void Serialize(ref bool value) { }
        public void Serialize(ref char value) { }
        public void Serialize(ref short value) { }
        public void Serialize(ref int value) { }
        public void Serialize(ref float value) { }
        public void Serialize(ref float value, float maxDelta) { }
        public void Serialize(ref Quaternion value) { }
        public void Serialize(ref Quaternion value, float maxDelta) { }
        public void Serialize(ref Vector3 value) { }
        public void Serialize(ref Vector3 value, float maxDelta) { }
        public void Serialize(ref NetworkPlayer value) { }
        public void Serialize(ref NetworkViewID viewID) { }
    }

    public sealed class HostData
    {
        public bool useNat;
        public string gameType = string.Empty;
        public string gameName = string.Empty;
        public int connectedPlayers;
        public int playerLimit;
        public string[] ip = new string[0];
        public int port;
        public bool passwordProtected;
        public string comment = string.Empty;
        public string guid = string.Empty;
    }

    /// <summary>Stand-in for the removed static Network class: always disconnected.</summary>
    public sealed class Network
    {
        private static readonly NetworkPlayer[] s_NoConnections = new NetworkPlayer[0];

        private Network() { }

        public static string incomingPassword { get; set; } = string.Empty;
        public static NetworkLogLevel logLevel { get; set; } = NetworkLogLevel.Off;
        public static NetworkPlayer[] connections { get { return s_NoConnections; } }
        public static NetworkPlayer player { get { return default(NetworkPlayer); } }
        public static bool isClient { get { return false; } }
        public static bool isServer { get { return false; } }
        public static NetworkPeerType peerType { get { return NetworkPeerType.Disconnected; } }
        public static float sendRate { get; set; } = 15f;
        public static bool isMessageQueueRunning { get; set; } = true;
        public static double time { get { return Time.realtimeSinceStartupAsDouble; } }
        public static int minimumAllocatableViewIDs { get; set; } = 100;
        public static int maxConnections { get; set; }

        public static NetworkConnectionError InitializeServer(int connections, int listenPort, bool useNat)
        {
            return NetworkConnectionError.CreateSocketOrThreadFailure;
        }

        public static void InitializeSecurity() { }

        public static NetworkConnectionError Connect(string IP, int remotePort)
        {
            return NetworkConnectionError.ConnectionFailed;
        }

        public static NetworkConnectionError Connect(string IP, int remotePort, string password)
        {
            return NetworkConnectionError.ConnectionFailed;
        }

        public static void Disconnect() { }
        public static void Disconnect(int timeout) { }
        public static void CloseConnection(NetworkPlayer target, bool sendDisconnectionNotification) { }
        public static bool HavePublicAddress() { return false; }
        public static void SetLevelPrefix(int prefix) { }
        public static void RemoveRPCs(NetworkPlayer player) { }
        public static void RemoveRPCs(NetworkViewID viewID) { }
        public static void DestroyPlayerObjects(NetworkPlayer playerID) { }
        public static int GetLastPing(NetworkPlayer player) { return -1; }
        public static int GetAveragePing(NetworkPlayer player) { return -1; }

        /// <summary>
        /// With no session to replicate to, the object is created locally only, which is what a
        /// single machine would end up with.
        /// </summary>
        public static Object Instantiate(Object prefab, Vector3 position, Quaternion rotation, int group)
        {
            return Object.Instantiate(prefab, position, rotation);
        }
    }

    /// <summary>Stand-in for the removed MasterServer: no master server, no hosts.</summary>
    public sealed class MasterServer
    {
        private static readonly HostData[] s_NoHosts = new HostData[0];

        private MasterServer() { }

        public static string ipAddress { get; set; } = string.Empty;
        public static int port { get; set; }
        public static int updateRate { get; set; }
        public static bool dedicatedServer { get; set; }

        public static void RequestHostList(string gameTypeName) { }
        public static HostData[] PollHostList() { return s_NoHosts; }
        public static void ClearHostList() { }
        public static void RegisterHost(string gameTypeName, string gameName) { }
        public static void RegisterHost(string gameTypeName, string gameName, string comment) { }
        public static void UnregisterHost() { }
    }
}
