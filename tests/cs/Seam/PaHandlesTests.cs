using System;
using System.Collections.Generic;
using System.Threading;

namespace PortAudio.Tests
{
    public static class PaHandlesTests
    {
        [PaTest]
        public static void HandlesAreDistinctAndNonzero()
        {
            IntPtr a = PaHandles.New();
            IntPtr b = PaHandles.New();
            PaAssert.True(a != IntPtr.Zero && b != IntPtr.Zero);
            PaAssert.True(a != b);
            PaAssert.Equal(16L, b.ToInt64() - a.ToInt64(), "handles step by 16 as before");
        }

        [PaTest]
        public static void FieldsRoundTripPerHandle()
        {
            IntPtr a = PaHandles.New();
            IntPtr b = PaHandles.New();
            PaHandles.Set(a, "AkExternalSourceInfo_szFile", "English(UK)\\en-GB_TY1.wem");
            PaHandles.Set(a, "AkExternalSourceInfo_iExternalSrcCookie", 123u);
            PaHandles.Set(b, "AkExternalSourceInfo_iExternalSrcCookie", 456u);
            PaHandles.Set(a, "AkVector_X", 1.5f);
            PaAssert.Equal("English(UK)\\en-GB_TY1.wem", PaHandles.Get<string>(a, "AkExternalSourceInfo_szFile"));
            PaAssert.Equal(123u, PaHandles.Get<uint>(a, "AkExternalSourceInfo_iExternalSrcCookie"));
            PaAssert.Equal(456u, PaHandles.Get<uint>(b, "AkExternalSourceInfo_iExternalSrcCookie"));
            PaAssert.Near(1.5, PaHandles.Get<float>(a, "AkVector_X"));
            PaAssert.Equal(0u, PaHandles.Get<uint>(a, "AkVector_X"), "wrong type reads the default");
            PaAssert.Null(PaHandles.Get<string>(b, "AkExternalSourceInfo_szFile"));
            PaHandles.Set(a, "AkExternalSourceInfo_iExternalSrcCookie", 789u);
            PaAssert.Equal(789u, PaHandles.Get<uint>(a, "AkExternalSourceInfo_iExternalSrcCookie"), "last write wins");
            PaHandles.Release(a);
            PaAssert.Equal(0u, PaHandles.Get<uint>(a, "AkExternalSourceInfo_iExternalSrcCookie"), "released handles forget");
            PaAssert.Equal(456u, PaHandles.Get<uint>(b, "AkExternalSourceInfo_iExternalSrcCookie"));
        }

        [PaTest]
        public static void ZeroHandleIsIgnored()
        {
            PaHandles.Set(IntPtr.Zero, "f", 5);
            PaAssert.Equal(0, PaHandles.Get<int>(IntPtr.Zero, "f"));
            PaHandles.Release(IntPtr.Zero);
            PaAssert.Equal(IntPtr.Zero, PaHandles.Member(IntPtr.Zero, "f"));
        }

        [PaTest]
        public static void MemberCreatesAStableHandle()
        {
            IntPtr pos = PaHandles.New();
            IntPtr m1 = PaHandles.Member(pos, "AkSoundPosition_Position");
            IntPtr m2 = PaHandles.Member(pos, "AkSoundPosition_Position");
            PaAssert.True(m1 != IntPtr.Zero);
            PaAssert.Equal(m1, m2);
            PaHandles.Set(m1, "AkVector_Z", 3f);
            PaAssert.Near(3, PaHandles.Get<float>(PaHandles.Member(pos, "AkSoundPosition_Position"), "AkVector_Z"));
            PaAssert.True(PaHandles.Member(pos, "AkSoundPosition_Orientation") != m1);
        }

        [PaTest]
        public static void NewIdIsNonzeroAndUnique()
        {
            var seen = new HashSet<uint>();
            for (int i = 0; i < 1000; i++)
            {
                uint id = PaHandles.NewId();
                PaAssert.True(id != 0);
                PaAssert.True(seen.Add(id), "unique");
            }
        }

        [PaTest]
        public static void ThreadSafe()
        {
            int failures = 0;
            var threads = new List<Thread>();
            for (int t = 0; t < 8; t++)
            {
                var th = new Thread(() =>
                {
                    for (int i = 0; i < 2000; i++)
                    {
                        IntPtr h = PaHandles.New();
                        PaHandles.Set(h, "v", i);
                        if (PaHandles.Get<int>(h, "v") != i)
                            Interlocked.Increment(ref failures);
                        PaHandles.Release(h);
                    }
                });
                threads.Add(th);
                th.Start();
            }
            foreach (Thread th in threads)
                th.Join();
            PaAssert.Equal(0, failures);
        }

        [PaTest]
        public static void ChannelMasks()
        {
            PaAssert.Equal(0u, PaHandles.CountBits(0));
            PaAssert.Equal(2u, PaHandles.CountBits(0x3));
            PaAssert.Equal(6u, PaHandles.CountBits(0x3F));
            PaAssert.Equal(32u, PaHandles.CountBits(0xFFFFFFFFu));
            PaAssert.Equal(0u, PaHandles.MaskFromChannelCount(0));
            PaAssert.Equal(0x4u, PaHandles.MaskFromChannelCount(1));
            PaAssert.Equal(0x3u, PaHandles.MaskFromChannelCount(2));
            PaAssert.Equal(0x3Fu, PaHandles.MaskFromChannelCount(6));
            PaAssert.Equal(0xFFFFFFFFu, PaHandles.MaskFromChannelCount(32));
            PaAssert.Equal(0xFFFFFFFFu, PaHandles.MaskFromChannelCount(40));
        }
    }
}
