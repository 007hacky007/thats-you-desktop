// Playing instances: media lookup and missing media, external voice, music contexts,
// ExecuteActionOnEvent / StopPlayingID / StopAll, instance limits, callback cancellation,
// playing id queries and audio input.

using System;
using System.Collections.Generic;

namespace PortAudio.Tests
{
    public static class PaInstanceTests
    {
        static readonly uint ExternalCookie = PaIds.Hash("External_Voice");

        static EngineFixture Fixture()
        {
            EngineFixture f = new EngineFixture();
            BankBuilder b = f.Bank("Inst");
            b.Sound(100, 0, 500);
            f.MediaLi.Add(Media.Entry(500, 2.0));
            b.Event("Play_A", b.Action(PaActionType.Play, 100));
            b.Sound(103, 0, 503);   // no media entry
            b.Event("Play_NoMedia", b.Action(PaActionType.Play, 103));
            b.Sound(104, 0, 504, "vorbis", true);
            f.MediaByLang["english_uk"] = new List<PaMediaEntry> { Media.Entry(504, 1.0, "lang/english_uk/504.ogg") };
            f.MediaByLang["french_france"] = new List<PaMediaEntry> { Media.Entry(504, 3.0, "lang/french_france/504.ogg") };
            b.Event("Play_Localized", b.Action(PaActionType.Play, 104));
            b.Sound(105, 0, ExternalCookie, "external");
            f.ExternalByLang["english_uk"] = new List<PaMediaEntry>
            {
                new PaMediaEntry { Name = "en-gb_ty2225", File = "lang/english_uk/ext/en-GB_TY2225.ogg", Format = "ogg", Rate = 1000, Channels = 1, Samples = 3000 },
            };
            b.Event("Play_External_Voice", b.Action(PaActionType.Play, 105));
            b.Sound(106, 0, 0, "audioInput");
            b.Event("Play_AudioInput", b.Action(PaActionType.Play, 106));
            b.Node(107, PaNodeType.MusicSwitch, 0);
            b.Event("Play_Music", b.Action(PaActionType.Play, 107));
            PaAction stopMusic = b.Action(PaActionType.Stop, 107);
            stopMusic.TransitionMs = 1000;
            b.Event("Stop_Music", stopMusic);
            return f;
        }

        static void StartWithMusic(EngineFixture f)
        {
            f.Start();
            f.Music.MusicNodes.Add(107);
        }

        static uint PostExternal(string file)
        {
            IntPtr h = PaHandles.New();
            PaHandles.Set(h, "AkExternalSourceInfo_iExternalSrcCookie", ExternalCookie);
            PaHandles.Set(h, "AkExternalSourceInfo_szFile", file);
            PaHandles.Set(h, "AkExternalSourceInfo_idCodec", 4u);
            uint pid = PaEngine.PostEvent("Play_External_Voice", EngineFixture.Go, 1u, EngineFixture.Callback, EngineFixture.Cookie, 1u, h, 0);
            PaHandles.Release(h);   // the proxy may be finalized right after PostEvent returns
            return pid;
        }

        [PaTest]
        public static void MissingMediaEndsOnTheNextTick()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                uint pid = f.Post("Play_NoMedia");
                f.Step();
                PaAssert.Equal(1, f.Voices.All.Count, "the voice is still started (and traced)");
                PaAssert.Null(f.Voices.All[0].Spec.Media);
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents());
                PaAssert.Equal(PaVoiceEnd.NoMedia, f.Voices.All[0].EndReason);
            }
        }

        [PaTest]
        public static void LanguageMediaFollowsTheCurrentLanguage()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                f.Post("Play_Localized");
                f.Step();
                PaAssert.Near(1.0, f.Voices.All[0].Spec.Media.Duration, 1e-9, "default language English(UK)");
                PaEngine.SetCurrentLanguage("French(France)");
                f.Post("Play_Localized");
                f.Step();
                PaAssert.Near(3.0, f.Voices.All[1].Spec.Media.Duration, 1e-9, "French(France)");
                PaEngine.SetCurrentLanguage("German");
                f.Post("Play_Localized");
                f.Step();
                PaAssert.Near(1.0, f.Voices.All[2].Spec.Media.Duration, 1e-9, "no German voice: fallback to English(UK)");
                PaAssert.True(f.Platform.Logs.Exists(l => l.Contains("German")), "fallback warned");
            }
        }

        [PaTest]
        public static void ExternalVoiceIsCopiedAtPostTime()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                uint pid = PostExternal("English(UK)\\en-GB_TY2225.wem");
                PaAssert.NotEqual(0u, pid);
                f.Step();
                PaAssert.NotNull(f.Voices.All[0].Spec.Media, "external file found after the handle was released");
                PaAssert.Equal("en-gb_ty2225", f.Voices.All[0].Spec.Media.Name);
                f.StepUntil(2.9);
                PaAssert.Equal(0, f.EndOfEvents().Count);
                f.StepUntil(3.0);
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents(), "paced by the clip length");
            }
        }

        [PaTest]
        public static void ExternalVoiceFallsBackToTheDefaultLanguage()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                PostExternal("German\\de-DE_TY2225.wem");
                f.Step();
                PaAssert.NotNull(f.Voices.All[0].Spec.Media, "locale prefix swapped to the fallback language");
                PaAssert.Equal("en-gb_ty2225", f.Voices.All[0].Spec.Media.Name);
            }
        }

        [PaTest]
        public static void MissingExternalFileEndsOnTheNextTick()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                uint pid = PostExternal("English(UK)\\en-GB_NOPE.wem");
                f.Step();
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents(), "no 30 s commentary timeout");
            }
        }

        [PaTest]
        public static void MusicContextsKeepTheInstanceAlive()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                uint pid = f.Post("Play_Music");
                f.Step();
                PaAssert.Equal(1, f.Music.Contexts.Count, "music node goes to the music player");
                PaAssert.Equal(pid, f.Music.Contexts[0].PlayingId);
                PaAssert.Equal(0, f.Voices.All.Count);
                f.StepUntil(5.0, 0.5);
                PaAssert.Equal(0, f.EndOfEvents().Count);
                f.Post("Stop_Music", 3, false);
                f.Step();
                PaAssert.True(f.Music.Calls.Contains("stop 107 1000"), "stop reaches the context through Involves");
                f.Step();
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents(), "ends when the context ends");
            }
        }

        [PaTest]
        public static void ExecuteActionOnEventStopsOnlyThatEventOnThatObject()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                uint a = f.Post("Play_A", 1);
                uint b = f.Post("Play_A", 2);
                f.Step();
                PaAssert.Equal(PaResult.Success, PaEngine.ExecuteActionOnEvent("Play_A", 0, 1, 300, 4, 0));
                PaAssert.Equal(PaResult.IDNotFound, PaEngine.ExecuteActionOnEvent("Play_Nope", 0, 1, 300, 4, 0));
                f.Step();
                PaAssert.Equal("stop 300", f.Voices.All[0].Ops[0]);
                PaAssert.Equal(0, f.Voices.All[1].Ops.Count);
                f.Step(0.31);
                PaAssert.SequenceEqual(new[] { a }, f.EndOfEvents());
                PaEngine.ExecuteActionOnEvent("Play_A", 0, PaIds.GlobalObject, 0, 4, 0);
                f.Step();
                PaAssert.SequenceEqual(new[] { b }, f.EndOfEvents(), "global object: every object");
            }
        }

        [PaTest]
        public static void StopPlayingIdWithAnEventIdDoesNothing()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                uint pid = f.Post("Play_A");
                f.Step();
                PaEngine.StopPlayingID(PaIds.Hash("Play_A"), 0, 4);   // StopAudioEvent's original bug
                f.Step(0.5);
                PaAssert.Equal(0, f.EndOfEvents().Count);
                PaEngine.StopPlayingID(pid, 0, 4);
                f.Step();
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents());
            }
        }

        [PaTest]
        public static void StopAllStopsAnObjectOrEverything()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                uint a = f.Post("Play_A", 1);
                uint b = f.Post("Play_A", 2);
                uint m = f.Post("Play_Music", 2);
                f.Step();
                PaEngine.StopAll(1);
                f.Step();
                PaAssert.SequenceEqual(new[] { a }, f.EndOfEvents());
                PaEngine.StopAll(PaIds.GlobalObject);
                f.Step();
                f.Step();
                List<uint> ends = f.EndOfEvents();
                ends.Sort();
                List<uint> want = new List<uint> { b, m };
                want.Sort();
                PaAssert.SequenceEqual(want, ends);
            }
        }

        [PaTest]
        public static void MaxInstancesStopsTheOldestOrRefusesTheNewest()
        {
            using (EngineFixture f = Fixture())
            {
                BankBuilder b = new BankBuilder("Limits");
                f.Banks.Add(b.Bank);
                PaNode oldest = b.Sound(120, 0, 500);
                oldest.Advanced = new PaAdvanced { MaxInstances = 2 };
                b.Event("Play_Limited", b.Action(PaActionType.Play, 120));
                PaNode newest = b.Sound(121, 0, 500);
                newest.Advanced = new PaAdvanced { MaxInstances = 1, KillNewest = true, GlobalLimit = true };
                b.Event("Play_KillNewest", b.Action(PaActionType.Play, 121));
                StartWithMusic(f);
                f.Post("Play_Limited", 1, false);
                f.Post("Play_Limited", 1, false);
                f.Post("Play_Limited", 2, false);
                f.Step();
                f.Post("Play_Limited", 1, false);
                f.Step();
                PaAssert.Equal(4, f.Voices.All.Count);
                PaAssert.Equal("stop 0", f.Voices.All[0].Ops[0], "the oldest on that object is stopped");
                PaAssert.Equal(0, f.Voices.All[2].Ops.Count, "limit is per object");
                f.Post("Play_KillNewest", 1, false);
                f.Post("Play_KillNewest", 2, false);
                f.Step();
                PaAssert.Equal(5, f.Voices.All.Count, "global limit 1 with kill newest: the second is refused");
            }
        }

        [PaTest]
        public static void CancelledCallbacksAreNotDelivered()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                uint a = f.Post("Play_A");
                PaEngine.CancelEventCallbackCookie(EngineFixture.Cookie);
                uint b = PaEngine.PostEvent("Play_A", EngineFixture.Go, 1u, EngineFixture.Callback, new IntPtr(0x9999), 0, IntPtr.Zero, 0);
                PaEngine.CancelEventCallback(b);
                f.Step();
                f.StepUntil(2.5);
                PaAssert.Equal(0, f.EndOfEvents().Count);
                PaAssert.NotEqual(0u, a);
            }
        }

        [PaTest]
        public static void PlayingIdQueries()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                uint a = f.Post("Play_A", 5);
                uint b = f.Post("Play_A", 5);
                f.Post("Play_A", 6);
                PaAssert.Equal(PaIds.Hash("Play_A"), PaEngine.GetEventIDFromPlayingID(a));
                PaAssert.Equal(5ul, PaEngine.GetGameObjectFromPlayingID(b));
                uint count = 0;
                PaEngine.GetPlayingIDsFromGameObject(5, ref count, null);
                PaAssert.Equal(2u, count);
                uint[] ids = new uint[4];
                count = 4;
                PaEngine.GetPlayingIDsFromGameObject(5, ref count, ids);
                PaAssert.Equal(2u, count);
                PaAssert.SequenceEqual(new[] { a, b }, new[] { ids[0], ids[1] });
                f.Step();
                f.StepUntil(0.5);
                int ms;
                PaAssert.Equal(PaResult.Success, PaEngine.GetSourcePlayPosition(a, out ms));
                PaAssert.Equal(500, ms);
                PaAssert.Equal(77u, PaEngine.PostEvent("Play_A", 5, 0u, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, 77u), "requested playing id");
            }
        }

        [PaTest]
        public static void SeekRestartsPlayingVoicesAtThePosition()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                uint pid = f.Post("Play_A");
                f.Step();
                f.StepUntil(1.5);
                PaAssert.Equal(PaResult.Success, PaEngine.SeekOnEvent("Play_A", EngineFixture.Go, 0.25f, true, false, 0));
                f.Step();
                PaAssert.Equal(2, f.Voices.All.Count);
                PaAssert.Near(0.5, f.Voices.All[1].Spec.SourceOffset, 1e-6, "25% of 2 s");
                f.StepUntil(2.9);
                PaAssert.Equal(0, f.EndOfEvents().Count, "the instance follows the new voice");
                f.StepUntil(3.05);
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents());
            }
        }

        [PaTest]
        public static void AudioInputPlaysTheClip()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                byte[] pcm = new byte[9600];   // 0.1 s of 16-bit mono at 48 kHz
                pcm[0] = 0xFF;
                pcm[1] = 0x7F;
                PaAssert.True(PaEngine.AudioInputPlay(pcm, pcm.Length));
                PaAssert.True(PaEngine.AudioInputIsPlaying());
                f.Step();
                PaAssert.Equal(1, f.Voices.All.Count);
                PaVoiceSpec spec = f.Voices.All[0].Spec;
                PaAssert.Equal(106u, spec.NodeId, "played through the audio input Sound of the banks");
                PaAssert.Equal(4800, spec.Pcm.Length);
                PaAssert.Equal(48000, spec.PcmRate);
                PaAssert.Near(32767.0 / 32768.0, spec.Pcm[0], 1e-6);
                f.Step(0.05);
                PaAssert.Equal(2400, PaEngine.AudioInputPosition());
                f.Step(0.06);
                PaAssert.False(PaEngine.AudioInputIsPlaying(), "clip over");
                PaEngine.AudioInputPlay(pcm, pcm.Length);
                f.Step();
                PaEngine.AudioInputStop();
                PaAssert.False(PaEngine.AudioInputIsPlaying());
                f.Step();
                PaAssert.Equal("stop 0", f.Voices.All[1].Ops[0]);
            }
        }

        [PaTest]
        public static void ObjectsAndListener()
        {
            using (EngineFixture f = Fixture())
            {
                StartWithMusic(f);
                IPaWorld w = PaRuntime.World;
                PaAssert.NotNull(w);
                PaEngine.RegisterGameObject(-5, "neg", 3);
                ulong go = PaIds.FromRegisteredId(-5);
                PaAssert.Equal(3u, w.GetListenerMask(go));
                PaEngine.SetObjectPosition(go, 1, 2, 3, 0, 0, -1);
                PaAssert.Near(2, w.GetPosition(go).Y);
                PaAssert.Near(-1, w.GetFront(go).Z);
                PaAssert.Near(1, w.GetFront(99).Z, 1e-9, "default front");
                PaEngine.SetGameObjectOutputBusVolume(go, 0.5f);
                PaEngine.SetAttenuationScalingFactor(go, 2f);
                PaAssert.Near(0.5, w.GetOutputBusVolume(go));
                PaAssert.Near(2, w.GetAttenuationScaling(go));
                PaAssert.Near(1, w.GetOutputBusVolume(99), 1e-9, "default");
                PaEngine.SetListenerPosition(0, 0, 1, 0, 1, 0, 4, 5, 6, 0);
                PaAssert.Near(5, w.Listener.Position.Y);
                PaEngine.MuteBackgroundMusic(true);
                PaAssert.True(w.BackgroundMusicMuted);
                w.SetMeterRtpc(PaIds.Hash("SIDECHAIN"), -12f);
                PaAssert.Near(-12, w.GetRtpc(PaIds.Hash("SIDECHAIN"), go), 1e-6, "meter value is global");
            }
        }
    }
}
