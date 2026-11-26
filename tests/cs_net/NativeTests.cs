// IwykNative constants, NativeTransport without the library, and the optional native loop
// (real iwyknet library + engine + iwykbot over 127.0.0.1) when IWYK_NATIVE_LOOP_BOT is set.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using PortAudio.Tests;

namespace PortNet.Tests
{
    public static class NativeTests
    {
        static bool LoopEnabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IWYK_NATIVE_LOOP_BOT"));

        [PaTest]
        public static void CoreCodesMatchTheAbi()
        {
            PaAssert.Equal(IwykNative.IWYK_OK, NetCodes.Ok);
            PaAssert.Equal(IwykNative.IWYK_E_NOT_STARTED, NetCodes.NotStarted);
            PaAssert.Equal(IwykNative.IWYK_E_ALREADY_STARTED, NetCodes.AlreadyStarted);
            PaAssert.Equal(IwykNative.IWYK_E_ARG, NetCodes.Arg);
            PaAssert.Equal(IwykNative.IWYK_E_NO_SESSION, NetCodes.NoSession);
            PaAssert.Equal(IwykNative.IWYK_E_SMALLBUF, NetCodes.SmallBuf);
            PaAssert.Equal(IwykNative.IWYK_E_TOO_BIG, NetCodes.TooBig);
            PaAssert.Equal(IwykNative.IWYK_E_BIND, NetCodes.Bind);
            PaAssert.Equal(IwykNative.IWYK_E_CERT, NetCodes.Cert);
            PaAssert.Equal(IwykNative.IWYK_E_NO_IP, NetCodes.NoIp);
            PaAssert.Equal(IwykNative.IWYK_E_FULL, NetCodes.Full);
            PaAssert.Equal(IwykNative.IWYK_E_INTERNAL, NetCodes.Internal);
            PaAssert.Equal(IwykNative.IWYK_EV_OPENED, (int)NetEventKind.Opened);
            PaAssert.Equal(IwykNative.IWYK_EV_TEXT, (int)NetEventKind.Text);
            PaAssert.Equal(IwykNative.IWYK_EV_BINARY, (int)NetEventKind.Binary);
            PaAssert.Equal(IwykNative.IWYK_EV_CLOSED, (int)NetEventKind.Closed);
            PaAssert.Equal(IwykNative.IWYK_EV_LOG, (int)NetEventKind.Log);
            PaAssert.Equal(IwykNative.IWYK_CLOSE_HOST, NetCodes.CloseHost);
            PaAssert.Equal(IwykNative.IWYK_CLOSE_PROTOCOL, NetCodes.CloseProtocol);
            PaAssert.Equal(IwykNative.IWYK_LOG_WARN, NetCodes.LogWarn);
            PaAssert.Equal(IwykNative.IWYK_LOG_OFF, NetCodes.LogOff);
            PaAssert.Equal(IwykNative.IWYK_MAX_TEXT, NetLimits.MaxText);
            PaAssert.Equal(IwykNative.IWYK_MAX_BINARY, NetLimits.MaxBinary);
            PaAssert.Equal(20, System.Runtime.InteropServices.Marshal.SizeOf(typeof(IwykEvent)));
        }

        [PaTest]
        public static void Utf8zIsNulTerminated()
        {
            PaAssert.SequenceEqual(new byte[] { 0x61, 0xC3, 0xA9, 0 }, IwykNative.Utf8z("a\u00e9"));
            PaAssert.SequenceEqual(new byte[] { 0 }, IwykNative.Utf8z(""));
            PaAssert.Null(IwykNative.Utf8z(null));
        }

        [PaTest]
        public static void MissingLibraryIsReportedNotThrown()
        {
            if (LoopEnabled)
            {
                Console.WriteLine("SKIP MissingLibraryIsReportedNotThrown: the library is present");
                return;
            }
            var t = new NativeTransport();
            PaAssert.False(t.Available);
            t.Stop();
            t.SetLogLevel(1);
            PaAssert.Equal(NetCodes.Internal, t.Start(0, "", "/tmp/x.pem"));
            PaAssert.Equal(NativeTransport.NotFound, t.LastError);
            PaAssert.False(t.Poll(out _));
            PaAssert.Null(t.Endpoint);
            PaAssert.Null(t.JoinUrl("https://example.invalid/"));
            PaAssert.False(t.Qr("x", out _, out _));
            PaAssert.Equal(NetCodes.NotStarted, t.SendText(1, new byte[1], 1));

            var e = new NetEngine(t, new ListLog());
            PaAssert.False(e.Start(new NetConfig(), "/tmp/x.pem"));
            PaAssert.Equal(NativeTransport.NotFound, e.Problem);
            e.Update(0.1f, 1f, true);
        }

        // ---------------- native loop ----------------

        sealed class Bot : IDisposable
        {
            readonly Process m_p;
            public readonly ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();
            public readonly List<string> Seen = new List<string>();

            public Bot(string exe, string url, string uid)
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add("-url");
                psi.ArgumentList.Add(url);
                psi.ArgumentList.Add("-uid");
                psi.ArgumentList.Add(uid);
                m_p = Process.Start(psi);
                m_p.OutputDataReceived += (s, e) => { if (e.Data != null) Lines.Enqueue(e.Data); };
                m_p.ErrorDataReceived += (s, e) => { if (e.Data != null) Lines.Enqueue("stderr: " + e.Data); };
                m_p.BeginOutputReadLine();
                m_p.BeginErrorReadLine();
            }

            public void Send(string jsonLine)
            {
                m_p.StandardInput.WriteLine(jsonLine);
                m_p.StandardInput.Flush();
            }

            public bool Collect(string fragment)
            {
                while (Lines.TryDequeue(out string l))
                    Seen.Add(l);
                foreach (string l in Seen)
                    if (l.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                        return true;
                return false;
            }

            public bool Exited => m_p.HasExited;

            public void Dispose()
            {
                try
                {
                    if (!m_p.WaitForExit(3000))
                        m_p.Kill();
                }
                catch (Exception)
                {
                }
                m_p.Dispose();
            }
        }

        static bool Pump(NetEngine e, ref float now, Func<bool> done, double seconds = 20)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                now += 0.02f;
                e.Update(0.02f, now, true);
                if (done())
                    return true;
                Thread.Sleep(20);
            }
            return false;
        }

        [PaTest]
        public static void NativeLoop()
        {
            string botExe = Environment.GetEnvironmentVariable("IWYK_NATIVE_LOOP_BOT");
            if (string.IsNullOrEmpty(botExe))
            {
                Console.WriteLine("SKIP NativeLoop: IWYK_NATIVE_LOOP_BOT not set (needs work/native/macos-arm64/iwyknet.dylib and iwykbot)");
                return;
            }
            using (var tmp = new PaTempDir())
            {
                var t = new NativeTransport();
                PaAssert.True(t.Available, "iwyknet loads and reports ABI 1");
                Console.WriteLine("native: " + t.Version);
                var log = new ListLog();
                var ev = new FakeEvents();
                var policy = new FakePolicy();
                var e = new NetEngine(t, log) { Policy = policy, Events = ev };
                policy.Engine = e;
                var cfg = new NetConfig { UdpPort = 0, LanIp = "127.0.0.1", PhonePageUrl = "http://127.0.0.1:9/", LogLevel = NetCodes.LogInfo };
                string cert = tmp.PathOf("iwyknet/dtls.pem");
                try
                {
                    PaAssert.True(e.Start(cfg, cert), "start: " + e.Problem + " | " + string.Join(" | ", log.Lines));
                    PaAssert.True(File.Exists(cert), "certificate written");
                    string url = e.JoinUrl;
                    PaAssert.NotNull(url, "join url");
                    PaAssert.True(url.StartsWith("http://127.0.0.1:9/#h=127.0.0.1&p=", StringComparison.Ordinal), url);
                    PaAssert.True(e.TryGetQr(out byte[] modules, out int side) && side >= 21 && modules.Length == side * side, "qr");

                    float now = 0;
                    using (var bot = new Bot(botExe, url, "loop-uid"))
                    {
                        NetAddr addr = NetAddrAllocator.FromCounter(1);
                        PaAssert.True(Pump(e, ref now, () => e.CanStartSend(addr)), "joined: " + string.Join(" | ", log.Lines));
                        PaAssert.True(Pump(e, ref now, () => bot.Collect("\"joined\"")), "bot saw joined: " + string.Join(" | ", bot.Seen));

                        byte[] json = Encoding.UTF8.GetBytes("{\"TypeString\":\"SessionStateMessage\",\"SessionID\":12345}");
                        e.StartSend(addr, -7503, json, json.Length);
                        PaAssert.True(Pump(e, ref now, () => bot.Collect("\"SessionID\":12345")), "bot got msg: " + string.Join(" | ", bot.Seen));
                        PaAssert.True(ev.Only("done").Count >= 1, "send completed");

                        var content = new byte[20000];
                        new Random(7).NextBytes(content);
                        byte[] batch = GameBatchTests.Batch(-7503, "{\"TypeString\":\"ImageResourceContentTransferMessage\",\"TransferID\":9}", 9, content);
                        e.StartSend(addr, 9, batch, batch.Length);
                        PaAssert.True(Pump(e, ref now, () => bot.Collect("\"id\":9,\"len\":20000")), "bot got content: " + string.Join(" | ", bot.Seen));

                        bot.Send("{\"cmd\":\"msg\",\"json\":{\"TypeString\":\"ClientRequestPlayerIDMessage\",\"UID\":\"loop-uid\"}}");
                        var buf = new byte[131072];
                        int size = 0;
                        PaAssert.True(Pump(e, ref now, () => e.TryGetData(-7503, addr, buf, ref size)), "engine got msg");
                        string got = Encoding.UTF8.GetString(buf, 0, size);
                        PaAssert.True(got.Contains("\"TypeString\":\"ClientRequestPlayerIDMessage\""), got);

                        bot.Send("{\"cmd\":\"binary\",\"id\":5,\"len\":3000}");
                        PaAssert.True(Pump(e, ref now, () => e.TryGetData(5, addr, buf, ref size)), "engine got content");
                        PaAssert.Equal(3000, size);

                        bot.Send("{\"cmd\":\"close\"}");
                        PaAssert.True(Pump(e, ref now, () => ev.Only("stop").Count == 1), "close noticed: " + string.Join(" | ", log.Lines));
                    }
                }
                finally
                {
                    e.Stop();
                }
            }
        }
    }
}
