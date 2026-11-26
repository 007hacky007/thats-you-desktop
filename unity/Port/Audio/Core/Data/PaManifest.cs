// Loads <root>/manifest.json, the index of the converted audio (audio architecture 4.2).

using System;
using System.IO;
using System.Text;

namespace PortAudio
{
    public static class PaManifestLoader
    {
        public const string FileName = "manifest.json";

        // null when <root>/manifest.json is missing. A manifest that cannot be read or parsed is
        // logged as an error and also gives null, so the engine falls back to silent mode instead
        // of failing the game's audio init.
        public static PaManifest Load(string root)
        {
            if (string.IsNullOrEmpty(root))
                return null;
            string path = Path.Combine(root, FileName);
            if (!File.Exists(path))
                return null;
            try
            {
                PaManifest m = PaModelReader.ReadManifest(File.ReadAllText(path, Encoding.UTF8));
                if (m.Format != 1)
                    PaLog.Warn("manifest format " + m.Format + " (expected 1): " + path);
                return m;
            }
            catch (Exception e)
            {
                if (!(e is PaDataException || e is IOException || e is UnauthorizedAccessException))
                    throw;
                PaLog.Error("cannot read " + path + ": " + e.Message);
                return null;
            }
        }
    }
}
