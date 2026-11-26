// Loaded banks and the object index over them (audio architecture 6.3).
//
// Read() only does file IO and parsing and may run on any thread. Register() and Release() are
// reference counted per bank id. Every object of a registered bank goes into a per-kind index
// that remembers which loaded banks hold it: an object present in several banks (the same id,
// for example a music switch shared by every scenario bank) is indexed once and stays until the
// last bank holding it is released. All members take one lock, so concurrent readers are safe.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PortAudio
{
    public sealed class PaBankStore
    {
        const string MasterBusName = "Master Audio Bus";

        sealed class Entry<T> where T : class
        {
            public T Value;
            public readonly List<uint> Holders = new List<uint>(1);
        }

        sealed class Index<T> where T : class
        {
            readonly Dictionary<uint, Entry<T>> map = new Dictionary<uint, Entry<T>>();

            public void Add(uint id, T value, uint bank)
            {
                Entry<T> e;
                if (!map.TryGetValue(id, out e))
                {
                    e = new Entry<T> { Value = value };
                    map[id] = e;
                }
                if (!e.Holders.Contains(bank))
                    e.Holders.Add(bank);
            }

            public void Remove(uint id, uint bank)
            {
                Entry<T> e;
                if (!map.TryGetValue(id, out e))
                    return;
                e.Holders.Remove(bank);
                if (e.Holders.Count == 0)
                    map.Remove(id);
            }

            public T Get(uint id)
            {
                Entry<T> e;
                return map.TryGetValue(id, out e) ? e.Value : null;
            }

            public IEnumerable<T> Values
            {
                get
                {
                    foreach (Entry<T> e in map.Values)
                        yield return e.Value;
                }
            }

            public void Clear()
            {
                map.Clear();
            }
        }

        sealed class LoadedBank
        {
            public PaBank Bank;
            public int RefCount;
        }

        readonly object sync = new object();
        readonly string root;
        readonly PaManifest manifest;
        readonly Dictionary<uint, PaBankInfo> infoById = new Dictionary<uint, PaBankInfo>();
        readonly Dictionary<string, PaBankInfo> infoByName = new Dictionary<string, PaBankInfo>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<uint, LoadedBank> loaded = new Dictionary<uint, LoadedBank>();
        readonly List<uint> loadOrder = new List<uint>();

        readonly Index<PaEvent> events = new Index<PaEvent>();
        readonly Index<PaAction> actions = new Index<PaAction>();
        readonly Index<PaNode> nodes = new Index<PaNode>();
        readonly Index<PaBus> busses = new Index<PaBus>();
        readonly Index<PaAttenuation> attenuations = new Index<PaAttenuation>();
        readonly Index<PaGameParameter> gameParameters = new Index<PaGameParameter>();
        readonly Index<PaStateGroup> stateGroups = new Index<PaStateGroup>();
        readonly Index<PaEffect> effects = new Index<PaEffect>();

        int version;
        int masterVersion = -1;
        PaBus master;

        public PaBankStore(string root, PaManifest manifest)
        {
            this.root = root ?? "";
            this.manifest = manifest;
            if (manifest != null && manifest.Banks != null)
            {
                foreach (PaBankInfo b in manifest.Banks)
                {
                    if (b == null)
                        continue;
                    if (!infoById.ContainsKey(b.Id))
                        infoById[b.Id] = b;
                    if (!string.IsNullOrEmpty(b.Name) && !infoByName.ContainsKey(b.Name))
                        infoByName[b.Name] = b;
                }
            }
        }

        public string Root
        {
            get { return root; }
        }

        public PaManifest Manifest
        {
            get { return manifest; }
        }

        // Incremented on every change of the loaded set (register, release, clear).
        public int Version
        {
            get { lock (sync) return version; }
        }

        // ---- bank lookup and IO ----

        public PaBankInfo Find(uint bankId)
        {
            PaBankInfo info;
            return infoById.TryGetValue(bankId, out info) ? info : null;
        }

        // Accepts "Name", "Name.bnk" and a path ending in either; case-insensitive.
        public PaBankInfo Find(string name)
        {
            string stem = BankStem(name);
            if (stem.Length == 0)
                return null;
            PaBankInfo info;
            if (infoByName.TryGetValue(stem, out info))
                return info;
            return Find(PaIds.Hash(stem));
        }

        static string BankStem(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";
            string s = name.Trim();
            int slash = Math.Max(s.LastIndexOf('/'), s.LastIndexOf('\\'));
            if (slash >= 0)
                s = s.Substring(slash + 1);
            if (s.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 4);
            return s;
        }

        // The JSON path of a bank for a language key: localized banks use the key when the bank
        // exists in that language, else the manifest default language.
        public string FilePath(PaBankInfo info, string languageKey)
        {
            if (info == null || string.IsNullOrEmpty(info.File))
                return null;
            string file = info.File;
            if (info.Localized || file.IndexOf("{lang}", StringComparison.Ordinal) >= 0)
                file = file.Replace("{lang}", LanguageFor(info, languageKey) ?? "");
            return Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
        }

        string LanguageFor(PaBankInfo info, string languageKey)
        {
            List<string> langs = info.Languages;
            if (!string.IsNullOrEmpty(languageKey) && langs != null)
            {
                foreach (string l in langs)
                {
                    if (string.Equals(l, languageKey, StringComparison.OrdinalIgnoreCase))
                        return l;
                }
            }
            PaLanguage def = manifest != null ? manifest.ResolveLanguage(null, null) : null;
            return def != null ? def.Key : languageKey;
        }

        // File IO and parsing only; safe on any thread. result: Success, FileNotFound or BankReadError.
        public PaBank Read(PaBankInfo info, string languageKey, out int result)
        {
            string path = FilePath(info, languageKey);
            if (path == null || !File.Exists(path))
            {
                result = PaResult.FileNotFound;
                return null;
            }
            try
            {
                PaBank bank = PaModelReader.ReadBank(File.ReadAllText(path, Encoding.UTF8));
                if (bank.Id == 0)
                    bank.Id = info.Id;
                if (string.IsNullOrEmpty(bank.Name))
                    bank.Name = info.Name;
                result = PaResult.Success;
                return bank;
            }
            catch (Exception e)
            {
                if (!(e is PaDataException || e is IOException || e is UnauthorizedAccessException))
                    throw;
                PaLog.Warn("bank " + info.Name + ": cannot read " + path + ": " + e.Message);
                result = PaResult.BankReadError;
                return null;
            }
        }

        // ---- loaded set ----

        public int Register(PaBank bank)
        {
            if (bank == null)
                return PaResult.InvalidParameter;
            lock (sync)
            {
                version++;
                LoadedBank l;
                if (loaded.TryGetValue(bank.Id, out l))
                {
                    l.RefCount++;
                    return PaResult.Success;
                }
                loaded[bank.Id] = new LoadedBank { Bank = bank, RefCount = 1 };
                loadOrder.Add(bank.Id);
                uint id = bank.Id;
                if (bank.Events != null) foreach (PaEvent o in bank.Events) if (o != null) events.Add(o.Id, o, id);
                if (bank.Actions != null) foreach (PaAction o in bank.Actions) if (o != null) actions.Add(o.Id, o, id);
                if (bank.Nodes != null) foreach (PaNode o in bank.Nodes) if (o != null) nodes.Add(o.Id, o, id);
                if (bank.Busses != null) foreach (PaBus o in bank.Busses) if (o != null) busses.Add(o.Id, o, id);
                if (bank.Attenuations != null) foreach (PaAttenuation o in bank.Attenuations) if (o != null) attenuations.Add(o.Id, o, id);
                if (bank.GameParameters != null) foreach (PaGameParameter o in bank.GameParameters) if (o != null) gameParameters.Add(o.Id, o, id);
                if (bank.StateGroups != null) foreach (PaStateGroup o in bank.StateGroups) if (o != null) stateGroups.Add(o.Id, o, id);
                if (bank.Effects != null) foreach (PaEffect o in bank.Effects) if (o != null) effects.Add(o.Id, o, id);
                return PaResult.Success;
            }
        }

        public int Release(uint bankId)
        {
            lock (sync)
            {
                LoadedBank l;
                if (!loaded.TryGetValue(bankId, out l))
                    return PaResult.UnknownBankID;
                version++;
                l.RefCount--;
                if (l.RefCount > 0)
                    return PaResult.Success;
                loaded.Remove(bankId);
                loadOrder.Remove(bankId);
                PaBank bank = l.Bank;
                if (bank.Events != null) foreach (PaEvent o in bank.Events) if (o != null) events.Remove(o.Id, bankId);
                if (bank.Actions != null) foreach (PaAction o in bank.Actions) if (o != null) actions.Remove(o.Id, bankId);
                if (bank.Nodes != null) foreach (PaNode o in bank.Nodes) if (o != null) nodes.Remove(o.Id, bankId);
                if (bank.Busses != null) foreach (PaBus o in bank.Busses) if (o != null) busses.Remove(o.Id, bankId);
                if (bank.Attenuations != null) foreach (PaAttenuation o in bank.Attenuations) if (o != null) attenuations.Remove(o.Id, bankId);
                if (bank.GameParameters != null) foreach (PaGameParameter o in bank.GameParameters) if (o != null) gameParameters.Remove(o.Id, bankId);
                if (bank.StateGroups != null) foreach (PaStateGroup o in bank.StateGroups) if (o != null) stateGroups.Remove(o.Id, bankId);
                if (bank.Effects != null) foreach (PaEffect o in bank.Effects) if (o != null) effects.Remove(o.Id, bankId);
                return PaResult.Success;
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                version++;
                loaded.Clear();
                loadOrder.Clear();
                events.Clear();
                actions.Clear();
                nodes.Clear();
                busses.Clear();
                attenuations.Clear();
                gameParameters.Clear();
                stateGroups.Clear();
                effects.Clear();
            }
        }

        public bool IsLoaded(uint bankId)
        {
            lock (sync) return loaded.ContainsKey(bankId);
        }

        public int RefCount(uint bankId)
        {
            lock (sync)
            {
                LoadedBank l;
                return loaded.TryGetValue(bankId, out l) ? l.RefCount : 0;
            }
        }

        // Snapshot in load order.
        public IEnumerable<PaBank> Loaded
        {
            get
            {
                lock (sync)
                {
                    var list = new List<PaBank>(loadOrder.Count);
                    foreach (uint id in loadOrder)
                        list.Add(loaded[id].Bank);
                    return list;
                }
            }
        }

        // ---- object lookup ----

        public PaEvent Event(uint id) { lock (sync) return events.Get(id); }
        public PaAction Action(uint id) { lock (sync) return actions.Get(id); }
        public PaNode Node(uint id) { lock (sync) return nodes.Get(id); }
        public PaBus Bus(uint id) { lock (sync) return busses.Get(id); }
        public PaAttenuation Attenuation(uint id) { lock (sync) return attenuations.Get(id); }
        public PaGameParameter GameParameter(uint id) { lock (sync) return gameParameters.Get(id); }
        public PaStateGroup StateGroup(uint id) { lock (sync) return stateGroups.Get(id); }
        public PaEffect Effect(uint id) { lock (sync) return effects.Get(id); }

        // The root audio bus: "Master Audio Bus" (by its short id) when loaded, else the first
        // non-aux bus without a parent in load order. null when no bus is loaded.
        public PaBus MasterBus
        {
            get
            {
                lock (sync)
                {
                    if (masterVersion == version)
                        return master;
                    masterVersion = version;
                    master = busses.Get(PaIds.Hash(MasterBusName));
                    for (int i = 0; master == null && i < loadOrder.Count; i++)
                    {
                        List<PaBus> list = loaded[loadOrder[i]].Bank.Busses;
                        if (list == null)
                            continue;
                        foreach (PaBus b in list)
                        {
                            if (b != null && b.Parent == 0 && !b.IsAux)
                            {
                                master = b;
                                break;
                            }
                        }
                    }
                    return master;
                }
            }
        }

        // Manifest names first, then names carried by loaded events, busses, nodes, game parameters
        // and state groups. null when unknown.
        public string NameOf(uint id)
        {
            string name;
            if (manifest != null && manifest.Names != null && manifest.Names.TryGetValue(id, out name))
                return name;
            lock (sync)
            {
                PaEvent e = events.Get(id);
                if (e != null && !string.IsNullOrEmpty(e.Name))
                    return e.Name;
                PaBus b = busses.Get(id);
                if (b != null && !string.IsNullOrEmpty(b.Name))
                    return b.Name;
                PaNode n = nodes.Get(id);
                if (n != null && !string.IsNullOrEmpty(n.Name))
                    return n.Name;
                PaGameParameter g = gameParameters.Get(id);
                if (g != null && !string.IsNullOrEmpty(g.Name))
                    return g.Name;
                PaStateGroup s = stateGroups.Get(id);
                if (s != null && !string.IsNullOrEmpty(s.Name))
                    return s.Name;
            }
            PaBankInfo info = Find(id);
            return info != null ? info.Name : null;
        }

        // ---- hierarchy ----

        // The node itself, its parent, and so on up to the root; only loaded nodes; cycle-safe.
        public List<uint> Chain(uint nodeId)
        {
            var chain = new List<uint>();
            lock (sync)
            {
                uint cur = nodeId;
                while (cur != 0 && !chain.Contains(cur))
                {
                    PaNode n = nodes.Get(cur);
                    if (n == null)
                        break;
                    chain.Add(cur);
                    cur = n.Parent;
                }
            }
            return chain;
        }

        // True when ancestorId is the node itself or any node on its parent chain.
        public bool IsUnder(uint nodeId, uint ancestorId)
        {
            if (nodeId == 0 || ancestorId == 0)
                return false;
            lock (sync)
            {
                uint cur = nodeId;
                var seen = new HashSet<uint>();
                while (cur != 0 && seen.Add(cur))
                {
                    if (cur == ancestorId)
                        return true;
                    PaNode n = nodes.Get(cur);
                    if (n == null)
                        return false;
                    cur = n.Parent;
                }
                return false;
            }
        }

        // First nonzero Bus on the Chain, else the master bus id (0 if none).
        public uint OutputBus(uint nodeId)
        {
            lock (sync)
            {
                uint cur = nodeId;
                var seen = new HashSet<uint>();
                while (cur != 0 && seen.Add(cur))
                {
                    PaNode n = nodes.Get(cur);
                    if (n == null)
                        break;
                    if (n.Bus != 0)
                        return n.Bus;
                    cur = n.Parent;
                }
            }
            PaBus m = MasterBus;
            return m != null ? m.Id : 0;
        }

        // True when ancestorBusId is the bus itself or any bus on its parent chain.
        public bool IsUnderBus(uint busId, uint ancestorBusId)
        {
            if (busId == 0 || ancestorBusId == 0)
                return false;
            lock (sync)
            {
                uint cur = busId;
                var seen = new HashSet<uint>();
                while (cur != 0 && seen.Add(cur))
                {
                    if (cur == ancestorBusId)
                        return true;
                    PaBus b = busses.Get(cur);
                    if (b == null)
                        return false;
                    cur = b.Parent;
                }
                return false;
            }
        }

        // First non-null Positioning on the Chain; null when no node on it overrides positioning.
        public PaPositioning EffectivePositioning(uint nodeId)
        {
            lock (sync)
            {
                uint cur = nodeId;
                var seen = new HashSet<uint>();
                while (cur != 0 && seen.Add(cur))
                {
                    PaNode n = nodes.Get(cur);
                    if (n == null)
                        break;
                    if (n.Positioning != null)
                        return n.Positioning;
                    cur = n.Parent;
                }
                return null;
            }
        }

        // Sound and MusicTrack sources of a loaded bank whose Stream is "embedded".
        public IEnumerable<PaSource> EmbeddedSources(uint bankId)
        {
            var list = new List<PaSource>();
            lock (sync)
            {
                LoadedBank l;
                if (!loaded.TryGetValue(bankId, out l) || l.Bank.Nodes == null)
                    return list;
                foreach (PaNode n in l.Bank.Nodes)
                {
                    if (n == null)
                        continue;
                    if (n.Type == PaNodeType.Sound && IsEmbedded(n.Source))
                        list.Add(n.Source);
                    else if (n.Type == PaNodeType.MusicTrack && n.Music != null && n.Music.Sources != null)
                    {
                        foreach (PaSource s in n.Music.Sources)
                        {
                            if (IsEmbedded(s))
                                list.Add(s);
                        }
                    }
                }
            }
            return list;
        }

        static bool IsEmbedded(PaSource s)
        {
            return s != null && string.Equals(s.Stream, "embedded", StringComparison.OrdinalIgnoreCase);
        }

        // Every source reachable from the node through Children (the node's own included), each
        // node visited once. Used to pin or prefetch the media of an event's play targets.
        public IEnumerable<PaSource> SourcesUnder(uint nodeId)
        {
            var list = new List<PaSource>();
            lock (sync)
            {
                var seen = new HashSet<uint>();
                var stack = new Stack<uint>();
                stack.Push(nodeId);
                while (stack.Count > 0)
                {
                    uint id = stack.Pop();
                    if (id == 0 || !seen.Add(id))
                        continue;
                    PaNode n = nodes.Get(id);
                    if (n == null)
                        continue;
                    if (n.Source != null)
                        list.Add(n.Source);
                    if (n.Music != null && n.Music.Sources != null)
                    {
                        foreach (PaSource s in n.Music.Sources)
                        {
                            if (s != null)
                                list.Add(s);
                        }
                    }
                    if (n.Children != null)
                    {
                        for (int i = n.Children.Length - 1; i >= 0; i--)
                            stack.Push(n.Children[i]);
                    }
                }
            }
            return list;
        }
    }
}
