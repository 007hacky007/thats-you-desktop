// Interactive music player (IPaMusic): plays MusicSwitch, MusicRanSeq, MusicSegment and
// MusicTrack nodes as segment chains on the voice clock, switches between them on state and
// switch changes using the transition rules, and fires stingers on triggers.
//
// Pure C#: data and game syncs come from PaRuntime.World, voices from PaRuntime.Voices.
// Main thread only (called from the engine tick).

using System.Collections.Generic;

namespace PortAudio
{
    public sealed class PaMusicPlayer : IPaMusic
    {
        // Earliest a new segment, transition or stinger may sound, from now.
        public double LeadSec = 0.3;
        // Voices are created this far ahead of their start.
        public double HorizonSec = 1.0;
        // The first segment of a Play waits at most this long for its clips to be loaded.
        public double ReadyTimeoutSec = 2.0;

        readonly List<PaMusicContext> contexts = new List<PaMusicContext>();

        public PaMusicPlayer()
        {
        }

        static PaBankStore Banks { get { return PaRuntime.World != null ? PaRuntime.World.Banks : null; } }

        public bool IsMusicNode(uint nodeId)
        {
            var b = Banks;
            var n = b != null ? b.Node(nodeId) : null;
            if (n == null)
                return false;
            switch (n.Type)
            {
                case PaNodeType.MusicSegment:
                case PaNodeType.MusicTrack:
                case PaNodeType.MusicRanSeq:
                case PaNodeType.MusicSwitch:
                    return true;
                default:
                    return false;
            }
        }

        public IPaMusicContext Play(uint nodeId, ulong gameObject, uint playingId, int fadeInMs, PaCurve curve, IPaMusicListener listener)
        {
            if (!IsMusicNode(nodeId))
                return null;
            var c = new PaMusicContext(this, nodeId, gameObject, playingId, listener);
            if (!c.Start(fadeInMs, curve))
            {
                if (PaLog.Tracing)
                    PaLog.Trace("music", "op", "play", "playingId", playingId, "node", nodeId, "segment", 0u, "sync", "nothing", "at",
                        PaRuntime.Voices != null ? PaRuntime.Voices.Now : 0.0);
                return null;
            }
            contexts.Add(c);
            return c;
        }

        public void Stop(IPaMusicContext c, int fadeMs, PaCurve curve)
        {
            var mc = c as PaMusicContext;
            if (mc != null)
                mc.Stop(fadeMs, curve);
        }

        public void Pause(IPaMusicContext c, int fadeMs, PaCurve curve)
        {
            var mc = c as PaMusicContext;
            if (mc != null)
                mc.Pause(fadeMs, curve);
        }

        public void Resume(IPaMusicContext c, int fadeMs, PaCurve curve)
        {
            var mc = c as PaMusicContext;
            if (mc != null)
                mc.Resume(fadeMs, curve);
        }

        public void OnStateChanged(uint stateGroup, uint state)
        {
            foreach (var c in contexts.ToArray())
                if (!c.Ended && c.UsesSync(stateGroup, true))
                    c.OnSyncChanged(stateGroup, true);
        }

        public void OnSwitchChanged(uint switchGroup, uint value, ulong gameObject)
        {
            bool global = PaIds.IsGlobal(gameObject);
            foreach (var c in contexts.ToArray())
                if (!c.Ended && (global || c.GameObject == gameObject) && c.UsesSync(switchGroup, false))
                    c.OnSyncChanged(switchGroup, false);
        }

        public int PostTrigger(uint triggerId, ulong gameObject)
        {
            bool global = PaIds.IsGlobal(gameObject);
            int n = 0;
            foreach (var c in contexts.ToArray())
                if (!c.Ended && (global || c.GameObject == gameObject))
                    n += c.PostTrigger(triggerId);
            return n;
        }

        public void Tick()
        {
            if (contexts.Count == 0)
                return;
            List<PaMusicContext> done = null;
            foreach (var c in contexts.ToArray())
            {
                c.Tick();
                if (c.Ended)
                {
                    if (done == null)
                        done = new List<PaMusicContext>();
                    done.Add(c);
                }
            }
            if (done == null)
                return;
            foreach (var c in done)
            {
                contexts.Remove(c);
                if (c.EndReported)
                    continue;
                c.EndReported = true;
                if (c.Listener != null)
                    c.Listener.OnMusicEnded(c);
            }
        }

        public void StopAll()
        {
            foreach (var c in contexts.ToArray())
                c.Stop(0, PaCurve.Linear);
        }

        public IEnumerable<IPaMusicContext> Active
        {
            get
            {
                foreach (var c in contexts)
                    if (!c.Ended)
                        yield return c;
            }
        }
    }
}
