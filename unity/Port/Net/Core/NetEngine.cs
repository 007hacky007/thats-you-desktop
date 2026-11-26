// Game-agnostic comms engine (contract 4.5): turns transport sessions into the peer model the
// game's UDP layer exposed (synthetic addresses, 6 slots, claimable received data, send
// completions, death timers) and runs the join handshake of the wire envelope.
//
// Single-threaded: every method runs on the caller's (Unity main) thread. Hooks and events may
// re-enter the engine, so loops iterate over snapshots.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PortNet
{
    public sealed class NetEngine
    {
        enum State
        {
            Joining,   // hello received, decision outstanding
            Pending,   // policy said Pending; retried every 1.5 s
            Joined,
            Refused,
            Closing,
            Closed
        }

        sealed class Session
        {
            public int Id;
            public NetAddr Addr;
            public State State;
            public string Uid;
            public float NextRetry;
            public bool DropLogged;
            public long Sent;
            public long Received;
        }

        sealed class Slot
        {
            public NetAddr Addr;
            public readonly Liveness Live = new Liveness();
        }

        struct Completion
        {
            public NetAddr Addr;
            public int OuterId;
        }

        readonly INetTransport m_transport;
        readonly INetLog m_log;
        readonly Dictionary<int, Session> m_sessions = new Dictionary<int, Session>();
        readonly Dictionary<NetAddr, Session> m_byAddr = new Dictionary<NetAddr, Session>();
        readonly List<Session> m_order = new List<Session>();
        readonly Slot[] m_slots = new Slot[NetLimits.Slots];
        readonly ReceiveStore m_store = new ReceiveStore();
        readonly NetAddrAllocator m_alloc = new NetAddrAllocator();
        readonly List<BatchSegment> m_segments = new List<BatchSegment>();
        List<Completion> m_completions = new List<Completion>();
        List<Completion> m_due = new List<Completion>();

        NetConfig m_config = new NetConfig();
        bool m_running;
        string m_startProblem;
        string m_joinUrl;
        string m_endpoint;
        float m_lastDt;
        float m_now;

        bool m_stopping;
        NetAddr m_stoppingAddr;
        readonly List<NetAddr> m_stopQueue = new List<NetAddr>();
        readonly HashSet<NetAddr> m_stopSeen = new HashSet<NetAddr>();

        string m_qrText;
        byte[] m_qrModules;
        int m_qrSide;

        public INetJoinPolicy Policy;
        public INetEvents Events;

        public NetEngine(INetTransport transport, INetLog log)
        {
            m_transport = transport ?? throw new ArgumentNullException(nameof(transport));
            m_log = log ?? new NullLog();
        }

        public bool Running => m_running;
        public string Endpoint => m_running ? m_endpoint : null;
        public string JoinUrl => m_running ? m_joinUrl : null;
        public NetConfig Config => m_config.Clone();
        public int SessionCount
        {
            get
            {
                int n = 0;
                foreach (Session s in m_order)
                    if (IsOpen(s))
                        n++;
                return n;
            }
        }

        // Human readable reason why phones cannot join right now; null when fine.
        public string Problem
        {
            get
            {
                if (m_startProblem != null)
                    return m_startProblem;
                if (!m_running)
                    return "network not started";
                if (m_joinUrl == null)
                {
                    if (m_config.PhonePageUrl == null)
                        return "phone page address not set: add " + NetConfig.KeyPageUrl + " to " + (m_config.GameConfigPath ?? NetConfig.FileName);
                    return "phone page address is not a valid http(s) URL";
                }
                return null;
            }
        }

        // ---------------- lifecycle ----------------

        public bool Start(NetConfig config, string certPath)
        {
            Stop();
            m_config = (config ?? new NetConfig()).Clone();
            m_startProblem = null;
            try
            {
                m_transport.Stop();  // an editor domain reload can leave the native side running
                m_transport.SetLogLevel(m_config.LogLevel);
                int port = m_config.UdpPort;
                int rc = m_transport.Start(port, m_config.LanIp, certPath);
                for (int i = 1; rc == NetCodes.Bind && port > 0 && i <= NetLimits.UdpPortRetries && port + i <= 65535; i++)
                    rc = m_transport.Start(port + i, m_config.LanIp, certPath);
                if (rc != NetCodes.Ok)
                {
                    m_startProblem = StartProblem(rc, port, certPath);
                    m_log.Error("network start failed: " + m_startProblem);
                    return false;
                }
            }
            catch (Exception e)
            {
                m_startProblem = "network start failed: " + e.Message;
                m_log.Error(m_startProblem);
                return false;
            }
            m_running = true;
            m_endpoint = m_transport.Endpoint;
            RefreshJoinUrl();
            m_log.Info("listening on " + (m_endpoint ?? "?") + " (udp)");
            string p = Problem;
            if (p != null)
                m_log.Warn(p);
            return true;
        }

        public void Stop()
        {
            if (m_running)
            {
                try
                {
                    m_transport.Stop();
                }
                catch (Exception e)
                {
                    m_log.Error("transport stop failed: " + e.Message);
                }
                m_log.Info("stopped");
            }
            m_running = false;
            m_sessions.Clear();
            m_byAddr.Clear();
            m_order.Clear();
            for (int i = 0; i < m_slots.Length; i++)
                m_slots[i] = null;
            m_store.Clear();
            m_completions.Clear();
            m_joinUrl = null;
            m_endpoint = null;
            m_qrText = null;
            m_qrModules = null;
        }

        string StartProblem(int rc, int port, string certPath)
        {
            switch (rc)
            {
                case NetCodes.Bind:
                    return port > 0
                        ? "UDP ports " + port.ToString(CultureInfo.InvariantCulture) + ".." + Math.Min(65535, port + NetLimits.UdpPortRetries).ToString(CultureInfo.InvariantCulture) + " in use"
                        : "no free UDP port";
                case NetCodes.Cert:
                    return "cannot read or create the certificate file " + certPath;
                case NetCodes.NoIp:
                    return "no LAN IPv4 address found: connect to a network or set " + NetConfig.KeyLanIp + " in " + NetConfig.FileName;
                case NetCodes.Arg:
                    return "invalid network settings: " + (m_transport.LastError ?? "bad argument");
                default:
                    string e = m_transport.LastError;
                    return string.IsNullOrEmpty(e) ? "error " + rc.ToString(CultureInfo.InvariantCulture) : e;
            }
        }

        public void SetPageUrl(string url)
        {
            m_config.PhonePageUrl = NetConfig.NormalizeUrl(url);
            RefreshJoinUrl();
        }

        void RefreshJoinUrl()
        {
            m_joinUrl = m_running && m_config.PhonePageUrl != null ? m_transport.JoinUrl(m_config.PhonePageUrl) : null;
        }

        public bool TryGetQr(out byte[] modules, out int side)
        {
            modules = null;
            side = 0;
            string url = JoinUrl;
            if (url == null)
                return false;
            if (m_qrText != url)
            {
                if (!m_transport.Qr(url, out byte[] m, out int s) || m == null || s <= 0)
                    return false;
                m_qrText = url;
                m_qrModules = m;
                m_qrSide = s;
            }
            modules = m_qrModules;
            side = m_qrSide;
            return true;
        }

        // ---------------- per frame ----------------

        public void Update(float dt, float realtime, bool updateHeartbeats)
        {
            m_lastDt = dt;
            m_now = realtime;

            // completions of sends accepted before this Update (raised in step 3)
            List<Completion> due = m_completions;
            m_completions = m_due;
            m_completions.Clear();
            m_due = due;

            if (m_running)
                Drain();
            RetryPending();

            for (int i = 0; i < due.Count; i++)
            {
                Completion c = due[i];
                Raise(() => Events?.OnSendActivity(c.Addr));
                Raise(() => Events?.OnSendComplete(c.Addr, c.OuterId));
            }
            due.Clear();

            if (updateHeartbeats)
            {
                var snapshot = (Slot[])m_slots.Clone();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    Slot s = snapshot[i];
                    if (s == null || m_slots[i] != s)
                        continue;
                    if (s.Live.Tick(dt))
                    {
                        m_log.Info(s.Addr + ": no frame for " + NetLimits.DeathTime.ToString(CultureInfo.InvariantCulture) + " s, connection lost");
                        StopHeartbeat(s.Addr);
                    }
                }
            }

            int abandoned = m_store.Abandon(realtime - NetLimits.UnclaimedAbandonSeconds);
            if (abandoned > 0)
                m_log.Warn(abandoned.ToString(CultureInfo.InvariantCulture) + " received message(s) not claimed within 2 s, dropped");
        }

        void Drain()
        {
            int events = 0;
            long bytes = 0;
            while (m_running && events < NetLimits.DrainEventBudget && bytes < NetLimits.DrainByteBudget)
            {
                if (!m_transport.Poll(out NetEvent ev))
                    break;
                events++;
                bytes += ev.Data == null ? 0 : ev.Data.Length;
                Handle(ev);
            }
        }

        void Handle(NetEvent ev)
        {
            byte[] data = ev.Data ?? new byte[0];
            Session s;
            switch (ev.Kind)
            {
                case NetEventKind.Opened:
                    if (m_sessions.ContainsKey(ev.Session))
                        return;
                    s = new Session { Id = ev.Session, State = State.Joining };
                    s.Addr = m_alloc.Allocate(a => m_byAddr.ContainsKey(a) || SlotOf(a) >= 0);
                    m_sessions.Add(s.Id, s);
                    m_byAddr.Add(s.Addr, s);
                    m_order.Add(s);
                    m_log.Info("session " + s.Id.ToString(CultureInfo.InvariantCulture) + " opened as " + s.Addr);
                    return;

                case NetEventKind.Text:
                    if (!m_sessions.TryGetValue(ev.Session, out s) || !IsOpen(s))
                        return;
                    if (s.State == State.Joining && s.Uid == null)
                        OnHello(s, data);
                    else
                        OnFrame(s, data);
                    return;

                case NetEventKind.Binary:
                    if (!m_sessions.TryGetValue(ev.Session, out s) || !IsOpen(s))
                        return;
                    OnBinary(s, ev.TransferId, data);
                    return;

                case NetEventKind.Closed:
                    if (!m_sessions.TryGetValue(ev.Session, out s))
                        return;
                    bool hadSlot = SlotOf(s.Addr) >= 0;
                    s.State = State.Closed;
                    m_log.Info("session " + s.Id.ToString(CultureInfo.InvariantCulture) + " (" + s.Addr + ") closed: " + CloseReason(ev.Code));
                    if (hadSlot)
                        StopHeartbeat(s.Addr);
                    Forget(s);
                    return;

                case NetEventKind.Log:
                    string m = "iwyknet: " + System.Text.Encoding.UTF8.GetString(data);
                    if (ev.Code >= NetCodes.LogError)
                        m_log.Error(m);
                    else if (ev.Code == NetCodes.LogWarn)
                        m_log.Warn(m);
                    else
                        m_log.Info(m);
                    return;
            }
        }

        static string CloseReason(int code)
        {
            switch (code)
            {
                case NetCodes.CloseHost: return "closed by the game";
                case NetCodes.CloseRemote: return "closed by the phone";
                case NetCodes.CloseTransport: return "transport failure or timeout";
                case NetCodes.CloseProtocol: return "protocol error";
                default: return "code " + code.ToString(CultureInfo.InvariantCulture);
            }
        }

        void OnHello(Session s, byte[] data)
        {
            if (!Envelope.TryParse(data, data.Length, out EnvelopeFrame f) || f.T != "hello" || !Envelope.IsValidUid(f.Uid))
            {
                m_log.Warn("session " + s.Id.ToString(CultureInfo.InvariantCulture) + ": first frame is not a valid hello, closing");
                s.State = State.Closing;
                m_transport.Close(s.Id);
                return;
            }
            s.Uid = f.Uid;

            // stale-session rule: the same device joined again from a new session
            foreach (Session other in m_order.ToArray())
            {
                if (other == s || other.Uid != s.Uid || !IsOpen(other))
                    continue;
                m_log.Info("session " + other.Id.ToString(CultureInfo.InvariantCulture) + " (" + other.Addr + ") replaced by session " + s.Id.ToString(CultureInfo.InvariantCulture));
                SendFrame(other, Envelope.Bye(Envelope.Replaced));
                other.State = State.Closing;
                m_transport.Close(other.Id);
                if (SlotOf(other.Addr) >= 0)
                    StopHeartbeat(other.Addr);
            }
            if (s.State == State.Joining)
                Decide(s);
        }

        void Decide(Session s)
        {
            JoinDecision d = AskPolicy(s);
            if (!IsOpen(s))
                return;  // closed while the policy ran
            switch (d.Outcome)
            {
                case JoinOutcome.Joined:
                    s.State = State.Joined;
                    SendFrame(s, Envelope.Joined());
                    m_log.Info("session " + s.Id.ToString(CultureInfo.InvariantCulture) + " (" + s.Addr + ") joined");
                    break;
                case JoinOutcome.Refused:
                    s.State = State.Refused;
                    SendFrame(s, Envelope.Refused(d.RefuseReason));
                    m_transport.Close(s.Id);
                    m_log.Info("session " + s.Id.ToString(CultureInfo.InvariantCulture) + " refused, reason " + d.RefuseReason.ToString(CultureInfo.InvariantCulture));
                    if (SlotOf(s.Addr) >= 0)
                        StopHeartbeat(s.Addr);
                    break;
                default:
                    if (s.State == State.Joining)
                    {
                        s.State = State.Pending;
                        SendFrame(s, Envelope.Pending());
                        m_log.Info("session " + s.Id.ToString(CultureInfo.InvariantCulture) + " pending");
                    }
                    s.NextRetry = m_now + NetLimits.JoinRetrySeconds;
                    break;
            }
        }

        JoinDecision AskPolicy(Session s)
        {
            try
            {
                if (Policy != null)
                    return Policy.OnJoinRequest(s.Addr, s.Uid);
                return ConnectClient(s.Addr) ? JoinDecision.Joined : JoinDecision.Refused(2);
            }
            catch (Exception e)
            {
                m_log.Error("join policy failed: " + e);
                return JoinDecision.Pending;
            }
        }

        void RetryPending()
        {
            if (m_order.Count == 0)
                return;
            foreach (Session s in m_order.ToArray())
                if (s.State == State.Pending && m_now >= s.NextRetry)
                    Decide(s);
        }

        void OnFrame(Session s, byte[] data)
        {
            int slot = SlotOf(s.Addr);
            if (slot < 0)
            {
                LogDropOnce(s);
                return;
            }
            m_slots[slot].Live.Reset();
            s.Received += data.Length;
            if (Envelope.TryParse(data, data.Length, out EnvelopeFrame f) && f.T == "msg")
            {
                if (f.HasId && f.Id == NetLimits.JsonTransferId && f.HasJson && NetJson.IsObject(data, f.Json))
                {
                    var json = new byte[f.Json.Length];
                    Buffer.BlockCopy(data, f.Json.Start, json, 0, json.Length);
                    Store(s, NetLimits.JsonTransferId, json);
                    NetAddr a = s.Addr;
                    Raise(() => Events?.OnReceived(a, NetLimits.JsonTransferId));
                }
            }
            NetAddr addr = s.Addr;
            Raise(() => Events?.OnReceiveActivity(addr));
        }

        void OnBinary(Session s, int transferId, byte[] data)
        {
            int slot = SlotOf(s.Addr);
            if (slot < 0)
            {
                LogDropOnce(s);
                return;
            }
            m_slots[slot].Live.Reset();
            s.Received += data.Length;
            NetAddr addr = s.Addr;
            if (data.Length > NetLimits.MaxGameContent)
            {
                m_log.Warn(addr + ": content of " + data.Length.ToString(CultureInfo.InvariantCulture) + " bytes is over the game's limit, dropped");
            }
            else
            {
                Store(s, transferId, data);
                Raise(() => Events?.OnReceived(addr, transferId));
            }
            Raise(() => Events?.OnReceiveActivity(addr));
        }

        void Store(Session s, int transferId, byte[] data)
        {
            int dropped = m_store.Add(s.Addr, transferId, data, m_now);
            if (dropped > 0)
                m_log.Warn("receive store full: dropped " + dropped.ToString(CultureInfo.InvariantCulture) + " unclaimed message(s)");
        }

        void LogDropOnce(Session s)
        {
            if (s.DropLogged)
                return;
            s.DropLogged = true;
            m_log.Warn("session " + s.Id.ToString(CultureInfo.InvariantCulture) + " (" + s.Addr + ") holds no slot: frames dropped");
        }

        void Forget(Session s)
        {
            m_sessions.Remove(s.Id);
            if (m_byAddr.TryGetValue(s.Addr, out Session cur) && cur == s)
                m_byAddr.Remove(s.Addr);
            m_order.Remove(s);
        }

        static bool IsOpen(Session s)
        {
            return s.State == State.Joining || s.State == State.Pending || s.State == State.Joined;
        }

        int SlotOf(NetAddr addr)
        {
            for (int i = 0; i < m_slots.Length; i++)
                if (m_slots[i] != null && m_slots[i].Addr == addr)
                    return i;
            return -1;
        }

        void SendFrame(Session s, byte[] frame)
        {
            int rc = m_transport.SendText(s.Id, frame, frame.Length);
            if (rc < 0 && rc != NetCodes.NoSession)
                m_log.Warn("session " + s.Id.ToString(CultureInfo.InvariantCulture) + ": send failed (" + rc.ToString(CultureInfo.InvariantCulture) + ")");
        }

        void Raise(Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                m_log.Error("game callback failed: " + e);
            }
        }

        // ---------------- the UDPDataComms surface (by NetAddr) ----------------

        public bool ConnectClient(NetAddr addr)
        {
            if (SlotOf(addr) >= 0)
                return true;
            if (!m_byAddr.TryGetValue(addr, out Session s) || !IsOpen(s))
                return false;
            for (int i = 0; i < m_slots.Length; i++)
            {
                if (m_slots[i] == null)
                {
                    m_slots[i] = new Slot { Addr = addr };
                    return true;
                }
            }
            return false;
        }

        public bool IsPeerConnected(NetAddr addr)
        {
            return SlotOf(addr) >= 0;
        }

        public bool AnyClientConnected()
        {
            foreach (Slot s in m_slots)
                if (s != null)
                    return true;
            return false;
        }

        public bool CanStartSend(NetAddr addr)
        {
            if (!m_byAddr.TryGetValue(addr, out Session s) || s.State != State.Joined)
                return false;
            int b = m_transport.Buffered(s.Id);
            return b >= 0 && b < NetLimits.MaxBufferedForSend;
        }

        public void StartSend(NetAddr addr, int transferId, byte[] data, int size)
        {
            if (!m_byAddr.TryGetValue(addr, out Session s) || s.State != State.Joined)
            {
                m_log.Warn("send to " + addr + " dropped: no joined session");
                return;
            }
            if (data == null || size <= 0 || size > data.Length)
            {
                m_log.Warn("send to " + addr + " dropped: empty or bad size");
            }
            else if (GameBatch.IsBatch(data, size))
            {
                if (GameBatch.TrySplit(data, size, m_segments))
                {
                    var segs = m_segments.ToArray();
                    foreach (BatchSegment seg in segs)
                        SendSegment(s, seg.TransferId, data, seg.Offset, seg.Length);
                }
                else
                {
                    m_log.Error("send to " + addr + " dropped: malformed batch of " + size.ToString(CultureInfo.InvariantCulture) + " bytes");
                }
            }
            else
            {
                SendSegment(s, transferId, data, 0, size);
            }
            m_completions.Add(new Completion { Addr = addr, OuterId = transferId });
        }

        void SendSegment(Session s, int id, byte[] data, int off, int len)
        {
            int rc;
            if (id == NetLimits.JsonTransferId)
            {
                if (Envelope.WrappedLength(len) > NetLimits.MaxText)
                {
                    m_log.Error("session " + s.Id.ToString(CultureInfo.InvariantCulture) + ": JSON message of " + len.ToString(CultureInfo.InvariantCulture) + " bytes is over the text frame limit, dropped");
                    return;
                }
                byte[] frame = Envelope.WrapMsg(data, off, len);
                rc = m_transport.SendText(s.Id, frame, frame.Length);
            }
            else if (id > 0)
            {
                if (len > NetLimits.MaxBinary)
                {
                    m_log.Error("session " + s.Id.ToString(CultureInfo.InvariantCulture) + ": content of " + len.ToString(CultureInfo.InvariantCulture) + " bytes is over the transfer limit, dropped");
                    return;
                }
                var copy = new byte[len];
                Buffer.BlockCopy(data, off, copy, 0, len);
                rc = m_transport.SendBinary(s.Id, id, copy, len);
            }
            else
            {
                m_log.Warn("session " + s.Id.ToString(CultureInfo.InvariantCulture) + ": segment with transfer id " + id.ToString(CultureInfo.InvariantCulture) + " dropped");
                return;
            }
            if (rc < 0)
                m_log.Error("session " + s.Id.ToString(CultureInfo.InvariantCulture) + ": send failed (" + rc.ToString(CultureInfo.InvariantCulture) + "), " + len.ToString(CultureInfo.InvariantCulture) + " bytes lost");
            else
                s.Sent += len;
        }

        public bool TryGetData(int transferId, NetAddr addr, byte[] buffer, ref int size)
        {
            int r = m_store.TryTake(transferId, addr, buffer, ref size);
            if (r == ReceiveStore.DroppedTooSmall)
                m_log.Error(addr + ": received message does not fit the game's buffer, dropped");
            return r == ReceiveStore.Taken;
        }

        // Frees the slot, says bye and closes a live session, raises OnHeartStopped. Reentrant:
        // a nested call for an address already handled in this chain is ignored, others are
        // queued and run after the current one.
        public void StopHeartbeat(NetAddr addr)
        {
            if (m_stopping)
            {
                if (addr != m_stoppingAddr && m_stopSeen.Add(addr))
                    m_stopQueue.Add(addr);
                return;
            }
            m_stopping = true;
            m_stopSeen.Clear();
            m_stopSeen.Add(addr);
            try
            {
                m_stoppingAddr = addr;
                DoStop(addr);
                while (m_stopQueue.Count > 0)
                {
                    NetAddr next = m_stopQueue[0];
                    m_stopQueue.RemoveAt(0);
                    m_stoppingAddr = next;
                    DoStop(next);
                }
            }
            finally
            {
                m_stopping = false;
                m_stopQueue.Clear();
                m_stopSeen.Clear();
            }
        }

        void DoStop(NetAddr addr)
        {
            int slot = SlotOf(addr);
            if (slot >= 0)
                m_slots[slot] = null;
            if (m_byAddr.TryGetValue(addr, out Session s) && IsOpen(s))
            {
                SendFrame(s, Envelope.Bye(Envelope.Closed));
                s.State = State.Closing;
                m_transport.Close(s.Id);
                m_log.Info("session " + s.Id.ToString(CultureInfo.InvariantCulture) + " (" + addr + ") dropped by the game");
            }
            Raise(() => Events?.OnHeartStopped(addr));
        }

        public int GetAddresses(List<NetAddr> outAddrs)
        {
            outAddrs.Clear();
            foreach (Slot s in m_slots)
                if (s != null)
                    outAddrs.Add(s.Addr);
            return outAddrs.Count;
        }

        public float GetDeathTime(NetAddr addr)
        {
            int i = SlotOf(addr);
            return i >= 0 ? m_slots[i].Live.Death : -1f;
        }

        public void ResetDeathTime(NetAddr addr)
        {
            int i = SlotOf(addr);
            if (i >= 0)
                m_slots[i].Live.Reset();
        }

        public void SetDeathTimeNearZero(NetAddr addr)
        {
            int i = SlotOf(addr);
            if (i >= 0)
                m_slots[i].Live.SetNearZero(m_lastDt);
        }

        public bool IsBadConnection(NetAddr addr)
        {
            int i = SlotOf(addr);
            return i >= 0 && m_slots[i].Live.IsBad;
        }

        public bool AnyActivity()
        {
            if (m_store.Count > 0 || m_completions.Count > 0)
                return true;
            foreach (Session s in m_order)
                if (s.State == State.Joined && m_transport.Buffered(s.Id) > 0)
                    return true;
            return false;
        }

        public long GetSentBytes(NetAddr addr)
        {
            return m_byAddr.TryGetValue(addr, out Session s) ? s.Sent : 0;
        }

        public void ResetSentBytes(NetAddr addr)
        {
            if (m_byAddr.TryGetValue(addr, out Session s))
                s.Sent = 0;
        }

        public long GetReceivedBytes(NetAddr addr)
        {
            return m_byAddr.TryGetValue(addr, out Session s) ? s.Received : 0;
        }

        public void ResetReceivedBytes(NetAddr addr)
        {
            if (m_byAddr.TryGetValue(addr, out Session s))
                s.Received = 0;
        }

        public void ResetStats()
        {
            foreach (Session s in m_order)
            {
                s.Sent = 0;
                s.Received = 0;
            }
        }

        public string StatsLine()
        {
            int slots = 0;
            foreach (Slot s in m_slots)
                if (s != null)
                    slots++;
            long sent = 0, recv = 0;
            foreach (Session s in m_order)
            {
                sent += s.Sent;
                recv += s.Received;
            }
            return "PortNet: " + SessionCount.ToString(CultureInfo.InvariantCulture) + " sessions, " + slots.ToString(CultureInfo.InvariantCulture)
                + " slots, sent " + sent.ToString(CultureInfo.InvariantCulture) + " B, received " + recv.ToString(CultureInfo.InvariantCulture)
                + " B, unclaimed " + m_store.Count.ToString(CultureInfo.InvariantCulture);
        }

        sealed class NullLog : INetLog
        {
            public void Info(string m) { }
            public void Warn(string m) { }
            public void Error(string m) { }
        }
    }
}
