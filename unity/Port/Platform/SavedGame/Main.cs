// PC stand-in for Sony.PS4.SavedGame.Main from the PS4 MemorySave plugin.
//
// Same public members as the PS4 API: the three log events, Initialise,
// Terminate and a per-frame Update that delivers queued messages on the
// main thread. Bodies are written for this port and call no native code.

namespace Sony.PS4.SavedGame
{
    public class Main
    {
        public static event Messages.EventHandler OnLog;

        public static event Messages.EventHandler OnLogWarning;

        public static event Messages.EventHandler OnLogError;

        public static void Initialise()
        {
            MemorySaveLoad.Initialise();
        }

        public static void Terminate()
        {
            MemorySaveLoad.Terminate();
            Messages.Clear();
        }

        // Called every frame by the game. Drains the message queue and
        // raises each message's event on the calling (main) thread.
        public static void Update()
        {
            // Bound the loop so a handler that posts new messages cannot
            // keep this frame spinning forever; leftovers go next frame.
            for (int i = 0; i < 64 && Messages.HasMessage(); i++)
            {
                Messages.PluginMessage msg;
                Messages.GetFirstMessage(out msg);
                Messages.RemoveFirstMessage();
                Dispatch(msg);
            }
        }

        private static void Dispatch(Messages.PluginMessage msg)
        {
            Messages.EventHandler handler = null;
            switch (msg.type)
            {
                case Messages.MessageType.kMemorySave_Log:
                    handler = OnLog;
                    break;
                case Messages.MessageType.kMemorySave_LogWarning:
                    handler = OnLogWarning;
                    break;
                case Messages.MessageType.kMemorySave_LogError:
                    handler = OnLogError;
                    break;
                default:
                    MemorySaveLoad.ProcessMessage(msg);
                    return;
            }
            if (handler != null)
            {
                handler(msg);
            }
        }
    }
}
