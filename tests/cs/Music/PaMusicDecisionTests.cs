using System.Collections.Generic;
using static PortAudio.Tests.MusicData;

namespace PortAudio.Tests
{
    public sealed class PaMusicDecisionTests
    {
        static PaMusicDecision Tree(bool weighted, params PaMusicPath[] paths)
        {
            return new PaMusicDecision(paths, 2, weighted);
        }

        [PaTest]
        public void ExactKeyBeatsWildcard()
        {
            var d = Tree(false, Path(10, 5, 7), Path(20, 0, 7), Path(30, 0, 0));
            var c = d.Resolve(new uint[] { 5, 7 }, null);
            Check.True(c.Matched, "matched");
            Check.Equal(10u, c.Node, "exact path");
            Check.Equal(20u, d.Resolve(new uint[] { 6, 7 }, null).Node, "wildcard first level");
            Check.Equal(30u, d.Resolve(new uint[] { 6, 9 }, null).Node, "wildcard both levels");
        }

        [PaTest]
        public void BacktracksFromDeadExactBranch()
        {
            // 5 exists on level 0 but has no child for 8; the wildcard branch does
            var d = Tree(false, Path(10, 5, 7), Path(20, 0, 8));
            Check.Equal(20u, d.Resolve(new uint[] { 5, 8 }, null).Node, "backtracked to wildcard");
            Check.True(!d.Resolve(new uint[] { 5, 9 }, null).Matched, "no path at all");
        }

        [PaTest]
        public void NodeZeroMeansNothing()
        {
            var d = Tree(false, Path(0, 3, 0), Path(10, 0, 0));
            var c = d.Resolve(new uint[] { 3, 1 }, null);
            Check.True(c.Matched, "matched");
            Check.Equal(0u, c.Node, "nothing");
            Check.Equal(10u, d.Resolve(new uint[] { 4, 1 }, null).Node, "other value");
        }

        [PaTest]
        public void UnsetValueOnlyMatchesWildcard()
        {
            var d = Tree(false, Path(10, 5, 0), Path(20, 0, 0));
            Check.Equal(20u, d.Resolve(new uint[] { 0, 0 }, null).Node, "unset state");
        }

        [PaTest]
        public void WeightedPicksAmongEqualMatches()
        {
            var a = Path(10, 1, 1); a.Weight = 0;
            var b = Path(20, 1, 1); b.Weight = 100;
            var d = Tree(true, a, b);
            var rnd = new PaRandom(7);
            for (int i = 0; i < 20; i++)
                Check.Equal(20u, d.Resolve(new uint[] { 1, 1 }, rnd).Node, "weight 0 never wins");
            var c = Path(30, 2, 2); c.Weight = 50;
            var e = Path(40, 2, 2); e.Weight = 50;
            var d2 = Tree(true, c, e);
            var seen = new HashSet<uint>();
            for (int i = 0; i < 50; i++)
                seen.Add(d2.Resolve(new uint[] { 2, 2 }, rnd).Node);
            Check.Equal(2, seen.Count, "both equal-weight paths chosen");
            Check.Equal(30u, Tree(false, c, e).Resolve(new uint[] { 2, 2 }, rnd).Node, "best match takes the first");
        }

        [PaTest]
        public void ValuesReadStatesAndSwitches()
        {
            var w = new FakeWorld();
            w.States[100] = 4;
            w.Switches["200/9"] = 6;
            var m = new PaMusic { Arguments = new[] { new PaMusicArg { Group = 100, IsState = true }, new PaMusicArg { Group = 200 } } };
            var v = PaMusicDecision.Values(m, w, 9);
            Check.Equal(4u, v[0], "state");
            Check.Equal(6u, v[1], "switch on object");
        }
    }
}
