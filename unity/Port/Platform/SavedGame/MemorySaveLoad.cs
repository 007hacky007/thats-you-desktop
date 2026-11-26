// PC stand-in for Sony.PS4.SavedGame.MemorySaveLoad from the PS4 MemorySave
// plugin.
//
// The PS4 plugin kept one fixed-size memory block mirrored to a console save
// slot. Here the block is mirrored to a file under
// Application.persistentDataPath (see PortSaveStore). Public names and
// signatures match the PS4 API; bodies are written for this port.

namespace Sony.PS4.SavedGame
{
    public class MemorySaveLoad
    {
        public struct SavedGameSlotParams
        {
            public int userId;

            public string titleId;

            public string dirName;

            public string fileName;

            public string newTitle;

            public string title;

            public string subTitle;

            public string detail;

            public string iconPath;

            public long sizeKiB;
        }

        public static bool IsReady
        {
            get { return PortSaveStore.IsReady; }
        }

        public static event Messages.EventHandler OnInitialised;

        // Routes non-log messages posted by the store. Main.Update calls
        // this on the main thread.
        public static void ProcessMessage(Messages.PluginMessage msg)
        {
            if (msg.type == Messages.MessageType.kMemorySave_Initialised)
            {
                Messages.EventHandler handler = OnInitialised;
                if (handler != null)
                {
                    handler(msg);
                }
            }
        }

        public static void Initialise()
        {
        }

        public static void Terminate()
        {
            PortSaveStore.Shutdown();
        }

        // Sets up the backing file. Completion is reported through
        // OnInitialised from the next Main.Update, as on the console.
        public static bool MemorySaveInit(int memorySizeBytes, SavedGameSlotParams slotParams)
        {
            return PortSaveStore.Init(memorySizeBytes, slotParams.fileName, slotParams.dirName);
        }

        // Writes the block to disk. Returns false when the store is not
        // initialised or the write failed.
        public static bool MemorySave(byte[] data)
        {
            return PortSaveStore.Save(data);
        }

        // Fills data from disk. When there is no save yet the block is left
        // all zero, which is what a fresh console slot reads back as, so the
        // game keeps its first-boot defaults. Returns true only when a save
        // file was read.
        public static bool MemoryLoad(byte[] data)
        {
            return PortSaveStore.Load(data);
        }
    }
}
