// Container selection and voice specs.
//
// A Play action names a node. Music nodes go to the music player; everything else walks the
// actor-mixer hierarchy here:
//   Sound    -> one voice (media lookup, randomizers summed over the parent chain, loop,
//               initial delay, instance limit)
//   RanSeq   -> step: one child chosen by the random or sequence rules;
//               continuous: a sequencer that plays the playlist over time (PaSequencer)
//   Switch   -> the children assigned to the current switch or state value; continuous switch
//               containers follow later changes (PaSwitchPlay)
//   Layer    -> all children
// ActorMixer nodes are only property and stop-target parents and never play.

using System;
using System.Collections.Generic;

namespace PortAudio
{
    // Selection state of a random or sequence container, per (container, object) or per
    // container when the container is global.
    internal sealed class PaSelector
    {
        public int Index = -1;
        public int Direction = 1;
        public readonly List<int> History = new List<int>();
        public readonly List<int> Pool = new List<int>();

        public int Next(PaRanSeq rs, PaRandom random)
        {
            int n = rs.Playlist != null ? rs.Playlist.Length : 0;
            if (n == 0)
                return -1;
            if (rs.Sequence)
                return NextSequence(rs, n);
            return NextRandom(rs, random, n);
        }

        // Sequence: next index; at the end restart at the first item, or with RestartBackward
        // go back down without repeating the end items (A B C B A B C ...).
        int NextSequence(PaRanSeq rs, int n)
        {
            if (n == 1)
            {
                Index = 0;
                return 0;
            }
            if (Index < 0)
            {
                Index = 0;
                Direction = 1;
                return 0;
            }
            int next = Index + Direction;
            if (next >= n)
            {
                if (rs.RestartBackward)
                {
                    Direction = -1;
                    next = n - 2;
                }
                else
                {
                    next = 0;
                }
            }
            else if (next < 0)
            {
                Direction = 1;
                next = 1;
            }
            Index = next;
            return next;
        }

        // Random: weighted (or uniform) choice that avoids the last AvoidRepeat picks; Shuffle
        // draws without replacement until every item was played.
        int NextRandom(PaRanSeq rs, PaRandom random, int n)
        {
            int avoid = Math.Min(Math.Max(0, rs.AvoidRepeat), n - 1);
            List<int> candidates = new List<int>(n);
            if (rs.Shuffle)
            {
                if (Pool.Count == 0)
                    for (int i = 0; i < n; i++)
                        Pool.Add(i);
                foreach (int i in Pool)
                    if (!Recent(i, avoid))
                        candidates.Add(i);
                if (candidates.Count == 0)
                    candidates.AddRange(Pool);
            }
            else
            {
                for (int i = 0; i < n; i++)
                    if (!Recent(i, avoid))
                        candidates.Add(i);
                if (candidates.Count == 0)
                    for (int i = 0; i < n; i++)
                        candidates.Add(i);
            }
            int pick = Weighted(rs, random, candidates);
            if (rs.Shuffle)
                Pool.Remove(pick);
            History.Add(pick);
            if (History.Count > 64)
                History.RemoveAt(0);
            Index = pick;
            return pick;
        }

        bool Recent(int index, int avoid)
        {
            for (int k = 0; k < avoid && k < History.Count; k++)
                if (History[History.Count - 1 - k] == index)
                    return true;
            return false;
        }

        static int Weighted(PaRanSeq rs, PaRandom random, List<int> candidates)
        {
            double total = 0;
            if (rs.UseWeights)
                foreach (int i in candidates)
                    total += Math.Max(0, rs.Playlist[i].Weight);
            if (!rs.UseWeights || total <= 0)
                return candidates[random.Range(0, candidates.Count)];
            double r = random.NextDouble() * total;
            foreach (int i in candidates)
            {
                r -= Math.Max(0, rs.Playlist[i].Weight);
                if (r < 0)
                    return i;
            }
            return candidates[candidates.Count - 1];
        }
    }

    // One playlist item of a continuous container: the voices it started.
    internal sealed class PaSeqItem
    {
        public double StartAt;
        public readonly List<PaVoiceRef> Voices = new List<PaVoiceRef>();

        public bool Ended
        {
            get
            {
                foreach (PaVoiceRef v in Voices)
                    if (!v.Ended)
                        return false;
                return true;
            }
        }

        // Predicted end on the voice clock: NaN when unknown, +Inf when infinite.
        public double EndTime
        {
            get
            {
                double end = StartAt;
                foreach (PaVoiceRef v in Voices)
                {
                    double e = v.Ended ? v.EndedAt : (v.Voice != null ? v.Voice.EndTime : double.NaN);
                    if (double.IsNaN(e))
                        return double.NaN;
                    if (e > end)
                        end = e;
                }
                return end;
            }
        }
    }

    // A continuous random/sequence container playing its playlist over time for one instance.
    internal sealed class PaSequencer
    {
        public const double LookAheadSec = 0.5;

        public PaInstance Instance;
        public PaNode Container;
        public PaRanSeq RanSeq;
        public PaSelector Selector;
        public PaPlayContext Context;
        public int LoopsLeft;           // 0 = infinite
        public int ItemsLeft;           // in the current loop
        public bool Paused;
        public bool Done;
        public PaSeqItem Current;
        public double NextStart = double.NaN;

        public uint ContainerId
        {
            get { return Container.Id; }
        }

        public bool AllEnded
        {
            get { return Current == null || Current.Ended; }
        }

        public void Stop()
        {
            Done = true;
        }
    }

    // A continuous switch container that re-checks its switch while its instance lives.
    internal sealed class PaSwitchPlay
    {
        public PaInstance Instance;
        public PaNode Node;
        public uint Value;
        public bool Stopped;
        public PaPlayContext Context;
        public readonly Dictionary<uint, List<PaVoiceRef>> Children = new Dictionary<uint, List<PaVoiceRef>>();
    }

    internal sealed partial class PaCore
    {
        readonly Dictionary<ulong, PaSelector> selectors = new Dictionary<ulong, PaSelector>();
        readonly List<PaVoiceRef> liveVoices = new List<PaVoiceRef>();

        // Play action target: music to the music player, everything else to the containers.
        void PlayTarget(PaInstance inst, uint nodeId, int fadeMs, PaCurve curve)
        {
            if (music != null && music.IsMusicNode(nodeId))
            {
                PaMusicRef m = new PaMusicRef(this, inst, nodeId);
                m.Context = music.Play(nodeId, inst.GameObject, inst.PlayingId, fadeMs, curve, m);
                if (m.Context != null)
                {
                    inst.Music.Add(m);
                    stats.MusicPlays++;
                }
                TracePlay(inst, nodeId, m.Context != null ? "music" : "nothing", m.Context != null ? null : "musicNothing");
                return;
            }
            PaPlayContext ctx = new PaPlayContext { FadeMs = fadeMs, Curve = curve };
            int before = inst.Voices.Count;
            int sequencers = inst.Sequencers.Count;
            string reason = PlayNode(inst, nodeId, ctx);
            bool any = inst.Voices.Count > before || inst.Sequencers.Count > sequencers;
            TracePlay(inst, nodeId, any ? "voices" : (reason == "maxInstances" || reason == "probability" ? "skipped" : "nothing"), any ? null : reason);
        }

        void TracePlay(PaInstance inst, uint nodeId, string result, string reason)
        {
            if (PaLog.Tracing)
                PaLog.Trace("play", "playingId", inst.PlayingId, "node", nodeId, "nname", banks.NameOf(nodeId), "result", result, "reason", reason);
        }

        // Plays a node of the actor-mixer hierarchy. Returns null, or why nothing played.
        string PlayNode(PaInstance inst, uint nodeId, PaPlayContext ctx)
        {
            PaNode node = banks.Node(nodeId);
            if (node == null)
                return "missingNode";
            if (ctx.Depth > 32)
                return "depth";
            if (node.Props != null && node.Props.Probability < 100f && random.NextDouble() * 100.0 >= node.Props.Probability)
                return "probability";
            switch (node.Type)
            {
                case PaNodeType.Sound:
                    return PlaySound(inst, node, ctx);
                case PaNodeType.RanSeq:
                    return PlayRanSeq(inst, node, ctx);
                case PaNodeType.Switch:
                    return PlaySwitch(inst, node, ctx);
                case PaNodeType.Layer:
                    {
                        string reason = "empty";
                        if (node.Children != null)
                            foreach (uint child in node.Children)
                                if (PlayNode(inst, child, ctx.Child()) == null)
                                    reason = null;
                        return reason;
                    }
                case PaNodeType.ActorMixer:
                    return "actorMixer";
                default:
                    if (music != null && music.IsMusicNode(nodeId))
                    {
                        PlayTarget(inst, nodeId, ctx.FadeMs, ctx.Curve);
                        return null;
                    }
                    return "music";
            }
        }

        // ---- Sound ----

        string PlaySound(PaInstance inst, PaNode node, PaPlayContext ctx)
        {
            if (!MakeRoom(inst, node))
                return "maxInstances";
            double now = voices.Now;
            PaVoiceSpec spec = new PaVoiceSpec();
            spec.PlayingId = inst.PlayingId;
            spec.GameObject = inst.GameObject;
            spec.NodeId = node.Id;
            ResolveMedia(inst, node, spec);

            double delay = 0;
            foreach (uint id in banks.Chain(node.Id))
            {
                PaNode n = banks.Node(id);
                if (n == null || n.Props == null)
                    continue;
                spec.VolumeDb += Roll(n.Props.VolumeRand);
                spec.PitchCents += Roll(n.Props.PitchRand);
                spec.Lpf += Roll(n.Props.LpfRand);
                delay += n.Props.InitialDelay + Roll(n.Props.InitialDelayRand);
            }
            int loop = node.Props != null ? node.Props.Loop : -1;
            spec.LoopCount = loop < 0 ? 1 : loop;
            double baseTime = ctx.StartAt > 0 ? ctx.StartAt : now;
            spec.StartAt = delay > 0 ? baseTime + delay : (ctx.StartAt > 0 ? ctx.StartAt : now);
            spec.FadeInMs = Math.Max(0, ctx.FadeMs);
            spec.FadeInCurve = ctx.Curve;

            PaVoiceRef v = new PaVoiceRef(this, inst, node.Id, ++voiceCounter);
            spec.Tag = v;
            v.Item = ctx.Item;
            v.Switches = ctx.Switches;
            IPaVoice voice = voices.Play(spec, v);
            if (voice == null)
                return "voiceRefused";
            v.Voice = voice;
            inst.Voices.Add(v);
            liveVoices.Add(v);
            stats.VoicesStarted++;
            if (ctx.Item != null)
                ctx.Item.Voices.Add(v);
            if (ctx.Switches != null)
            {
                foreach (KeyValuePair<PaSwitchPlay, uint> sw in ctx.Switches)
                {
                    List<PaVoiceRef> list;
                    if (!sw.Key.Children.TryGetValue(sw.Value, out list))
                        sw.Key.Children[sw.Value] = list = new List<PaVoiceRef>();
                    list.Add(v);
                }
            }
            return null;
        }

        void ResolveMedia(PaInstance inst, PaNode node, PaVoiceSpec spec)
        {
            PaSource src = node.Source;
            if (src == null)
                return;
            string kind = src.Kind ?? "";
            if (string.Equals(kind, "external", StringComparison.OrdinalIgnoreCase))
            {
                PaExternal? ext = inst.External(src.Media);
                if (ext == null || string.IsNullOrEmpty(ext.Value.File))
                {
                    WarnOnce("ext-missing:" + inst.EventId, "event " + inst.EventId + " plays an external source without a matching external (cookie " + src.Media + ")");
                    return;
                }
                string key = ExternalLanguageKey(ext.Value.File);
                spec.Media = media.FindExternal(ext.Value.File, key);
                if (spec.Media == null && PaLog.Tracing)
                    PaLog.Trace("warn", "msg", "external file not found: " + ext.Value.File + " (" + key + ")");
                return;
            }
            if (string.Equals(kind, "audioInput", StringComparison.OrdinalIgnoreCase))
            {
                float[] pcm = audioInput.Pcm;
                if (pcm != null)
                {
                    spec.Pcm = pcm;
                    spec.PcmRate = PaAudioInput.Rate;
                    spec.PcmChannels = 1;
                }
                return;
            }
            spec.Media = media.Find(src.Media, src.LanguageSpecific, world.VoiceLanguageKey);
        }

        float Roll(PaRange r)
        {
            if (r.Min == 0f && r.Max == 0f)
                return 0f;
            return random.Range(Math.Min(r.Min, r.Max), Math.Max(r.Min, r.Max));
        }

        // Advanced.MaxInstances on the node or a parent: either refuse the new voice
        // (KillNewest) or stop the oldest ones under that node.
        bool MakeRoom(PaInstance inst, PaNode node)
        {
            foreach (uint id in banks.Chain(node.Id))
            {
                PaNode n = banks.Node(id);
                if (n == null || n.Advanced == null || n.Advanced.MaxInstances <= 0)
                    continue;
                PaAdvanced adv = n.Advanced;
                List<PaVoiceRef> under = new List<PaVoiceRef>();
                foreach (PaVoiceRef v in liveVoices)
                    if (!v.Ended && !v.Stopping && (adv.GlobalLimit || v.GameObject == inst.GameObject) && banks.IsUnder(v.NodeId, id))
                        under.Add(v);
                if (under.Count < adv.MaxInstances)
                    continue;
                if (adv.KillNewest)
                    return false;
                under.Sort((a, b) => a.Order.CompareTo(b.Order));
                for (int i = 0; i <= under.Count - adv.MaxInstances; i++)
                    under[i].Stop(0, PaCurve.Linear);
            }
            return true;
        }

        // ---- RanSeq ----

        PaSelector SelectorFor(PaNode node, ulong go)
        {
            bool global = node.RanSeq != null && node.RanSeq.Global;
            ulong key = ((ulong)node.Id << 32) ^ (global ? 0UL : go * 0x9E3779B97F4A7C15UL);
            PaSelector s;
            if (!selectors.TryGetValue(key, out s))
                selectors[key] = s = new PaSelector();
            return s;
        }

        string PlayRanSeq(PaInstance inst, PaNode node, PaPlayContext ctx)
        {
            PaRanSeq rs = node.RanSeq;
            if (rs == null || rs.Playlist == null || rs.Playlist.Length == 0)
                return "empty";
            if (rs.Continuous)
            {
                PaSequencer seq = new PaSequencer();
                seq.Instance = inst;
                seq.Container = node;
                seq.RanSeq = rs;
                seq.Selector = rs.ResetEachPlay ? new PaSelector() : SelectorFor(node, inst.GameObject);
                seq.Context = ctx.Child();
                int loops = rs.LoopCount;
                if (loops > 0 && (rs.LoopMin != 0 || rs.LoopMax != 0))
                    loops = Math.Max(1, loops + random.Range(Math.Min(rs.LoopMin, rs.LoopMax), Math.Max(rs.LoopMin, rs.LoopMax) + 1));
                seq.LoopsLeft = loops <= 0 ? 0 : loops;
                seq.ItemsLeft = rs.Playlist.Length;
                inst.Sequencers.Add(seq);
                StartNextItem(seq, ctx.StartAt > 0 ? ctx.StartAt : voices.Now, false);
                return null;
            }
            int index = SelectorFor(node, inst.GameObject).Next(rs, random);
            if (index < 0)
                return "empty";
            return PlayNode(inst, rs.Playlist[index].Id, ctx.Child());
        }

        // Starts the next playlist item at `at`. Returns false when the sequencer finished.
        bool StartNextItem(PaSequencer seq, double at, bool crossfade)
        {
            int n = seq.RanSeq.Playlist.Length;
            for (int attempt = 0; attempt <= n; attempt++)
            {
                if (seq.ItemsLeft <= 0)
                {
                    if (seq.LoopsLeft == 1)
                    {
                        seq.Done = true;
                        return false;
                    }
                    if (seq.LoopsLeft > 1)
                        seq.LoopsLeft--;
                    seq.ItemsLeft = n;
                }
                seq.ItemsLeft--;
                int index = seq.Selector.Next(seq.RanSeq, random);
                PaSeqItem item = new PaSeqItem { StartAt = at };
                PaPlayContext ctx = seq.Context.Child();
                ctx.StartAt = at;
                ctx.Item = item;
                if (crossfade)
                {
                    ctx.FadeMs = (int)Math.Round(seq.RanSeq.TransitionSec * 1000.0);
                    ctx.Curve = PaCurve.Linear;
                }
                if (index >= 0)
                    PlayNode(seq.Instance, seq.RanSeq.Playlist[index].Id, ctx);
                if (item.Voices.Count > 0)
                {
                    seq.Current = item;
                    seq.NextStart = double.NaN;
                    return true;
                }
            }
            // A whole loop played nothing: stop rather than spin.
            seq.Done = true;
            return false;
        }

        // Step 3 of Tick (after delayed actions): continuous containers schedule their next item.
        void AdvanceSequencers()
        {
            double now = voices.Now;
            foreach (PaInstance inst in instanceOrder.ToArray())
            {
                for (int i = 0; i < inst.Sequencers.Count; i++)
                {
                    PaSequencer seq = inst.Sequencers[i];
                    if (seq.Done || seq.Paused || seq.Current == null)
                        continue;
                    for (int guard = 0; guard < 8 && !seq.Done; guard++)
                        if (!AdvanceSequencer(seq, now))
                            break;
                }
            }
        }

        // Returns true when it started an item (so the caller may check again).
        bool AdvanceSequencer(PaSequencer seq, double now)
        {
            PaRanSeq rs = seq.RanSeq;
            PaSeqItem cur = seq.Current;
            string mode = rs.Transition ?? "none";
            double end = cur.EndTime;
            if (mode == "delay")
            {
                if (!cur.Ended)
                    return false;
                double gap = rs.TransitionSec;
                if (rs.TransitionMinSec != 0f || rs.TransitionMaxSec != 0f)
                    gap += random.Range(Math.Min(rs.TransitionMinSec, rs.TransitionMaxSec), Math.Max(rs.TransitionMinSec, rs.TransitionMaxSec));
                double at = (double.IsNaN(end) ? now : end) + Math.Max(0.0, gap);
                return StartNextItem(seq, at, false);
            }
            if (mode == "triggerRate")
            {
                double at = cur.StartAt + Math.Max(0.001, rs.TransitionSec);
                if (now < at - LookAhead)
                    return false;
                return StartNextItem(seq, Math.Max(at, now), false);
            }
            if (mode == "crossfadeAmp" || mode == "crossfadePower")
            {
                if (cur.Ended)
                    return StartNextItem(seq, now, false);
                if (double.IsNaN(end) || double.IsInfinity(end))
                    return false;
                double at = Math.Max(cur.StartAt, end - Math.Max(0.0, rs.TransitionSec));
                if (now < at - LookAhead)
                    return false;
                int fadeMs = (int)Math.Round(Math.Max(0.0, end - at) * 1000.0);
                foreach (PaVoiceRef v in cur.Voices)
                    if (!v.Ended && v.Voice != null)
                        v.Voice.StopAt(at, fadeMs, rs.Transition == "crossfadePower" ? PaCurve.Sine : PaCurve.Linear);
                return StartNextItem(seq, Math.Max(at, now), true);
            }
            // none, sampleAccurate: gapless, the next item starts where the current one ends.
            if (cur.Ended)
                return StartNextItem(seq, double.IsNaN(end) ? now : end, false);
            if (double.IsNaN(end) || double.IsInfinity(end) || now < end - LookAhead)
                return false;
            return StartNextItem(seq, end, false);
        }

        double LookAhead
        {
            get { return PaSequencer.LookAheadSec; }
        }

        // ---- Switch ----

        uint SwitchValue(PaSwitch sw, ulong go)
        {
            uint value = sw.IsState ? world.GetState(sw.Group) : world.GetSwitch(sw.Group, go);
            return value != 0 ? value : sw.Default;
        }

        static uint[] Assigned(PaSwitch sw, uint value)
        {
            if (sw.Assignments != null)
                foreach (PaSwitchAssign a in sw.Assignments)
                    if (a.Value == value)
                        return a.Nodes ?? new uint[0];
            return new uint[0];
        }

        static PaSwitchChild ChildParams(PaSwitch sw, uint node)
        {
            if (sw.ChildParams != null)
                foreach (PaSwitchChild c in sw.ChildParams)
                    if (c.Node == node)
                        return c;
            return null;
        }

        string PlaySwitch(PaInstance inst, PaNode node, PaPlayContext ctx)
        {
            PaSwitch sw = node.Switch;
            if (sw == null)
                return "empty";
            uint value = SwitchValue(sw, inst.GameObject);
            uint[] children = Assigned(sw, value);
            PaPlayContext childCtx = ctx.Child();
            PaSwitchPlay rec = null;
            if (sw.Continuous)
            {
                rec = new PaSwitchPlay { Instance = inst, Node = node, Value = value, Context = ctx.Child() };
                inst.Switches.Add(rec);
            }
            string reason = children.Length == 0 ? "switchEmpty" : "nothing";
            foreach (uint child in children)
            {
                PaPlayContext c = childCtx.Child();
                if (rec != null)
                    c.Switches = WithSwitch(ctx.Switches, rec, child);
                if (PlayNode(inst, child, c) == null)
                    reason = null;
            }
            return reason;
        }

        static List<KeyValuePair<PaSwitchPlay, uint>> WithSwitch(List<KeyValuePair<PaSwitchPlay, uint>> outer, PaSwitchPlay rec, uint child)
        {
            List<KeyValuePair<PaSwitchPlay, uint>> list = outer != null ? new List<KeyValuePair<PaSwitchPlay, uint>>(outer) : new List<KeyValuePair<PaSwitchPlay, uint>>();
            list.Add(new KeyValuePair<PaSwitchPlay, uint>(rec, child));
            return list;
        }

        // Continuous validation after a switch (or state) change: children that are no longer
        // selected stop with their fade-out unless they play to the end; newly selected children
        // start with their fade-in; children selected before and after keep playing when they
        // have ContinuePlayback, otherwise they restart.
        void RecheckSwitches(uint group, bool isState, ulong go)
        {
            foreach (PaInstance inst in instanceOrder.ToArray())
            {
                if (inst.Completed)
                    continue;
                for (int i = 0; i < inst.Switches.Count; i++)
                {
                    PaSwitchPlay rec = inst.Switches[i];
                    PaSwitch sw = rec.Node.Switch;
                    if (rec.Stopped || sw.Group != group || sw.IsState != isState)
                        continue;
                    if (!isState && !PaIds.IsGlobal(go) && inst.GameObject != go)
                        continue;
                    uint value = SwitchValue(sw, inst.GameObject);
                    if (value == rec.Value)
                        continue;
                    uint[] before = Assigned(sw, rec.Value);
                    uint[] after = Assigned(sw, value);
                    rec.Value = value;
                    foreach (uint child in before)
                    {
                        PaSwitchChild p = ChildParams(sw, child);
                        bool still = Array.IndexOf(after, child) >= 0;
                        if (still && p != null && p.ContinuePlayback)
                            continue;
                        if (p == null || p.OnSwitchMode != 1)
                            continue;   // play to end
                        List<PaVoiceRef> list;
                        if (rec.Children.TryGetValue(child, out list))
                        {
                            foreach (PaVoiceRef v in list)
                                v.Stop(p.FadeOutMs, PaCurve.Linear);
                            list.Clear();
                        }
                    }
                    foreach (uint child in after)
                    {
                        PaSwitchChild p = ChildParams(sw, child);
                        bool was = Array.IndexOf(before, child) >= 0;
                        if (was && p != null && p.ContinuePlayback)
                            continue;
                        PaPlayContext c = rec.Context.Child();
                        c.StartAt = 0;
                        c.FadeMs = p != null ? p.FadeInMs : 0;
                        c.Curve = PaCurve.Linear;
                        c.Switches = WithSwitch(rec.Context.Switches, rec, child);
                        PlayNode(inst, child, c);
                    }
                }
            }
        }

        // Voices whose node disappeared with an unloaded bank stop with a short fade.
        void StopOrphans()
        {
            foreach (PaVoiceRef v in liveVoices.ToArray())
                if (!v.Ended && !v.Stopping && banks.Node(v.NodeId) == null)
                    v.Stop(50, PaCurve.Linear);
            if (music != null)
                foreach (PaInstance inst in instanceOrder)
                    foreach (PaMusicRef m in inst.Music)
                        if (!m.Ended && banks.Node(m.NodeId) == null)
                            music.Stop(m.Context, 50, PaCurve.Linear);
        }
    }
}
