// Test doubles for the music tests: a voice backend with a manual clock that records every
// start, offset, length, gain change and fade; a world with settable states and switches; data
// builders; and a fixture that runs the music player and voices tick by tick.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PortAudio.Tests
{
    public sealed class FakeOp
    {
        public string Kind;      // gain | fade | stop | stopAt | pause | resume
        public double Now, At;
        public int Slot, Ms;
        public float Db;
        public PaCurve Curve;
        public override string ToString() { return Kind + " slot " + Slot + " db " + Db + " ms " + Ms + " now " + Now + " at " + At; }
    }

    public sealed class FakeVoice : IPaVoice
    {
        readonly FakeVoices owner;
        internal IPaVoiceListener Listener;
        internal bool Reported;
        internal double PlayedAt, PausedAt = double.NaN, StopTime = double.PositiveInfinity;
        public readonly List<FakeOp> Ops = new List<FakeOp>();
        public PaVoiceEnd EndReason;

        internal FakeVoice(FakeVoices owner, int id, PaVoiceSpec spec, IPaVoiceListener listener)
        {
            this.owner = owner;
            Id = id;
            Spec = spec;
            Listener = listener;
            PlayedAt = owner.Now;
            State = PaVoiceState.Scheduled;
            StartTime = double.NaN;
            EndTime = double.NaN;
        }

        public int Id { get; private set; }
        public PaVoiceSpec Spec { get; private set; }
        public PaVoiceState State { get; internal set; }
        public double StartTime { get; internal set; }
        public double EndTime { get; internal set; }
        public double Position { get { return double.IsNaN(StartTime) ? double.NaN : Spec.SourceOffset + (owner.Now - StartTime); } }
        public bool Started { get { return !double.IsNaN(StartTime); } }

        public double NaturalLength
        {
            get
            {
                if (Spec.PlayLength > 0)
                    return Spec.PlayLength;
                if (Spec.Media != null)
                    return Math.Max(0, Spec.Media.Duration - Spec.SourceOffset);
                return 0;
            }
        }

        void Log(string kind, int slot, float db, int ms, PaCurve curve, double at)
        {
            Ops.Add(new FakeOp { Kind = kind, Now = owner.Now, At = at, Slot = slot, Db = db, Ms = ms, Curve = curve });
        }

        public void Stop(int fadeMs, PaCurve curve)
        {
            Log("stop", 0, 0, fadeMs, curve, owner.Now);
            if (State == PaVoiceState.Ended)
                return;
            if (!Started)
            {
                Finish(PaVoiceEnd.Stopped);
                return;
            }
            State = PaVoiceState.Stopping;
            StopTime = Math.Min(StopTime, owner.Now + fadeMs / 1000.0);
            EndTime = Math.Min(EndTime, StopTime);
        }

        public void StopAt(double time, int fadeMs, PaCurve curve)
        {
            Log("stopAt", 0, 0, fadeMs, curve, time);
            StopTime = Math.Min(StopTime, time + fadeMs / 1000.0);
            if (Started)
                EndTime = Math.Min(EndTime, StopTime);
        }

        public void Pause(int fadeMs, PaCurve curve)
        {
            Log("pause", 0, 0, fadeMs, curve, owner.Now);
            if (State == PaVoiceState.Playing || State == PaVoiceState.Stopping)
            {
                State = PaVoiceState.Paused;
                PausedAt = owner.Now;
            }
        }

        public void Resume(int fadeMs, PaCurve curve)
        {
            Log("resume", 0, 0, fadeMs, curve, owner.Now);
            if (State == PaVoiceState.Paused)
            {
                double d = owner.Now - PausedAt;
                StartTime += d;
                EndTime += d;
                PausedAt = double.NaN;
                State = PaVoiceState.Playing;
            }
        }

        public void SetGainDb(int slot, float db) { Log("gain", slot, db, 0, PaCurve.Linear, owner.Now); }
        public void FadeTo(int slot, float db, int ms, PaCurve curve) { Log("fade", slot, db, ms, curve, owner.Now); }

        internal void Finish(PaVoiceEnd reason)
        {
            State = PaVoiceState.Ended;
            EndReason = reason;
            owner.PendingEnds.Add(this);
        }

        internal void Advance(double now)
        {
            if (State == PaVoiceState.Ended || State == PaVoiceState.Paused)
                return;
            if (!Started)
            {
                if (now + 1e-9 < Spec.StartAt)
                    return;
                if (Spec.Media == null && Spec.Pcm == null)
                {
                    Finish(PaVoiceEnd.NoMedia);
                    return;
                }
                StartTime = Math.Max(Spec.StartAt, PlayedAt);
                EndTime = Math.Min(StartTime + NaturalLength, StopTime);
                State = PaVoiceState.Playing;
            }
            if (now + 1e-9 >= EndTime)
                Finish(now + 1e-9 >= StopTime ? PaVoiceEnd.Stopped : PaVoiceEnd.Finished);
        }

        public bool Has(string kind, int slot, float db)
        {
            foreach (var o in Ops)
                if (o.Kind == kind && o.Slot == slot && Math.Abs(o.Db - db) < 0.01f)
                    return true;
            return false;
        }

        public FakeOp Find(string kind, int slot)
        {
            foreach (var o in Ops)
                if (o.Kind == kind && o.Slot == slot)
                    return o;
            return null;
        }
    }

    public sealed class FakeVoices : IPaVoices
    {
        public double Now { get; set; }
        public bool AudioRunning { get { return true; } }
        public readonly List<FakeVoice> All = new List<FakeVoice>();
        public readonly HashSet<uint> NotReady = new HashSet<uint>();
        public readonly List<uint> Prefetched = new List<uint>();
        internal readonly List<FakeVoice> PendingEnds = new List<FakeVoice>();
        int nextId = 1;

        public IPaVoice Play(PaVoiceSpec spec, IPaVoiceListener listener)
        {
            var v = new FakeVoice(this, nextId++, spec, listener);
            All.Add(v);
            return v;
        }

        public void Preload(IEnumerable<PaMediaEntry> media, object owner) { }
        public void ReleaseOwner(object owner) { }
        public void Prefetch(PaMediaEntry media) { if (media != null) Prefetched.Add(media.Id); }
        public bool IsReady(PaMediaEntry media) { return media == null || !NotReady.Contains(media.Id); }

        public void Tick()
        {
            foreach (var v in All.ToArray())
                v.Advance(Now);
            var ends = PendingEnds.ToArray();
            PendingEnds.Clear();
            foreach (var v in ends)
            {
                if (v.Reported)
                    continue;
                v.Reported = true;
                if (v.Listener != null)
                    v.Listener.OnVoiceEnded(v, v.EndReason);
            }
        }

        public void StopAll(int fadeMs) { foreach (var v in All) v.Stop(fadeMs, PaCurve.Linear); }

        public IEnumerable<IPaVoice> Active
        {
            get
            {
                foreach (var v in All)
                    if (v.State != PaVoiceState.Ended)
                        yield return v;
            }
        }

        public void Shutdown() { }

        public List<FakeVoice> ForMedia(uint media)
        {
            var l = new List<FakeVoice>();
            foreach (var v in All)
                if (v.Spec.Media != null && v.Spec.Media.Id == media)
                    l.Add(v);
            return l;
        }

        // Voices of a media that were not cancelled before they started.
        public List<FakeVoice> Sounding(uint media)
        {
            var l = new List<FakeVoice>();
            foreach (var v in ForMedia(media))
                if (v.Started)
                    l.Add(v);
            return l;
        }
    }

    public sealed class FakeWorld : IPaWorld
    {
        public readonly Dictionary<uint, uint> States = new Dictionary<uint, uint>();
        public readonly Dictionary<string, uint> Switches = new Dictionary<string, uint>();
        public PaBankStore BankStore;
        public PaMediaIndex MediaIndex;
        readonly PaRandom random = new PaRandom(12345);

        public float GetRtpc(uint rtpcId, ulong gameObject) { return 0; }
        public uint GetState(uint g) { uint v; return States.TryGetValue(g, out v) ? v : 0u; }
        public uint GetSwitch(uint g, ulong go)
        {
            uint v;
            if (Switches.TryGetValue(g + "/" + go, out v))
                return v;
            return Switches.TryGetValue(g + "/global", out v) ? v : 0u;
        }
        public PaVec3 GetPosition(ulong go) { return new PaVec3(0, 0, 0); }
        public PaVec3 GetFront(ulong go) { return new PaVec3(0, 0, 1); }
        public uint GetListenerMask(ulong go) { return 1; }
        public float GetOutputBusVolume(ulong go) { return 1; }
        public float GetAttenuationScaling(ulong go) { return 1; }
        public PaListenerPose Listener { get { return new PaListenerPose(); } }
        public bool BackgroundMusicMuted { get { return false; } }
        public string VoiceLanguageKey { get { return "english_uk"; } }
        public PaRandom Random { get { return random; } }
        public PaBankStore Banks { get { return BankStore; } }
        public PaMediaIndex Media { get { return MediaIndex; } }
        public void SetMeterRtpc(uint rtpcId, float value) { }
    }

    public sealed class ListSink : IPaTraceSink
    {
        public readonly List<KeyValuePair<string, object[]>> Lines = new List<KeyValuePair<string, object[]>>();
        public void Write(string kind, object[] kv) { lock (Lines) Lines.Add(new KeyValuePair<string, object[]>(kind, kv)); }
        public void Flush() { }
        public void Close() { }

        public int Count(string kind, string op)
        {
            int n = 0;
            foreach (var l in Lines)
                if (l.Key == kind && l.Value.Length >= 2 && Equals(l.Value[1], op))
                    n++;
            return n;
        }
    }

    // Builders for synthetic music data.
    public static class MusicData
    {
        public static PaNode Node(uint id, uint parent, PaNodeType type, PaMusic music, params uint[] children)
        {
            return new PaNode { Id = id, Parent = parent, Type = type, Name = "n" + id, Children = children, Props = new PaProps(), Music = music };
        }

        public static PaNode Segment(uint id, uint parent, double durationMs, double entryMs, double exitMs, params uint[] tracks)
        {
            var m = new PaMusic { Tempo = 120, TimeSigNum = 4, TimeSigDen = 4, DurationMs = durationMs, EntryMs = entryMs, ExitMs = exitMs,
                Markers = new[] { new PaMarker { Id = 1, PosMs = entryMs, Kind = "entry" }, new PaMarker { Id = 2, PosMs = exitMs, Kind = "exit" } } };
            return Node(id, parent, PaNodeType.MusicSegment, m, tracks);
        }

        public static PaClip Clip(uint media, double playAtMs, double beginTrimMs, double endTrimMs, double srcDurationMs)
        {
            return new PaClip { Media = media, PlayAtMs = playAtMs, BeginTrimMs = beginTrimMs, EndTrimMs = endTrimMs, SrcDurationMs = srcDurationMs };
        }

        public static PaNode Track(uint id, uint parent, params PaClip[] clips)
        {
            var sources = new List<PaSource>();
            foreach (var c in clips)
                sources.Add(new PaSource { Media = c.Media, Kind = "vorbis", Stream = "embedded" });
            var m = new PaMusic { TrackType = "normal", Clips = clips, Sources = sources.ToArray() };
            return Node(id, parent, PaNodeType.MusicTrack, m);
        }

        public static PaPlaylistItem Leaf(uint segment, int loop = 1, int weight = 50)
        {
            return new PaPlaylistItem { Id = segment + 100000, Segment = segment, Loop = loop, Weight = weight, Type = "sequenceContinuous" };
        }

        public static PaPlaylistItem Group(string type, int loop, params PaPlaylistItem[] children)
        {
            return new PaPlaylistItem { Id = 900000, Type = type, Loop = loop, Weight = 50, Children = children };
        }

        public static PaNode RanSeq(uint id, uint parent, PaPlaylistItem playlist, params uint[] children)
        {
            return Node(id, parent, PaNodeType.MusicRanSeq, new PaMusic { Playlist = playlist }, children);
        }

        public static PaNode Switch(uint id, uint parent, PaMusicArg[] args, PaMusicPath[] paths, params uint[] children)
        {
            return Node(id, parent, PaNodeType.MusicSwitch, new PaMusic { Arguments = args, Paths = paths, Mode = "bestMatch" }, children);
        }

        public static PaMusicPath Path(uint node, params uint[] keys)
        {
            return new PaMusicPath { Keys = keys, Node = node, Weight = 50 };
        }

        public static PaTransitionRule Rule(int src, int dst, string sync, string entry, int srcFadeMs = 0, int srcFadeOffsetMs = 0, int dstFadeMs = 0,
            int dstFadeOffsetMs = 0, bool postExit = false, bool preEntry = false)
        {
            return new PaTransitionRule
            {
                Src = new[] { src }, Dst = new[] { dst },
                SrcRule = new PaTransitionSrc { Sync = sync, FadeMs = srcFadeMs, FadeOffsetMs = srcFadeOffsetMs, PlayPostExit = postExit },
                DstRule = new PaTransitionDst { Entry = entry, FadeMs = dstFadeMs, FadeOffsetMs = dstFadeOffsetMs, PlayPreEntry = preEntry },
            };
        }
    }

    public sealed class MusicFixture : IPaMusicListener, IDisposable
    {
        public readonly FakeVoices Voices = new FakeVoices();
        public readonly FakeWorld World = new FakeWorld();
        public readonly PaMusicPlayer Player = new PaMusicPlayer();
        public readonly ListSink Sink = new ListSink();
        public readonly List<IPaMusicContext> EndedContexts = new List<IPaMusicContext>();
        readonly List<PaNode> nodes = new List<PaNode>();
        readonly Dictionary<uint, double> media = new Dictionary<uint, double>();
        readonly string root;
        bool built;

        public MusicFixture()
        {
            root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pa-music-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Voices.Now = 100.0;
        }

        public MusicFixture Add(params PaNode[] n) { nodes.AddRange(n); return this; }
        public MusicFixture Media(uint id, double seconds) { media[id] = seconds; return this; }
        public PaNode Get(uint id) { foreach (var n in nodes) if (n.Id == id) return n; return null; }

        public void Build()
        {
            if (built)
                return;
            built = true;
            var sb = new StringBuilder();
            sb.Append("{\"format\":1,\"scope\":\"li\",\"language\":\"\",\"externalPrefix\":\"\",\"media\":[");
            bool first = true;
            foreach (var kv in media)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                long samples = (long)Math.Round(kv.Value * 48000);
                sb.Append("{\"id\":" + kv.Key + ",\"file\":\"media/" + kv.Key + ".ogg\",\"format\":\"ogg\",\"rate\":48000,\"channels\":2,\"samples\":" + samples
                    + ",\"bytes\":1000,\"label\":\"m" + kv.Key + "\"}");
            }
            sb.Append("],\"external\":[]}");
            File.WriteAllText(System.IO.Path.Combine(root, "media.json"), sb.ToString());
            var manifest = new PaManifest
            {
                Format = 1, WwiseBankVersion = 113, DefaultLanguage = "English(UK)",
                Languages = new List<PaLanguage> { new PaLanguage { Name = "English(UK)", Key = "english_uk", BankLanguageId = 11 } },
                Banks = new List<PaBankInfo> { new PaBankInfo { Name = "Music", Id = PaIds.Hash("Music"), File = "banks/Music.json", Languages = new List<string>() } },
                Names = new Dictionary<uint, string>(),
            };
            World.BankStore = new PaBankStore(root, manifest);
            World.MediaIndex = new PaMediaIndex(root, manifest);
            var bank = new PaBank
            {
                Format = 1, Name = "Music", Id = PaIds.Hash("Music"), Language = "", LanguageKey = "",
                Events = new List<PaEvent>(), Actions = new List<PaAction>(), Nodes = new List<PaNode>(nodes), Busses = new List<PaBus>(),
                Attenuations = new List<PaAttenuation>(), GameParameters = new List<PaGameParameter>(), StateGroups = new List<PaStateGroup>(),
                Effects = new List<PaEffect>(),
            };
            World.BankStore.Register(bank);
            PaRuntime.World = World;
            PaRuntime.Voices = Voices;
            PaRuntime.Music = Player;
            PaLog.Sink = Sink;
        }

        public IPaMusicContext Play(uint node, ulong go = 1, uint playingId = 77, int fadeInMs = 0)
        {
            Build();
            return Player.Play(node, go, playingId, fadeInMs, PaCurve.Linear, this);
        }

        public void SetState(uint group, uint value)
        {
            World.States[group] = value;
            Player.OnStateChanged(group, value);
        }

        public void SetSwitch(uint group, uint value, ulong go)
        {
            World.Switches[group + "/" + go] = value;
            Player.OnSwitchChanged(group, value, go);
        }

        // Engine order per frame: music, then voices.
        public void Run(double seconds, double step = 0.01)
        {
            Build();
            int n = (int)Math.Round(seconds / step);
            for (int i = 0; i < n; i++)
            {
                Voices.Now += step;
                Player.Tick();
                Voices.Tick();
            }
        }

        public void OnMusicEnded(IPaMusicContext context) { EndedContexts.Add(context); }

        public void Dispose()
        {
            PaLog.Sink = null;
            PaRuntime.World = null;
            PaRuntime.Voices = null;
            PaRuntime.Music = null;
            try { Directory.Delete(root, true); } catch (Exception) { }
        }
    }

    // Assertions local to the music tests (independent of the shared test kit).
    public static class Check
    {
        public static void True(bool cond, string what)
        {
            if (!cond)
                throw new Exception("expected: " + what);
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(what + ": expected " + expected + ", got " + actual);
        }

        public static void Near(double expected, double actual, double tol, string what)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tol)
                throw new Exception(what + ": expected " + expected + " +- " + tol + ", got " + actual);
        }
    }
}
