// NetAddr, ReceiveStore, Liveness and NetConfig.

using System;
using System.Collections.Generic;
using PortAudio.Tests;

namespace PortNet.Tests
{
    public static class NetAddrTests
    {
        [PaTest]
        public static void LittleEndianIp()
        {
            NetAddr a = NetAddrAllocator.FromCounter(1);
            PaAssert.Equal(0x010012C6u, a.Ip);  // bytes 198 18 0 1 read little-endian
            PaAssert.Equal(0, a.PortOffset);
            PaAssert.Equal("198.18.0.1", a.IpString);
            PaAssert.Equal("198.18.0.1:+0", a.ToString());
            PaAssert.Equal("198.18.1.2", NetAddrAllocator.FromCounter(0x102).IpString);
            PaAssert.Equal("198.19.0.0", NetAddrAllocator.FromCounter(0x10000).IpString);
            PaAssert.Equal("198.19.255.255", NetAddrAllocator.FromCounter(NetAddrAllocator.MaxCounter).IpString);
            PaAssert.True(a.IsValid);
            PaAssert.False(new NetAddr(0, 0).IsValid);
        }

        [PaTest]
        public static void UniqueSkipsInUseAndWraps()
        {
            var alloc = new NetAddrAllocator();
            var used = new HashSet<NetAddr>();
            for (int i = 0; i < 1000; i++)
                PaAssert.True(used.Add(alloc.Allocate(used.Contains)), "unique");

            var inUse = new HashSet<NetAddr> { NetAddrAllocator.FromCounter(2), NetAddrAllocator.FromCounter(3) };
            var b = new NetAddrAllocator();
            PaAssert.Equal("198.18.0.1", b.Allocate(inUse.Contains).IpString);
            PaAssert.Equal("198.18.0.4", b.Allocate(inUse.Contains).IpString);

            var w = new NetAddrAllocator(NetAddrAllocator.MaxCounter - 1);
            PaAssert.Equal("198.19.255.255", w.Allocate(null).IpString);
            var skip = new HashSet<NetAddr> { NetAddrAllocator.FromCounter(1) };
            PaAssert.Equal("198.18.0.2", w.Allocate(skip.Contains).IpString);
        }
    }

    public static class ReceiveStoreTests
    {
        static readonly NetAddr A = NetAddrAllocator.FromCounter(1);
        static readonly NetAddr B = NetAddrAllocator.FromCounter(2);

        [PaTest]
        public static void FifoPerAddressAndId()
        {
            var s = new ReceiveStore();
            s.Add(A, -7503, new byte[] { 1 }, 0);
            s.Add(B, -7503, new byte[] { 2 }, 0);
            s.Add(A, 9, new byte[] { 3, 3 }, 0);
            s.Add(A, -7503, new byte[] { 4 }, 0);
            var buf = new byte[8];
            int size = -1;
            PaAssert.Equal(ReceiveStore.Taken, s.TryTake(-7503, A, buf, ref size));
            PaAssert.Equal(1, (int)buf[0]);
            PaAssert.Equal(ReceiveStore.Taken, s.TryTake(-7503, A, buf, ref size));
            PaAssert.Equal(4, (int)buf[0]);
            PaAssert.Equal(ReceiveStore.NotFound, s.TryTake(-7503, A, buf, ref size));
            PaAssert.Equal(ReceiveStore.Taken, s.TryTake(9, A, buf, ref size));
            PaAssert.Equal(2, size);
            PaAssert.Equal(1, s.Count);
            PaAssert.Equal(1L, s.Bytes);
        }

        [PaTest]
        public static void TooSmallBufferDropsEntry()
        {
            var s = new ReceiveStore();
            s.Add(A, 5, new byte[10], 0);
            int size = 0;
            PaAssert.Equal(ReceiveStore.DroppedTooSmall, s.TryTake(5, A, new byte[9], ref size));
            PaAssert.Equal(0, size);
            PaAssert.Equal(0, s.Count);
        }

        [PaTest]
        public static void AbandonAfterTwoSeconds()
        {
            var s = new ReceiveStore();
            s.Add(A, 1, new byte[1], 10f);
            s.Add(A, 2, new byte[1], 11f);
            PaAssert.Equal(0, s.Abandon(10f));
            PaAssert.Equal(1, s.Abandon(10.5f));
            PaAssert.Equal(1, s.Count);
            int size = 0;
            PaAssert.Equal(ReceiveStore.NotFound, s.TryTake(1, A, new byte[1], ref size));
        }

        [PaTest]
        public static void CapsDropOldest()
        {
            var s = new ReceiveStore(3, 100);
            s.Add(A, 1, new byte[10], 0);
            s.Add(A, 2, new byte[10], 0);
            s.Add(A, 3, new byte[10], 0);
            PaAssert.Equal(1, s.Add(A, 4, new byte[10], 0));
            int size = 0;
            PaAssert.Equal(ReceiveStore.NotFound, s.TryTake(1, A, new byte[10], ref size));
            PaAssert.Equal(2, s.Add(A, 5, new byte[90], 0));  // bytes cap: 3 + 4 must go
            PaAssert.Equal(2, s.Count);
            PaAssert.Equal(100L, s.Bytes);
        }
    }

    public static class LivenessTests
    {
        [PaTest]
        public static void FormulaAndCrossing()
        {
            var l = new Liveness();
            PaAssert.Near(17, l.Death);
            PaAssert.Near(17, l.Indicator);
            PaAssert.False(l.Tick(1f));
            PaAssert.Near(16, l.Death, 1e-5);
            PaAssert.Near(0.999f * 17f + 0.001f * 16f, l.Indicator, 1e-5);
            PaAssert.False(l.IsBad);

            l.Death = 7f;
            l.Indicator = 10f;
            l.Tick(1f);  // 6 < 6.5 and < indicator: snaps down
            PaAssert.Near(6, l.Indicator, 1e-5);
            PaAssert.True(l.IsBad);
            l.Reset();
            PaAssert.Near(17, l.Death);
            l.Tick(0.1f);
            PaAssert.Near(0.999f * 6f + 0.001f * 16.9f, l.Indicator, 1e-5);  // recovers slowly
            PaAssert.True(l.IsBad);

            l.Death = 0.5f;
            PaAssert.False(l.Tick(0.4f));
            PaAssert.True(l.Tick(0.2f), "crossing to <= 0");
            PaAssert.False(l.Tick(0.2f), "only once");
            float d = l.Death;
            l.Tick(1f);
            PaAssert.Near(d, l.Death, 1e-6, "no countdown below zero");
        }

        [PaTest]
        public static void NearZeroWithZeroDtNeverCrosses()
        {
            var l = new Liveness();
            l.SetNearZero(0f);
            PaAssert.False(l.Tick(0.1f));
            PaAssert.True(l.Death < 0);
            l.SetNearZero(0.016f);
            PaAssert.True(l.Tick(0.016f));
        }
    }

    public static class NetConfigTests
    {
        static Func<string, string> Env(params string[] kv)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i < kv.Length; i += 2)
                d[kv[i]] = kv[i + 1];
            return k => d.TryGetValue(k, out string v) ? v : null;
        }

        [PaTest]
        public static void Defaults()
        {
            var w = new List<string>();
            NetConfig c = NetConfig.Load(null, null, Env(), w);
            PaAssert.Null(c.PhonePageUrl);
            PaAssert.Equal(47000, c.UdpPort);
            PaAssert.Equal("", c.LanIp);
            PaAssert.Equal(NetCodes.LogInfo, c.LogLevel);
            PaAssert.Equal(0, w.Count);
        }

        [PaTest]
        public static void MergeOrder()
        {
            var w = new List<string>();
            string game = "{ \"phone_page_url\": \"https://example.invalid/iwyk/#frag\", \"udp_port\": 47001, \"lan_ip\": \"10.0.0.2\", \"log_level\": \"debug\", \"unknown\": [1] }";
            string user = "{\"udp_port\": 47002, \"lan_ip\": \"\"}";
            NetConfig c = NetConfig.Load(game, user, Env(), w, "/g/iwyk-net.json", "/u/iwyk-net.json");
            PaAssert.Equal("https://example.invalid/iwyk/", c.PhonePageUrl);
            PaAssert.Equal(47002, c.UdpPort);
            PaAssert.Equal("", c.LanIp);
            PaAssert.Equal(NetCodes.LogDebug, c.LogLevel);
            PaAssert.Equal("/g/iwyk-net.json", c.GameConfigPath);
            PaAssert.Equal(0, w.Count, string.Join("; ", w));

            c = NetConfig.Load(game, user, Env("IWYK_PAGE_URL", "http://127.0.0.1:8080/", "IWYK_UDP_PORT", "0",
                                               "IWYK_LAN_IP", "192.168.1.5", "IWYK_LOG_LEVEL", "WARNING"), w);
            PaAssert.Equal("http://127.0.0.1:8080/", c.PhonePageUrl);
            PaAssert.Equal(0, c.UdpPort);
            PaAssert.Equal("192.168.1.5", c.LanIp);
            PaAssert.Equal(NetCodes.LogWarn, c.LogLevel);

            c = NetConfig.Load(game, "{\"phone_page_url\": \"\"}", Env("IWYK_PAGE_URL", ""), w);
            PaAssert.Null(c.PhonePageUrl, "an empty user value clears, an empty env value is ignored");
        }

        [PaTest]
        public static void InvalidValuesAreIgnoredWithWarnings()
        {
            var w = new List<string>();
            string game = "{\"phone_page_url\":\"https://example.invalid/ok/\",\"udp_port\":47001}";
            string user = "{\"phone_page_url\":\"ftp://example.invalid/\",\"udp_port\":70000,\"lan_ip\":\"10.0.0.256\",\"log_level\":\"loud\"}";
            NetConfig c = NetConfig.Load(game, user, Env("IWYK_UDP_PORT", "x"), w);
            PaAssert.Equal("https://example.invalid/ok/", c.PhonePageUrl);
            PaAssert.Equal(47001, c.UdpPort);
            PaAssert.Equal("", c.LanIp);
            PaAssert.Equal(NetCodes.LogInfo, c.LogLevel);
            PaAssert.Equal(5, w.Count, string.Join("; ", w));
            foreach (string s in w)
                PaAssert.True(s.IndexOf("example.invalid", StringComparison.Ordinal) < 0, "warnings never echo the URL: " + s);

            w.Clear();
            NetConfig.Load("not json", "{\"udp_port\":\"47000\"}", null, w);
            PaAssert.Equal(2, w.Count);
        }

        [PaTest]
        public static void UrlAndIpValidation()
        {
            PaAssert.Equal("https://example.invalid/iwyk/", NetConfig.NormalizeUrl(" https://example.invalid/iwyk/#h=1&p=2 "));
            PaAssert.Equal("HTTP://127.0.0.1:9/", NetConfig.NormalizeUrl("HTTP://127.0.0.1:9/"));
            PaAssert.Null(NetConfig.NormalizeUrl("https://"));
            PaAssert.Null(NetConfig.NormalizeUrl("https:///x"));
            PaAssert.Null(NetConfig.NormalizeUrl("example.invalid/"));
            PaAssert.Null(NetConfig.NormalizeUrl("https://exa mple.invalid/"));
            PaAssert.True(NetConfig.IsIPv4("192.168.0.1"));
            PaAssert.True(NetConfig.IsIPv4("0.0.0.0"));
            PaAssert.False(NetConfig.IsIPv4("192.168.0"));
            PaAssert.False(NetConfig.IsIPv4("192.168.0.01"));
            PaAssert.False(NetConfig.IsIPv4("::1"));
            PaAssert.False(NetConfig.IsIPv4("1.2.3.4 "));
        }

        [PaTest]
        public static void GameDirRule()
        {
            PaAssert.Equal("C:/Games/IWYK", NetConfig.GameDirFromDataPath("C:/Games/IWYK/ThatsYou_Data"));
            PaAssert.Equal("C:/Games/IWYK", NetConfig.GameDirFromDataPath("C:\\Games\\IWYK\\ThatsYou_Data"));
            PaAssert.Equal("/Users/x/Games", NetConfig.GameDirFromDataPath("/Users/x/Games/That's You.app/Contents"));
            PaAssert.Equal("/Users/x/proj", NetConfig.GameDirFromDataPath("/Users/x/proj/Assets"));
        }

        [PaTest]
        public static void SetKeyKeepsOtherMembers()
        {
            string s = NetConfig.SetKey("{\"udp_port\": 47001, \"phone_page_url\": \"x\", \"z\": {\"a\": [1]}}", "phone_page_url", NetJson.Quote("https://example.invalid/n/"));
            PaAssert.Equal("{\n  \"udp_port\": 47001,\n  \"z\": {\"a\": [1]},\n  \"phone_page_url\": \"https://example.invalid/n/\"\n}\n", s);
            var w = new List<string>();
            NetConfig c = NetConfig.Load(null, s, null, w);
            PaAssert.Equal("https://example.invalid/n/", c.PhonePageUrl);
            PaAssert.Equal(47001, c.UdpPort);
            PaAssert.Equal("{\n  \"phone_page_url\": null\n}\n", NetConfig.SetKey("garbage", "phone_page_url", "null"));
        }
    }
}
