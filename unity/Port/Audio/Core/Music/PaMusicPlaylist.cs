// Music playlist cursor: walks the PaPlaylistItem tree of a MusicRanSeq and yields segment ids.
//
// Leaf items play their segment Loop times. Group items:
//   sequenceContinuous  every child in order, the whole group Loop times
//   sequenceStep        one child per iteration, the next one each time (position kept)
//   randomContinuous    every child in random order (Shuffle: no repeats within a pass)
//   randomStep          one random child per iteration
// Loop 0 is infinite. Random picks honour Weight and AvoidRepeat (the last N picks of that
// group are excluded while any other child is available); Shuffle draws without replacement
// until the group's children are exhausted. One cursor holds the state for one context.

using System.Collections.Generic;

namespace PortAudio
{
    public sealed class PaMusicPlaylist
    {
        sealed class State
        {
            public PaPlaylistItem Item;
            public State[] Children;
            public int LoopsDone;
            public bool InIteration;
            public List<int> Order = new List<int>();   // children for the current iteration
            public int OrderPos;
            public int StepIndex = -1;                    // sequenceStep position, kept across entries
            public readonly List<int> History = new List<int>();
            public readonly List<int> Bag = new List<int>();
            public bool YieldedThisIteration;
        }

        readonly State root;
        readonly PaRandom random;
        const int MaxEmptyIterations = 4;

        public PaMusicPlaylist(PaPlaylistItem rootItem, PaRandom random)
        {
            this.random = random;
            root = rootItem != null ? Build(rootItem, 0) : null;
        }

        static State Build(PaPlaylistItem item, int depth)
        {
            var s = new State { Item = item };
            var kids = item.Children ?? new PaPlaylistItem[0];
            var list = new List<State>();
            if (depth < 64)
                foreach (var k in kids)
                    if (k != null)
                        list.Add(Build(k, depth + 1));
            s.Children = list.ToArray();
            return s;
        }

        // Next segment id, or 0 when the playlist has run out.
        public uint Next()
        {
            return root == null ? 0u : Next(root);
        }

        static bool IsLeaf(State s)
        {
            return s.Children.Length == 0;
        }

        bool Infinite(State s)
        {
            return s.Item.Loop == 0;
        }

        bool LoopsLeft(State s)
        {
            return Infinite(s) || s.LoopsDone < s.Item.Loop;
        }

        uint Next(State s)
        {
            if (IsLeaf(s))
            {
                if (s.Item.Segment == 0 || s.Item.Loop < 0)
                    return 0;
                if (LoopsLeft(s))
                {
                    s.LoopsDone++;
                    return s.Item.Segment;
                }
                s.LoopsDone = 0;
                return 0;
            }
            int empty = 0;
            while (true)
            {
                if (!s.InIteration)
                {
                    if (!LoopsLeft(s) || s.Item.Loop < 0)
                    {
                        s.LoopsDone = 0;
                        return 0;
                    }
                    BeginIteration(s);
                }
                while (s.OrderPos < s.Order.Count)
                {
                    uint seg = Next(s.Children[s.Order[s.OrderPos]]);
                    if (seg != 0)
                    {
                        s.YieldedThisIteration = true;
                        return seg;
                    }
                    s.OrderPos++;
                }
                s.InIteration = false;
                s.LoopsDone++;
                if (!s.YieldedThisIteration && ++empty >= MaxEmptyIterations)
                {
                    s.LoopsDone = 0;
                    return 0;   // nothing playable below this group
                }
            }
        }

        void BeginIteration(State s)
        {
            s.InIteration = true;
            s.OrderPos = 0;
            s.YieldedThisIteration = false;
            s.Order.Clear();
            int n = s.Children.Length;
            string type = s.Item.Type ?? "sequenceContinuous";
            switch (type)
            {
                case "sequenceStep":
                    s.StepIndex = (s.StepIndex + 1) % n;
                    s.Order.Add(s.StepIndex);
                    break;
                case "randomStep":
                    s.Order.Add(PickRandom(s));
                    break;
                case "randomContinuous":
                    if (s.Item.Shuffle)
                    {
                        s.Bag.Clear();
                        for (int i = 0; i < n; i++)
                            s.Order.Add(PickRandom(s));
                    }
                    else
                        for (int i = 0; i < n; i++)
                            s.Order.Add(PickRandom(s));
                    break;
                default:
                    for (int i = 0; i < n; i++)
                        s.Order.Add(i);
                    break;
            }
        }

        int Weight(State child)
        {
            return child.Item.Weight;
        }

        int PickRandom(State s)
        {
            int n = s.Children.Length;
            if (n == 1)
                return 0;
            var candidates = new List<int>();
            if (s.Item.Shuffle)
            {
                if (s.Bag.Count == 0)
                    for (int i = 0; i < n; i++)
                        s.Bag.Add(i);
                candidates.AddRange(s.Bag);
            }
            else
                for (int i = 0; i < n; i++)
                    candidates.Add(i);
            int avoid = s.Item.AvoidRepeat;
            if (avoid > 0)
            {
                var filtered = new List<int>();
                int from = s.History.Count - avoid;
                foreach (int c in candidates)
                    if (s.History.LastIndexOf(c) < (from < 0 ? 0 : from))
                        filtered.Add(c);
                if (filtered.Count > 0)
                    candidates = filtered;
            }
            int pick = WeightedPick(s, candidates);
            s.History.Add(pick);
            if (s.History.Count > n + 8)
                s.History.RemoveAt(0);
            if (s.Item.Shuffle)
                s.Bag.Remove(pick);
            return pick;
        }

        int WeightedPick(State s, List<int> candidates)
        {
            if (random == null)
                return candidates[0];
            long total = 0;
            foreach (int c in candidates)
                total += Weight(s.Children[c]) > 0 ? Weight(s.Children[c]) : 0;
            if (total <= 0)
                return candidates[random.Range(0, candidates.Count)];
            double r = random.NextDouble() * total;
            foreach (int c in candidates)
            {
                int w = Weight(s.Children[c]);
                if (w <= 0)
                    continue;
                if (r < w)
                    return c;
                r -= w;
            }
            return candidates[candidates.Count - 1];
        }
    }
}
