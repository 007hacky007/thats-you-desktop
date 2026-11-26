// One playing music object (a Play action on a music node) and everything it owns: the tree of
// switch and playlist cursors, the chain of segment instances on the voice clock, their voices
// and the scheduled gain changes. Driven by PaMusicPlayer on the main thread.

using System;
using System.Collections.Generic;

namespace PortAudio
{
    public sealed class PaMusicContext : IPaMusicContext, IPaVoiceListener
    {
        // ---- cursors -------------------------------------------------------------------------

        internal sealed class SegRef
        {
            public PaSegmentInfo Info;
            public List<uint> Path = new List<uint>();   // root container ... segment
        }

        internal abstract class Cursor
        {
            public uint NodeId;
            public abstract SegRef Next(PaMusicContext c);
            public virtual bool Idle { get { return false; } }
        }

        sealed class SegmentCursor : Cursor
        {
            bool done;
            public override SegRef Next(PaMusicContext c)
            {
                if (done)
                    return null;
                done = true;
                var info = PaSegmentInfo.From(c.Banks != null ? c.Banks.Node(NodeId) : null, c.Banks);
                if (info == null)
                    return null;
                var r = new SegRef { Info = info };
                r.Path.Add(NodeId);
                return r;
            }
        }

        sealed class PlaylistCursor : Cursor
        {
            public PaMusicPlaylist Playlist;
            int misses;
            public override SegRef Next(PaMusicContext c)
            {
                while (true)
                {
                    uint id = Playlist.Next();
                    if (id == 0)
                        return null;
                    var info = PaSegmentInfo.From(c.Banks != null ? c.Banks.Node(id) : null, c.Banks);
                    if (info == null)
                    {
                        if (++misses > 64)
                            return null;   // playlist made of unknown segments
                        continue;
                    }
                    var r = new SegRef { Info = info };
                    r.Path.Add(NodeId);
                    r.Path.Add(id);
                    return r;
                }
            }
        }

        internal sealed class SwitchCursor : Cursor
        {
            public PaNode Node;
            public PaMusicDecision Decision;
            public uint[] Keys;
            public bool Evaluated;
            public uint Child;
            public Cursor ChildCursor;

            public void Select(PaMusicContext c, uint[] keys)
            {
                Keys = keys;
                Evaluated = true;
                var choice = Decision.Resolve(keys, c.World != null ? c.World.Random : null);
                Child = choice.Node;
                ChildCursor = Child != 0 ? c.CreateCursor(Child, 0) : null;
            }

            public override SegRef Next(PaMusicContext c)
            {
                if (!Evaluated)
                    Select(c, PaMusicDecision.Values(Node.Music, c.World, c.GameObject));
                if (ChildCursor == null)
                    return null;
                var r = ChildCursor.Next(c);
                if (r != null)
                    r.Path.Insert(0, NodeId);
                return r;
            }

            public override bool Idle { get { return ChildCursor == null || ChildCursor.Idle; } }
        }

        internal Cursor CreateCursor(uint nodeId, int depth)
        {
            var n = Banks != null ? Banks.Node(nodeId) : null;
            if (n == null || depth > 16)
                return null;
            played.Add(nodeId);
            switch (n.Type)
            {
                case PaNodeType.MusicSegment:
                case PaNodeType.MusicTrack:
                    return new SegmentCursor { NodeId = nodeId };
                case PaNodeType.MusicRanSeq:
                    return new PlaylistCursor { NodeId = nodeId, Playlist = new PaMusicPlaylist(n.Music != null ? n.Music.Playlist : null, World != null ? World.Random : null) };
                case PaNodeType.MusicSwitch:
                    return new SwitchCursor { NodeId = nodeId, Node = n, Decision = PaMusicDecision.For(n.Music) };
                default:
                    return null;
            }
        }

        // ---- segment instances and voices ------------------------------------------------------

        internal sealed class SegInst
        {
            public PaSegmentInfo Info;
            public List<uint> Path;
            public double T0;
            public bool Placed;            // T0 known
            public bool Realized;          // voices created
            public bool NextDecided;       // successor pulled (or none)
            public bool PlayPreEntry = true, PlayPostExit = true;
            public double CutBefore = double.NegativeInfinity, CutAfter = double.PositiveInfinity;
            public double FadeInAt = double.NaN; public int FadeInMs; public PaCurve FadeInCurve = PaCurve.Linear;
            public double FadeOutAt = double.NaN; public int FadeOutMs; public PaCurve FadeOutCurve = PaCurve.Linear;
            public bool Stinger;
            public readonly List<VoiceRec> Voices = new List<VoiceRec>();
            public double EntryAbs { get { return PaMusicTimeline.EntryTime(Info, T0); } }
            public double ExitAbs { get { return PaMusicTimeline.ExitTime(Info, T0); } }
        }

        internal sealed class VoiceRec
        {
            public IPaVoice Voice;
            public PaVoiceSpec Spec;
            public SegInst Seg;
            public bool Ended, Cancelled, Deferred;   // Deferred: cancelled by Pause, replayed on Resume
            public double StopAt = double.NaN; public int StopFadeMs; public PaCurve StopCurve = PaCurve.Linear;
            public readonly Dictionary<int, float> Gains = new Dictionary<int, float>();
            public double End { get { return Spec.PlayLength > 0 ? Spec.StartAt + Spec.PlayLength : double.PositiveInfinity; } }
        }

        sealed class GainOp
        {
            public double At; public VoiceRec Rec; public int Slot; public float Db; public int Ms; public PaCurve Curve;
        }

        // ---- state ---------------------------------------------------------------------------

        readonly PaMusicPlayer player;
        readonly IPaMusicListener listener;
        readonly HashSet<uint> played = new HashSet<uint>();
        readonly List<SegInst> segs = new List<SegInst>();
        readonly List<SegInst> stingerSegs = new List<SegInst>();
        readonly Dictionary<IPaVoice, VoiceRec> recs = new Dictionary<IPaVoice, VoiceRec>();
        readonly List<GainOp> ops = new List<GainOp>();
        readonly Dictionary<uint, int> subTrackPos = new Dictionary<uint, int>();
        readonly PaStingers stingers = new PaStingers();
        Cursor root;
        bool waitingFirst; double readyDeadline; int startFadeMs; PaCurve startFadeCurve;
        bool stopping, paused, ended, endReported, reevalPending;
        double pausedAt;
        static readonly HashSet<string> tracedOnce = new HashSet<string>();

        public uint PlayingId { get; private set; }
        public ulong GameObject { get; private set; }
        public uint RootNode { get; private set; }
        public bool Ended { get { return ended; } }
        public bool Paused { get { return paused; } }
        public bool Stopping { get { return stopping; } }
        internal IPaMusicListener Listener { get { return listener; } }
        internal bool EndReported { get { return endReported; } set { endReported = value; } }

        internal IPaWorld World { get { return PaRuntime.World; } }
        internal PaBankStore Banks { get { return PaRuntime.World != null ? PaRuntime.World.Banks : null; } }
        static IPaVoices V { get { return PaRuntime.Voices; } }
        double Now { get { return V != null ? V.Now : 0; } }

        internal PaMusicContext(PaMusicPlayer player, uint rootNode, ulong gameObject, uint playingId, IPaMusicListener listener)
        {
            this.player = player;
            this.listener = listener;
            RootNode = rootNode;
            GameObject = gameObject;
            PlayingId = playingId;
        }

        public bool Involves(uint nodeId)
        {
            if (nodeId == 0)
                return false;
            if (nodeId == RootNode || played.Contains(nodeId))
                return true;
            var b = Banks;
            return b != null && b.IsUnder(RootNode, nodeId);
        }

        // Voices currently owned (not cancelled, not ended).
        public IEnumerable<IPaVoice> Voices
        {
            get
            {
                foreach (var r in recs.Values)
                    if (!r.Ended && !r.Cancelled && r.Voice != null)
                        yield return r.Voice;
            }
        }

        int LiveVoices
        {
            get
            {
                int n = 0;
                foreach (var r in recs.Values)
                    if (!r.Ended && !r.Cancelled)
                        n++;
                foreach (var s in segs)
                    foreach (var r in s.Voices)
                        if (r.Deferred && !r.Cancelled)
                            n++;
                return n;
            }
        }

        static void Trace(params object[] kv)
        {
            if (PaLog.Tracing)
                PaLog.Trace("music", kv);
        }

        static void TraceOnce(string key, string msg)
        {
            lock (tracedOnce)
                if (!tracedOnce.Add(key))
                    return;
            PaLog.Trace("warn", "msg", msg);
        }

        // ---- start ----------------------------------------------------------------------------

        // False when there is nothing at all to play.
        internal bool Start(int fadeInMs, PaCurve curve)
        {
            played.Add(RootNode);
            root = CreateCursor(RootNode, 0);
            if (root == null)
                return false;
            startFadeMs = fadeInMs;
            startFadeCurve = curve;
            var first = root.Next(this);
            if (first == null)
            {
                if (!(root is SwitchCursor))
                    return false;
                Trace("op", "play", "playingId", PlayingId, "node", RootNode, "segment", 0u, "sync", "nothing", "at", Now);
                return true;   // a switch with nothing selected waits for a state or switch change
            }
            AddFirst(first, fadeInMs, curve);
            Trace("op", "play", "playingId", PlayingId, "node", RootNode, "segment", first.Info.Id, "sync", "start", "at", Now);
            if (FirstReady())
                PlaceFirst();
            return true;
        }

        void AddFirst(SegRef r, int fadeInMs, PaCurve curve)
        {
            var s = NewSeg(r);
            s.PlayPreEntry = false;
            segs.Add(s);
            waitingFirst = true;
            readyDeadline = Now + player.ReadyTimeoutSec;
            startFadeMs = fadeInMs;
            startFadeCurve = curve;
            Prefetch(s);
        }

        SegInst NewSeg(SegRef r)
        {
            foreach (var id in r.Path)
                played.Add(id);
            foreach (var t in r.Info.Tracks)
                played.Add(t.Id);
            return new SegInst { Info = r.Info, Path = r.Path };
        }

        bool FirstReady()
        {
            if (V == null || segs.Count == 0)
                return true;
            foreach (var m in MediaOf(segs[0]))
                if (!V.IsReady(m))
                    return false;
            return true;
        }

        void PlaceFirst()
        {
            waitingFirst = false;
            var s = segs[0];
            double start = Now + player.LeadSec;
            s.T0 = start - s.Info.EntryMs / 1000.0;
            s.CutBefore = start;
            s.Placed = true;
            if (startFadeMs > 0)
            {
                s.FadeInAt = start;
                s.FadeInMs = startFadeMs;
                s.FadeInCurve = startFadeCurve;
            }
        }

        IEnumerable<PaMediaEntry> MediaOf(SegInst s)
        {
            foreach (var t in s.Info.Tracks)
            {
                if (t.Music == null || t.Music.Clips == null)
                    continue;
                foreach (var c in t.Music.Clips)
                {
                    var m = FindMedia(t, c);
                    if (m != null)
                        yield return m;
                }
            }
        }

        void Prefetch(SegInst s)
        {
            if (V == null)
                return;
            foreach (var m in MediaOf(s))
                if (!V.IsReady(m))
                    V.Prefetch(m);
        }

        PaMediaEntry FindMedia(PaNode track, PaClip clip)
        {
            var w = World;
            if (w == null || w.Media == null || clip == null)
                return null;
            bool lang = false;
            if (track.Music != null && track.Music.Sources != null)
                foreach (var src in track.Music.Sources)
                    if (src != null && src.Media == clip.Media)
                    {
                        lang = src.LanguageSpecific;
                        break;
                    }
            return w.Media.Find(clip.Media, lang, w.VoiceLanguageKey);
        }

        // ---- tick -------------------------------------------------------------------------------

        internal void Tick()
        {
            if (ended)
                return;
            if (!paused && !stopping)
            {
                if (waitingFirst && segs.Count > 0 && (FirstReady() || Now >= readyDeadline))
                {
                    if (Now >= readyDeadline && !FirstReady())
                        Trace("op", "late", "playingId", PlayingId, "node", RootNode, "segment", segs[0].Info.Id, "sync", "readyTimeout", "at", Now);
                    PlaceFirst();
                }
                PlanSuccessors();
                RealizeDue();
                RunOps();
                Prune();
            }
            CheckEnd();
        }

        void PlanSuccessors()
        {
            for (int guard = 0; guard < 16; guard++)
            {
                if (segs.Count == 0)
                    return;
                var last = segs[segs.Count - 1];
                if (!last.Placed || last.NextDecided)
                    return;
                // keep at most one unrealized instance ahead of the realized ones
                if (segs.Count >= 2 && !segs[segs.Count - 2].Realized)
                    return;
                if (last.ExitAbs > Now + player.HorizonSec + 60.0 && segs.Count >= 2)
                    return;
                last.NextDecided = true;
                var r = root != null ? root.Next(this) : null;
                if (r == null)
                {
                    last.PlayPostExit = true;   // the final segment rings out
                    return;
                }
                var next = NewSeg(r);
                uint owner = r.Path.Count >= 2 ? r.Path[r.Path.Count - 2] : r.Path[0];
                var rule = PaMusicTransitions.Find(Banks, owner, last.Path, r.Path);
                if (rule != null && rule.Segment != null && rule.Segment.Segment != 0)
                    TraceOnce("tseg" + owner, "music transition segment " + rule.Segment.Segment + " ignored");
                last.PlayPostExit = rule == null || rule.SrcRule == null || rule.SrcRule.PlayPostExit;
                next.PlayPreEntry = rule == null || rule.DstRule == null || rule.DstRule.PlayPreEntry;
                next.T0 = PaMusicTimeline.ChainT0(last.T0, last.Info, next.Info);
                next.Placed = true;
                next.CutBefore = next.PlayPreEntry ? double.NegativeInfinity : next.EntryAbs;
                if (rule != null && rule.SrcRule != null && rule.SrcRule.FadeMs > 0)
                    ApplyFadeOut(last, last.ExitAbs + rule.SrcRule.FadeOffsetMs / 1000.0, rule.SrcRule.FadeMs, rule.SrcRule.Curve);
                if (rule != null && rule.DstRule != null && rule.DstRule.FadeMs > 0)
                {
                    next.FadeInAt = next.EntryAbs + rule.DstRule.FadeOffsetMs / 1000.0;
                    next.FadeInMs = rule.DstRule.FadeMs;
                    next.FadeInCurve = rule.DstRule.Curve;
                }
                segs.Add(next);
                Prefetch(next);
                if (next.Info.ExitMs <= next.Info.EntryMs && last.Info.ExitMs <= last.Info.EntryMs)
                    return;   // zero-length segments: plan one per tick
            }
        }

        double CutAfterOf(SegInst s)
        {
            double cut = s.PlayPostExit ? double.PositiveInfinity : s.ExitAbs;
            return Math.Min(cut, s.CutAfter);
        }

        void RealizeDue()
        {
            foreach (var s in segs.ToArray())
            {
                if (s.Realized || !s.Placed)
                    continue;
                if (!s.NextDecided)
                    return;   // the successor decides whether the post-exit plays
                double cutBefore = Math.Max(s.CutBefore, Now);
                double earliest = PaMusicTimeline.EarliestStart(s.Info, s.T0, cutBefore, CutAfterOf(s));
                if (double.IsInfinity(earliest))
                    earliest = s.EntryAbs;
                if (earliest > Now + player.HorizonSec)
                    return;
                Realize(s);
            }
        }

        void Realize(SegInst s)
        {
            s.Realized = true;
            double cutBefore = Math.Max(s.CutBefore, Now);
            double cutAfter = CutAfterOf(s);
            Trace("op", s.Stinger ? "stinger" : "segment", "playingId", PlayingId, "node", RootNode, "segment", s.Info.Id,
                "sync", s.Stinger ? "stinger" : "entry", "at", s.EntryAbs);
            for (int ti = 0; ti < s.Info.Tracks.Length; ti++)
            {
                var track = s.Info.Tracks[ti];
                if (track.Music == null || track.Music.Clips == null)
                    continue;
                foreach (int ci in PaMusicTimeline.ClipsFor(track, ChooseSubTrack(track)))
                {
                    var clip = track.Music.Clips[ci];
                    PaClipPlacement p;
                    if (!PaMusicTimeline.PlaceClip(clip, s.T0, cutBefore, cutAfter, out p))
                        continue;
                    p.ClipIndex = ci;
                    var spec = new PaVoiceSpec
                    {
                        PlayingId = PlayingId, GameObject = GameObject, NodeId = track.Id,
                        Media = FindMedia(track, clip), Music = true, LoopCount = 1,
                        StartAt = p.StartAt, SourceOffset = p.SourceOffset, PlayLength = p.PlayLength, Tag = this,
                    };
                    var rec = new VoiceRec { Spec = spec, Seg = s };
                    s.Voices.Add(rec);
                    StartVoice(rec);
                    ApplyAutomation(rec, track, ci, p);
                    ApplySegFades(rec);
                }
            }
        }

        int ChooseSubTrack(PaNode track)
        {
            var m = track.Music;
            if (m == null || m.TrackType == null || m.TrackType == "normal")
                return -1;
            int n = PaMusicTimeline.SubTrackCount(track);
            if (n <= 1)
                return 0;
            if (m.TrackType == "random")
                return World != null && World.Random != null ? World.Random.Range(0, n) : 0;
            if (m.TrackType == "switch")
                TraceOnce("swtrack" + track.Id, "music switch track " + track.Id + " plays sub-track 0");
            int pos;
            subTrackPos.TryGetValue(track.Id, out pos);
            subTrackPos[track.Id] = pos + 1;
            return m.TrackType == "sequence" ? pos % n : 0;
        }

        void StartVoice(VoiceRec rec)
        {
            if (V == null)
                return;
            rec.Voice = V.Play(rec.Spec, this);
            if (rec.Voice == null)
            {
                rec.Ended = true;
                return;
            }
            recs[rec.Voice] = rec;
            foreach (var g in rec.Gains)
                rec.Voice.SetGainDb(g.Key, g.Value);
            if (!double.IsNaN(rec.StopAt))
                rec.Voice.StopAt(rec.StopAt, rec.StopFadeMs, rec.StopCurve);
        }

        void SetGain(VoiceRec rec, int slot, float db)
        {
            rec.Gains[slot] = db;
            if (rec.Voice != null && !rec.Cancelled)
                rec.Voice.SetGainDb(slot, db);
        }

        void StopVoiceAt(VoiceRec rec, double at, int fadeMs, PaCurve curve)
        {
            if (!double.IsNaN(rec.StopAt) && rec.StopAt <= at)
                return;
            rec.StopAt = at;
            rec.StopFadeMs = fadeMs;
            rec.StopCurve = curve;
            if (rec.Voice != null && !rec.Cancelled && !rec.Ended)
                rec.Voice.StopAt(at, fadeMs, curve);
        }

        void AddOp(VoiceRec rec, double at, int slot, float db, int ms, PaCurve curve)
        {
            ops.Add(new GainOp { At = at, Rec = rec, Slot = slot, Db = db, Ms = ms < 0 ? 0 : ms, Curve = curve });
        }

        // Points along a curve: gain at the voice start, then one fade per remaining span.
        void Ramp(VoiceRec rec, int slot, PaPoint[] pts, double origin, bool linearGain)
        {
            if (pts == null || pts.Length == 0)
                return;
            double start = rec.Spec.StartAt;
            float x0 = (float)((start - origin) * 1000.0);
            float v0 = PaCurves.Evaluate(pts, x0);
            SetGain(rec, slot, linearGain ? PaDb.FromLinear(v0) : v0);
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                double a = origin + pts[i].X / 1000.0, b = origin + pts[i + 1].X / 1000.0;
                if (b <= start)
                    continue;
                double from = Math.Max(a, start);
                float y = pts[i + 1].Y;
                AddOp(rec, from, slot, linearGain ? PaDb.FromLinear(y) : y, (int)Math.Round((b - from) * 1000.0), pts[i].Interp);
            }
        }

        void ApplyAutomation(VoiceRec rec, PaNode track, int clipIndex, PaClipPlacement p)
        {
            var autos = track.Music.Automation;
            if (autos == null)
                return;
            foreach (var a in autos)
            {
                if (a == null || a.Clip != clipIndex || a.Points == null || a.Points.Length == 0)
                    continue;
                switch (a.Type)
                {
                    case "fadeIn":
                        Ramp(rec, 2, a.Points, p.ClipStart, true);
                        break;
                    case "fadeOut":
                    {
                        if (double.IsInfinity(p.ClipEnd))
                            break;
                        double span = (a.Points[a.Points.Length - 1].X - a.Points[0].X) / 1000.0;
                        double origin = p.ClipEnd - span - a.Points[0].X / 1000.0;
                        if (p.ClipEnd - span < rec.Spec.StartAt)
                        {
                            Ramp(rec, 2, a.Points, origin, true);
                        }
                        else
                        {
                            // gain 1 until the fade begins
                            var pts = a.Points;
                            AddOp(rec, origin + pts[0].X / 1000.0, 2, PaDb.FromLinear(pts[0].Y), 0, PaCurve.Linear);
                            for (int i = 0; i + 1 < pts.Length; i++)
                                AddOp(rec, origin + pts[i].X / 1000.0, 2, PaDb.FromLinear(pts[i + 1].Y),
                                    (int)Math.Round((pts[i + 1].X - pts[i].X)), pts[i].Interp);
                        }
                        break;
                    }
                    case "volume":
                        Ramp(rec, 3, a.Points, p.ClipStart, false);
                        break;
                    default:
                        TraceOnce("auto" + a.Type, "music clip automation '" + a.Type + "' not reproduced");
                        break;
                }
            }
        }

        void ApplySegFades(VoiceRec rec)
        {
            var s = rec.Seg;
            if (!double.IsNaN(s.FadeInAt) && s.FadeInMs > 0)
                FadeInVoice(rec, s.FadeInAt, s.FadeInMs, s.FadeInCurve);
            if (!double.IsNaN(s.FadeOutAt))
                FadeOutVoice(rec, s.FadeOutAt, s.FadeOutMs, s.FadeOutCurve);
        }

        void FadeInVoice(VoiceRec rec, double at, int ms, PaCurve curve)
        {
            double end = at + ms / 1000.0;
            double start = rec.Spec.StartAt;
            if (start >= end)
                return;
            SetGain(rec, 1, -200f);
            double from = Math.Max(at, start);
            AddOp(rec, from, 1, 0f, (int)Math.Round((end - from) * 1000.0), curve);
        }

        void FadeOutVoice(VoiceRec rec, double at, int ms, PaCurve curve)
        {
            if (rec.Ended || rec.Cancelled)
                return;
            double end = at + Math.Max(0, ms) / 1000.0;
            if (rec.Spec.StartAt >= end - 1e-6)
            {
                Cancel(rec);
                return;
            }
            if (ms > 0)
                AddOp(rec, Math.Max(at, rec.Spec.StartAt), 1, -200f, (int)Math.Round((end - Math.Max(at, rec.Spec.StartAt)) * 1000.0), curve);
            StopVoiceAt(rec, end, 0, curve);
        }

        void ApplyFadeOut(SegInst s, double at, int ms, PaCurve curve)
        {
            s.FadeOutAt = at;
            s.FadeOutMs = ms;
            s.FadeOutCurve = curve;
            foreach (var r in s.Voices.ToArray())
                FadeOutVoice(r, at, ms, curve);
        }

        void Cancel(VoiceRec rec)
        {
            if (rec.Cancelled)
                return;
            rec.Cancelled = true;
            if (rec.Voice != null && !rec.Ended)
                rec.Voice.Stop(0, PaCurve.Linear);
        }

        void RunOps()
        {
            if (ops.Count == 0)
                return;
            double now = Now;
            for (int i = 0; i < ops.Count; )
            {
                var op = ops[i];
                if (op.Rec.Cancelled || op.Rec.Ended)
                {
                    ops.RemoveAt(i);
                    continue;
                }
                if (op.At > now + 1e-9 || op.Rec.Voice == null)
                {
                    i++;
                    continue;
                }
                ops.RemoveAt(i);
                int ms = op.Ms - (int)Math.Round((now - op.At) * 1000.0);
                op.Rec.Gains[op.Slot] = op.Db;
                if (ms <= 0)
                    op.Rec.Voice.SetGainDb(op.Slot, op.Db);
                else
                    op.Rec.Voice.FadeTo(op.Slot, op.Db, ms, op.Curve);
            }
        }

        void Prune()
        {
            double now = Now;
            while (segs.Count > 2)
            {
                var s = segs[0];
                if (!s.Realized || s.ExitAbs > now - 1.0)
                    break;
                bool live = false;
                foreach (var r in s.Voices)
                    if (!r.Ended && !r.Cancelled)
                        live = true;
                if (live)
                    break;
                segs.RemoveAt(0);
            }
            stingerSegs.RemoveAll(st =>
            {
                foreach (var r in st.Voices)
                    if (!r.Ended && !r.Cancelled)
                        return false;
                return true;
            });
        }

        void CheckEnd()
        {
            if (ended)
                return;
            if (LiveVoices > 0)
                return;
            if (stopping)
            {
                ended = true;
                return;
            }
            if (paused || waitingFirst)
                return;
            foreach (var s in segs)
                if (!s.Realized || !s.NextDecided)
                    return;
            if (root == null || root.Idle)
                return;   // a switch on nothing keeps listening
            if (segs.Count > 0 && segs[segs.Count - 1].ExitAbs > Now)
                return;
            ended = true;
            Trace("op", "end", "playingId", PlayingId, "node", RootNode, "segment", 0u, "sync", "", "at", Now);
        }

        public void OnVoiceEnded(IPaVoice voice, PaVoiceEnd reason)
        {
            VoiceRec rec;
            if (voice != null && recs.TryGetValue(voice, out rec))
            {
                if (rec.Voice == voice)
                    rec.Ended = !rec.Deferred;
                recs.Remove(voice);
            }
        }

        // ---- control -----------------------------------------------------------------------------

        internal void Stop(int fadeMs, PaCurve curve)
        {
            if (ended)
                return;
            stopping = true;
            waitingFirst = false;
            ops.Clear();
            foreach (var s in segs)
                foreach (var r in s.Voices)
                    if (r.Deferred)
                        r.Cancelled = true;
            foreach (var r in new List<VoiceRec>(recs.Values))
            {
                if (r.Ended || r.Cancelled || r.Voice == null)
                    continue;
                bool started = r.Voice.State == PaVoiceState.Playing || r.Voice.State == PaVoiceState.Paused || r.Voice.State == PaVoiceState.Stopping;
                r.Voice.Stop(started ? fadeMs : 0, curve);
            }
            Trace("op", "stop", "playingId", PlayingId, "node", RootNode, "segment", 0u, "sync", "", "at", Now);
        }

        internal void Pause(int fadeMs, PaCurve curve)
        {
            if (ended || paused || stopping)
                return;
            paused = true;
            pausedAt = Now;
            foreach (var r in new List<VoiceRec>(recs.Values))
            {
                if (r.Ended || r.Cancelled || r.Voice == null)
                    continue;
                var st = r.Voice.State;
                bool unstarted = st == PaVoiceState.Scheduled || (st == PaVoiceState.Loading && r.Spec.StartAt > pausedAt);
                if (unstarted)
                {
                    r.Deferred = true;
                    recs.Remove(r.Voice);
                    r.Voice.Stop(0, PaCurve.Linear);
                    r.Voice = null;
                }
                else
                    r.Voice.Pause(fadeMs, curve);
            }
        }

        internal void Resume(int fadeMs, PaCurve curve)
        {
            if (!paused || ended)
                return;
            paused = false;
            double shift = Math.Max(0, Now - pausedAt);
            if (shift > 0)
            {
                foreach (var s in segs)
                    ShiftSeg(s, shift);
                foreach (var s in stingerSegs)
                    ShiftSeg(s, shift);
                foreach (var op in ops)
                    op.At += shift;
                readyDeadline += shift;
                stingers.Shift(shift);
            }
            foreach (var r in new List<VoiceRec>(recs.Values))
            {
                if (r.Ended || r.Cancelled || r.Voice == null)
                    continue;
                r.Voice.Resume(fadeMs, curve);
                if (!double.IsNaN(r.StopAt))
                    r.Voice.StopAt(r.StopAt, r.StopFadeMs, r.StopCurve);
            }
            foreach (var list in new[] { segs, stingerSegs })
                foreach (var s in list)
                    foreach (var r in s.Voices)
                        if (r.Deferred && !r.Cancelled)
                        {
                            r.Deferred = false;
                            StartVoice(r);
                        }
            if (reevalPending)
            {
                reevalPending = false;
                ReevaluateAll();
            }
        }

        static void ShiftSeg(SegInst s, double dt)
        {
            s.T0 += dt;
            s.CutBefore += dt;
            s.CutAfter += dt;
            s.FadeInAt += dt;
            s.FadeOutAt += dt;
            foreach (var r in s.Voices)
            {
                if (r.Ended || r.Cancelled)
                    continue;
                if (!double.IsNaN(r.StopAt))
                    r.StopAt += dt;
                if (r.Deferred)
                    r.Spec.StartAt += dt;
            }
        }

        // ---- switches and transitions -----------------------------------------------------------------

        IEnumerable<SwitchCursor> LiveSwitches()
        {
            var c = root;
            while (c is SwitchCursor)
            {
                var sw = (SwitchCursor)c;
                yield return sw;
                c = sw.ChildCursor;
            }
        }

        static bool UsesGroup(SwitchCursor sw, uint group, bool isState)
        {
            var args = sw.Node.Music != null ? sw.Node.Music.Arguments : null;
            if (args == null)
                return false;
            foreach (var a in args)
                if (a != null && a.Group == group && a.IsState == isState)
                    return true;
            return false;
        }

        internal bool UsesSync(uint group, bool isState)
        {
            foreach (var sw in LiveSwitches())
                if (UsesGroup(sw, group, isState))
                    return true;
            return false;
        }

        internal void OnSyncChanged(uint group, bool isState)
        {
            if (ended || stopping)
                return;
            if (paused)
            {
                reevalPending = true;
                return;
            }
            foreach (var sw in new List<SwitchCursor>(LiveSwitches()))
            {
                if (!sw.Evaluated || !UsesGroup(sw, group, isState))
                    continue;
                if (Reevaluate(sw))
                    return;   // the subtree below was rebuilt with current values
            }
        }

        void ReevaluateAll()
        {
            foreach (var sw in new List<SwitchCursor>(LiveSwitches()))
                if (sw.Evaluated && Reevaluate(sw))
                    return;
        }

        static bool SameKeys(uint[] a, uint[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        // True when a transition happened.
        bool Reevaluate(SwitchCursor sw)
        {
            var keys = PaMusicDecision.Values(sw.Node.Music, World, GameObject);
            if (SameKeys(keys, sw.Keys))
                return false;
            uint oldChild = sw.Child;
            bool hadChild = sw.ChildCursor != null;
            var choice = sw.Decision.Resolve(keys, World != null ? World.Random : null);
            if (choice.Node == oldChild && hadChild && sw.Node.Music != null && sw.Node.Music.ContinuePlaying)
            {
                sw.Keys = keys;
                return false;
            }
            if (choice.Node == oldChild && !hadChild)
            {
                sw.Keys = keys;
                return false;   // still nothing
            }
            sw.Keys = keys;
            sw.Child = choice.Node;
            sw.ChildCursor = choice.Node != 0 ? CreateCursor(choice.Node, 0) : null;
            Transition(sw, oldChild);
            return true;
        }

        void Transition(SwitchCursor sw, uint oldChild)
        {
            double now = Now;
            double t = now + player.LeadSec;
            if (waitingFirst || segs.Count == 0 || !segs[0].Placed)
            {
                // nothing sounding yet: start over from the new selection
                foreach (var s in segs)
                    foreach (var r in s.Voices)
                        Cancel(r);
                segs.Clear();
                waitingFirst = false;
                var first = root.Next(this);
                Trace("op", "transition", "playingId", PlayingId, "node", sw.NodeId, "segment", first != null ? first.Info.Id : 0u, "sync", "start", "at", t);
                if (first != null)
                {
                    AddFirst(first, startFadeMs, startFadeCurve);
                    if (FirstReady())
                        PlaceFirst();
                }
                return;
            }

            // source: the first instance under this switch with a sync point at or after t
            SegInst src = null;
            double sync = double.NaN;
            int srcIndex = -1;
            PaTransitionRule rule = null;
            var dstRef = (SegRef)null;
            // the rule needs the destination path, which needs the destination cursor's first segment
            dstRef = root.Next(this);
            var dstPath = new List<uint>();
            if (dstRef != null)
                dstPath.AddRange(Below(dstRef.Path, sw.NodeId));
            else
                dstPath.Add(sw.Child);

            SegInst lastUnder = null;
            int lastUnderIndex = -1;
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                if (!s.Placed || !s.Path.Contains(sw.NodeId))
                    continue;
                if (s.ExitAbs < t - 1e-6 && i + 1 < segs.Count)
                    continue;
                lastUnder = s;
                lastUnderIndex = i;
                var srcPath = new List<uint>(Below(s.Path, sw.NodeId));
                var r = PaMusicTransitions.Find(Banks, sw.NodeId, srcPath, dstPath);
                string syncType = r != null && r.SrcRule != null ? r.SrcRule.Sync : "exitMarker";
                uint cue = r != null && r.SrcRule != null ? r.SrcRule.CueFilter : 0;
                double at = PaMusicTransitions.SyncIn(syncType, cue, s.Info, s.T0, t);
                if (!double.IsNaN(at))
                {
                    src = s;
                    srcIndex = i;
                    sync = at;
                    rule = r;
                    break;
                }
            }
            if (src == null)
            {
                if (lastUnder != null)
                {
                    src = lastUnder;
                    srcIndex = lastUnderIndex;
                    sync = Math.Max(t, src.ExitAbs);
                    rule = PaMusicTransitions.Find(Banks, sw.NodeId, new List<uint>(Below(src.Path, sw.NodeId)), dstPath);
                }
                else
                {
                    sync = t;
                    var oldPath = new List<uint> { oldChild };
                    rule = PaMusicTransitions.Find(Banks, sw.NodeId, oldPath, dstPath);
                }
            }
            if (rule != null && rule.Segment != null && rule.Segment.Segment != 0)
            {
                TraceOnce("tseg" + sw.NodeId + "_" + rule.Segment.Segment, "music transition segment " + rule.Segment.Segment + " ignored");
                Trace("op", "transition", "playingId", PlayingId, "node", sw.NodeId, "segment", rule.Segment.Segment, "sync", "segmentIgnored", "at", sync);
            }

            // source side: drop everything after the source instance, cut or fade the source
            int keepUntil = srcIndex >= 0 ? srcIndex : segs.Count - 1;
            for (int i = segs.Count - 1; i > keepUntil; i--)
            {
                foreach (var r in segs[i].Voices)
                    Cancel(r);
                segs.RemoveAt(i);
            }
            var srcRule = rule != null ? rule.SrcRule : null;
            int fadeMs = srcRule != null ? srcRule.FadeMs : 0;
            double fadeAt = sync + (srcRule != null ? srcRule.FadeOffsetMs / 1000.0 : 0);
            if (fadeAt < now)
                fadeAt = now;
            foreach (var s in segs)
            {
                if (s.Stinger)
                    continue;
                bool atExit = src != null && s == src && Math.Abs(sync - s.ExitAbs) < 0.002;
                bool keepPost = atExit && srcRule != null && srcRule.PlayPostExit;
                s.NextDecided = true;
                if (s.ExitAbs < now - 1e-6 && s != src)
                    continue;
                if (fadeMs > 0)
                {
                    if (!keepPost)
                        foreach (var r in s.Voices.ToArray())
                            if (r.Spec.StartAt >= sync - 1e-6)
                                Cancel(r);
                    ApplyFadeOut(s, fadeAt, fadeMs, srcRule.Curve);
                    s.CutAfter = Math.Min(s.CutAfter, fadeAt + fadeMs / 1000.0);
                }
                else if (!keepPost)
                {
                    s.CutAfter = Math.Min(s.CutAfter, sync);
                    s.PlayPostExit = false;
                    foreach (var r in s.Voices.ToArray())
                    {
                        if (r.Spec.StartAt >= sync - 1e-6)
                            Cancel(r);
                        else if (r.End > sync)
                            StopVoiceAt(r, sync, 0, PaCurve.Linear);
                    }
                }
                else
                    s.PlayPostExit = true;
            }

            double srcOffset = src != null ? sync - src.EntryAbs : 0;
            Trace("op", "transition", "playingId", PlayingId, "node", sw.NodeId, "segment", dstRef != null ? dstRef.Info.Id : 0u,
                "sync", srcRule != null && srcRule.Sync != null ? srcRule.Sync : "exitMarker", "at", sync);
            if (dstRef == null)
                return;   // destination nothing: the source fades to silence

            var dst = NewSeg(dstRef);
            var dstRule = rule != null ? rule.DstRule : null;
            dst.T0 = PaMusicTransitions.DestinationT0(dstRule, dst.Info, sync, srcOffset, World != null ? World.Random : null);
            string entry = dstRule != null && dstRule.Entry != null ? dstRule.Entry : "entryMarker";
            bool fromEntry = entry == "entryMarker" || entry == "lastExitTime";
            dst.PlayPreEntry = fromEntry && dstRule != null && dstRule.PlayPreEntry;
            dst.CutBefore = dst.PlayPreEntry ? now : sync;
            dst.Placed = true;
            if (dstRule != null && dstRule.FadeMs > 0)
            {
                dst.FadeInAt = sync + dstRule.FadeOffsetMs / 1000.0;
                dst.FadeInMs = dstRule.FadeMs;
                dst.FadeInCurve = dstRule.Curve;
            }
            segs.Add(dst);
            Prefetch(dst);
        }

        static IEnumerable<uint> Below(List<uint> path, uint node)
        {
            int i = path.IndexOf(node);
            for (int k = i + 1; k < path.Count; k++)
                yield return path[k];
        }

        // ---- stingers ----------------------------------------------------------------------------------

        internal int PostTrigger(uint triggerId)
        {
            if (ended || stopping || paused)
                return 0;
            double now = Now, t = now + player.LeadSec;
            SegInst cur = null;
            foreach (var s in segs)
                if (s.Placed && !s.Stinger && s.ExitAbs >= t)
                {
                    cur = s;
                    break;
                }
            if (cur == null && segs.Count > 0 && segs[segs.Count - 1].Placed)
                cur = segs[segs.Count - 1];
            if (cur == null)
                return 0;
            var st = PaStingers.Find(Banks, cur.Info.Id, triggerId);
            if (st == null)
            {
                foreach (var id in cur.Path)
                {
                    st = PaStingers.Find(Banks, id, triggerId);
                    if (st != null)
                        break;
                }
            }
            if (st == null || st.Segment == 0)
                return 0;
            var info = PaSegmentInfo.From(Banks != null ? Banks.Node(st.Segment) : null, Banks);
            if (info == null)
                return 0;
            if (!stingers.TryFire(st, now))
            {
                Trace("op", "stinger", "playingId", PlayingId, "node", RootNode, "segment", st.Segment, "sync", "dontRepeat", "at", now);
                return 0;
            }
            double at = double.NaN;
            foreach (var s in segs)
            {
                if (!s.Placed || s.Stinger || s.ExitAbs < t - 1e-6)
                    continue;
                at = PaMusicTransitions.SyncIn(st.Sync, st.CueFilter, s.Info, s.T0, t);
                if (!double.IsNaN(at))
                    break;
            }
            if (double.IsNaN(at))
                at = t;
            var inst = new SegInst { Info = info, Path = new List<uint> { st.Segment }, Stinger = true, Placed = true, NextDecided = true };
            played.Add(st.Segment);
            inst.T0 = at - info.EntryMs / 1000.0;
            inst.CutBefore = now;
            stingerSegs.Add(inst);
            Prefetch(inst);
            Realize(inst);
            return 1;
        }
    }
}
