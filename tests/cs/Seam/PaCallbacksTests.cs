using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace PortAudio.Tests
{
    public static class PaCallbacksTests
    {
        // The layout AkCallbackManager.PostCallbacks reads, as the previous PortAudioNull.LockCallbacks
        // wrote it (Pack = 4, records on 8-byte boundaries, padding left untouched):
        //   header       cookie (ptr) | next (ptr) | type (int32)
        //   end of event cookie (ptr) | gameObject (ptr) | playingId (u32) | eventId (u32)
        //   bank         bankId (u32) | inMemoryBankPtr (ptr, 0) | loadResult (int32) | memPoolId (int32, -1)
        sealed class Expected
        {
            readonly byte[] bytes;
            readonly int ptr = IntPtr.Size;
            int offset;
            int previous = -1;

            public Expected(int size, byte fill)
            {
                bytes = new byte[size];
                for (int i = 0; i < size; i++)
                    bytes[i] = fill;
            }

            public byte[] Bytes
            {
                get { return bytes; }
            }

            void Ptr(int at, long v)
            {
                byte[] b = ptr == 8 ? BitConverter.GetBytes(v) : BitConverter.GetBytes((int)v);
                Array.Copy(b, 0, bytes, at, b.Length);
            }

            void I32(int at, int v)
            {
                Array.Copy(BitConverter.GetBytes(v), 0, bytes, at, 4);
            }

            int Begin(long cookie, int type, int payload, long bufferBase)
            {
                int at = offset;
                if (previous >= 0)
                    Ptr(previous + ptr, bufferBase + at);
                Ptr(at, cookie);
                Ptr(at + ptr, 0);
                I32(at + ptr * 2, type);
                previous = at;
                offset += (ptr * 2 + 4 + payload + 7) & ~7;
                return at + ptr * 2 + 4;
            }

            public void EndOfEvent(long cookie, ulong go, uint playingId, uint eventId, long bufferBase)
            {
                int p = Begin(cookie, 1, ptr * 2 + 8, bufferBase);
                Ptr(p, cookie);
                Ptr(p + ptr, unchecked((long)go));
                I32(p + ptr * 2, unchecked((int)playingId));
                I32(p + ptr * 2 + 4, unchecked((int)eventId));
            }

            public void Bank(long cookie, uint bankId, int result, long bufferBase)
            {
                int p = Begin(cookie, 0x40000000, ptr + 12, bufferBase);
                I32(p, unchecked((int)bankId));
                Ptr(p + 4, 0);
                I32(p + 4 + ptr, result);
                I32(p + 8 + ptr, -1);
            }
        }

        sealed class Buffer : IDisposable
        {
            public readonly IntPtr Ptr;
            public readonly int Size;

            public Buffer(int size)
            {
                Size = size;
                Ptr = Marshal.AllocHGlobal(size);
                Fill(0xCD);
            }

            public void Fill(byte b)
            {
                for (int i = 0; i < Size; i++)
                    Marshal.WriteByte(Ptr, i, b);
            }

            public byte[] Read()
            {
                var b = new byte[Size];
                Marshal.Copy(Ptr, b, 0, Size);
                return b;
            }

            public void Dispose()
            {
                Marshal.FreeHGlobal(Ptr);
            }
        }

        static void Reset()
        {
            PaCallbacks.SerializerTerm();
        }

        [PaTest]
        public static void RecordLayoutIsByteForByteTheOldOne()
        {
            Reset();
            using (var buf = new Buffer(512))
            {
                PaAssert.Equal(PaResult.Success, PaCallbacks.SerializerInit(buf.Ptr, (uint)buf.Size));
                PaCallbacks.QueueEndOfEvent(new IntPtr(0x1111), ulong.MaxValue, 7, 0x89ABCDEF);
                PaCallbacks.QueueBank(new IntPtr(0x2222), 0x12345678, PaResult.Success);
                PaCallbacks.QueueEndOfEvent(new IntPtr(0x3333), 5, 8, 9);
                PaCallbacks.QueueBank(new IntPtr(0x4444), 2713970726u, PaResult.FileNotFound);
                PaAssert.Equal(4, PaCallbacks.PendingCount);

                IntPtr first = PaCallbacks.Lock();
                PaAssert.Equal(buf.Ptr, first, "the first record is at the start of the buffer");
                PaCallbacks.Unlock();
                PaAssert.Equal(0, PaCallbacks.PendingCount);

                long b = buf.Ptr.ToInt64();
                var e = new Expected(buf.Size, 0xCD);
                e.EndOfEvent(0x1111, ulong.MaxValue, 7, 0x89ABCDEF, b);
                e.Bank(0x2222, 0x12345678, PaResult.Success, b);
                e.EndOfEvent(0x3333, 5, 8, 9, b);
                e.Bank(0x4444, 2713970726u, PaResult.FileNotFound, b);
                byte[] got = buf.Read();
                for (int i = 0; i < got.Length; i++)
                    PaAssert.Equal(e.Bytes[i], got[i], "byte " + i);
                if (IntPtr.Size == 8)
                {
                    // Spelled out for 64-bit: record sizes 44 -> 48 and 40 -> 40.
                    PaAssert.Equal(48L, Marshal.ReadIntPtr(buf.Ptr, 8).ToInt64() - b, "next of record 1");
                    PaAssert.Equal(88L, Marshal.ReadIntPtr(buf.Ptr, 48 + 8).ToInt64() - b, "next of record 2");
                    PaAssert.Equal(136L, Marshal.ReadIntPtr(buf.Ptr, 88 + 8).ToInt64() - b, "next of record 3");
                    PaAssert.Equal(IntPtr.Zero, Marshal.ReadIntPtr(buf.Ptr, 136 + 8), "last record ends the list");
                    PaAssert.Equal(0x40000000, Marshal.ReadInt32(buf.Ptr, 136 + 16));
                    PaAssert.Equal(66, Marshal.ReadInt32(buf.Ptr, 136 + 20 + 12), "load result");
                    PaAssert.Equal(-1, Marshal.ReadInt32(buf.Ptr, 136 + 20 + 16), "memPoolId");
                }
            }
            Reset();
        }

        [PaTest]
        public static void OverflowWaitsForTheNextLock()
        {
            Reset();
            int ptr = IntPtr.Size;
            int eoe = (ptr * 2 + 4 + ptr * 2 + 8 + 7) & ~7;
            int bank = (ptr * 2 + 4 + ptr + 12 + 7) & ~7;
            using (var buf = new Buffer(eoe + bank + 8))
            {
                PaCallbacks.SerializerInit(buf.Ptr, (uint)buf.Size);
                PaCallbacks.QueueEndOfEvent(new IntPtr(1), 1, 1, 1);
                PaCallbacks.QueueBank(new IntPtr(2), 2, PaResult.Success);
                PaCallbacks.QueueEndOfEvent(new IntPtr(3), 3, 3, 3);
                PaAssert.Equal(buf.Ptr, PaCallbacks.Lock());
                PaAssert.Equal(1, PaCallbacks.PendingCount, "the third record did not fit");
                PaAssert.Equal(IntPtr.Zero, Marshal.ReadIntPtr(buf.Ptr, eoe + ptr), "the list ends at the last written record");

                buf.Fill(0);
                PaAssert.Equal(buf.Ptr, PaCallbacks.Lock());
                PaAssert.Equal(0, PaCallbacks.PendingCount);
                PaAssert.Equal(new IntPtr(3), Marshal.ReadIntPtr(buf.Ptr, 0), "written at the start next time");
                PaAssert.Equal(IntPtr.Zero, PaCallbacks.Lock(), "nothing left");
            }
            Reset();
        }

        [PaTest]
        public static void NoBufferKeepsTheQueue()
        {
            Reset();
            PaCallbacks.QueueEndOfEvent(new IntPtr(1), 1, 1, 1);
            PaAssert.Equal(IntPtr.Zero, PaCallbacks.Lock());
            PaAssert.Equal(1, PaCallbacks.PendingCount);
            using (var tiny = new Buffer(8))
            {
                PaCallbacks.SerializerInit(tiny.Ptr, (uint)tiny.Size);
                PaAssert.Equal(IntPtr.Zero, PaCallbacks.Lock(), "a record that never fits is not written");
                PaAssert.Equal(1, PaCallbacks.PendingCount);
            }
            PaCallbacks.SerializerTerm();
            PaAssert.Equal(0, PaCallbacks.PendingCount, "term drops the queue");
        }

        [PaTest]
        public static void CancelCookieDropsOnlyThatCookie()
        {
            Reset();
            PaCallbacks.QueueEndOfEvent(new IntPtr(10), 1, 1, 1);
            PaCallbacks.QueueBank(new IntPtr(20), 2, PaResult.Success);
            PaCallbacks.QueueEndOfEvent(new IntPtr(10), 1, 2, 1);
            PaCallbacks.QueueEndOfEvent(new IntPtr(30), 1, 3, 1);
            PaCallbacks.CancelCookie(new IntPtr(10));
            PaCallbacks.CancelCookie(IntPtr.Zero);
            PaAssert.Equal(2, PaCallbacks.PendingCount);
            using (var buf = new Buffer(256))
            {
                PaCallbacks.SerializerInit(buf.Ptr, (uint)buf.Size);
                PaCallbacks.Lock();
                PaAssert.Equal(new IntPtr(20), Marshal.ReadIntPtr(buf.Ptr, 0));
                IntPtr second = Marshal.ReadIntPtr(buf.Ptr, IntPtr.Size);
                PaAssert.Equal(new IntPtr(30), Marshal.ReadIntPtr(second, 0));
            }
            Reset();
        }

        [PaTest]
        public static void QueueingIsThreadSafe()
        {
            Reset();
            var threads = new List<Thread>();
            for (int t = 0; t < 8; t++)
            {
                int id = t;
                var th = new Thread(() =>
                {
                    for (int i = 0; i < 500; i++)
                    {
                        if ((i & 1) == 0)
                            PaCallbacks.QueueEndOfEvent(new IntPtr(id + 1), (ulong)i, (uint)i, 1);
                        else
                            PaCallbacks.QueueBank(new IntPtr(id + 1), (uint)i, PaResult.Success);
                    }
                });
                threads.Add(th);
                th.Start();
            }
            int written = 0;
            using (var buf = new Buffer(4096))
            {
                PaCallbacks.SerializerInit(buf.Ptr, (uint)buf.Size);
                bool running = true;
                while (running || PaCallbacks.PendingCount > 0)
                {
                    running = threads.Exists(x => x.IsAlive);
                    IntPtr rec = PaCallbacks.Lock();
                    while (rec != IntPtr.Zero)
                    {
                        written++;
                        rec = Marshal.ReadIntPtr(rec, IntPtr.Size);
                    }
                }
            }
            foreach (Thread th in threads)
                th.Join();
            PaAssert.Equal(4000, written);
            Reset();
        }
    }
}
