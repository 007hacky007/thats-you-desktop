// Test doubles for the engine tests: a platform with a manual clock and frame counter, voices
// that end when the test advances the clock, a music player whose contexts end on request,
// a JSON writer for PaModel objects (4.1 rules) and a fixture that writes a PortAudio data
// root, initializes PaEngine on it and reads the callback records the game would receive.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PortAudio.Tests
{
    public sealed class FakePlatform : IPaPlatform
    {
        public double Clock;
        public int Frame;
        public readonly PaSettings settings = new PaSettings();
        public string Streaming = "";
        public Action DriverTick;
        public readonly List<string> Logs = new List<string>();
        public readonly int MainThreadId = Thread.CurrentThread.ManagedThreadId;
        public EngineVoices Voices;
        public EngineMusic Music;
        public string RecorderPath;

        public string StreamingAssetsPath { get { return Streaming; } }
        public string[] CommandLineArgs { get { return new string[0]; } }
        public double RealtimeSeconds { get { return Clock; } }
        public int FrameCount { get { return Frame; } }
        public bool IsMainThread { get { return Thread.CurrentThread.ManagedThreadId == MainThreadId; } }
        public PaSettings Settings { get { return settings; } }
        public IPaTraceSink CreateTraceSink(string path) { return new MemoryTrace(); }
        public IPaVoices CreateVoices(PaMixerConfig config) { Voices = new EngineVoices(this); return Voices; }
        public IPaMusic CreateMusic() { Music = new EngineMusic(); return Music; }
        public void StartDriver(Action tick) { DriverTick = tick; }
        public void StartRecorder(string wavPath) { RecorderPath = wavPath; }
        public void StopRecorder() { RecorderPath = null; }
        public void Log(int level, string message) { lock (Logs) Logs.Add(level + " " + message); }
    }

    public sealed class MemoryTrace : IPaTraceSink
    {
        public readonly List<string> Lines = new List<string>();

        public void Write(string kind, object[] keyValues)
        {
            StringBuilder sb = new StringBuilder(kind);
            for (int i = 0; keyValues != null && i + 1 < keyValues.Length; i += 2)
                sb.Append(' ').Append(keyValues[i]).Append('=').Append(Convert.ToString(keyValues[i + 1], CultureInfo.InvariantCulture));
            lock (Lines) Lines.Add(sb.ToString());
        }

        public void Flush() { }
        public void Close() { }

        public int Count(string prefix)
        {
            int n = 0;
            lock (Lines)
                foreach (string l in Lines)
                    if (l.StartsWith(prefix, StringComparison.Ordinal))
                        n++;
            return n;
        }
    }

    // ---- voices ----

    public sealed class EngineVoice : IPaVoice
    {
        readonly EngineVoices owner;
        internal readonly IPaVoiceListener Listener;
        internal bool Reported;
        internal double PausedAt = double.NaN;
        public PaVoiceEnd EndReason;
        public double Length;          // one play, seconds
        public int StopFadeMs = -1;
        public int PauseCount, ResumeCount;
        public readonly List<string> Ops = new List<string>();

        internal EngineVoice(EngineVoices owner, int id, PaVoiceSpec spec, IPaVoiceListener listener)
        {
            this.owner = owner;
            Id = id;
            Spec = spec;
            Listener = listener;
            StartTime = Math.Max(spec.StartAt, owner.Now);
            if (spec.PlayLength > 0)
                Length = spec.PlayLength;
            else if (spec.Media != null)
                Length = Math.Max(0, spec.Media.Duration - spec.SourceOffset);
            else if (spec.Pcm != null && spec.PcmRate > 0)
                Length = (double)spec.Pcm.Length / Math.Max(1, spec.PcmChannels) / spec.PcmRate;
            EndTime = spec.LoopCount == 0 ? double.PositiveInfinity : StartTime + Length * Math.Max(1, spec.LoopCount);
            State = StartTime > owner.Now ? PaVoiceState.Scheduled : PaVoiceState.Playing;
        }

        public int Id { get; private set; }
        public PaVoiceSpec Spec { get; private set; }
        public PaVoiceState State { get; internal set; }
        public double StartTime { get; internal set; }
        public double EndTime { get; internal set; }
        public double Position
        {
            get
            {
                double now = double.IsNaN(PausedAt) ? owner.Now : PausedAt;
                return now < StartTime ? 0 : Spec.SourceOffset + (now - StartTime);
            }
        }
        public bool NoMedia { get { return Spec.Media == null && Spec.Pcm == null; } }

        public void Stop(int fadeMs, PaCurve curve)
        {
            Ops.Add("stop " + fadeMs);
            if (State == PaVoiceState.Ended)
                return;
            StopFadeMs = fadeMs;
            State = PaVoiceState.Stopping;
            EndTime = Math.Min(EndTime, owner.Now + fadeMs / 1000.0);
            PausedAt = double.NaN;
        }

        public void StopAt(double time, int fadeMs, PaCurve curve)
        {
            Ops.Add("stopAt " + time.ToString("R", CultureInfo.InvariantCulture) + " " + fadeMs);
            EndTime = Math.Min(EndTime, Math.Max(time, owner.Now) + fadeMs / 1000.0);
        }

        public void Pause(int fadeMs, PaCurve curve)
        {
            Ops.Add("pause " + fadeMs);
            PauseCount++;
            if (State == PaVoiceState.Ended || State == PaVoiceState.Stopping || !double.IsNaN(PausedAt))
                return;
            PausedAt = owner.Now;
            State = PaVoiceState.Paused;
        }

        public void Resume(int fadeMs, PaCurve curve)
        {
            Ops.Add("resume " + fadeMs);
            ResumeCount++;
            if (double.IsNaN(PausedAt))
                return;
            double paused = owner.Now - PausedAt;
            StartTime += paused;
            EndTime += paused;
            PausedAt = double.NaN;
            State = PaVoiceState.Playing;
        }

        public void SetGainDb(int slot, float db) { Ops.Add("gain " + slot + " " + db); }
        public void FadeTo(int slot, float db, int ms, PaCurve curve) { Ops.Add("fade " + slot + " " + db + " " + ms); }
    }

    public sealed class EngineVoices : IPaVoices
    {
        readonly FakePlatform platform;
        public readonly List<EngineVoice> All = new List<EngineVoice>();
        public readonly List<KeyValuePair<object, PaMediaEntry>> Preloaded = new List<KeyValuePair<object, PaMediaEntry>>();
        public readonly List<object> Released = new List<object>();
        public readonly List<PaMediaEntry> Prefetched = new List<PaMediaEntry>();
        public readonly HashSet<uint> NotReady = new HashSet<uint>();
        public int Ticks;
        public bool ShutdownCalled;
        int nextId;

        public EngineVoices(FakePlatform platform) { this.platform = platform; }

        public double Now { get { return platform.Clock; } }
        public bool AudioRunning { get { return true; } }

        public IPaVoice Play(PaVoiceSpec spec, IPaVoiceListener listener)
        {
            EngineVoice v = new EngineVoice(this, ++nextId, spec, listener);
            All.Add(v);
            return v;
        }

        public void Preload(IEnumerable<PaMediaEntry> media, object owner)
        {
            foreach (PaMediaEntry e in media)
                Preloaded.Add(new KeyValuePair<object, PaMediaEntry>(owner, e));
        }

        public void ReleaseOwner(object owner) { Released.Add(owner); }
        public void Prefetch(PaMediaEntry media) { Prefetched.Add(media); }
        public bool IsReady(PaMediaEntry media) { return !NotReady.Contains(media.Id); }

        public void Tick()
        {
            Ticks++;
            double now = Now;
            foreach (EngineVoice v in All.ToArray())
            {
                if (v.Reported)
                    continue;
                PaVoiceEnd? end = null;
                if (v.NoMedia)
                    end = PaVoiceEnd.NoMedia;
                else if (v.State == PaVoiceState.Paused)
                    continue;
                else if (now >= v.EndTime - 1e-9)
                    end = v.State == PaVoiceState.Stopping ? PaVoiceEnd.Stopped : PaVoiceEnd.Finished;
                else if (v.State == PaVoiceState.Scheduled && now >= v.StartTime)
                    v.State = PaVoiceState.Playing;
                if (end == null)
                    continue;
                v.Reported = true;
                v.State = PaVoiceState.Ended;
                v.EndReason = end.Value;
                v.Listener.OnVoiceEnded(v, end.Value);
            }
        }

        public void StopAll(int fadeMs)
        {
            foreach (EngineVoice v in All)
                if (!v.Reported)
                    v.Stop(fadeMs, PaCurve.Linear);
        }

        public IEnumerable<IPaVoice> Active
        {
            get
            {
                foreach (EngineVoice v in All)
                    if (!v.Reported)
                        yield return v;
            }
        }

        public void Shutdown() { ShutdownCalled = true; }

        public List<EngineVoice> Live()
        {
            List<EngineVoice> l = new List<EngineVoice>();
            foreach (EngineVoice v in All)
                if (!v.Reported)
                    l.Add(v);
            return l;
        }

        public List<uint> Nodes()
        {
            List<uint> l = new List<uint>();
            foreach (EngineVoice v in All)
                l.Add(v.Spec.NodeId);
            return l;
        }
    }

    // ---- music ----

    public sealed class EngineMusicContext : IPaMusicContext
    {
        public uint PlayingId { get; set; }
        public ulong GameObject { get; set; }
        public uint RootNode { get; set; }
        public bool Ended { get; set; }
        public IPaMusicListener Listener;
        public bool Paused, Stopped, Reported;
        public bool Involves(uint nodeId) { return nodeId == RootNode; }
    }

    public sealed class EngineMusic : IPaMusic
    {
        public readonly HashSet<uint> MusicNodes = new HashSet<uint>();
        public readonly List<EngineMusicContext> Contexts = new List<EngineMusicContext>();
        public readonly List<string> Calls = new List<string>();

        public bool IsMusicNode(uint nodeId) { return MusicNodes.Contains(nodeId); }

        public IPaMusicContext Play(uint nodeId, ulong gameObject, uint playingId, int fadeInMs, PaCurve curve, IPaMusicListener listener)
        {
            EngineMusicContext c = new EngineMusicContext { PlayingId = playingId, GameObject = gameObject, RootNode = nodeId, Listener = listener };
            Contexts.Add(c);
            Calls.Add("play " + nodeId);
            return c;
        }

        public void Stop(IPaMusicContext c, int fadeMs, PaCurve curve) { ((EngineMusicContext)c).Stopped = true; Calls.Add("stop " + c.RootNode + " " + fadeMs); }
        public void Pause(IPaMusicContext c, int fadeMs, PaCurve curve) { ((EngineMusicContext)c).Paused = true; Calls.Add("pause " + c.RootNode); }
        public void Resume(IPaMusicContext c, int fadeMs, PaCurve curve) { ((EngineMusicContext)c).Paused = false; Calls.Add("resume " + c.RootNode); }
        public void OnStateChanged(uint stateGroup, uint state) { Calls.Add("state " + stateGroup + " " + state); }
        public void OnSwitchChanged(uint switchGroup, uint value, ulong gameObject) { Calls.Add("switch " + switchGroup + " " + value + " " + gameObject); }
        public int PostTrigger(uint triggerId, ulong gameObject) { Calls.Add("trigger " + triggerId); return 0; }

        // A stopped context ends on the next tick.
        public void Tick()
        {
            foreach (EngineMusicContext c in Contexts)
            {
                if (c.Stopped)
                    c.Ended = true;
                if (c.Ended && !c.Reported)
                {
                    c.Reported = true;
                    c.Listener.OnMusicEnded(c);
                }
            }
        }

        public void StopAll() { foreach (EngineMusicContext c in Contexts) c.Stopped = true; }

        public IEnumerable<IPaMusicContext> Active
        {
            get
            {
                foreach (EngineMusicContext c in Contexts)
                    if (!c.Ended)
                        yield return c;
            }
        }
    }

    // ---- JSON for PaModel objects (architecture 4.1) ----

    public static class ModelJson
    {
        public static string Write(object o)
        {
            StringBuilder sb = new StringBuilder();
            Value(sb, o);
            return sb.ToString();
        }

        static void Value(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            Type t = v.GetType();
            if (v is string) { Str(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is PaCurve) { sb.Append((int)(PaCurve)v); return; }
            if (t.IsEnum) { Str(sb, v.ToString()); return; }
            if (v is float) { sb.Append(((float)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (v is double) { sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (t.IsPrimitive) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            if (v is PaPoint)
            {
                PaPoint p = (PaPoint)v;
                sb.Append('[').Append(p.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(p.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append((int)p.Interp).Append(']');
                return;
            }
            if (v is PaCue)
            {
                PaCue c = (PaCue)v;
                sb.Append('[').Append(c.Pos).Append(',');
                Str(sb, c.Label);
                sb.Append(']');
                return;
            }
            if (v is PaRange)
            {
                PaRange r = (PaRange)v;
                sb.Append("{\"min\":").Append(r.Min.ToString("R", CultureInfo.InvariantCulture)).Append(",\"max\":")
                  .Append(r.Max.ToString("R", CultureInfo.InvariantCulture)).Append('}');
                return;
            }
            if (v is Dictionary<uint, string>)
            {
                sb.Append('[');
                bool first = true;
                foreach (KeyValuePair<uint, string> kv in (Dictionary<uint, string>)v)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('[').Append(kv.Key).Append(',');
                    Str(sb, kv.Value);
                    sb.Append(']');
                }
                sb.Append(']');
                return;
            }
            if (v is Dictionary<int, double>)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<int, double> kv in (Dictionary<int, double>)v)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Str(sb, kv.Key.ToString(CultureInfo.InvariantCulture));
                    sb.Append(':').Append(kv.Value.ToString("R", CultureInfo.InvariantCulture));
                }
                sb.Append('}');
                return;
            }
            if (v is IEnumerable)
            {
                sb.Append('[');
                bool first = true;
                foreach (object o in (IEnumerable)v)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Value(sb, o);
                }
                sb.Append(']');
                return;
            }
            Obj(sb, v);
        }

        static void Obj(StringBuilder sb, object v)
        {
            Type t = v.GetType();
            object defaults = null;
            try { defaults = Activator.CreateInstance(t); } catch (Exception) { }
            sb.Append('{');
            bool first = true;
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object value = f.GetValue(v);
                if (value == null)
                    continue;
                if (value is ICollection && ((ICollection)value).Count == 0)
                    continue;
                if (defaults != null && !(value is ICollection) && !(value is IEnumerable && !(value is string)) && Equals(value, f.GetValue(defaults)))
                    continue;
                if (!first) sb.Append(',');
                first = false;
                Str(sb, char.ToLowerInvariant(f.Name[0]) + f.Name.Substring(1));
                sb.Append(':');
                Value(sb, value);
            }
            sb.Append('}');
        }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }

    // ---- data builder ----

    public sealed class BankBuilder
    {
        public readonly PaBank Bank;
        uint nextActionId;

        public BankBuilder(string name, string languageKey = "")
        {
            Bank = new PaBank
            {
                Format = 1, Name = name, Id = PaIds.Hash(name), Language = "", LanguageKey = languageKey ?? "",
                Events = new List<PaEvent>(), Actions = new List<PaAction>(), Nodes = new List<PaNode>(), Busses = new List<PaBus>(),
                Attenuations = new List<PaAttenuation>(), GameParameters = new List<PaGameParameter>(),
                StateGroups = new List<PaStateGroup>(), Effects = new List<PaEffect>(),
            };
            nextActionId = Bank.Id % 100000u * 1000u + 1u;
        }

        public PaAction Action(PaActionType type, uint target)
        {
            PaAction a = new PaAction { Id = nextActionId++, Type = type, Target = target, Exceptions = new PaObjectRef[0] };
            Bank.Actions.Add(a);
            return a;
        }

        public PaEvent Event(string name, params PaAction[] actions)
        {
            uint[] ids = new uint[actions.Length];
            for (int i = 0; i < actions.Length; i++)
                ids[i] = actions[i].Id;
            PaEvent e = new PaEvent { Id = PaIds.Hash(name), Name = name, Actions = ids };
            Bank.Events.Add(e);
            return e;
        }

        public PaNode Node(uint id, PaNodeType type, uint parent, params uint[] children)
        {
            PaNode n = new PaNode { Id = id, Type = type, Parent = parent, Children = children, Props = new PaProps() };
            Bank.Nodes.Add(n);
            return n;
        }

        public PaNode Sound(uint id, uint parent, uint media, string kind = "vorbis", bool languageSpecific = false)
        {
            PaNode n = Node(id, PaNodeType.Sound, parent);
            n.Source = new PaSource { Media = media, Kind = kind, Stream = "embedded", LanguageSpecific = languageSpecific };
            return n;
        }

        public PaNode RanSeq(uint id, uint parent, PaRanSeq rs, params uint[] children)
        {
            PaNode n = Node(id, PaNodeType.RanSeq, parent, children);
            PaWeighted[] list = new PaWeighted[children.Length];
            for (int i = 0; i < children.Length; i++)
                list[i] = new PaWeighted { Id = children[i], Weight = 50 };
            if (rs.Playlist == null || rs.Playlist.Length == 0)
                rs.Playlist = list;
            n.RanSeq = rs;
            return n;
        }
    }

    public static class Media
    {
        public static PaMediaEntry Entry(uint id, double seconds, string file = null)
        {
            return new PaMediaEntry { Id = id, File = file ?? ("media/" + id + ".ogg"), Format = "ogg", Rate = 1000, Channels = 1, Samples = (long)Math.Round(seconds * 1000) };
        }
    }

    // ---- fixture ----

    public struct CallbackRecord
    {
        public uint Type;
        public IntPtr Cookie;
        public ulong GameObject;
        public uint PlayingId, EventId, BankId;
        public int Result;
        public override string ToString()
        {
            return Type == PaCallbackType.EndOfEvent ? "end " + PlayingId : "bank " + BankId + " " + Result;
        }
    }

    public sealed class EngineFixture : IDisposable
    {
        public readonly PaTempDir Dir = new PaTempDir();
        public readonly FakePlatform Platform = new FakePlatform();
        public readonly List<PaBank> Banks = new List<PaBank>();
        public readonly List<PaMediaEntry> MediaLi = new List<PaMediaEntry>();
        public readonly Dictionary<string, List<PaMediaEntry>> ExternalByLang = new Dictionary<string, List<PaMediaEntry>>();
        public readonly Dictionary<string, List<PaMediaEntry>> MediaByLang = new Dictionary<string, List<PaMediaEntry>>();
        public readonly List<string> MissingBankFiles = new List<string>();
        public MemoryTrace Trace;
        IntPtr buffer;
        const int BufferSize = 64 * 1024;
        public static readonly IntPtr Cookie = new IntPtr(0x7777);
        public static readonly IntPtr Callback = new IntPtr(0x1234);
        public const ulong Go = 42;

        public EngineFixture()
        {
            Platform.settings.Root = Dir.Root;
            Platform.settings.Seed = 12345;
            Platform.Frame = 1;
            buffer = Marshal.AllocHGlobal(BufferSize);
            PaCallbacks.SerializerInit(buffer, BufferSize);
            while (PaCallbacks.Lock() != IntPtr.Zero) { }
        }

        public EngineVoices Voices { get { return Platform.Voices; } }
        public EngineMusic Music { get { return Platform.Music; } }

        public BankBuilder Bank(string name)
        {
            BankBuilder b = new BankBuilder(name);
            Banks.Add(b.Bank);
            return b;
        }

        public void WriteData()
        {
            PaManifest m = new PaManifest
            {
                Format = 1, WwiseBankVersion = 113, DefaultLanguage = "English(UK)",
                Languages = new List<PaLanguage>
                {
                    new PaLanguage { Name = "English(UK)", Key = "english_uk", BankLanguageId = 11 },
                    new PaLanguage { Name = "French(France)", Key = "french_france", BankLanguageId = 12 },
                },
                Banks = new List<PaBankInfo>(), Names = new Dictionary<uint, string>(),
            };
            foreach (PaBank b in Banks)
            {
                m.Banks.Add(new PaBankInfo { Name = b.Name, Id = b.Id, File = "banks/" + b.Name + ".json", Languages = new List<string>() });
                if (!MissingBankFiles.Contains(b.Name))
                    Dir.Write("banks/" + b.Name + ".json", ModelJson.Write(b));
                foreach (PaEvent e in b.Events)
                    m.Names[e.Id] = e.Name;
            }
            foreach (string missing in MissingBankFiles)
                if (m.Banks.Find(x => x.Name == missing) == null)
                    m.Banks.Add(new PaBankInfo { Name = missing, Id = PaIds.Hash(missing), File = "banks/" + missing + ".json", Languages = new List<string>() });
            Dir.Write("manifest.json", ModelJson.Write(m));
            Dir.Write("media.json", MediaTable("li", "", "", MediaLi, null));
            foreach (string key in new[] { "english_uk", "french_france" })
            {
                List<PaMediaEntry> media, ext;
                MediaByLang.TryGetValue(key, out media);
                ExternalByLang.TryGetValue(key, out ext);
                string prefix = key == "english_uk" ? "en-GB_" : "fr-FR_";
                Dir.Write("lang/" + key + "/media.json", MediaTable(key, key == "english_uk" ? "English(UK)" : "French(France)", prefix, media, ext));
            }
        }

        static string MediaTable(string scope, string language, string prefix, List<PaMediaEntry> media, List<PaMediaEntry> external)
        {
            StringBuilder sb = new StringBuilder("{\"format\":1,\"scope\":\"" + scope + "\",\"language\":\"" + language + "\",\"externalPrefix\":\"" + prefix + "\"");
            sb.Append(",\"media\":").Append(ModelJson.Write(media ?? new List<PaMediaEntry>()));
            sb.Append(",\"external\":").Append(ModelJson.Write(external ?? new List<PaMediaEntry>()));
            return sb.Append('}').ToString();
        }

        // Writes the data, installs the platform, initializes the engine (real mode) and loads
        // every bank synchronously.
        public void Start(bool loadBanks = true, bool trace = true)
        {
            WriteData();
            if (trace)
            {
                Trace = new MemoryTrace();
                PaLog.Sink = Trace;
            }
            PaRuntime.Platform = Platform;
            PaAssert.Equal(PaResult.Success, PaEngine.Init(), "Init");
            if (loadBanks)
                foreach (PaBank b in Banks)
                {
                    uint id;
                    PaAssert.Equal(PaResult.Success, PaEngine.LoadBank(b.Name, out id), "LoadBank " + b.Name);
                }
        }

        // Advances the clock and runs one frame (one tick).
        public void Step(double seconds = 0.0)
        {
            Platform.Clock += seconds;
            Platform.Frame++;
            PaEngine.Tick();
        }

        public void StepUntil(double time, double dt = 0.05)
        {
            while (Platform.Clock < time - 1e-9)
                Step(Math.Min(dt, time - Platform.Clock));
        }

        public uint Post(string name, ulong go = Go, bool callback = true)
        {
            return PaEngine.PostEvent(name, go, callback ? 1u : 0u, callback ? Callback : IntPtr.Zero, callback ? Cookie : IntPtr.Zero, 0, IntPtr.Zero, 0);
        }

        public List<CallbackRecord> Callbacks()
        {
            List<CallbackRecord> list = new List<CallbackRecord>();
            for (IntPtr first = PaCallbacks.Lock(); first != IntPtr.Zero; first = PaCallbacks.Lock())
            {
                int ptr = IntPtr.Size;
                int header = ptr * 2 + 4;
                for (IntPtr rec = first; rec != IntPtr.Zero; rec = Marshal.ReadIntPtr(rec, ptr))
                {
                    CallbackRecord r = new CallbackRecord();
                    r.Type = unchecked((uint)Marshal.ReadInt32(rec, ptr * 2));
                    if (r.Type == PaCallbackType.EndOfEvent)
                    {
                        r.Cookie = Marshal.ReadIntPtr(rec, header);
                        r.GameObject = unchecked((ulong)Marshal.ReadIntPtr(rec, header + ptr).ToInt64());
                        r.PlayingId = unchecked((uint)Marshal.ReadInt32(rec, header + ptr * 2));
                        r.EventId = unchecked((uint)Marshal.ReadInt32(rec, header + ptr * 2 + 4));
                    }
                    else
                    {
                        r.Cookie = Marshal.ReadIntPtr(rec, 0);
                        r.BankId = unchecked((uint)Marshal.ReadInt32(rec, header));
                        r.Result = Marshal.ReadInt32(rec, header + 4 + ptr);
                    }
                    list.Add(r);
                }
                PaCallbacks.Unlock();
            }
            return list;
        }

        public List<uint> EndOfEvents()
        {
            List<uint> ids = new List<uint>();
            foreach (CallbackRecord r in Callbacks())
                if (r.Type == PaCallbackType.EndOfEvent)
                    ids.Add(r.PlayingId);
            return ids;
        }

        public void Dispose()
        {
            PaEngine.Term();
            while (PaCallbacks.Lock() != IntPtr.Zero) { }
            PaCallbacks.SerializerTerm();
            Marshal.FreeHGlobal(buffer);
            PaLog.Sink = null;
            PaRuntime.Platform = null;
            Dir.Dispose();
        }
    }
}
