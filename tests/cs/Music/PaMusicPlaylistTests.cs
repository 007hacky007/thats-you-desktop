using System.Collections.Generic;
using static PortAudio.Tests.MusicData;

namespace PortAudio.Tests
{
    public sealed class PaMusicPlaylistTests
    {
        static List<uint> Take(PaMusicPlaylist p, int n)
        {
            var l = new List<uint>();
            for (int i = 0; i < n; i++)
                l.Add(p.Next());
            return l;
        }

        static string Join(List<uint> l) { return string.Join(",", l); }

        [PaTest]
        public void SequenceContinuousLoops()
        {
            var p = new PaMusicPlaylist(Group("sequenceContinuous", 2, Leaf(1), Leaf(2)), new PaRandom(1));
            Check.Equal("1,2,1,2,0", Join(Take(p, 5)), "two passes then end");
        }

        [PaTest]
        public void LeafLoopCountAndInfinite()
        {
            var p = new PaMusicPlaylist(Group("sequenceContinuous", 1, Leaf(1, 3), Leaf(2)), new PaRandom(1));
            Check.Equal("1,1,1,2,0", Join(Take(p, 5)), "leaf loop 3");
            var inf = new PaMusicPlaylist(Group("sequenceContinuous", 0, Leaf(1), Leaf(2)), new PaRandom(1));
            Check.Equal("1,2,1,2,1,2,1,2", Join(Take(inf, 8)), "loop 0 is infinite");
        }

        [PaTest]
        public void SequenceStepKeepsPosition()
        {
            var step = Group("sequenceStep", 1, Leaf(1), Leaf(2), Leaf(3));
            var p = new PaMusicPlaylist(Group("sequenceContinuous", 0, step, Leaf(9)), new PaRandom(1));
            Check.Equal("1,9,2,9,3,9,1,9", Join(Take(p, 8)), "one child per entry");
        }

        [PaTest]
        public void RandomStepAvoidsRepeat()
        {
            var g = Group("randomStep", 0, Leaf(1), Leaf(2), Leaf(3));
            g.AvoidRepeat = 1;
            var p = new PaMusicPlaylist(g, new PaRandom(3));
            uint last = 0;
            var seen = new HashSet<uint>();
            for (int i = 0; i < 60; i++)
            {
                uint s = p.Next();
                Check.True(s != 0 && s != last, "no immediate repeat at " + i);
                seen.Add(s);
                last = s;
            }
            Check.Equal(3, seen.Count, "all children used");
        }

        [PaTest]
        public void ShuffleCoversEveryChildPerPass()
        {
            var g = Group("randomContinuous", 4, Leaf(1), Leaf(2), Leaf(3), Leaf(4));
            g.Shuffle = true;
            var p = new PaMusicPlaylist(g, new PaRandom(11));
            for (int pass = 0; pass < 4; pass++)
            {
                var set = new HashSet<uint>(Take(p, 4));
                Check.Equal(4, set.Count, "pass " + pass + " is a permutation");
            }
            Check.Equal(0u, p.Next(), "finite end");
        }

        [PaTest]
        public void WeightZeroNeverPicked()
        {
            var g = Group("randomStep", 0, Leaf(1, 1, 0), Leaf(2, 1, 100));
            var p = new PaMusicPlaylist(g, new PaRandom(5));
            foreach (var s in Take(p, 30))
                Check.Equal(2u, s, "weighted pick");
        }

        [PaTest]
        public void EmptyPlaylistEnds()
        {
            Check.Equal(0u, new PaMusicPlaylist(Group("sequenceContinuous", 0), new PaRandom(1)).Next(), "empty infinite group");
            Check.Equal(0u, new PaMusicPlaylist(null, new PaRandom(1)).Next(), "no playlist");
        }
    }
}
