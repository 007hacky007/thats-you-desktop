using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace PortAudio.Tests
{
    public static class PaMediaIndexTests
    {
        static string J(string s)
        {
            return PaModelReaderTests.J(s);
        }

        // li: 1 (li copy), 2 (li only); czech: 1 (czech copy), 3; english_uk: 3 (english copy), externals.
        static PaTempDir Fixture()
        {
            var dir = new PaTempDir();
            dir.Write("manifest.json", PaModelReaderTests.Manifest);
            dir.Write("media.json", J(
                "{'format':1,'scope':'li','media':[{'id':1,'file':'media/1.ogg','format':'ogg','rate':48000,'channels':2,'samples':48000}," +
                "{'id':2,'file':'media/2.wav','format':'wav'}]}"));
            dir.Write("lang/czech/media.json", J(
                "{'format':1,'scope':'czech','language':'Czech','externalPrefix':'cs-CZ_'," +
                "'media':[{'id':1,'file':'lang/czech/1.ogg'},{'id':3,'file':'lang/czech/3.ogg'}]," +
                "'external':[{'name':'cs-cz_ty1','file':'lang/czech/ext/cs-CZ_TY1.ogg'}]}"));
            dir.Write("lang/english_uk/media.json", J(
                "{'format':1,'scope':'english_uk','language':'English(UK)','externalPrefix':'en-GB_'," +
                "'media':[{'id':3,'file':'lang/english_uk/3.ogg'}]," +
                "'external':[{'name':'en-gb_ty2225','file':'lang/english_uk/ext/en-GB_TY2225.ogg'}," +
                "{'name':'en-gb_ty7','file':'lang/english_uk/ext/en-GB_TY7.ogg'},{'file':'lang/english_uk/ext/en-GB_NoName.ogg'}]}"));
            return dir;
        }

        static PaMediaIndex Index(PaTempDir dir)
        {
            return new PaMediaIndex(dir.Root, PaManifestLoader.Load(dir.Root));
        }

        [PaTest]
        public static void SearchOrder()
        {
            using (PaTempDir dir = Fixture())
            {
                PaMediaIndex m = Index(dir);
                PaAssert.Equal("lang/czech/1.ogg", m.Find(1, true, "czech").File, "language-specific: language first");
                PaAssert.Equal("media/1.ogg", m.Find(1, false, "czech").File, "otherwise li first");
                PaAssert.Equal("media/2.wav", m.Find(2, true, "czech").File, "language-specific falls back to li");
                PaAssert.Equal("lang/czech/3.ogg", m.Find(3, false, "czech").File, "li-first falls back to the language");
                PaAssert.Equal("lang/english_uk/3.ogg", m.Find(3, true, null).File, "no key -> manifest default language");
                PaAssert.Equal("lang/english_uk/3.ogg", m.Find(3, true, "").File);
                PaAssert.Null(m.Find(3, true, "french_france"), "language without a table and not in li");
                PaAssert.Null(m.Find(99, true, "czech"));
                PaAssert.Null(m.Find(99, false, "czech"));
            }
        }

        [PaTest]
        public static void EntriesGetFullPathScopeAndDuration()
        {
            using (PaTempDir dir = Fixture())
            {
                PaMediaIndex m = Index(dir);
                PaMediaEntry e = m.Find(1, false, null);
                PaAssert.Equal(dir.PathOf("media/1.ogg"), e.FullPath);
                PaAssert.Equal("li", e.Scope);
                PaAssert.Near(1.0, e.Duration);
                PaAssert.Equal("czech", m.Find(3, false, "czech").Scope);
                PaAssert.Same(e, m.Find(1, false, "english_uk"), "tables are loaded once");
            }
        }

        [PaTest]
        public static void FindExternalStripsFolderAndExtension()
        {
            using (PaTempDir dir = Fixture())
            {
                PaMediaIndex m = Index(dir);
                string expected = "lang/english_uk/ext/en-GB_TY2225.ogg";
                PaAssert.Equal(expected, m.FindExternal("English(UK)\\en-GB_TY2225.wem", "english_uk").File);
                PaAssert.Equal(expected, m.FindExternal("English(UK)/en-GB_TY2225.wem", "english_uk").File);
                PaAssert.Equal(expected, m.FindExternal("en-GB_TY2225.wem", "english_uk").File);
                PaAssert.Equal(expected, m.FindExternal("en-GB_TY2225", "english_uk").File);
                PaAssert.Equal(expected, m.FindExternal("EN-gb_ty2225.WEM", "english_uk").File, "case-insensitive");
                PaAssert.Equal(expected, m.FindExternal("en-GB_TY2225", null).File, "default language");
                PaAssert.Equal("lang/english_uk/ext/en-GB_NoName.ogg", m.FindExternal("en-GB_NoName.wem", "english_uk").File,
                    "an entry without a name is keyed by its file stem");
                PaAssert.Equal(dir.PathOf(expected), m.FindExternal("en-GB_TY2225", "english_uk").FullPath);
                PaAssert.Null(m.FindExternal("en-GB_TY9999.wem", "english_uk"));
                PaAssert.Null(m.FindExternal("", "english_uk"));
                PaAssert.Null(m.FindExternal(null, "english_uk"));
            }
        }

        [PaTest]
        public static void FindExternalSwapsTheLocalePrefix()
        {
            using (PaTempDir dir = Fixture())
            {
                PaMediaIndex m = Index(dir);
                // The game asks for Czech voice; the voice is served from English after fallback.
                PaAssert.Equal("lang/english_uk/ext/en-GB_TY7.ogg", m.FindExternal("Czech\\cs-CZ_TY7.wem", "english_uk").File);
                PaAssert.Equal("lang/english_uk/ext/en-GB_TY7.ogg", m.FindExternal("German(Germany)\\de-DE_TY7.wem", "english_uk").File);
                PaAssert.Equal("lang/czech/ext/cs-CZ_TY1.ogg", m.FindExternal("English(UK)\\en-GB_TY1.wem", "czech").File);
                PaAssert.Null(m.FindExternal("cs-CZ_TY1.wem", "french_france"), "no table for that language");
                PaAssert.Null(m.FindExternal("TY7.wem", "czech"), "no prefix, not in the table");
            }
        }

        [PaTest]
        public static void ExternalPrefixAndFolder()
        {
            using (PaTempDir dir = Fixture())
            {
                PaMediaIndex m = Index(dir);
                PaAssert.Equal("en-GB_", m.ExternalPrefix("english_uk"));
                PaAssert.Equal("cs-CZ_", m.ExternalPrefix("czech"));
                PaAssert.Equal("en-GB_", m.ExternalPrefix(null));
                PaAssert.Equal("", m.ExternalPrefix("french_france"));
            }
            PaAssert.Equal("English(UK)", PaMediaIndex.LanguageFolderOf("English(UK)\\en-GB_TY1.wem"));
            PaAssert.Equal("English(UK)", PaMediaIndex.LanguageFolderOf("English(UK)/en-GB_TY1.wem"));
            PaAssert.Equal("Czech", PaMediaIndex.LanguageFolderOf("Audio\\Czech\\cs-CZ_TY1.wem"));
            PaAssert.Null(PaMediaIndex.LanguageFolderOf("en-GB_TY1.wem"));
            PaAssert.Null(PaMediaIndex.LanguageFolderOf("\\en-GB_TY1.wem"));
            PaAssert.Null(PaMediaIndex.LanguageFolderOf(""));
            PaAssert.Null(PaMediaIndex.LanguageFolderOf(null));
        }

        [PaTest]
        public static void TablesLoadLazily()
        {
            using (PaTempDir dir = Fixture())
            {
                PaMediaIndex m = Index(dir);
                // Created before any lookup: a table changed now is what the first lookup sees.
                dir.Write("lang/czech/media.json", J("{'scope':'czech','media':[{'id':55,'file':'lang/czech/55.ogg'}]}"));
                PaAssert.NotNull(m.Find(55, true, "czech"));
                File.Delete(dir.PathOf("lang/czech/media.json"));
                PaAssert.NotNull(m.Find(55, true, "czech"), "loaded once, kept");
            }
        }

        [PaTest]
        public static void MissingOrBrokenTablesAreEmpty()
        {
            using (var dir = new PaTempDir())
            {
                dir.Write("lang/czech/media.json", "{\"media\":[");
                var m = new PaMediaIndex(dir.Root, null);
                PaAssert.Null(m.Find(1, false, "czech"));
                PaAssert.Null(m.FindExternal("cs-CZ_TY1", "czech"));
                PaAssert.Null(m.Find(1, true, null), "no manifest, no key: li only");
                PaAssert.Null(m.FindExternal("cs-CZ_TY1", null));
                PaAssert.Equal("", m.ExternalPrefix(null));
            }
        }

        [PaTest]
        public static void ConcurrentLookups()
        {
            using (PaTempDir dir = Fixture())
            {
                PaMediaIndex m = Index(dir);
                int failures = 0;
                var threads = new List<Thread>();
                for (int t = 0; t < 8; t++)
                {
                    int seed = t;
                    var th = new Thread(() =>
                    {
                        for (int i = 0; i < 200; i++)
                        {
                            string key = (i + seed) % 2 == 0 ? "czech" : "english_uk";
                            if (m.Find(3, true, key) == null || m.Find(1, false, key) == null)
                                Interlocked.Increment(ref failures);
                            if (key == "english_uk" && m.FindExternal("cs-CZ_TY7.wem", key) == null)
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
        public static void ManifestLoader()
        {
            using (var dir = new PaTempDir())
            {
                PaAssert.Null(PaManifestLoader.Load(dir.Root), "missing manifest");
                PaAssert.Null(PaManifestLoader.Load(null));
                dir.Write("manifest.json", "{\"banks\":");
                PaAssert.Null(PaManifestLoader.Load(dir.Root), "broken manifest");
                dir.Write("manifest.json", PaModelReaderTests.Manifest);
                PaManifest m = PaManifestLoader.Load(dir.Root);
                PaAssert.NotNull(m);
                PaAssert.Equal(2, m.Banks.Count);
            }
        }
    }
}
