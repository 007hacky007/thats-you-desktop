using System.Collections.Generic;
using System.Threading;

namespace PortAudio.Tests
{
    public static class PaBankStoreTests
    {
        static string J(string s)
        {
            return PaModelReaderTests.J(s);
        }

        static PaManifest Manifest()
        {
            return PaModelReader.ReadManifest(PaModelReaderTests.Manifest);
        }

        static PaBank Bank(string json)
        {
            return PaModelReader.ReadBank(J(json));
        }

        static readonly uint MasterId = PaIds.Hash("Master Audio Bus");

        // 10 ActorMixer (bus 501, 3D) > 11 RanSeq > {12 Sound embedded, 13 Sound streamed, bus 502, own positioning}
        // 15 MusicSegment > 14 MusicTrack (embedded + streamed); 20 <-> 21 parent cycle; 30 <-> 31 children cycle
        // busses: master > 501 > 502, 503 aux without parent
        static PaBank Hierarchy()
        {
            return Bank(
                "{'id':900,'name':'H','events':[{'id':1,'name':'Play_H'}]," +
                "'nodes':[" +
                "{'id':10,'type':'ActorMixer','bus':501,'children':[11],'positioning':{'threeD':true,'attenuation':5}}," +
                "{'id':11,'parent':10,'type':'RanSeq','children':[12,13],'name':'Random'}," +
                "{'id':12,'parent':11,'source':{'media':7001,'stream':'embedded','kind':'vorbis'}}," +
                "{'id':13,'parent':11,'bus':502,'positioning':{'usePanner':true},'source':{'media':7002,'stream':'streamed','kind':'vorbis'}}," +
                "{'id':14,'parent':15,'type':'MusicTrack','music':{'sources':[{'media':7003,'stream':'Embedded'},{'media':7004,'stream':'streamed'}]}}," +
                "{'id':15,'type':'MusicSegment','children':[14]}," +
                "{'id':20,'parent':21},{'id':21,'parent':20}," +
                "{'id':30,'type':'Layer','children':[31]},{'id':31,'type':'Layer','children':[30,12]}]," +
                "'busses':[{'id':" + MasterId + ",'name':'Master Audio Bus'},{'id':501,'parent':" + MasterId + ",'name':'Music'}," +
                "{'id':502,'parent':501},{'id':503,'isAux':true}]}");
        }

        static PaBankStore LoadedStore()
        {
            var store = new PaBankStore("/nowhere", Manifest());
            PaAssert.Equal(PaResult.Success, store.Register(Hierarchy()));
            return store;
        }

        [PaTest]
        public static void FindByNameOrId()
        {
            var store = new PaBankStore("/x", Manifest());
            PaAssert.Equal("Init", store.Find("Init").Name);
            PaAssert.Equal("Init", store.Find("init.bnk").Name);
            PaAssert.Equal("Init", store.Find("INIT.BNK").Name);
            PaAssert.Equal("Init", store.Find("Audio/GeneratedSoundBanks\\Init.bnk").Name);
            PaAssert.Equal("Commentary_Global_SoundBank", store.Find(2713970726u).Name);
            PaAssert.Equal(1355168291u, PaIds.Hash("Init"), "bank id is the short-ID hash");
            PaAssert.Null(store.Find("Missing.bnk"));
            PaAssert.Null(store.Find(5u));
            PaAssert.Null(store.Find((string)null));
            PaAssert.Null(store.Find(""));
            PaAssert.Null(new PaBankStore("/x", null).Find("Init"));
        }

        [PaTest]
        public static void ReadPicksTheLanguageFile()
        {
            using (var dir = new PaTempDir())
            {
                dir.Write("banks/Init.json", J("{'format':1,'name':'Init','id':1355168291}"));
                dir.Write("banks/czech/Commentary_Global_SoundBank.json", J("{'format':1,'language':'Czech','languageKey':'czech'}"));
                dir.Write("banks/english_uk/Commentary_Global_SoundBank.json", J("{'format':1,'languageKey':'english_uk'}"));
                var store = new PaBankStore(dir.Root, Manifest());
                PaAssert.Equal(dir.Root, store.Root);
                int result;
                PaBank init = store.Read(store.Find("Init"), "czech", out result);
                PaAssert.Equal(PaResult.Success, result);
                PaAssert.Equal(1355168291u, init.Id);

                PaBankInfo com = store.Find("Commentary_Global_SoundBank");
                PaBank cz = store.Read(com, "czech", out result);
                PaAssert.Equal(PaResult.Success, result);
                PaAssert.Equal("czech", cz.LanguageKey);
                PaAssert.Equal(2713970726u, cz.Id, "id taken from the manifest when the file has none");
                PaAssert.Equal("Commentary_Global_SoundBank", cz.Name);
                PaAssert.Equal("czech", store.Read(com, "CZECH", out result).LanguageKey, "key case ignored");
                PaAssert.Equal("english_uk", store.Read(com, "french_france", out result).LanguageKey, "not in this bank -> default");
                PaAssert.Equal("english_uk", store.Read(com, null, out result).LanguageKey, "no language -> default");
                PaAssert.Equal(0, new List<PaBank>(store.Loaded).Count, "Read does not register");
            }
        }

        [PaTest]
        public static void ReadErrors()
        {
            using (var dir = new PaTempDir())
            {
                var store = new PaBankStore(dir.Root, Manifest());
                int result;
                PaAssert.Null(store.Read(store.Find("Init"), null, out result));
                PaAssert.Equal(PaResult.FileNotFound, result);
                PaAssert.Null(store.Read(null, null, out result));
                PaAssert.Equal(PaResult.FileNotFound, result);
                dir.Write("banks/Init.json", "{\"nodes\":[");
                PaAssert.Null(store.Read(store.Find("Init"), null, out result));
                PaAssert.Equal(PaResult.BankReadError, result);
                dir.Write("banks/Init.json", "{\"nodes\":[{\"type\":\"Bogus\"}]}");
                PaAssert.Null(store.Read(store.Find("Init"), null, out result));
                PaAssert.Equal(PaResult.BankReadError, result);
            }
        }

        [PaTest]
        public static void ReadIsThreadSafe()
        {
            using (var dir = new PaTempDir())
            {
                dir.Write("banks/Init.json", PaModelReaderTests.FullBank);
                var store = new PaBankStore(dir.Root, Manifest());
                PaBankInfo info = store.Find("Init");
                int failures = 0;
                var threads = new List<Thread>();
                for (int t = 0; t < 8; t++)
                {
                    var th = new Thread(() =>
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            int r;
                            PaBank b = store.Read(info, null, out r);
                            if (r != PaResult.Success || b == null || b.Nodes.Count != 9)
                                Interlocked.Increment(ref failures);
                        }
                    });
                    threads.Add(th);
                    th.Start();
                }
                foreach (Thread th in threads)
                    th.Join();
                PaAssert.Equal(0, failures);
            }
        }

        [PaTest]
        public static void RegisterAndReleaseAreRefCounted()
        {
            var store = new PaBankStore("/x", Manifest());
            int v0 = store.Version;
            PaBank b = Hierarchy();
            PaAssert.Equal(PaResult.InvalidParameter, store.Register(null));
            PaAssert.Equal(PaResult.Success, store.Register(b));
            PaAssert.True(store.Version > v0, "version moves on register");
            PaAssert.Equal(PaResult.Success, store.Register(Hierarchy()), "second register of a loaded id");
            PaAssert.Equal(2, store.RefCount(900));
            PaAssert.Same(b.Nodes[0], store.Node(10), "the first registration's objects are kept");
            int v1 = store.Version;
            PaAssert.Equal(PaResult.Success, store.Release(900));
            PaAssert.True(store.Version > v1, "version moves on release");
            PaAssert.True(store.IsLoaded(900));
            PaAssert.NotNull(store.Node(12));
            PaAssert.Equal(PaResult.Success, store.Release(900));
            PaAssert.False(store.IsLoaded(900));
            PaAssert.Equal(0, store.RefCount(900));
            PaAssert.Null(store.Node(12));
            PaAssert.Null(store.Event(1));
            PaAssert.Null(store.Bus(501));
            PaAssert.Equal(PaResult.UnknownBankID, store.Release(900));
            PaAssert.Equal(PaResult.UnknownBankID, store.Release(12345));
        }

        [PaTest]
        public static void DuplicateObjectsLiveUntilTheLastHolderUnloads()
        {
            var store = new PaBankStore("/x", Manifest());
            PaBank a = Bank("{'id':1,'events':[{'id':100}],'nodes':[{'id':77,'name':'Shared'},{'id':78}],'busses':[{'id':5}]," +
                            "'actions':[{'id':300}],'gameParameters':[{'id':400}],'stateGroups':[{'id':401}],'effects':[{'id':402}],'attenuations':[{'id':403}]}");
            PaBank b = Bank("{'id':2,'events':[{'id':101}],'nodes':[{'id':77,'name':'Shared'}],'busses':[{'id':5}],'gameParameters':[{'id':400}]}");
            store.Register(b);
            store.Register(a);
            PaAssert.Same(b.Nodes[0], store.Node(77), "kept once, first copy wins");
            PaAssert.NotNull(store.Action(300));
            PaAssert.NotNull(store.StateGroup(401));
            PaAssert.NotNull(store.Effect(402));
            PaAssert.NotNull(store.Attenuation(403));
            store.Release(2);
            PaAssert.NotNull(store.Node(77), "still held by bank 1");
            PaAssert.NotNull(store.Bus(5));
            PaAssert.NotNull(store.GameParameter(400));
            PaAssert.Null(store.Event(101), "events of the released bank go");
            PaAssert.NotNull(store.Event(100));
            store.Release(1);
            PaAssert.Null(store.Node(77));
            PaAssert.Null(store.Node(78));
            PaAssert.Null(store.Bus(5));
            PaAssert.Null(store.GameParameter(400));
            PaAssert.Null(store.Action(300));
        }

        [PaTest]
        public static void ClearAndLoadedOrder()
        {
            var store = new PaBankStore("/x", Manifest());
            store.Register(Bank("{'id':3}"));
            store.Register(Bank("{'id':1,'nodes':[{'id':5}]}"));
            store.Register(Bank("{'id':2}"));
            store.Release(1);
            store.Register(Bank("{'id':1}"));
            var ids = new List<uint>();
            foreach (PaBank b in store.Loaded)
                ids.Add(b.Id);
            PaAssert.SequenceEqual(new uint[] { 3, 2, 1 }, ids);
            int v = store.Version;
            store.Clear();
            PaAssert.True(store.Version > v);
            PaAssert.False(store.IsLoaded(3));
            PaAssert.Equal(0, new List<PaBank>(store.Loaded).Count);
        }

        [PaTest]
        public static void ChainAndIsUnder()
        {
            PaBankStore s = LoadedStore();
            PaAssert.SequenceEqual(new uint[] { 12, 11, 10 }, s.Chain(12));
            PaAssert.SequenceEqual(new uint[] { 10 }, s.Chain(10));
            PaAssert.SequenceEqual(new uint[] { 20, 21 }, s.Chain(20), "cycle-safe");
            PaAssert.Equal(0, s.Chain(999).Count);
            PaAssert.Equal(0, s.Chain(0).Count);
            PaAssert.True(s.IsUnder(12, 10));
            PaAssert.True(s.IsUnder(12, 11));
            PaAssert.True(s.IsUnder(12, 12), "a node is under itself");
            PaAssert.False(s.IsUnder(10, 12));
            PaAssert.False(s.IsUnder(13, 12));
            PaAssert.False(s.IsUnder(20, 99), "cycle terminates");
            PaAssert.True(s.IsUnder(20, 21));
            PaAssert.False(s.IsUnder(12, 0));
            PaAssert.False(s.IsUnder(0, 0));
        }

        [PaTest]
        public static void BussesAndPositioning()
        {
            PaBankStore s = LoadedStore();
            PaAssert.Equal(MasterId, s.MasterBus.Id);
            PaAssert.Equal(501u, s.OutputBus(12), "inherited from the actor mixer");
            PaAssert.Equal(502u, s.OutputBus(13), "own override");
            PaAssert.Equal(MasterId, s.OutputBus(15), "no override -> master");
            PaAssert.Equal(MasterId, s.OutputBus(999), "unknown node -> master");
            PaAssert.True(s.IsUnderBus(502, MasterId));
            PaAssert.True(s.IsUnderBus(502, 501));
            PaAssert.True(s.IsUnderBus(502, 502));
            PaAssert.False(s.IsUnderBus(501, 502));
            PaAssert.False(s.IsUnderBus(503, MasterId), "aux bus outside the tree");
            PaAssert.Same(s.Node(10).Positioning, s.EffectivePositioning(12));
            PaAssert.Same(s.Node(13).Positioning, s.EffectivePositioning(13));
            PaAssert.Null(s.EffectivePositioning(15));
            PaAssert.Null(s.EffectivePositioning(20), "cycle without positioning terminates");

            var empty = new PaBankStore("/x", Manifest());
            PaAssert.Null(empty.MasterBus);
            PaAssert.Equal(0u, empty.OutputBus(12));
        }

        [PaTest]
        public static void MasterBusFallback()
        {
            var s = new PaBankStore("/x", Manifest());
            s.Register(Bank("{'id':1,'busses':[{'id':40,'isAux':true},{'id':41,'parent':42},{'id':42,'name':'Root'}]}"));
            PaAssert.Equal(42u, s.MasterBus.Id, "first non-aux bus without a parent");
            s.Register(Bank("{'id':2,'busses':[{'id':" + MasterId + ",'name':'Master Audio Bus'}]}"));
            PaAssert.Equal(MasterId, s.MasterBus.Id, "the master bus wins once loaded");
            s.Release(2);
            PaAssert.Equal(42u, s.MasterBus.Id);
        }

        [PaTest]
        public static void Sources()
        {
            PaBankStore s = LoadedStore();
            var embedded = new List<uint>();
            foreach (PaSource src in s.EmbeddedSources(900))
                embedded.Add(src.Media);
            PaAssert.SequenceEqual(new uint[] { 7001, 7003 }, embedded, "Sound and MusicTrack embedded sources");
            PaAssert.Equal(0, new List<PaSource>(s.EmbeddedSources(1)).Count);

            var under = new List<uint>();
            foreach (PaSource src in s.SourcesUnder(10))
                under.Add(src.Media);
            PaAssert.SequenceEqual(new uint[] { 7001, 7002 }, under);
            under.Clear();
            foreach (PaSource src in s.SourcesUnder(15))
                under.Add(src.Media);
            PaAssert.SequenceEqual(new uint[] { 7003, 7004 }, under);
            under.Clear();
            foreach (PaSource src in s.SourcesUnder(30))
                under.Add(src.Media);
            PaAssert.SequenceEqual(new uint[] { 7001 }, under, "children cycle visited once");
            PaAssert.Equal(0, new List<PaSource>(s.SourcesUnder(999)).Count);
        }

        [PaTest]
        public static void NameOf()
        {
            PaBankStore s = LoadedStore();
            PaAssert.Equal("PLAY_MUSIC_FRONTEND", s.NameOf(3991942870u), "manifest names");
            PaAssert.Equal("INIT", s.NameOf(1355168291u), "manifest names before bank names");
            PaAssert.Equal("Play_H", s.NameOf(1), "event name");
            PaAssert.Equal("Music", s.NameOf(501), "bus name");
            PaAssert.Equal("Random", s.NameOf(11), "node name");
            PaAssert.Equal("Commentary_Global_SoundBank", s.NameOf(2713970726u), "bank name");
            PaAssert.Null(s.NameOf(424242));
        }
    }
}
