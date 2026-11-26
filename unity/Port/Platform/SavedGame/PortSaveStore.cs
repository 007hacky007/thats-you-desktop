// File backing for the PC MemorySave stand-in.
//
// Stores the game's single save block as one file:
//   <Application.persistentDataPath>/SavedGame[/<dirName>]/<fileName>
// or, when the player is started with "-portSaveDir <dir>", <dir>[/<dirName>]/<fileName>
// (used by pipeline/run_player.py --fresh-save for runs that start from an empty save).
// Trailing zero bytes are not written (the block is mostly empty) and are
// restored on load, so the in-memory block always matches what was saved.
// Writes go to a temporary file first and are then swapped in, so a crash
// mid-save leaves the previous save intact.

using System;
using System.IO;
using UnityEngine;

namespace Sony.PS4.SavedGame
{
    internal static class PortSaveStore
    {
        private const string DefaultFileName = "unitySaveData";

        private const string SaveDirArg = "-portSaveDir";

        private static bool ready;

        private static int blockSize;

        private static string savePath;

        public static bool IsReady
        {
            get { return ready; }
        }

        public static string SavePath
        {
            get { return savePath; }
        }

        public static bool Init(int memorySizeBytes, string fileName, string dirName)
        {
            if (memorySizeBytes <= 0)
            {
                Messages.Post(Messages.MessageType.kMemorySave_LogError, "MemorySaveInit: invalid size " + memorySizeBytes);
                Messages.Post(Messages.MessageType.kMemorySave_InitialiseFailed);
                return false;
            }
            try
            {
                string dir = SaveDirOverride() ?? Path.Combine(Application.persistentDataPath, "SavedGame");
                string sub = SafeName(dirName, null);
                if (sub != null)
                {
                    dir = Path.Combine(dir, sub);
                }
                Directory.CreateDirectory(dir);
                savePath = Path.Combine(dir, SafeName(fileName, DefaultFileName));
                blockSize = memorySizeBytes;
                ready = true;
            }
            catch (Exception e)
            {
                ready = false;
                Messages.Post(Messages.MessageType.kMemorySave_LogError, "MemorySaveInit failed: " + e.Message);
                Messages.Post(Messages.MessageType.kMemorySave_InitialiseFailed);
                return false;
            }
            Messages.Post(Messages.MessageType.kMemorySave_Log, "MemorySave file: " + savePath);
            Messages.Post(Messages.MessageType.kMemorySave_Initialised);
            return true;
        }

        public static void Shutdown()
        {
            ready = false;
        }

        public static bool Save(byte[] data)
        {
            if (!ready || data == null)
            {
                return false;
            }
            int length = Math.Min(data.Length, blockSize);
            while (length > 0 && data[length - 1] == 0)
            {
                length--;
            }
            string tmpPath = savePath + ".tmp";
            try
            {
                using (FileStream fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(data, 0, length);
                    fs.Flush(true);
                }
                if (File.Exists(savePath))
                {
                    try
                    {
                        File.Replace(tmpPath, savePath, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(savePath);
                        File.Move(tmpPath, savePath);
                    }
                }
                else
                {
                    File.Move(tmpPath, savePath);
                }
                return true;
            }
            catch (Exception e)
            {
                Messages.Post(Messages.MessageType.kMemorySave_LogError, "MemorySave failed: " + e.Message);
                TryDelete(tmpPath);
                return false;
            }
        }

        public static bool Load(byte[] data)
        {
            if (data == null)
            {
                return false;
            }
            int limit = ready ? Math.Min(data.Length, blockSize) : data.Length;
            Array.Clear(data, 0, data.Length);
            if (!ready || !File.Exists(savePath))
            {
                return false;
            }
            try
            {
                using (FileStream fs = new FileStream(savePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int total = 0;
                    while (total < limit)
                    {
                        int n = fs.Read(data, total, limit - total);
                        if (n <= 0)
                        {
                            break;
                        }
                        total += n;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Array.Clear(data, 0, data.Length);
                Messages.Post(Messages.MessageType.kMemorySave_LogError, "MemoryLoad failed: " + e.Message);
                return false;
            }
        }

        // The directory given with -portSaveDir, made absolute, or null when the argument is absent.
        private static string SaveDirOverride()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], SaveDirArg, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(args[i + 1]))
                {
                    return Path.GetFullPath(args[i + 1]);
                }
            }
            return null;
        }

        private static string SafeName(string name, string fallback)
        {
            if (string.IsNullOrEmpty(name))
            {
                return fallback;
            }
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            if (name == "." || name == "..")
            {
                return fallback;
            }
            return name;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
