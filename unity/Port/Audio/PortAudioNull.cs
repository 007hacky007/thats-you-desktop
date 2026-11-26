// Silent, managed stand-in for the Wwise native sound engine (the "AkSoundEngine" library).
//
// The PS4 build shipped a native AkSoundEngine module that the Wwise Unity integration reaches
// through SWIG P/Invoke entry points (AkSoundEnginePINVOKE). There is no macOS or Windows build
// of that module for this Wwise version, so every call used to throw DllNotFoundException:
// engine init aborted, bank loads failed and end-of-event callbacks never arrived, which can
// stall flows that wait for a voice line to finish.
//
// This backend keeps the whole C# Wwise API surface working with no audio output:
// - Every engine call succeeds (AKRESULT.AK_Success) and does nothing audible.
// - Native objects (settings structs, vectors, channel configs) are fake opaque handles.
//   Field setters store their value per handle so the matching getter reads it back; delete_*
//   forgets the handle.
// - Posting an event returns a fresh nonzero playing ID. If the post asked for callbacks, an
//   AK_EndOfEvent notification is queued; asynchronous bank, prepare and unload requests with a
//   callback queue an AK_Bank notification. Queued notifications are written into the callback
//   buffer, in the record layout AkCallbackManager.PostCallbacks reads, the next time the
//   integration locks the serializer (AkInitializer.LateUpdate, main thread). So callbacks fire
//   once, one frame after the post, as if every sound had zero length.
// - IDs computed from names use the Wwise short-ID hash (32-bit FNV-1 over the lowercase name),
//   so they match the IDs in the game's generated sound banks.
// - The extra audio-input entry points the game added to its native module (playback of voice
//   clips received from phones) are no-ops that report "not playing".
//
// A real audio backend replaces this class later; AkSoundEnginePINVOKE keeps the original
// entry point names and signatures so it can be pointed at a real implementation.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class PortAudioNull
{
    public const int Success = 1;           // AKRESULT.AK_Success
    public const int InvalidPoolId = -1;    // AK_INVALID_POOL_ID

    const int EndOfEventType = 1;           // AkCallbackType.AK_EndOfEvent
    const int BankType = 0x40000000;        // AkCallbackType.AK_Bank

    static readonly object sync = new object();

    // ---- engine state ----

    static bool initialized;

    public static bool IsInitialized
    {
        get { lock (sync) return initialized; }
    }

    public static int Init()
    {
        lock (sync) initialized = true;
        return Success;
    }

    public static void Term()
    {
        lock (sync)
        {
            initialized = false;
            pending.Clear();
            eventOfPlaying.Clear();
        }
    }

    // ---- fake native objects ----

    static long nextHandle = 0x10000;
    static readonly Dictionary<IntPtr, Dictionary<string, object>> fields = new Dictionary<IntPtr, Dictionary<string, object>>();

    public static IntPtr NewHandle()
    {
        lock (sync)
        {
            nextHandle += 16;
            return new IntPtr(nextHandle);
        }
    }

    public static void Release(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;
        lock (sync) fields.Remove(handle);
    }

    public static void SetField<T>(IntPtr handle, string name, T value)
    {
        if (handle == IntPtr.Zero)
            return;
        lock (sync)
        {
            Dictionary<string, object> map;
            if (!fields.TryGetValue(handle, out map))
            {
                map = new Dictionary<string, object>();
                fields[handle] = map;
            }
            map[name] = value;
        }
    }

    public static T GetField<T>(IntPtr handle, string name)
    {
        lock (sync)
        {
            Dictionary<string, object> map;
            object value;
            if (handle != IntPtr.Zero && fields.TryGetValue(handle, out map) && map.TryGetValue(name, out value) && value is T)
                return (T)value;
        }
        return default(T);
    }

    // Embedded struct member (for example AkSoundPosition.Position): returns the stored handle,
    // creating one on first access so the proxy getter never hands back null.
    public static IntPtr GetMember(IntPtr handle, string name)
    {
        IntPtr member = GetField<IntPtr>(handle, name);
        if (member == IntPtr.Zero && handle != IntPtr.Zero)
        {
            member = NewHandle();
            SetField(handle, name, member);
        }
        return member;
    }

    // ---- IDs ----

    public static uint HashName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return 0;
        uint hash = 2166136261u;
        byte[] bytes = Encoding.UTF8.GetBytes(name.ToLowerInvariant());
        for (int i = 0; i < bytes.Length; i++)
        {
            hash *= 16777619u;
            hash ^= bytes[i];
        }
        return hash;
    }

    public static uint BankIdFromName(string name)
    {
        if (name != null && name.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);
        uint id = HashName(name);
        return id != 0 ? id : NewId();
    }

    static uint nextId = 1000;

    public static uint NewId()
    {
        lock (sync)
        {
            nextId++;
            if (nextId == 0)
                nextId = 1;
            return nextId;
        }
    }

    static readonly Dictionary<uint, uint> eventOfPlaying = new Dictionary<uint, uint>();

    public static uint EventIdFromPlayingId(uint playingId)
    {
        lock (sync)
        {
            uint eventId;
            return eventOfPlaying.TryGetValue(playingId, out eventId) ? eventId : 0u;
        }
    }

    // ---- events ----

    public static uint PostEvent(uint eventId, ulong gameObject, uint flags, IntPtr callback, IntPtr cookie, uint requestedPlayingId)
    {
        uint playingId = requestedPlayingId != 0 ? requestedPlayingId : NewId();
        lock (sync)
        {
            if (eventOfPlaying.Count > 4096)
                eventOfPlaying.Clear();
            eventOfPlaying[playingId] = eventId;
            if (flags != 0 && callback != IntPtr.Zero && cookie != IntPtr.Zero)
                pending.Add(new Notification { kind = EndOfEventType, cookie = cookie, gameObject = gameObject, playingId = playingId, eventId = eventId });
        }
        return playingId;
    }

    public static uint PostEvent(string eventName, ulong gameObject, uint flags, IntPtr callback, IntPtr cookie, uint requestedPlayingId)
    {
        return PostEvent(HashName(eventName), gameObject, flags, callback, cookie, requestedPlayingId);
    }

    // ---- banks ----

    public static int BankRequest(uint bankId, IntPtr cookie)
    {
        if (cookie != IntPtr.Zero)
        {
            lock (sync)
                pending.Add(new Notification { kind = BankType, cookie = cookie, bankId = bankId });
        }
        return Success;
    }

    // ---- callback serializer ----

    struct Notification
    {
        public int kind;
        public IntPtr cookie;
        public ulong gameObject;
        public uint playingId;
        public uint eventId;
        public uint bankId;
    }

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
        return Success;
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

    // Writes as many queued notifications as fit into the serializer memory and returns the
    // first record, or IntPtr.Zero when nothing is queued. Record layout (Pack = 4):
    //   header:      pPackage (pointer), pNext (pointer), eType (int32)
    //   end of event: pCookie (pointer), gameObjID (pointer), playingID (uint32), eventID (uint32)
    //   bank:        bankID (uint32), inMemoryBankPtr (pointer), eLoadResult (int32), memPoolId (uint32)
    public static IntPtr LockCallbacks()
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
                    Marshal.WriteInt32(record, p + 4 + ptr, Success);
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

    public static void UnlockCallbacks()
    {
    }

    static IntPtr Offset(int bytes)
    {
        return new IntPtr(buffer.ToInt64() + bytes);
    }

    // ---- helpers for channel masks ----

    public static uint CountBits(uint mask)
    {
        uint count = 0;
        while (mask != 0)
        {
            mask &= mask - 1;
            count++;
        }
        return count;
    }

    public static uint MaskFromChannelCount(uint channels)
    {
        if (channels == 0)
            return 0;
        if (channels == 1)
            return 0x4;     // center
        if (channels >= 32)
            return 0xFFFFFFFFu;
        return (1u << (int)channels) - 1;
    }

    // ---- audio input playback (game-specific additions to the native module) ----

    public static void SetWishAudioInputCallbacks()
    {
    }

    public static bool GetIsAudioInputPlayingSample()
    {
        return false;
    }

    public static bool PlayAudioInputSampleData(byte[] data, int dataSizeBytes)
    {
        return true;
    }

    public static int GetAudioInputSamplePosition()
    {
        return 0;
    }

    public static void StopAudioInputPlaying()
    {
    }
}
