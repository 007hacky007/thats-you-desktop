// Scripted fakes for the PortNet engine tests.

using System;
using System.Collections.Generic;
using System.Text;
using PortAudio.Tests;

namespace PortNet.Tests
{
    public sealed class FakeTransport : INetTransport
    {
        public readonly Queue<NetEvent> Pending = new Queue<NetEvent>();
        public readonly List<string> Calls = new List<string>();
        public readonly List<KeyValuePair<int, string>> Texts = new List<KeyValuePair<int, string>>();
        public readonly List<Tuple<int, int, byte[]>> Binaries = new List<Tuple<int, int, byte[]>>();
        public readonly List<int> Closes = new List<int>();
        public readonly Dictionary<int, int> BufferedBy = new Dictionary<int, int>();
        public readonly Queue<int> StartResults = new Queue<int>();
        public readonly List<int> StartPorts = new List<int>();
        public int StopCount;
        public int LogLevel = -1;
        public bool Started;
        public string EndpointValue = "192.0.2.10:47000";
        public string LastErrorValue;
        public int QrCalls;

        public int Start(int udpPort, string lanIp, string certPath)
        {
            StartPorts.Add(udpPort);
            int rc = StartResults.Count > 0 ? StartResults.Dequeue() : NetCodes.Ok;
            Started = rc == NetCodes.Ok;
            Calls.Add("start " + udpPort);
            return rc;
        }

        public void Stop()
        {
            StopCount++;
            Started = false;
            Calls.Add("stop");
        }

        public void SetLogLevel(int level)
        {
            LogLevel = level;
        }

        public bool Poll(out NetEvent ev)
        {
            if (Pending.Count == 0)
            {
                ev = default;
                return false;
            }
            ev = Pending.Dequeue();
            return true;
        }

        public int SendText(int session, byte[] utf8, int len)
        {
            string s = Encoding.UTF8.GetString(utf8, 0, len);
            Texts.Add(new KeyValuePair<int, string>(session, s));
            Calls.Add("text " + session + " " + s);
            return NetCodes.Ok;
        }

        public int SendBinary(int session, int transferId, byte[] data, int len)
        {
            var copy = new byte[len];
            Buffer.BlockCopy(data, 0, copy, 0, len);
            Binaries.Add(Tuple.Create(session, transferId, copy));
            Calls.Add("bin " + session + " " + transferId + " " + len);
            return NetCodes.Ok;
        }

        public int Close(int session)
        {
            Closes.Add(session);
            Calls.Add("close " + session);
            return NetCodes.Ok;
        }

        public int Buffered(int session)
        {
            return BufferedBy.TryGetValue(session, out int b) ? b : 0;
        }

        public string Endpoint => Started ? EndpointValue : null;

        public string JoinUrl(string baseUrl)
        {
            return Started && baseUrl != null ? baseUrl + "#h=192.0.2.10&p=47000" : null;
        }

        public bool Qr(string text, out byte[] modules, out int side)
        {
            QrCalls++;
            side = 21;
            modules = new byte[side * side];
            modules[0] = 1;
            return true;
        }

        public string LastError => LastErrorValue;

        // ---- scripting ----

        public void Open(int session)
        {
            Pending.Enqueue(new NetEvent { Kind = NetEventKind.Opened, Session = session, Data = Encoding.ASCII.GetBytes("192.0.2.50:5000") });
        }

        public void Text(int session, string text)
        {
            Pending.Enqueue(new NetEvent { Kind = NetEventKind.Text, Session = session, Data = Encoding.UTF8.GetBytes(text) });
        }

        public void Hello(int session, string uid)
        {
            Text(session, "{\"t\":\"hello\",\"token\":\"tok\",\"uid\":\"" + uid + "\"}");
        }

        public void Bin(int session, int transferId, byte[] data)
        {
            Pending.Enqueue(new NetEvent { Kind = NetEventKind.Binary, Session = session, TransferId = transferId, Data = data });
        }

        public void Closed(int session, int code = NetCodes.CloseRemote)
        {
            Pending.Enqueue(new NetEvent { Kind = NetEventKind.Closed, Session = session, Code = code, Data = new byte[0] });
        }

        public List<string> TextsTo(int session)
        {
            var l = new List<string>();
            foreach (var kv in Texts)
                if (kv.Key == session)
                    l.Add(kv.Value);
            return l;
        }
    }

    public sealed class FakeEvents : INetEvents
    {
        public readonly List<string> Log = new List<string>();
        public Action<NetAddr> OnStop;
        public Action<NetAddr, int> OnRecv;

        public void OnReceived(NetAddr addr, int transferId)
        {
            Log.Add("recv " + addr.IpString + " " + transferId);
            OnRecv?.Invoke(addr, transferId);
        }

        public void OnReceiveActivity(NetAddr addr)
        {
            Log.Add("act " + addr.IpString);
        }

        public void OnSendActivity(NetAddr addr)
        {
            Log.Add("sact " + addr.IpString);
        }

        public void OnSendComplete(NetAddr addr, int outerTransferId)
        {
            Log.Add("done " + addr.IpString + " " + outerTransferId);
        }

        public void OnHeartStopped(NetAddr addr)
        {
            Log.Add("stop " + addr.IpString);
            OnStop?.Invoke(addr);
        }

        public List<string> Only(string prefix)
        {
            return Log.FindAll(s => s.StartsWith(prefix + " ", StringComparison.Ordinal));
        }
    }

    public sealed class FakePolicy : INetJoinPolicy
    {
        public readonly List<string> Calls = new List<string>();
        public Func<NetAddr, string, JoinDecision> Decide;
        public NetEngine Engine;

        public JoinDecision OnJoinRequest(NetAddr addr, string uid)
        {
            Calls.Add(addr.IpString + " " + uid);
            if (Decide != null)
                return Decide(addr, uid);
            return Engine.ConnectClient(addr) ? JoinDecision.Joined : JoinDecision.Refused(2);
        }
    }

    public sealed class ListLog : INetLog
    {
        public readonly List<string> Lines = new List<string>();

        public void Info(string m)
        {
            Lines.Add("I " + m);
        }

        public void Warn(string m)
        {
            Lines.Add("W " + m);
        }

        public void Error(string m)
        {
            Lines.Add("E " + m);
        }

        public bool Has(string level, string fragment)
        {
            foreach (string l in Lines)
                if (l.StartsWith(level + " ", StringComparison.Ordinal) && l.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                    return true;
            return false;
        }
    }

    public sealed class Rig
    {
        public readonly FakeTransport T = new FakeTransport();
        public readonly FakeEvents Ev = new FakeEvents();
        public readonly FakePolicy Policy = new FakePolicy();
        public readonly ListLog Log = new ListLog();
        public readonly NetEngine E;
        public float Now;

        public const string PageUrl = "https://example.invalid/iwyk/";

        public Rig(bool start = true)
        {
            E = new NetEngine(T, Log) { Policy = Policy, Events = Ev };
            Policy.Engine = E;
            if (start)
                PaAssert.True(E.Start(new NetConfig { PhonePageUrl = PageUrl }, "/tmp/cert.pem"), "engine start");
        }

        public static NetAddr Addr(int n)
        {
            return NetAddrAllocator.FromCounter(n);
        }

        public static string Ip(int n)
        {
            return Addr(n).IpString;
        }

        public void Update(float dt = 0.1f, bool heartbeats = true)
        {
            Now += dt;
            E.Update(dt, Now, heartbeats);
        }

        // Opens session id, says hello with uid and runs one Update.
        public void Join(int session, string uid)
        {
            T.Open(session);
            T.Hello(session, uid);
            Update();
        }
    }
}
