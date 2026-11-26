// Stingers: short segments fired by PostTrigger over the playing music.
//
// The stinger is looked up on the playing segment, then on its music ancestors; the first object
// that has a stinger for the trigger wins. A stinger does not fire again within DontRepeatMs of
// its last firing in the same context.

using System.Collections.Generic;

namespace PortAudio
{
    public sealed class PaStingers
    {
        readonly Dictionary<uint, double> lastFired = new Dictionary<uint, double>();

        public static PaStinger Find(PaBankStore banks, uint segmentId, uint triggerId)
        {
            if (banks == null || segmentId == 0)
                return null;
            foreach (uint id in banks.Chain(segmentId))
            {
                var n = banks.Node(id);
                if (n == null || n.Music == null || n.Music.Stingers == null)
                    continue;
                if (!PaMusicTransitions.IsMusicContainer(n.Type))
                    continue;
                foreach (var s in n.Music.Stingers)
                    if (s != null && s.Trigger == triggerId)
                        return s;
            }
            return null;
        }

        // True when the stinger may fire at `now`; records the firing.
        public bool TryFire(PaStinger s, double now)
        {
            double last;
            if (s.DontRepeatMs > 0 && lastFired.TryGetValue(s.Trigger, out last) && now - last < s.DontRepeatMs / 1000.0)
                return false;
            lastFired[s.Trigger] = now;
            return true;
        }

        public void Shift(double dt)
        {
            var keys = new List<uint>(lastFired.Keys);
            foreach (var k in keys)
                lastFired[k] += dt;
        }
    }
}
