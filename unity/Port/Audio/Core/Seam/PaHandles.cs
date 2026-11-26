// Fake native objects for the SWIG proxies (audio architecture 6.4; moved out of PortAudioNull
// with the same behaviour).
//
// The Wwise C# proxies (AkExternalSourceInfo, AkSoundPosition, init settings, arrays...) own a
// pointer to a native struct and read and write its fields through the P/Invoke table. Here a
// "pointer" is an opaque nonzero handle; field setters store their value per handle and field
// name so the getter reads it back, and delete_* forgets the handle. Thread safe: finalizers
// release handles from the GC thread.

using System;
using System.Collections.Generic;

namespace PortAudio
{
    public static class PaHandles
    {
        static readonly object sync = new object();
        static long nextHandle = 0x10000;
        static uint nextId = 1000;
        static readonly Dictionary<IntPtr, Dictionary<string, object>> fields = new Dictionary<IntPtr, Dictionary<string, object>>();

        public static IntPtr New()
        {
            lock (sync)
            {
                nextHandle += 16;
                return new IntPtr(nextHandle);
            }
        }

        public static void Release(IntPtr h)
        {
            if (h == IntPtr.Zero)
                return;
            lock (sync) fields.Remove(h);
        }

        public static void Set<T>(IntPtr h, string field, T v)
        {
            if (h == IntPtr.Zero)
                return;
            lock (sync)
            {
                Dictionary<string, object> map;
                if (!fields.TryGetValue(h, out map))
                {
                    map = new Dictionary<string, object>();
                    fields[h] = map;
                }
                map[field] = v;
            }
        }

        public static T Get<T>(IntPtr h, string field)
        {
            lock (sync)
            {
                Dictionary<string, object> map;
                object value;
                if (h != IntPtr.Zero && fields.TryGetValue(h, out map) && map.TryGetValue(field, out value) && value is T)
                    return (T)value;
            }
            return default(T);
        }

        // Embedded struct member (for example AkSoundPosition.Position): returns the stored handle,
        // creating one on first access so the proxy getter never hands back null.
        public static IntPtr Member(IntPtr h, string field)
        {
            IntPtr member = Get<IntPtr>(h, field);
            if (member == IntPtr.Zero && h != IntPtr.Zero)
            {
                member = New();
                Set(h, field, member);
            }
            return member;
        }

        // A fresh nonzero id (dynamic sequences and other ids that carry no meaning here).
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
    }
}
