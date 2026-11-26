// NetEngine behaviour (contract 4.5) against the scripted FakeTransport.

using System;
using System.Collections.Generic;
using System.Text;
using PortAudio.Tests;

namespace PortNet.Tests
{
    public static class EngineJoinTests
    {
        [PaTest]
        public static void HelloJoined()
        {
            var r = new Rig();
            r.Join(1, "u1");
            PaAssert.SequenceEqual(new[] { "{\"t\":\"joined\"}" }, r.T.TextsTo(1));
            PaAssert.SequenceEqual(new[] { Rig.Ip(1) + " u1" }, r.Policy.Calls);
            PaAssert.True(r.E.IsPeerConnected(Rig.Addr(1)));
            PaAssert.True(r.E.AnyClientConnected());
            PaAssert.Equal(1, r.E.SessionCount);
            PaAssert.True(r.E.CanStartSend(Rig.Addr(1)));
            PaAssert.Equal(0, r.Ev.Log.Count, string.Join(",", r.Ev.Log));
        }

        [PaTest]
        public static void HelloRefused()
        {
            var r = new Rig();
            r.Policy.Decide = (a, u) => JoinDecision.Refused(1);
            r.Join(1, "u1");
            PaAssert.SequenceEqual(new[] { "{\"t\":\"refused\",\"reason\":1}" }, r.T.TextsTo(1));
            PaAssert.SequenceEqual(new[] { 1 }, r.T.Closes);
            PaAssert.False(r.E.IsPeerConnected(Rig.Addr(1)));
            r.T.Text(1, "{\"t\":\"ping\"}");
            r.T.Closed(1, NetCodes.CloseHost);
            r.Update();
            PaAssert.Equal(0, r.Ev.Log.Count, "no events for a refused session");
            PaAssert.Equal(0, r.E.SessionCount);
        }

        [PaTest]
        public static void RefusedAfterTakingASlotStopsIt()
        {
            var r = new Rig();
            r.Policy.Decide = (a, u) => r.E.ConnectClient(a) ? JoinDecision.Refused(3) : JoinDecision.Refused(2);
            r.Join(1, "u1");
            PaAssert.SequenceEqual(new[] { "{\"t\":\"refused\",\"reason\":3}" }, r.T.TextsTo(1), "no bye after refused");
            PaAssert.SequenceEqual(new[] { "stop " + Rig.Ip(1) }, r.Ev.Log);
            PaAssert.False(r.E.IsPeerConnected(Rig.Addr(1)));
        }

        [PaTest]
        public static void PendingRetriesEvery1500msThenJoins()
        {
            var r = new Rig();
            int calls = 0;
            r.Policy.Decide = (a, u) =>
            {
                r.E.ConnectClient(a);
                return ++calls < 3 ? JoinDecision.Pending : JoinDecision.Joined;
            };
            r.Join(1, "u1");
            PaAssert.SequenceEqual(new[] { "{\"t\":\"pending\"}" }, r.T.TextsTo(1));
            PaAssert.False(r.E.CanStartSend(Rig.Addr(1)), "no game data while pending");
            r.Update(1.0f);
            PaAssert.Equal(1, calls);
            r.Update(0.6f);
            PaAssert.Equal(2, calls);
            PaAssert.SequenceEqual(new[] { "{\"t\":\"pending\"}" }, r.T.TextsTo(1), "pending is sent once");
            r.Update(1.0f);
            PaAssert.Equal(2, calls);
            r.Update(0.6f);
            PaAssert.Equal(3, calls);
            PaAssert.SequenceEqual(new[] { "{\"t\":\"pending\"}", "{\"t\":\"joined\"}" }, r.T.TextsTo(1));
            PaAssert.True(r.E.CanStartSend(Rig.Addr(1)));
            r.Update(2f);
            PaAssert.Equal(3, calls, "no retries after the decision");
        }

        [PaTest]
        public static void PendingThenRefused()
        {
            var r = new Rig();
            int calls = 0;
            r.Policy.Decide = (a, u) => ++calls < 2 ? JoinDecision.Pending : JoinDecision.Refused(1);
            r.Join(1, "u1");
            r.Update(1.6f);
            PaAssert.SequenceEqual(new[] { "{\"t\":\"pending\"}", "{\"t\":\"refused\",\"reason\":1}" }, r.T.TextsTo(1));
            PaAssert.SequenceEqual(new[] { 1 }, r.T.Closes);
        }

        [PaTest]
        public static void PendingSessionClosedStopsRetrying()
        {
            var r = new Rig();
            int calls = 0;
            r.Policy.Decide = (a, u) => { calls++; return JoinDecision.Pending; };
            r.Join(1, "u1");
            r.T.Closed(1);
            r.Update(2f);
            r.Update(2f);
            PaAssert.Equal(1, calls);
        }

        [PaTest]
        public static void SameUidReplacesTheOldSessionBeforeTheDecision()
        {
            var r = new Rig();
            r.Join(1, "u1");
            r.Policy.Decide = (a, u) =>
            {
                r.Ev.Log.Add("policy " + a.IpString);
                return r.E.ConnectClient(a) ? JoinDecision.Joined : JoinDecision.Refused(2);
            };
            r.Join(2, "u1");
            PaAssert.SequenceEqual(new[] { "stop " + Rig.Ip(1), "policy " + Rig.Ip(2) }, r.Ev.Log);
            PaAssert.SequenceEqual(new[] { "{\"t\":\"joined\"}", "{\"t\":\"bye\",\"reason\":\"replaced\"}" }, r.T.TextsTo(1));
            PaAssert.SequenceEqual(new[] { 1 }, r.T.Closes);
            PaAssert.SequenceEqual(new[] { "{\"t\":\"joined\"}" }, r.T.TextsTo(2));
            PaAssert.False(r.E.IsPeerConnected(Rig.Addr(1)));
            PaAssert.True(r.E.IsPeerConnected(Rig.Addr(2)));
            r.T.Closed(1, NetCodes.CloseHost);
            r.Update();
            PaAssert.Equal(1, r.Ev.Only("stop").Count, "the old session's CLOSED raises nothing more");
            PaAssert.Equal(1, r.E.SessionCount);
        }

        [PaTest]
        public static void ReplacedPendingSessionWithoutSlotGetsByeOnly()
        {
            var r = new Rig();
            r.Policy.Decide = (a, u) => JoinDecision.Pending;
            r.Join(1, "u1");
            r.Join(2, "u1");
            PaAssert.SequenceEqual(new[] { "{\"t\":\"pending\"}", "{\"t\":\"bye\",\"reason\":\"replaced\"}" }, r.T.TextsTo(1));
            PaAssert.Equal(0, r.Ev.Only("stop").Count);
        }

        [PaTest]
        public static void SixSlotsThenRefusedTwo()
        {
            var r = new Rig();
            for (int i = 1; i <= 7; i++)
                r.Join(i, "u" + i);
            for (int i = 1; i <= 6; i++)
                PaAssert.SequenceEqual(new[] { "{\"t\":\"joined\"}" }, r.T.TextsTo(i), "session " + i);
            PaAssert.SequenceEqual(new[] { "{\"t\":\"refused\",\"reason\":2}" }, r.T.TextsTo(7));
            var addrs = new List<NetAddr>();
            PaAssert.Equal(6, r.E.GetAddresses(addrs));
            for (int i = 0; i < 6; i++)
                PaAssert.Equal(Rig.Addr(i + 1), addrs[i]);

            r.E.StopHeartbeat(Rig.Addr(3));
            r.Join(8, "u8");
            PaAssert.SequenceEqual(new[] { "{\"t\":\"joined\"}" }, r.T.TextsTo(8));
            r.E.GetAddresses(addrs);
            PaAssert.Equal(Rig.Addr(8), addrs[2], "slot order: the freed slot is reused");
        }

        [PaTest]
        public static void ConnectClientRules()
        {
            var r = new Rig();
            r.Policy.Decide = (a, u) => JoinDecision.Pending;
            r.Join(1, "u1");
            PaAssert.False(r.E.ConnectClient(Rig.Addr(99)), "no phantom peers");
            PaAssert.True(r.E.ConnectClient(Rig.Addr(1)));
            PaAssert.True(r.E.ConnectClient(Rig.Addr(1)), "an address that holds a slot succeeds again");
            var addrs = new List<NetAddr>();
            PaAssert.Equal(1, r.E.GetAddresses(addrs));
        }

        [PaTest]
        public static void AddressesAreUniqueAndNotReused()
        {
            var r = new Rig();
            r.Join(1, "a");
            r.T.Closed(1);
            r.Update();
            r.Join(2, "a");
            PaAssert.True(r.E.IsPeerConnected(Rig.Addr(2)));
            PaAssert.False(r.E.IsPeerConnected(Rig.Addr(1)));
        }

        [PaTest]
        public static void BadHelloClosesAndSecondHelloIsIgnored()
        {
            var r = new Rig();
            r.T.Open(1);
            r.T.Text(1, "{\"t\":\"ping\"}");
            r.T.Open(2);
            r.T.Text(2, "{\"t\":\"hello\",\"token\":\"x\",\"uid\":\"\"}");
            r.Update();
            PaAssert.SequenceEqual(new[] { 1, 2 }, r.T.Closes);
            PaAssert.Equal(0, r.Policy.Calls.Count);

            r.Join(3, "u3");
            r.T.Hello(3, "other");
            r.Update();
            PaAssert.Equal(1, r.Policy.Calls.Count);
            PaAssert.SequenceEqual(new[] { "{\"t\":\"joined\"}" }, r.T.TextsTo(3));
        }

        [PaTest]
        public static void PolicyExceptionMeansPending()
        {
            var r = new Rig();
            r.Policy.Decide = (a, u) => throw new InvalidOperationException("boom");
            r.Join(1, "u1");
            PaAssert.SequenceEqual(new[] { "{\"t\":\"pending\"}" }, r.T.TextsTo(1));
            PaAssert.True(r.Log.Has("E", "boom"));
        }
    }

    public static class EngineReceiveTests
    {
        const string Json = "{\"TypeString\":\"ClientRequestPlayerIDMessage\", \"UID\":\"u\\u00e9\"}";

        static string Msg(string json)
        {
            return "{\"t\":\"msg\",\"id\":-7503,\"json\":" + json + "}";
        }

        [PaTest]
        public static void MsgIsStoredRawAndAnnounced()
        {
            var r = new Rig();
            r.Join(1, "u1");
            r.T.Text(1, Msg(Json));
            r.Update();
            string ip = Rig.Ip(1);
            PaAssert.SequenceEqual(new[] { "recv " + ip + " -7503", "act " + ip }, r.Ev.Log);
            var buf = new byte[131072];
            int size = 0;
            PaAssert.True(r.E.TryGetData(-7503, Rig.Addr(1), buf, ref size));
            PaAssert.Equal(Json, Encoding.UTF8.GetString(buf, 0, size));
            PaAssert.False(r.E.TryGetData(-7503, Rig.Addr(1), buf, ref size));
        }

        [PaTest]
        public static void OtherFramesOnlyCountAsActivity()
        {
            var r = new Rig();
            r.Join(1, "u1");
            r.T.Text(1, "{\"t\":\"ping\"}");
            r.T.Text(1, "{\"t\":\"whatever\",\"x\":1}");
            r.T.Text(1, "{\"t\":\"msg\",\"id\":5,\"json\":{}}");
            r.T.Text(1, "{\"t\":\"msg\",\"id\":-7503,\"json\":\"{}\"}");
            r.T.Text(1, "not json");
            r.Update();
            PaAssert.Equal(5, r.Ev.Only("act").Count);
            PaAssert.Equal(0, r.Ev.Only("recv").Count);
            PaAssert.False(r.E.AnyActivity());
        }

        [PaTest]
        public static void BinaryIsStoredUnderItsTransferId()
        {
            var r = new Rig();
            r.Join(1, "u1");
            var data = new byte[131062];
            data[131061] = 9;
            r.T.Bin(1, 42, data);
            r.T.Bin(1, 43, new byte[131063]);
            r.Update();
            string ip = Rig.Ip(1);
            PaAssert.SequenceEqual(new[] { "recv " + ip + " 42", "act " + ip, "act " + ip }, r.Ev.Log);
            PaAssert.True(r.Log.Has("W", "over the game's limit"));
            var buf = new byte[131062];
            int size = 0;
            PaAssert.True(r.E.TryGetData(42, Rig.Addr(1), buf, ref size));
            PaAssert.Equal(131062, size);
            PaAssert.Equal((byte)9, buf[131061]);
        }

        [PaTest]
        public static void TooSmallBufferDropsAndLogs()
        {
            var r = new Rig();
            r.Join(1, "u1");
            r.T.Bin(1, 42, new byte[100]);
            r.Update();
            int size = 0;
            PaAssert.False(r.E.TryGetData(42, Rig.Addr(1), new byte[50], ref size));
            PaAssert.True(r.Log.Has("E", "does not fit"));
            PaAssert.False(r.E.TryGetData(42, Rig.Addr(1), new byte[100], ref size));
        }

        [PaTest]
        public static void FramesWithoutSlotAreDroppedAndLoggedOnce()
        {
            var r = new Rig();
            r.Policy.Decide = (a, u) => JoinDecision.Joined;  // joined without ConnectClient
            r.Join(1, "u1");
            r.T.Text(1, Msg(Json));
            r.T.Text(1, Msg(Json));
            r.T.Bin(1, 5, new byte[3]);
            r.Update();
            PaAssert.Equal(0, r.Ev.Log.Count);
            int n = 0;
            foreach (string l in r.Log.Lines)
                if (l.Contains("holds no slot"))
                    n++;
            PaAssert.Equal(1, n);
        }

        [PaTest]
        public static void UnclaimedDataIsAbandonedAfterTwoSeconds()
        {
            var r = new Rig();
            r.Join(1, "u1");
            r.T.Text(1, Msg(Json));
            r.Update(0.1f);
            r.Update(1.8f);
            PaAssert.True(r.E.AnyActivity());
            r.Update(0.3f);
            PaAssert.False(r.E.AnyActivity());
            int size = 0;
            PaAssert.False(r.E.TryGetData(-7503, Rig.Addr(1), new byte[1000], ref size));
            PaAssert.True(r.Log.Has("W", "not claimed"));
        }

        [PaTest]
        public static void EveryFrameResetsTheDeathTimer()
        {
            var r = new Rig();
            r.Join(1, "u1");
            r.Update(10f);
            PaAssert.Near(17f - 0.1f - 10f, r.E.GetDeathTime(Rig.Addr(1)), 1e-4);
            r.T.Text(1, "{\"t\":\"ping\"}");
            r.Update(0.5f);
            PaAssert.Near(16.5f, r.E.GetDeathTime(Rig.Addr(1)), 1e-4);
            r.T.Bin(1, 3, new byte[1]);
            r.Update(0.25f);
            PaAssert.Near(16.75f, r.E.GetDeathTime(Rig.Addr(1)), 1e-4);
        }

        [PaTest]
        public static void LogEventsAreForwarded()
        {
            var r = new Rig();
            r.T.Pending.Enqueue(new NetEvent { Kind = NetEventKind.Log, Code = NetCodes.LogWarn, Data = Encoding.UTF8.GetBytes("careful") });
            r.T.Pending.Enqueue(new NetEvent { Kind = NetEventKind.Log, Code = NetCodes.LogError, Data = Encoding.UTF8.GetBytes("bad") });
            r.Update();
            PaAssert.True(r.Log.Has("W", "iwyknet: careful"));
            PaAssert.True(r.Log.Has("E", "iwyknet: bad"));
        }

        [PaTest]
        public static void DrainBudgetPerUpdate()
        {
            var r = new Rig();
            r.Join(1, "u1");
            for (int i = 0; i < 2500; i++)
                r.T.Text(1, "{\"t\":\"ping\"}");
            r.Update();
            PaAssert.Equal(2000, r.Ev.Only("act").Count);
            r.Update();
            PaAssert.Equal(2500, r.Ev.Only("act").Count);
        }
    }

    public static class EngineSendTests
    {
        static byte[] B(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        [PaTest]
        public static void JsonIsWrappedUnchanged()
        {
            var r = new Rig();
            r.Join(1, "u1");
            byte[] json = B("{\"TypeString\":\"InterfaceVersionMessage\",\"InterfaceVersion\":\"2016-07-12-1645\"}");
            var padded = new byte[json.Length + 10];
            Array.Copy(json, padded, json.Length);
            r.E.StartSend(Rig.Addr(1), -7503, padded, json.Length);
            PaAssert.Equal("{\"t\":\"msg\",\"id\":-7503,\"json\":{\"TypeString\":\"InterfaceVersionMessage\",\"InterfaceVersion\":\"2016-07-12-1645\"}}", r.T.TextsTo(1)[1]);
        }

        [PaTest]
        public static void BatchIsSplitInOrder()
        {
            var r = new Rig();
            r.Join(1, "u1");
            r.T.Calls.Clear();
            var content = new byte[5000];
            content[4999] = 7;
            byte[] batch = GameBatchTests.Batch(-7503, "{\"a\":1}", -7503, "{\"b\":2}", 31, content);
            r.E.StartSend(Rig.Addr(1), 31, batch, batch.Length);
            Array.Clear(batch, 0, batch.Length);  // the game reuses its buffer right away
            PaAssert.SequenceEqual(new[]
            {
                "text 1 {\"t\":\"msg\",\"id\":-7503,\"json\":{\"a\":1}}",
                "text 1 {\"t\":\"msg\",\"id\":-7503,\"json\":{\"b\":2}}",
                "bin 1 31 5000",
            }, r.T.Calls);
            PaAssert.Equal((byte)7, r.T.Binaries[0].Item3[4999]);
        }

        [PaTest]
        public static void CompletionsComeOnTheNextUpdateInCallOrder()
        {
            var r = new Rig();
            r.Join(1, "u1");
            r.Join(2, "u2");
            byte[] j = B("{\"x\":1}");
            r.E.StartSend(Rig.Addr(2), -7503, j, j.Length);
            byte[] batch = GameBatchTests.Batch(-7503, "{}", 8, new byte[10]);
            r.E.StartSend(Rig.Addr(1), 99, batch, batch.Length);
            PaAssert.Equal(0, r.Ev.Log.Count, "nothing during StartSend");
            PaAssert.True(r.E.AnyActivity());
            r.Update();
            string a = Rig.Ip(1), b = Rig.Ip(2);
            PaAssert.SequenceEqual(new[] { "sact " + b, "done " + b + " -7503", "sact " + a, "done " + a + " 99" }, r.Ev.Log);
            PaAssert.False(r.E.AnyActivity());
            r.Update();
            PaAssert.Equal(4, r.Ev.Log.Count, "each completion once");
        }

        [PaTest]
        public static void SendsDuringAnUpdateCompleteOnTheFollowingOne()
        {
            var r = new Rig();
            r.Join(1, "u1");
            byte[] j = B("{\"x\":1}");
            r.Ev.OnRecv = (addr, id) => r.E.StartSend(addr, -7503, j, j.Length);
            r.T.Text(1, "{\"t\":\"msg\",\"id\":-7503,\"json\":{}}");
            r.Update();
            PaAssert.Equal(0, r.Ev.Only("done").Count);
            r.Update();
            PaAssert.Equal(1, r.Ev.Only("done").Count);
        }

        [PaTest]
        public static void DropsAndCaps()
        {
            var r = new Rig();
            r.Policy.Decide = (a, u) => { r.E.ConnectClient(a); return u == "p" ? JoinDecision.Pending : JoinDecision.Joined; };
            r.Join(1, "u1");
            r.Join(2, "p");
            r.T.Calls.Clear();
            byte[] j = B("{\"x\":1}");
            r.E.StartSend(Rig.Addr(2), -7503, j, j.Length);
            r.E.StartSend(Rig.Addr(77), -7503, j, j.Length);
            PaAssert.Equal(0, r.T.Calls.Count, "no sends to pending or unknown sessions");

            var big = new byte[65536];
            big[0] = (byte)'{';
            r.E.StartSend(Rig.Addr(1), -7503, big, big.Length);
            PaAssert.True(r.Log.Has("E", "over the text frame limit"));
            byte[] bad = GameBatchTests.Batch(-7503, "{}");
            bad[bad.Length - 1] = 0;
            r.E.StartSend(Rig.Addr(1), 5, bad, bad.Length);
            PaAssert.True(r.Log.Has("E", "malformed batch"));
            byte[] zero = GameBatchTests.Batch(0, "{}");
            r.E.StartSend(Rig.Addr(1), 6, zero, zero.Length);
            PaAssert.True(r.Log.Has("W", "transfer id 0"));
            PaAssert.Equal(0, r.T.Calls.Count);
            r.Update();
            PaAssert.SequenceEqual(new[] { "done " + Rig.Ip(1) + " -7503", "done " + Rig.Ip(1) + " 5", "done " + Rig.Ip(1) + " 6" }, r.Ev.Only("done"),
                "accepted sends complete even when dropped, so game queues never stall");

            byte[] max = new byte[65536 - Envelope.WrappedLength(0)];
            max[0] = (byte)'{';
            r.E.StartSend(Rig.Addr(1), -7503, max, max.Length);
            List<string> sent = r.T.TextsTo(1);
            PaAssert.Equal(2, sent.Count);
            PaAssert.Equal(65536, Encoding.UTF8.GetByteCount(sent[1]), "a frame of exactly 65536 bytes is sent");
        }

        [PaTest]
        public static void CanStartSendFollowsBackpressure()
        {
            var r = new Rig();
            r.Join(1, "u1");
            NetAddr a = Rig.Addr(1);
            r.T.BufferedBy[1] = (1 << 20) - 1;
            PaAssert.True(r.E.CanStartSend(a));
            r.T.BufferedBy[1] = 1 << 20;
            PaAssert.False(r.E.CanStartSend(a));
            r.T.BufferedBy[1] = NetCodes.NoSession;
            PaAssert.False(r.E.CanStartSend(a));
            r.T.BufferedBy[1] = 5;
            PaAssert.True(r.E.AnyActivity(), "queued bytes count as activity");
            PaAssert.False(r.E.CanStartSend(Rig.Addr(2)));
        }

        [PaTest]
        public static void ByteCounters()
        {
            var r = new Rig();
            r.Join(1, "u1");
            NetAddr a = Rig.Addr(1);
            r.T.Bin(1, 4, new byte[100]);
            r.Update();
            PaAssert.Equal(100L, r.E.GetReceivedBytes(a));
            byte[] batch = GameBatchTests.Batch(-7503, "{}", 8, new byte[10]);
            r.E.StartSend(a, 8, batch, batch.Length);
            PaAssert.Equal(12L, r.E.GetSentBytes(a));
            r.E.ResetSentBytes(a);
            PaAssert.Equal(0L, r.E.GetSentBytes(a));
            r.E.ResetStats();
            PaAssert.Equal(0L, r.E.GetReceivedBytes(a));
            PaAssert.True(r.E.StatsLine().StartsWith("PortNet: 1 sessions, 1 slots", StringComparison.Ordinal), r.E.StatsLine());
        }
    }

    public static class EngineLivenessTests
    {
        [PaTest]
        public static void ClosedStopsHeartbeatWithoutBye()
        {
            var r = new Rig();
            r.Join(1, "u1");
            r.T.Closed(1, NetCodes.CloseTransport);
            r.Update();
            PaAssert.SequenceEqual(new[] { "stop " + Rig.Ip(1) }, r.Ev.Log);
            PaAssert.SequenceEqual(new[] { "{\"t\":\"joined\"}" }, r.T.TextsTo(1));
            PaAssert.Equal(0, r.T.Closes.Count);
            PaAssert.False(r.E.IsPeerConnected(Rig.Addr(1)));
            PaAssert.Equal(-1f, r.E.GetDeathTime(Rig.Addr(1)));
            PaAssert.Equal(0, r.E.SessionCount);
        }

        [PaTest]
        public static void GameStopSaysByeClosesAndKeepsStoredData()
        {
            var r = new Rig();
            r.Join(1, "u1");
            r.T.Text(1, "{\"t\":\"msg\",\"id\":-7503,\"json\":{\"k\":1}}");
            r.Update();
            r.Ev.Log.Clear();
            r.E.StopHeartbeat(Rig.Addr(1));
            PaAssert.SequenceEqual(new[] { "{\"t\":\"joined\"}", "{\"t\":\"bye\",\"reason\":\"closed\"}" }, r.T.TextsTo(1));
            PaAssert.SequenceEqual(new[] { 1 }, r.T.Closes);
            PaAssert.SequenceEqual(new[] { "stop " + Rig.Ip(1) }, r.Ev.Log);
            int size = 0;
            PaAssert.True(r.E.TryGetData(-7503, Rig.Addr(1), new byte[64], ref size), "stored data stays claimable");
            r.E.StopHeartbeat(Rig.Addr(1));
            PaAssert.Equal(2, r.T.TextsTo(1).Count, "no second bye");
            PaAssert.Equal(2, r.Ev.Only("stop").Count, "OnHeartStopped is always raised");
            r.E.StopHeartbeat(Rig.Addr(50));
            PaAssert.Equal(3, r.Ev.Only("stop").Count, "even for unknown addresses");
            r.T.Text(1, "{\"t\":\"ping\"}");
            r.T.Closed(1, NetCodes.CloseHost);
            r.Update();
            PaAssert.Equal(3, r.Ev.Only("stop").Count);
            PaAssert.Equal(0, r.Ev.Only("act").Count, "frames of a closing session are ignored");
        }

        [PaTest]
        public static void StopHeartbeatReentrancy()
        {
            var r = new Rig();
            r.Join(1, "u1");
            r.Join(2, "u2");
            r.Join(3, "u3");
            NetAddr a = Rig.Addr(1), b = Rig.Addr(2), c = Rig.Addr(3);
            var order = new List<string>();
            r.Ev.OnStop = x =>
            {
                order.Add("enter " + x.IpString);
                r.E.StopHeartbeat(x);          // same address: ignored
                if (x == a)
                {
                    r.E.StopHeartbeat(b);      // queued
                    r.E.StopHeartbeat(c);      // queued
                    r.E.StopHeartbeat(b);      // already queued
                }
                if (x == b)
                    r.E.StopHeartbeat(a);      // already handled in this chain
                order.Add("leave " + x.IpString);
            };
            r.E.StopHeartbeat(a);
            PaAssert.SequenceEqual(new[]
            {
                "enter " + a.IpString, "leave " + a.IpString,
                "enter " + b.IpString, "leave " + b.IpString,
                "enter " + c.IpString, "leave " + c.IpString,
            }, order);
            foreach (int s in new[] { 1, 2, 3 })
                PaAssert.Equal("{\"t\":\"bye\",\"reason\":\"closed\"}", r.T.TextsTo(s)[1]);
            PaAssert.False(r.E.AnyClientConnected());
            r.Ev.OnStop = null;
            r.E.StopHeartbeat(a);
            PaAssert.Equal(4, r.Ev.Only("stop").Count, "a new chain after the first one finished");
        }

        [PaTest]
        public static void DeathTimerStopsTheHeartbeat()
        {
            var r = new Rig();
            r.Join(1, "u1");
            NetAddr a = Rig.Addr(1);
            r.Update(5f, false);
            PaAssert.Near(16.9f, r.E.GetDeathTime(a), 1e-4, "no countdown without heartbeats");
            r.Update(5f);
            r.Update(5f);
            PaAssert.False(r.E.IsBadConnection(a));
            r.Update(5f);
            PaAssert.Near(1.9f, r.E.GetDeathTime(a), 1e-4);
            PaAssert.True(r.E.IsBadConnection(a));
            PaAssert.Equal(0, r.Ev.Only("stop").Count);
            r.Update(2f);
            PaAssert.SequenceEqual(new[] { "stop " + a.IpString }, r.Ev.Only("stop"));
            PaAssert.Equal("{\"t\":\"bye\",\"reason\":\"closed\"}", r.T.TextsTo(1)[1]);
            PaAssert.Equal(-1f, r.E.GetDeathTime(a));
            PaAssert.False(r.E.IsBadConnection(a));
        }

        [PaTest]
        public static void ResetAndNearZero()
        {
            var r = new Rig();
            r.Join(1, "u1");
            NetAddr a = Rig.Addr(1);
            r.Update(3f);
            r.E.ResetDeathTime(a);
            PaAssert.Near(17f, r.E.GetDeathTime(a));
            r.Update(0.25f);
            r.E.SetDeathTimeNearZero(a);
            PaAssert.Near(0.25f, r.E.GetDeathTime(a), 1e-6, "the last Update's dt");
            r.Update(0.25f);
            PaAssert.SequenceEqual(new[] { "stop " + a.IpString }, r.Ev.Only("stop"));
            r.E.SetDeathTimeNearZero(Rig.Addr(9));
            r.E.ResetDeathTime(Rig.Addr(9));
            PaAssert.Equal(-1f, r.E.GetDeathTime(Rig.Addr(9)));
        }
    }

    public static class EngineStartTests
    {
        [PaTest]
        public static void StopsTransportFirstAndSetsLogLevel()
        {
            var r = new Rig(false);
            r.E.Start(new NetConfig { PhonePageUrl = Rig.PageUrl, LogLevel = NetCodes.LogDebug }, "/c.pem");
            PaAssert.Equal("stop", r.T.Calls[0]);
            PaAssert.Equal(NetCodes.LogDebug, r.T.LogLevel);
            PaAssert.True(r.E.Running);
            PaAssert.Equal("192.0.2.10:47000", r.E.Endpoint);
            PaAssert.Equal(Rig.PageUrl + "#h=192.0.2.10&p=47000", r.E.JoinUrl);
            PaAssert.Null(r.E.Problem);
            foreach (string l in r.Log.Lines)
                PaAssert.True(l.IndexOf("example.invalid", StringComparison.Ordinal) < 0, "logs never contain the page URL: " + l);
        }

        [PaTest]
        public static void BindRetriesTheNextNinePorts()
        {
            var r = new Rig(false);
            for (int i = 0; i < 3; i++)
                r.T.StartResults.Enqueue(NetCodes.Bind);
            PaAssert.True(r.E.Start(new NetConfig { PhonePageUrl = Rig.PageUrl }, "/c.pem"));
            PaAssert.SequenceEqual(new[] { 47000, 47001, 47002, 47003 }, r.T.StartPorts);

            r = new Rig(false);
            for (int i = 0; i < 10; i++)
                r.T.StartResults.Enqueue(NetCodes.Bind);
            PaAssert.False(r.E.Start(new NetConfig { PhonePageUrl = Rig.PageUrl }, "/c.pem"));
            PaAssert.Equal(10, r.T.StartPorts.Count);
            PaAssert.Equal("UDP ports 47000..47009 in use", r.E.Problem);
            PaAssert.False(r.E.Running);
            PaAssert.Null(r.E.JoinUrl);

            r = new Rig(false);
            r.T.StartResults.Enqueue(NetCodes.Bind);
            PaAssert.False(r.E.Start(new NetConfig { UdpPort = 0 }, "/c.pem"));
            PaAssert.Equal(1, r.T.StartPorts.Count);
            PaAssert.Equal("no free UDP port", r.E.Problem);
        }

        [PaTest]
        public static void ProblemTexts()
        {
            var r = new Rig(false);
            PaAssert.Equal("network not started", r.E.Problem);
            r.T.StartResults.Enqueue(NetCodes.NoIp);
            r.E.Start(new NetConfig(), "/c.pem");
            PaAssert.True(r.E.Problem.StartsWith("no LAN IPv4 address", StringComparison.Ordinal), r.E.Problem);
            r.T.StartResults.Enqueue(NetCodes.Internal);
            r.T.LastErrorValue = NativeTransport.NotFound;
            r.E.Start(new NetConfig(), "/c.pem");
            PaAssert.Equal(NativeTransport.NotFound, r.E.Problem);
            r.T.StartResults.Enqueue(NetCodes.Cert);
            r.E.Start(new NetConfig(), "/c.pem");
            PaAssert.Equal("cannot read or create the certificate file /c.pem", r.E.Problem);
        }

        [PaTest]
        public static void MissingPageUrlAndSetPageUrl()
        {
            var r = new Rig(false);
            PaAssert.True(r.E.Start(new NetConfig { GameConfigPath = "/games/iwyk/iwyk-net.json" }, "/c.pem"));
            PaAssert.Null(r.E.JoinUrl);
            PaAssert.Equal("phone page address not set: add phone_page_url to /games/iwyk/iwyk-net.json", r.E.Problem);
            PaAssert.False(r.E.TryGetQr(out _, out _));
            r.E.SetPageUrl("https://example.invalid/x/#old");
            PaAssert.Equal("https://example.invalid/x/#h=192.0.2.10&p=47000", r.E.JoinUrl);
            PaAssert.Null(r.E.Problem);
            PaAssert.True(r.E.TryGetQr(out byte[] m, out int side));
            PaAssert.Equal(21, side);
            PaAssert.Equal(441, m.Length);
            r.E.TryGetQr(out m, out side);
            PaAssert.Equal(1, r.T.QrCalls, "QR cached per URL");
            r.E.SetPageUrl("ftp://nope");
            PaAssert.Null(r.E.JoinUrl);
            PaAssert.Equal("phone page address not set: add phone_page_url to /games/iwyk/iwyk-net.json", r.E.Problem);
        }

        [PaTest]
        public static void StopForgetsEverything()
        {
            var r = new Rig();
            r.Join(1, "u1");
            r.E.Stop();
            PaAssert.False(r.E.Running);
            PaAssert.False(r.E.AnyClientConnected());
            PaAssert.Equal(0, r.E.SessionCount);
            PaAssert.Null(r.E.Endpoint);
            r.Update();
        }
    }
}
