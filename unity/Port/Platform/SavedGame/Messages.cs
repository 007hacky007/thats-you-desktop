// PC stand-in for the message types of Sony's PS4 MemorySave plugin.
//
// The PS4 plugin posted log and status messages from native code into a
// queue that the title drained once per frame. This port keeps the same
// public shape (names, enum values, delegate and struct members) but the
// queue is a plain managed one filled by PortSaveStore. Written for this
// port; no native library is involved.

using System;
using System.Collections.Generic;

namespace Sony.PS4.SavedGame
{
    public class Messages
    {
        public enum MessageType
        {
            kMemorySave_NotSet = 0,
            kMemorySave_Log = 1,
            kMemorySave_LogWarning = 2,
            kMemorySave_LogError = 3,
            kMemorySave_Initialised = 4,
            kMemorySave_InitialiseFailed = 5,
        }

        public delegate void EventHandler(PluginMessage msg);

        public struct PluginMessage
        {
            public MessageType type;

            // Kept for API compatibility. The port never hands out native
            // memory, so data is always IntPtr.Zero; text travels in a
            // managed field instead.
            public int dataSize;

            public IntPtr data;

            internal string text;

            public string Text
            {
                get
                {
                    bool isLog = type == MessageType.kMemorySave_Log
                        || type == MessageType.kMemorySave_LogWarning
                        || type == MessageType.kMemorySave_LogError;
                    if (isLog && text != null)
                    {
                        return text;
                    }
                    return "no text";
                }
            }
        }

        private static readonly Queue<PluginMessage> queue = new Queue<PluginMessage>();

        private static readonly object queueLock = new object();

        public static bool HasMessage()
        {
            lock (queueLock)
            {
                return queue.Count > 0;
            }
        }

        public static void RemoveFirstMessage()
        {
            lock (queueLock)
            {
                if (queue.Count > 0)
                {
                    queue.Dequeue();
                }
            }
        }

        public static void GetFirstMessage(out PluginMessage msg)
        {
            lock (queueLock)
            {
                msg = queue.Count > 0 ? queue.Peek() : default(PluginMessage);
            }
        }

        // Port-only: queue a message for delivery on the next Main.Update.
        internal static void Post(MessageType type, string text = null)
        {
            PluginMessage msg = new PluginMessage
            {
                type = type,
                dataSize = text != null ? text.Length : 0,
                data = IntPtr.Zero,
                text = text,
            };
            lock (queueLock)
            {
                queue.Enqueue(msg);
            }
        }

        internal static void Clear()
        {
            lock (queueLock)
            {
                queue.Clear();
            }
        }
    }
}
