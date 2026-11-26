// Converted media lookup (audio architecture 4.4 and 6.3).
//
// Tables: <root>/media.json for language-independent media ("li") and
// <root>/lang/<languageKey>/media.json per voice language (streamed and embedded localized media
// plus the external voice files). Each table is read the first time it is needed; a missing or
// unreadable table counts as empty (warned once). Thread safe.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PortAudio
{
    public sealed class PaMediaIndex
    {
        sealed class Table
        {
            public string Prefix = "";
            public readonly Dictionary<uint, PaMediaEntry> Media = new Dictionary<uint, PaMediaEntry>();
            public readonly Dictionary<string, PaMediaEntry> External = new Dictionary<string, PaMediaEntry>(StringComparer.OrdinalIgnoreCase);
        }

        const string LiKey = "";

        readonly object sync = new object();
        readonly string root;
        readonly PaManifest manifest;
        readonly Dictionary<string, Table> tables = new Dictionary<string, Table>(StringComparer.OrdinalIgnoreCase);

        public PaMediaIndex(string root, PaManifest manifest)
        {
            this.root = root ?? "";
            this.manifest = manifest;
        }

        // languageSpecific: the language table first, then li; otherwise li first, then the language.
        // An empty languageKey means the manifest default language.
        public PaMediaEntry Find(uint mediaId, bool languageSpecific, string languageKey)
        {
            string lang = KeyOrDefault(languageKey);
            PaMediaEntry e;
            if (languageSpecific)
            {
                if (lang != null && Get(lang).Media.TryGetValue(mediaId, out e))
                    return e;
                return Get(LiKey).Media.TryGetValue(mediaId, out e) ? e : null;
            }
            if (Get(LiKey).Media.TryGetValue(mediaId, out e))
                return e;
            if (lang != null && Get(lang).Media.TryGetValue(mediaId, out e))
                return e;
            return null;
        }

        // requestedFile: "English(UK)\\en-GB_TY1.wem", "en-GB_TY1.wem" or "en-GB_TY1". The folder part
        // and the extension are ignored and matching ignores case. If the name is not in the table,
        // the requested locale prefix (the text up to the first '_') is replaced by the language's
        // ExternalPrefix and the lookup is retried, so a fallback language serves its own files.
        public PaMediaEntry FindExternal(string requestedFile, string languageKey)
        {
            string stem = ExternalStem(requestedFile);
            if (stem.Length == 0)
                return null;
            string lang = KeyOrDefault(languageKey);
            if (lang == null)
                return null;
            Table t = Get(lang);
            PaMediaEntry e;
            if (t.External.TryGetValue(stem, out e))
                return e;
            int us = stem.IndexOf('_');
            if (us >= 0 && t.Prefix.Length > 0)
            {
                string swapped = t.Prefix + stem.Substring(us + 1);
                if (t.External.TryGetValue(swapped, out e))
                    return e;
            }
            return null;
        }

        // The language's external file prefix including the '_' (for example "en-GB_"); "" when unknown.
        public string ExternalPrefix(string languageKey)
        {
            string lang = KeyOrDefault(languageKey);
            return lang == null ? "" : Get(lang).Prefix;
        }

        // The folder part of an external source path ("English(UK)" for "English(UK)\\en-GB_TY1.wem"),
        // or null when the path has no folder.
        public static string LanguageFolderOf(string requestedFile)
        {
            if (string.IsNullOrEmpty(requestedFile))
                return null;
            string s = requestedFile.Replace('\\', '/').Trim('/');
            int slash = s.LastIndexOf('/');
            if (slash <= 0)
                return null;
            string folder = s.Substring(0, slash);
            int inner = folder.LastIndexOf('/');
            if (inner >= 0)
                folder = folder.Substring(inner + 1);
            return folder.Length > 0 ? folder : null;
        }

        static string ExternalStem(string requestedFile)
        {
            if (string.IsNullOrEmpty(requestedFile))
                return "";
            string s = requestedFile.Trim();
            int slash = Math.Max(s.LastIndexOf('/'), s.LastIndexOf('\\'));
            if (slash >= 0)
                s = s.Substring(slash + 1);
            int dot = s.LastIndexOf('.');
            if (dot > 0)
                s = s.Substring(0, dot);
            return s.ToLowerInvariant();
        }

        string KeyOrDefault(string languageKey)
        {
            if (!string.IsNullOrEmpty(languageKey))
                return languageKey;
            PaLanguage def = manifest != null ? manifest.ResolveLanguage(null, null) : null;
            return def != null && !string.IsNullOrEmpty(def.Key) ? def.Key : null;
        }

        Table Get(string key)
        {
            lock (sync)
            {
                Table t;
                if (tables.TryGetValue(key, out t))
                    return t;
                t = Load(key);
                tables[key] = t;
                return t;
            }
        }

        Table Load(string key)
        {
            var t = new Table();
            string rel = key.Length == 0 ? "media.json" : "lang/" + key + "/media.json";
            string path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                PaLog.Warn("media table missing: " + path);
                return t;
            }
            try
            {
                string scope, prefix;
                List<PaMediaEntry> media, external;
                PaModelReader.ReadMediaTable(File.ReadAllText(path, Encoding.UTF8), out scope, out prefix, out media, out external);
                t.Prefix = prefix ?? "";
                foreach (PaMediaEntry e in media)
                {
                    Complete(e);
                    t.Media[e.Id] = e;
                }
                foreach (PaMediaEntry e in external)
                {
                    Complete(e);
                    string name = !string.IsNullOrEmpty(e.Name) ? e.Name : ExternalStem(e.File);
                    if (name.Length > 0)
                        t.External[name.ToLowerInvariant()] = e;
                }
            }
            catch (Exception e)
            {
                if (!(e is PaDataException || e is IOException || e is UnauthorizedAccessException))
                    throw;
                PaLog.Warn("media table unreadable: " + path + ": " + e.Message);
            }
            return t;
        }

        void Complete(PaMediaEntry e)
        {
            if (string.IsNullOrEmpty(e.FullPath) && !string.IsNullOrEmpty(e.File))
                e.FullPath = Path.Combine(root, e.File.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
