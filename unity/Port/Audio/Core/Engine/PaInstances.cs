// Playing instances: one per successful PostEvent.
//
// An instance owns everything its event started: voices, music contexts, delayed actions,
// continuous containers and switch containers that re-check their switch. It completes when
// none of them is alive any more (an instance that played nothing completes on the next tick),
// and then, if the post asked for it, AK_EndOfEvent is queued for the game exactly once.

using System;
using System.Collections.Generic;

namespace PortAudio
{
    internal struct PaExternal
    {
        public uint Cookie;
        public string File;
        public uint Codec;
    }

    internal sealed class PaInstance
    {
        public uint PlayingId, EventId;
        public ulong GameObject;
        public uint Flags;
        public IntPtr Cookie;
        public bool WantsCallback;          // cookie != 0 && callback != 0 && (flags & EndOfEvent)
        public PaExternal[] Externals = new PaExternal[0];
        public bool Started, Completed;
        public long Order;                  // post order, for deterministic completion order
        public readonly List<PaVoiceRef> Voices = new List<PaVoiceRef>();
        public readonly List<PaMusicRef> Music = new List<PaMusicRef>();
        public readonly List<PaSequencer> Sequencers = new List<PaSequencer>();
        public readonly List<PaSwitchPlay> Switches = new List<PaSwitchPlay>();
        public int PendingActions;          // delayed actions not yet run or cancelled

        public bool Alive
        {
            get
            {
                if (!Started || PendingActions > 0)
                    return true;
                for (int i = 0; i < Voices.Count; i++)
                    if (!Voices[i].Ended)
                        return true;
                for (int i = 0; i < Music.Count; i++)
                    if (!Music[i].Ended)
                        return true;
                for (int i = 0; i < Sequencers.Count; i++)
                    if (!Sequencers[i].Done)
                        return true;
                return false;
            }
        }

        public PaExternal? External(uint cookie)
        {
            for (int i = 0; i < Externals.Length; i++)
                if (Externals[i].Cookie == cookie)
                    return Externals[i];
            return null;
        }
    }

    // One voice an instance started. It is the voice's listener; the mixer reports the end here.
    internal sealed class PaVoiceRef : IPaVoiceListener
    {
        public readonly PaCore Core;
        public readonly PaInstance Instance;
        public readonly uint NodeId;
        public readonly long Order;
        public IPaVoice Voice;
        public bool Ended, Stopping;
        public double EndedAt = double.NaN;
        public PaSeqItem Item;                               // continuous container item, if any
        public List<KeyValuePair<PaSwitchPlay, uint>> Switches; // switch containers (and their child) above this voice

        public PaVoiceRef(PaCore core, PaInstance instance, uint nodeId, long order)
        {
            Core = core;
            Instance = instance;
            NodeId = nodeId;
            Order = order;
        }

        public ulong GameObject
        {
            get { return Instance.GameObject; }
        }

        public void OnVoiceEnded(IPaVoice voice, PaVoiceEnd reason)
        {
            Core.OnVoiceEnded(this, reason);
        }

        public void Stop(int fadeMs, PaCurve curve)
        {
            if (Ended || Voice == null)
                return;
            Stopping = true;
            Voice.Stop(Math.Max(0, fadeMs), curve);
        }
    }

    internal sealed class PaMusicRef : IPaMusicListener
    {
        public readonly PaCore Core;
        public readonly PaInstance Instance;
        public readonly uint NodeId;
        public IPaMusicContext Context;
        public bool EndedFlag;

        public PaMusicRef(PaCore core, PaInstance instance, uint nodeId)
        {
            Core = core;
            Instance = instance;
            NodeId = nodeId;
        }

        public bool Ended
        {
            get { return EndedFlag || Context == null || Context.Ended; }
        }

        public void OnMusicEnded(IPaMusicContext context)
        {
            EndedFlag = true;
            Core.OnMusicEnded(this);
        }
    }

    // A delayed action (Play with a delay, a delayed Stop, a PlayEvent with delay...).
    internal sealed class PaPending
    {
        public PaInstance Instance;
        public PaAction Action;
        public int Depth;
        public double Due;
        public bool Paused;
        public double Remaining;
        public bool Cancelled;
    }

    // Per-play options handed down the container tree.
    internal sealed class PaPlayContext
    {
        public int FadeMs;
        public PaCurve Curve = PaCurve.Linear;
        public double StartAt;              // absolute voice clock time; 0 = now
        public PaSeqItem Item;
        public List<KeyValuePair<PaSwitchPlay, uint>> Switches;
        public int Depth;

        public PaPlayContext Child()
        {
            return new PaPlayContext { FadeMs = FadeMs, Curve = Curve, StartAt = StartAt, Item = Item, Switches = Switches, Depth = Depth + 1 };
        }
    }

    internal sealed partial class PaCore
    {
        readonly Dictionary<uint, PaInstance> instances = new Dictionary<uint, PaInstance>();
        readonly List<PaInstance> instanceOrder = new List<PaInstance>();
        readonly List<PaPending> pending = new List<PaPending>();
        long instanceCounter, voiceCounter;
        uint nextPlayingId;

        public int InstanceCount
        {
            get { return instances.Count; }
        }

        uint AllocatePlayingId(uint requested)
        {
            if (requested != 0)
                return requested;
            do
            {
                nextPlayingId++;
            } while (nextPlayingId == 0 || instances.ContainsKey(nextPlayingId));
            return nextPlayingId;
        }

        // Creates the instance under the engine lock (PostEvent). External source fields are
        // copied now: the AkExternalSourceInfo proxy may be finalized as soon as PostEvent returns.
        public PaInstance CreateInstance(uint eventId, ulong go, uint flags, IntPtr callback, IntPtr cookie,
            uint externalCount, IntPtr externals, uint requestedPlayingId)
        {
            PaInstance inst = new PaInstance();
            inst.PlayingId = AllocatePlayingId(requestedPlayingId);
            inst.EventId = eventId;
            inst.GameObject = go;
            inst.Flags = flags;
            inst.Cookie = cookie;
            inst.WantsCallback = cookie != IntPtr.Zero && callback != IntPtr.Zero && (flags & PaCallbackType.EndOfEvent) != 0;
            inst.Order = ++instanceCounter;
            if (externalCount > 0 && externals != IntPtr.Zero)
            {
                if (externalCount > 1)
                    WarnOnce("externals>1", "PostEvent with " + externalCount + " external sources: only the first is read");
                inst.Externals = new PaExternal[]
                {
                    new PaExternal
                    {
                        Cookie = PaHandles.Get<uint>(externals, "AkExternalSourceInfo_iExternalSrcCookie"),
                        File = PaHandles.Get<string>(externals, "AkExternalSourceInfo_szFile"),
                        Codec = PaHandles.Get<uint>(externals, "AkExternalSourceInfo_idCodec"),
                    }
                };
            }
            if ((flags & ~(PaCallbackType.EndOfEvent | PaCallbackType.Bank)) != 0 && callback != IntPtr.Zero)
                WarnOnce("callbackflags", "callback types other than EndOfEvent are not delivered (flags 0x" + flags.ToString("X") + ")");
            PaInstance old;
            if (instances.TryGetValue(inst.PlayingId, out old))
                instanceOrder.Remove(old);
            instances[inst.PlayingId] = inst;
            instanceOrder.Add(inst);
            return inst;
        }

        public PaInstance Instance(uint playingId)
        {
            PaInstance inst;
            return instances.TryGetValue(playingId, out inst) ? inst : null;
        }

        public IEnumerable<PaInstance> Instances
        {
            get { return instanceOrder; }
        }

        public void OnVoiceEnded(PaVoiceRef v, PaVoiceEnd reason)
        {
            if (v.Ended)
                return;
            v.Ended = true;
            double end = v.Voice != null ? v.Voice.EndTime : double.NaN;
            double now = voices.Now;
            v.EndedAt = !double.IsNaN(end) && !double.IsInfinity(end) && end <= now ? end : now;
            liveVoices.Remove(v);
            if (reason == PaVoiceEnd.Finished)
                stats.VoicesFinished++;
            else if (reason == PaVoiceEnd.Stopped)
                stats.VoicesStopped++;
            else
                stats.VoicesFailed++;
        }

        public void OnMusicEnded(PaMusicRef m)
        {
        }

        // Step 6 of Tick: instances with nothing alive complete, in post order.
        void CompleteInstances()
        {
            for (int i = 0; i < instanceOrder.Count; i++)
            {
                PaInstance inst = instanceOrder[i];
                inst.Voices.RemoveAll(v => v.Ended);
                inst.Music.RemoveAll(m => m.Ended);
                inst.Sequencers.RemoveAll(s => s.Done && s.AllEnded);
                if (inst.Alive)
                    continue;
                Complete(inst);
                instanceOrder.RemoveAt(i);
                i--;
            }
        }

        void Complete(PaInstance inst)
        {
            inst.Completed = true;
            PaInstance cur;
            if (instances.TryGetValue(inst.PlayingId, out cur) && cur == inst)
                instances.Remove(inst.PlayingId);
            stats.InstancesCompleted++;
            if (!inst.WantsCallback)
                return;
            PaCallbacks.QueueEndOfEvent(inst.Cookie, inst.GameObject, inst.PlayingId, inst.EventId);
            stats.EndOfEvent++;
            if (PaLog.Tracing)
                PaLog.Trace("callback", "type", "endOfEvent", "playingId", inst.PlayingId, "eventId", inst.EventId);
        }

        public void CancelCookie(IntPtr cookie)
        {
            foreach (PaInstance inst in instanceOrder)
                if (inst.Cookie == cookie)
                    inst.WantsCallback = false;
        }

        public void CancelCallback(uint playingId)
        {
            PaInstance inst = Instance(playingId);
            if (inst != null)
                inst.WantsCallback = false;
        }

        // Stops everything an instance owns: voices (with a fade), music, pending actions,
        // continuous sequencing and switch re-checks.
        public void StopInstance(PaInstance inst, int fadeMs, PaCurve curve)
        {
            foreach (PaPending p in pending)
                if (p.Instance == inst && !p.Cancelled)
                    CancelPending(p);
            foreach (PaSequencer s in inst.Sequencers)
                s.Stop();
            foreach (PaSwitchPlay sw in inst.Switches)
                sw.Stopped = true;
            foreach (PaVoiceRef v in inst.Voices.ToArray())
                v.Stop(fadeMs, curve);
            foreach (PaMusicRef m in inst.Music)
                if (!m.Ended && music != null)
                    music.Stop(m.Context, fadeMs, curve);
        }

        public void PauseInstance(PaInstance inst, int fadeMs, PaCurve curve, bool pause)
        {
            double now = voices.Now;
            foreach (PaPending p in pending)
                if (p.Instance == inst && !p.Cancelled)
                    PausePending(p, pause, now);
            foreach (PaSequencer s in inst.Sequencers)
                s.Paused = pause;
            foreach (PaVoiceRef v in inst.Voices)
            {
                if (v.Ended || v.Voice == null)
                    continue;
                if (pause)
                    v.Voice.Pause(fadeMs, curve);
                else
                    v.Voice.Resume(fadeMs, curve);
            }
            foreach (PaMusicRef m in inst.Music)
            {
                if (m.Ended || music == null)
                    continue;
                if (pause)
                    music.Pause(m.Context, fadeMs, curve);
                else
                    music.Resume(m.Context, fadeMs, curve);
            }
        }

        void CancelPending(PaPending p)
        {
            if (p.Cancelled)
                return;
            p.Cancelled = true;
            p.Instance.PendingActions--;
        }

        static void PausePending(PaPending p, bool pause, double now)
        {
            if (pause && !p.Paused)
            {
                p.Paused = true;
                p.Remaining = Math.Max(0.0, p.Due - now);
            }
            else if (!pause && p.Paused)
            {
                p.Paused = false;
                p.Due = now + p.Remaining;
            }
        }
    }
}
