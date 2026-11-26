// Music switch decision tree: picks the child of a MusicSwitch for the current argument values.
//
// Paths are stored as a trie keyed by argument value, one level per argument. Key 0 on a level
// means "any value". Lookup tries the exact value first, then the wildcard, and backtracks when
// a branch dead-ends deeper down. A matched path whose Node is 0 selects nothing (silence).

using System.Collections.Generic;

namespace PortAudio
{
    public struct PaMusicChoice
    {
        public bool Matched;   // a path matched (Node may still be 0 = nothing)
        public uint Node;      // selected child, 0 = nothing
    }

    public sealed class PaMusicDecision
    {
        sealed class TrieNode
        {
            public Dictionary<uint, TrieNode> Next;
            public List<PaMusicPath> Leaves;
        }

        readonly TrieNode root = new TrieNode();
        readonly int depth;
        readonly bool weighted;

        public PaMusicDecision(PaMusicPath[] paths, int argumentCount, bool weighted)
        {
            depth = argumentCount < 0 ? 0 : argumentCount;
            this.weighted = weighted;
            if (paths == null)
                return;
            foreach (var p in paths)
            {
                if (p == null)
                    continue;
                var n = root;
                for (int i = 0; i < depth; i++)
                {
                    uint key = p.Keys != null && i < p.Keys.Length ? p.Keys[i] : 0u;
                    if (n.Next == null)
                        n.Next = new Dictionary<uint, TrieNode>();
                    TrieNode child;
                    if (!n.Next.TryGetValue(key, out child))
                    {
                        child = new TrieNode();
                        n.Next[key] = child;
                    }
                    n = child;
                }
                if (n.Leaves == null)
                    n.Leaves = new List<PaMusicPath>();
                n.Leaves.Add(p);
            }
        }

        public static PaMusicDecision For(PaMusic music)
        {
            if (music == null)
                return new PaMusicDecision(null, 0, false);
            int args = music.Arguments != null ? music.Arguments.Length : 0;
            bool w = string.Equals(music.Mode, "weighted", System.StringComparison.OrdinalIgnoreCase);
            return new PaMusicDecision(music.Paths, args, w);
        }

        // values[i] is the current value of argument i (0 = unset).
        public PaMusicChoice Resolve(uint[] values, PaRandom random)
        {
            var leaves = Find(root, values, 0);
            var choice = new PaMusicChoice();
            if (leaves == null || leaves.Count == 0)
                return choice;
            choice.Matched = true;
            choice.Node = Pick(leaves, random).Node;
            return choice;
        }

        List<PaMusicPath> Find(TrieNode n, uint[] values, int level)
        {
            if (level == depth)
                return n.Leaves;
            if (n.Next == null)
                return null;
            uint v = values != null && level < values.Length ? values[level] : 0u;
            TrieNode child;
            if (v != 0 && n.Next.TryGetValue(v, out child))
            {
                var r = Find(child, values, level + 1);
                if (r != null && r.Count > 0)
                    return r;
            }
            if (n.Next.TryGetValue(0u, out child))
                return Find(child, values, level + 1);
            return null;
        }

        PaMusicPath Pick(List<PaMusicPath> leaves, PaRandom random)
        {
            if (!weighted || leaves.Count == 1 || random == null)
                return leaves[0];
            long total = 0;
            foreach (var l in leaves)
                total += l.Weight > 0 ? l.Weight : 0;
            if (total <= 0)
                return leaves[random.Range(0, leaves.Count)];
            double r = random.NextDouble() * total;
            foreach (var l in leaves)
            {
                int w = l.Weight > 0 ? l.Weight : 0;
                if (r < w)
                    return l;
                r -= w;
            }
            return leaves[leaves.Count - 1];
        }

        // Current argument values of a MusicSwitch.
        public static uint[] Values(PaMusic music, IPaWorld world, ulong gameObject)
        {
            int n = music != null && music.Arguments != null ? music.Arguments.Length : 0;
            var v = new uint[n];
            if (world == null)
                return v;
            for (int i = 0; i < n; i++)
            {
                var a = music.Arguments[i];
                if (a == null)
                    continue;
                v[i] = a.IsState ? world.GetState(a.Group) : world.GetSwitch(a.Group, gameObject);
            }
            return v;
        }
    }
}
