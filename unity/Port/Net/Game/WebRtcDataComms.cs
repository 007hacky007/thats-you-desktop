// Drop-in replacement for the game's UDPDataComms as UDPCommsProvider uses it (contract 4.4):
// same events (game delegate types) and methods, backed by NetEngine over WebRTC data channels.
//
// The game's own join policy is reused through a join probe: when a phone says hello, the
// engine asks this adapter, which synthesises the game's raw join datagram and raises
// OnDataReceiveActivity with it. UDPCommsProvider then runs its checks and answers through
// ConnectClient and TrySendRawData; the answer is decoded here (join response = Joined,
// refusal = Refused(reason), nothing = Pending).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PortNet
{
    public sealed class WebRtcDataComms : INetJoinPolicy, INetEvents
    {
        static readonly byte[] s_noData = new byte[0];

        readonly NetEngine m_engine;
        readonly List<NetAddr> m_addrs = new List<NetAddr>();

        bool m_probing;
        NetAddr m_probeAddr;
        bool m_probeAnswered;
        JoinDecision m_probeDecision;

#pragma warning disable 0067 // OnReceiveBusy belongs to the surface but nothing is ever busy
        public event UDPDataComms.OnTransferDelegate OnTransferReceived;
        public event UDPAcknowledgedSend.OnActivityDelegate OnSendActivity;
        public event UDPAcknowledgedSend.OnSendCompleteDelegate OnSendComplete;
        public event UDPReceive.DataReceiveDelegate OnDataReceiveActivity;
        public event UDPDataComms.OnHeartStoppedDelegate OnHeartStopped;
        public event UDPReceive.ReceiveBusyDelegate OnReceiveBusy;
#pragma warning restore 0067

        // Runs from UDPCommsProvider's field initializer: no Unity API calls here.
        public WebRtcDataComms() : this(new NativeTransport())
        {
        }

        public WebRtcDataComms(INetTransport transport)
        {
            m_engine = new NetEngine(transport, PortNetHost.Log) { Policy = this, Events = this };
        }

        public NetEngine Engine => m_engine;

        static NetAddr Addr(SocketAddress a)
        {
            return a == null ? default : new NetAddr(a.IPAddr, a.PortOffset);
        }

        static SocketAddress Fresh(NetAddr a)
        {
            return new SocketAddress(a.Ip, a.PortOffset);
        }

        // ---------------- lifecycle ----------------

        public void Start()
        {
            PortNetHost.StartEngine(m_engine);
            PortJoinOverlay.Ensure();
        }

        public void ThreadFunc()
        {
        }

        public void Update(bool updateHeartbeats)
        {
            m_engine.Update(Time.deltaTime, Time.realtimeSinceStartup, updateHeartbeats);
            PortJoinOverlay.Ensure(); // scene loads may destroy it
        }

        public void OnDestroy()
        {
            PortNetHost.StopEngine(m_engine);
        }

        // ---------------- join probe ----------------

        JoinDecision INetJoinPolicy.OnJoinRequest(NetAddr addr, string uid)
        {
            bool wasProbing = m_probing;
            NetAddr prevAddr = m_probeAddr;
            bool prevAnswered = m_probeAnswered;
            JoinDecision prevDecision = m_probeDecision;
            m_probing = true;
            m_probeAddr = addr;
            m_probeAnswered = false;
            m_probeDecision = JoinDecision.Pending;
            try
            {
                byte[] datagram = JoinProbe.BuildRequest(uid);
                OnDataReceiveActivity?.Invoke(datagram, datagram.Length, Fresh(addr));
                return m_probeAnswered ? m_probeDecision : JoinDecision.Pending;
            }
            finally
            {
                m_probing = wasProbing;
                m_probeAddr = prevAddr;
                m_probeAnswered = prevAnswered;
                m_probeDecision = prevDecision;
            }
        }

        public bool TrySendRawData(SocketAddress socketAddr, byte[] data, int dataSize)
        {
            if (!m_probing || socketAddr == null || Addr(socketAddr) != m_probeAddr)
                return false;
            if (!JoinProbe.TryDecodeReply(data, dataSize, out JoinDecision d))
                return false;
            if (!m_probeAnswered)
            {
                m_probeAnswered = true;
                m_probeDecision = d;
            }
            return true;
        }

        // ---------------- engine events -> game events ----------------

        void INetEvents.OnReceived(NetAddr addr, int transferId)
        {
            OnTransferReceived?.Invoke(Fresh(addr), transferId);
        }

        void INetEvents.OnReceiveActivity(NetAddr addr)
        {
            OnDataReceiveActivity?.Invoke(s_noData, 0, Fresh(addr));
        }

        void INetEvents.OnSendActivity(NetAddr addr)
        {
            OnSendActivity?.Invoke(Fresh(addr));
        }

        void INetEvents.OnSendComplete(NetAddr addr, int outerTransferId)
        {
            OnSendComplete?.Invoke(Fresh(addr), outerTransferId);
        }

        void INetEvents.OnHeartStopped(NetAddr addr)
        {
            OnHeartStopped?.Invoke(addr.Ip, addr.PortOffset);
        }

        // ---------------- the UDPDataComms surface ----------------

        public bool TryGetData(int dataTransferID, SocketAddress socketAddr, byte[] buffer, ref int dataSize)
        {
            return socketAddr != null && m_engine.TryGetData(dataTransferID, Addr(socketAddr), buffer, ref dataSize);
        }

        public bool GetCanStartSend(SocketAddress socketAddr)
        {
            return socketAddr != null && m_engine.CanStartSend(Addr(socketAddr));
        }

        public bool GetIsPeerConnected(SocketAddress socketAddr)
        {
            return socketAddr != null && m_engine.IsPeerConnected(Addr(socketAddr));
        }

        public void StartDataSend(int transferID, SocketAddress socketAddr, byte[] data, int dataSize)
        {
            if (socketAddr != null)
                m_engine.StartSend(Addr(socketAddr), transferID, data, dataSize);
        }

        public bool GetIsAnyActivity()
        {
            return m_engine.AnyActivity();
        }

        public void StopHeartbeat(SocketAddress socketAddr)
        {
            if (socketAddr != null)
                m_engine.StopHeartbeat(Addr(socketAddr));
        }

        public void SetEnableHeartbeats(bool enable)
        {
            // native pings always run; the death countdown follows Update(updateHeartbeats)
        }

        public bool GetAreAnyClientsConnected()
        {
            return m_engine.AnyClientConnected();
        }

        public bool ConnectClient(SocketAddress socketAddr)
        {
            return socketAddr != null && m_engine.ConnectClient(Addr(socketAddr));
        }

        public int GetSocketAddresses(List<SocketAddress> outSocketAddresses)
        {
            int n = m_engine.GetAddresses(m_addrs);
            for (int i = 0; i < n; i++)
            {
                NetAddr a = m_addrs[i];
                if (i < outSocketAddresses.Count && outSocketAddresses[i] != null)
                {
                    outSocketAddresses[i].IPAddr = a.Ip;
                    outSocketAddresses[i].PortOffset = a.PortOffset;
                }
                else if (i < outSocketAddresses.Count)
                {
                    outSocketAddresses[i] = Fresh(a);
                }
                else
                {
                    outSocketAddresses.Add(Fresh(a));
                }
            }
            return n;
        }

        public float GetConnectionDeathTime(SocketAddress socketAddr)
        {
            return socketAddr == null ? -1f : m_engine.GetDeathTime(Addr(socketAddr));
        }

        public void ResetConnectionDeathTime(SocketAddress socketAddr)
        {
            if (socketAddr != null)
                m_engine.ResetDeathTime(Addr(socketAddr));
        }

        public void SetConnectionDeathTimeNearZero(SocketAddress socketAddr)
        {
            if (socketAddr != null)
                m_engine.SetDeathTimeNearZero(Addr(socketAddr));
        }

        public bool GetIsBadConnection(SocketAddress socketAddr)
        {
            return socketAddr != null && m_engine.IsBadConnection(Addr(socketAddr));
        }

        public void LogThreadStats(Action<object> log)
        {
            log?.Invoke(m_engine.StatsLine());
        }

        public void GetAcknowledgedUDPStats(SocketAddress sockAddr, ref double sendProp, ref double rcvProp)
        {
            sendProp = 1.0;
            rcvProp = 1.0;
        }

        public void GetAcknowledgedUDPStats(ref double sendProp, ref double rcvProp)
        {
            sendProp = 1.0;
            rcvProp = 1.0;
        }

        public void ResetAcknowledgedUDPStats(SocketAddress sockAddr)
        {
        }

        public long GetSentBytes(SocketAddress sockAddr)
        {
            return sockAddr == null ? 0 : m_engine.GetSentBytes(Addr(sockAddr));
        }

        public void ResetSentBytes(SocketAddress sockAddr)
        {
            if (sockAddr != null)
                m_engine.ResetSentBytes(Addr(sockAddr));
        }

        public long GetReceivedBytes(SocketAddress sockAddr)
        {
            return sockAddr == null ? 0 : m_engine.GetReceivedBytes(Addr(sockAddr));
        }

        public void ResetReceivedBytes(SocketAddress sockAddr)
        {
            if (sockAddr != null)
                m_engine.ResetReceivedBytes(Addr(sockAddr));
        }

        public void ResetStats()
        {
            m_engine.ResetStats();
        }

        // SCTP is reliable: there is never a send waiting for an acknowledgement (protocol.md 8.2.7).
        public float GetActiveSendTimeSinceAck(SocketAddress sockAddr)
        {
            return 0f;
        }
    }
}
