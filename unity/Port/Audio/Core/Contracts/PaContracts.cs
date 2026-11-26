// Shared contracts of the PortAudio runtime (the managed replacement for the Wwise sound engine).
//
// Every runtime unit (data, engine, mixer, music, observability) codes against these types. The
// signatures are fixed by the audio architecture (section 6.1); bodies are the small shared helpers:
// the Wwise short-ID hash, dB conversion, curve shapes, a deterministic random generator and the
// log/trace routing. This file is UnityEngine-free and compiles outside Unity.

using System;
using System.Collections.Generic;
using System.Text;

namespace PortAudio
{
    public static class PaResult
    {
        public const int NotImplemented = 0, Success = 1, Fail = 2, PartialSuccess = 3,
            InvalidID = 14, IDNotFound = 15, InvalidParameter = 31, UnknownBankID = 54, BankReadError = 56,
            UnknownObject = 61, FileNotFound = 66, BankAlreadyLoaded = 69;
    }

    public static class PaCallbackType
    {
        public const uint EndOfEvent = 1, EndOfDynamicSequenceItem = 2, Marker = 4,
            Duration = 8, MusicSyncAll = 0x7F00, Bank = 0x40000000;
    }

    public enum PaCurve { Log3 = 0, Sine = 1, Log1 = 2, InvSCurve = 3, Linear = 4, SCurve = 5, Exp1 = 6, SineRecip = 7, Exp3 = 8, Constant = 9 }

    public static class PaIds
    {
        public const ulong GlobalObject = ulong.MaxValue;                 // (ulong)(-1): global / any object

        public static bool IsGlobal(ulong go)
        {
            return go == ulong.MaxValue || go == 0xFFFFFFFFUL;
        }

        // The key PostEvent and every per-object call use for an object registered with an int id.
        public static ulong FromRegisteredId(int id)
        {
            return unchecked((ulong)(long)id);
        }

        // Wwise short ID: 32-bit FNV-1 over the lowercase UTF-8 name. null or "" give 0.
        public static uint Hash(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;
            uint h = 2166136261u;
            byte[] bytes = Encoding.UTF8.GetBytes(name.ToLowerInvariant());
            for (int i = 0; i < bytes.Length; i++)
            {
                h = unchecked(h * 16777619u);
                h ^= bytes[i];
            }
            return h;
        }
    }

    public static class PaDb
    {
        const float Floor = -200f;

        public static float ToLinear(float db)
        {
            if (db <= Floor || float.IsNaN(db))
                return 0f;
            return (float)Math.Pow(10.0, db / 20.0);
        }

        public static float FromLinear(float lin)
        {
            if (!(lin > 0f))
                return Floor;
            double db = 20.0 * Math.Log10(lin);
            return db <= Floor ? Floor : (float)db;
        }
    }

    public struct PaPoint
    {
        public float X, Y;
        public PaCurve Interp;

        public PaPoint(float x, float y, PaCurve interp)
        {
            X = x;
            Y = y;
            Interp = interp;
        }

        public override string ToString()
        {
            return "[" + X + "," + Y + "," + (int)Interp + "]";
        }
    }

    public static class PaCurves
    {
        // Normalized shape of a Wwise interpolation curve: 0 at t=0, 1 at t=1.
        public static double Shape(PaCurve c, double t)
        {
            if (double.IsNaN(t) || t < 0.0)
                t = 0.0;
            else if (t > 1.0)
                t = 1.0;
            switch (c)
            {
                case PaCurve.Log3: return 1.0 - Math.Pow(1.0 - t, 3.0);
                case PaCurve.Log1: return 1.0 - Math.Pow(1.0 - t, 1.41);
                case PaCurve.Exp1: return Math.Pow(t, 1.41);
                case PaCurve.Exp3: return t * t * t;
                case PaCurve.Sine: return Math.Sin(t * Math.PI / 2.0);
                case PaCurve.SineRecip: return 1.0 - Math.Cos(t * Math.PI / 2.0);
                case PaCurve.SCurve: return (1.0 - Math.Cos(Math.PI * t)) / 2.0;
                case PaCurve.InvSCurve: return 0.5 + Math.Asin(2.0 * t - 1.0) / Math.PI;
                case PaCurve.Constant: return t < 1.0 ? 0.0 : 1.0;
                default: return t;  // Linear and unknown values
            }
        }

        // Piecewise curve: clamped outside the first and last point; between point i and i+1 the
        // shape of point i is used. An empty or null curve evaluates to 0.
        public static float Evaluate(PaPoint[] points, float x)
        {
            if (points == null || points.Length == 0)
                return 0f;
            if (points.Length == 1 || x <= points[0].X)
                return points[0].Y;
            int last = points.Length - 1;
            if (x >= points[last].X)
                return points[last].Y;
            for (int i = 0; i < last; i++)
            {
                PaPoint a = points[i];
                PaPoint b = points[i + 1];
                if (x > b.X)
                    continue;
                float dx = b.X - a.X;
                if (dx <= 0f)
                    return b.Y;
                double t = (x - a.X) / (double)dx;
                return (float)(a.Y + (b.Y - a.Y) * Shape(a.Interp, t));
            }
            return points[last].Y;
        }
    }

    public struct PaVec3
    {
        public float X, Y, Z;

        public PaVec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    public struct PaListenerPose { public PaVec3 Position, Front, Top; }

    // Deterministic per seed (xorshift64*). Not Wwise's generator; only reproducibility matters.
    public sealed class PaRandom
    {
        ulong state;

        public PaRandom(uint seed)
        {
            Reseed(seed);
        }

        public void Reseed(uint seed)
        {
            // The high constant bits keep the state nonzero for every seed.
            state = 0x9E3779B97F4A7C15UL ^ ((ulong)seed * 0xBF58476D1CE4E5B9UL);
            if (state == 0)
                state = 0x9E3779B97F4A7C15UL;
        }

        public uint NextUInt()
        {
            ulong x = state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            state = x;
            return (uint)(unchecked(x * 2685821657736338717UL) >> 32);
        }

        // [0, 1) with 53 bits of precision.
        public double NextDouble()
        {
            ulong hi = NextUInt() >> 5;   // 27 bits
            ulong lo = NextUInt() >> 6;   // 26 bits
            return ((hi << 26) | lo) / 9007199254740992.0;
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            long span = (long)maxExclusive - minInclusive;
            long r = (long)(NextDouble() * span);
            if (r >= span)
                r = span - 1;
            return (int)(minInclusive + r);
        }

        public float Range(float min, float max)
        {
            if (!(max > min))
                return min;
            return (float)(min + NextDouble() * (max - min));
        }
    }

    public sealed class PaSettings
    {
        // Parsed by pa-observe PaOptions.Parse, held by the platform.
        public string TracePath, RecordPath, Root, VoiceLanguage;
        public bool Off, SuspendOnFocusLoss;
        public int RealVoices = 128;
        public float TrimDb;
        public long Seed = -1;
    }

    public interface IPaTraceSink
    {
        // Thread safe.
        void Write(string kind, object[] keyValues);
        void Flush();
        void Close();
    }

    public static class PaLog
    {
        public static IPaTraceSink Sink;                                  // null = tracing off

        public static bool Tracing
        {
            get { return Sink != null; }
        }

        // ("post","id",123u,"name","Play_X",...); a no-op when tracing is off. Never throws.
        public static void Trace(string kind, params object[] keyValues)
        {
            IPaTraceSink sink = Sink;
            if (sink == null)
                return;
            try
            {
                sink.Write(kind, keyValues ?? new object[0]);
            }
            catch (Exception)
            {
                // Tracing must never break audio or the game.
            }
        }

        public static void Info(string msg)
        {
            Emit(0, msg);
        }

        public static void Warn(string msg)
        {
            Emit(1, msg);
            Trace("warn", "msg", msg);
        }

        public static void Error(string msg)
        {
            Emit(2, msg);
            Trace("error", "msg", msg);
        }

        static void Emit(int level, string msg)
        {
            string line = "[PortAudio] " + msg;
            IPaPlatform platform = PaRuntime.Platform;
            try
            {
                if (platform != null)
                    platform.Log(level, line);
                else if (level == 0)
                    Console.WriteLine(line);
                else
                    Console.Error.WriteLine(line);
            }
            catch (Exception)
            {
            }
        }
    }

    public interface IPaPlatform
    {
        string StreamingAssetsPath { get; }
        string[] CommandLineArgs { get; }
        double RealtimeSeconds { get; }
        int FrameCount { get; }
        bool IsMainThread { get; }
        PaSettings Settings { get; }
        IPaTraceSink CreateTraceSink(string path);
        IPaVoices CreateVoices(PaMixerConfig config);
        IPaMusic CreateMusic();
        void StartDriver(Action tick);                                    // tick once per frame on the main thread (LateUpdate)
        void StartRecorder(string wavPath);
        void StopRecorder();
        void Log(int level, string message);                              // 0 info, 1 warning, 2 error
    }

    public interface IPaWorld
    {
        // Implemented by pa-engine; main-thread reads.
        float GetRtpc(uint rtpcId, ulong gameObject);                     // object value, else global, else Init default, else 0
        uint GetState(uint stateGroup);                                   // 0 = unset
        uint GetSwitch(uint switchGroup, ulong gameObject);               // 0 = unset
        PaVec3 GetPosition(ulong gameObject);                             // default (0,0,0)
        PaVec3 GetFront(ulong gameObject);                                // default (0,0,1)
        uint GetListenerMask(ulong gameObject);                           // default 1
        float GetOutputBusVolume(ulong gameObject);                       // default 1
        float GetAttenuationScaling(ulong gameObject);                    // default 1
        PaListenerPose Listener { get; }
        bool BackgroundMusicMuted { get; }
        string VoiceLanguageKey { get; }
        PaRandom Random { get; }
        PaBankStore Banks { get; }
        PaMediaIndex Media { get; }
        void SetMeterRtpc(uint rtpcId, float value);                      // global value written by the mixer's meter approximation
    }

    public enum PaVoiceState { Loading, Scheduled, Playing, Paused, Stopping, Ended }

    public enum PaVoiceEnd { Finished, Stopped, LoadFailed, NoMedia, Stolen, Shutdown }

    public sealed class PaVoiceSpec
    {
        public uint PlayingId; public ulong GameObject; public uint NodeId;   // leaf Sound or MusicTrack; mixer walks Parent chain
        public PaMediaEntry Media;                                           // null and Pcm null -> ends NoMedia
        public float[] Pcm; public int PcmRate, PcmChannels;                 // audio input clips
        public float VolumeDb, PitchCents, Lpf;                              // engine-rolled randomizers and offsets (not chain props)
        public int LoopCount = 1;                                            // 0 = infinite
        public double SourceOffset, StartAt, PlayLength;                     // sec; StartAt on the voice clock (<= Now: asap); PlayLength 0 = natural
        public int FadeInMs; public PaCurve FadeInCurve = PaCurve.Linear;
        public bool Music;                                                   // compensate late starts by seeking, never drift
        public object Tag;
    }

    public interface IPaVoiceListener
    {
        void OnVoiceEnded(IPaVoice voice, PaVoiceEnd reason);             // called from IPaVoices.Tick only, once
    }

    public interface IPaVoice
    {
        int Id { get; }
        PaVoiceSpec Spec { get; }
        PaVoiceState State { get; }
        double StartTime { get; }                                         // NaN unknown
        double EndTime { get; }                                           // NaN unknown; +Inf when infinite
        double Position { get; }                                          // NaN unknown
        void Stop(int fadeMs, PaCurve curve);
        void StopAt(double time, int fadeMs, PaCurve curve);
        void Pause(int fadeMs, PaCurve curve);
        void Resume(int fadeMs, PaCurve curve);
        void SetGainDb(int slot, float db);                               // slots 0..3: 0 engine, 1 music transition, 2 clip automation
        void FadeTo(int slot, float db, int ms, PaCurve curve);
    }

    public sealed class PaMixerConfig
    {
        public int RealVoices = 128;
        public float TrimDb;
        public double LookAheadSec = 0.1;
        public int StreamBudgetMb = 64;
        public int MaxConcurrentLoads = 16;
    }

    public interface IPaVoices
    {
        double Now { get; }                                               // AudioSettings.dspTime when running, else realtime (virtual voices)
        bool AudioRunning { get; }
        IPaVoice Play(PaVoiceSpec spec, IPaVoiceListener listener);         // never calls the listener synchronously; allowed inside Tick callbacks
        void Preload(IEnumerable<PaMediaEntry> media, object owner);
        void ReleaseOwner(object owner);
        void Prefetch(PaMediaEntry media);
        bool IsReady(PaMediaEntry media);
        void Tick();
        void StopAll(int fadeMs);
        IEnumerable<IPaVoice> Active { get; }
        void Shutdown();
    }

    public interface IPaMusicContext
    {
        uint PlayingId { get; }
        ulong GameObject { get; }
        uint RootNode { get; }
        bool Ended { get; }
        bool Involves(uint nodeId);                                       // root, an ancestor of root, or any node it has played
    }

    public interface IPaMusicListener
    {
        void OnMusicEnded(IPaMusicContext context);                       // called from IPaMusic.Tick only, once
    }

    public interface IPaMusic
    {
        bool IsMusicNode(uint nodeId);
        IPaMusicContext Play(uint nodeId, ulong gameObject, uint playingId, int fadeInMs, PaCurve curve, IPaMusicListener listener); // null: nothing to play
        void Stop(IPaMusicContext c, int fadeMs, PaCurve curve);
        void Pause(IPaMusicContext c, int fadeMs, PaCurve curve);
        void Resume(IPaMusicContext c, int fadeMs, PaCurve curve);
        void OnStateChanged(uint stateGroup, uint state);
        void OnSwitchChanged(uint switchGroup, uint value, ulong gameObject);
        int PostTrigger(uint triggerId, ulong gameObject);                  // stingers scheduled
        void Tick();
        void StopAll();
        IEnumerable<IPaMusicContext> Active { get; }
    }

    public static class PaRuntime
    {
        public static IPaPlatform Platform;
        public static IPaWorld World;
        public static IPaVoices Voices;
        public static IPaMusic Music;
    }
}
