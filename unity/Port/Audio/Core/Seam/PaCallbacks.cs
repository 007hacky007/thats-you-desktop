// Callback queue and serializer for the Wwise C# integration (audio architecture 6.4 and 7.8;
// moved out of PortAudioNull with the same record layout).
//
// The integration hands the engine a block of memory (AkCallbackSerializer.Init) and, once per
// frame on the main thread (AkInitializer.LateUpdate -> AkCallbackManager.PostCallbacks), locks it
// and walks a linked list of records written there. Notifications are queued from any thread;
// Lock() writes as many as fit, in queue order, and keeps the rest for the next frame.
//
// Record layout (Pack = 4, pointers of IntPtr.Size, each record starting on an 8-byte boundary):
//   header:       pPackage (pointer) = cookie, pNext (pointer), eType (int32)
//   end of event: pCookie (pointer), gameObjID (pointer), playingID (uint32), eventID (uint32)
//   bank:         bankID (uint32), inMemoryBankPtr (pointer) = 0, eLoadResult (int32), memPoolId (int32) = -1

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PortAudio
{
    public static class PaCallbacks
    {
        const int EndOfEventType = 1;           // AkCallbackType.AK_EndOfEvent
        const int BankType = 0x40000000;        // AkCallbackType.AK_Bank
        const int InvalidPoolId = -1;           // AK_INVALID_POOL_ID

        struct Notification
        {
            public int kind;
            public IntPtr cookie;
            public ulong gameObject;
            public uint playingId;
            public uint eventId;
            public uint bankId;
            public int result;
        }

        static readonly object sync = new object();
        static readonly List<Notification> pending = new List<Notification>();
        static IntPtr buffer;
        static int bufferSize;

        public static int SerializerInit(IntPtr memory, uint size)
        {
            lock (sync)
            {
                buffer = memory;
                bufferSize = (int)Math.Min(size, (uint)int.MaxValue);
            }
            return PaResult.Success;
        }

        public static void SerializerTerm()
        {
            lock (sync)
            {
                buffer = IntPtr.Zero;
                bufferSize = 0;
                pending.Clear();
            }
        }

        public static void QueueEndOfEvent(IntPtr cookie, ulong gameObject, uint playingId, uint eventId)
        {
            lock (sync)
                pending.Add(new Notification { kind = EndOfEventType, cookie = cookie, gameObject = gameObject, playingId = playingId, eventId = eventId });
        }

        public static void QueueBank(IntPtr cookie, uint bankId, int result)
        {
            lock (sync)
                pending.Add(new Notification { kind = BankType, cookie = cookie, bankId = bankId, result = result });
        }

        // Drops every queued record for that cookie. In-flight instances that would queue later are
        // the engine's business (it forgets their cookie).
        public static void CancelCookie(IntPtr cookie)
        {
            if (cookie == IntPtr.Zero)
                return;
            lock (sync)
                pending.RemoveAll(n => n.cookie == cookie);
        }

        public static int PendingCount
        {
            get { lock (sync) return pending.Count; }
        }

        // Writes as many queued notifications as fit into the serializer memory and returns the
        // first record, or IntPtr.Zero when nothing is queued (or no memory was given).
        public static IntPtr Lock()
        {
            lock (sync)
            {
                if (pending.Count == 0 || buffer == IntPtr.Zero)
                    return IntPtr.Zero;

                int ptr = IntPtr.Size;
                int header = ptr * 2 + 4;
                int offset = 0;
                int written = 0;
                int previous = -1;
                foreach (Notification n in pending)
                {
                    int size = header + (n.kind == EndOfEventType ? ptr * 2 + 8 : ptr + 12);
                    if (offset + size > bufferSize)
                        break;
                    IntPtr record = Offset(offset);
                    if (previous >= 0)
                        Marshal.WriteIntPtr(Offset(previous), ptr, record);

                    Marshal.WriteIntPtr(record, 0, n.cookie);
                    Marshal.WriteIntPtr(record, ptr, IntPtr.Zero);
                    Marshal.WriteInt32(record, ptr * 2, n.kind);
                    int p = header;
                    if (n.kind == EndOfEventType)
                    {
                        Marshal.WriteIntPtr(record, p, n.cookie);
                        Marshal.WriteIntPtr(record, p + ptr, new IntPtr(unchecked((long)n.gameObject)));
                        Marshal.WriteInt32(record, p + ptr * 2, unchecked((int)n.playingId));
                        Marshal.WriteInt32(record, p + ptr * 2 + 4, unchecked((int)n.eventId));
                    }
                    else
                    {
                        Marshal.WriteInt32(record, p, unchecked((int)n.bankId));
                        Marshal.WriteIntPtr(record, p + 4, IntPtr.Zero);
                        Marshal.WriteInt32(record, p + 4 + ptr, n.result);
                        Marshal.WriteInt32(record, p + 8 + ptr, InvalidPoolId);
                    }
                    previous = offset;
                    offset += (size + 7) & ~7;
                    written++;
                }
                pending.RemoveRange(0, written);
                return written > 0 ? buffer : IntPtr.Zero;
            }
        }

        public static void Unlock()
        {
        }

        static IntPtr Offset(int bytes)
        {
            return new IntPtr(buffer.ToInt64() + bytes);
        }
    }
}
