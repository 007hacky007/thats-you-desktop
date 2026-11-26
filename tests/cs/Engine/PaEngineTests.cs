// Engine behaviour seen from the game: EndOfEvent timing, unknown events, stop/pause targets,
// game parameter ramps, PlayEvent, bank loading, threading and silent mode.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace PortAudio.Tests
{
    public static class PaEngineTests
    {
        // Sound 100 under actor mixer 10, sound 101 under actor mixer 20; Play_A / Play_B play them.
        static EngineFixture TwoSounds(double lenA = 2.0, double lenB = 2.0)
        {
            EngineFixture f = new EngineFixture();
            BankBuilder b = f.Bank("Test");
            b.Node(10, PaNodeType.ActorMixer, 0, 100);
            b.Node(20, PaNodeType.ActorMixer, 0, 101);
            b.Sound(100, 10, 500);
            b.Sound(101, 20, 501);
            f.MediaLi.Add(Media.Entry(500, lenA));
            f.MediaLi.Add(Media.Entry(501, lenB));
            b.Event("Play_A", b.Action(PaActionType.Play, 100));
            b.Event("Play_B", b.Action(PaActionType.Play, 101));
            PaAction stop = b.Action(PaActionType.Stop, 10);
            stop.TransitionMs = 500;
            b.Event("Stop_Mixer10", stop);
            b.Event("Nothing");
            b.Event("Play_Missing", b.Action(PaActionType.Play, 999));
            return f;
        }

        [PaTest]
        public static void EndOfEventArrivesAfterTheVoiceLength()
        {
            using (EngineFixture f = TwoSounds())
            {
                f.Start();
                PaAssert.False(PaEngine.IsSilent, "real mode");
                uint pid = f.Post("Play_A");
                PaAssert.NotEqual(0u, pid);
                PaAssert.Equal(0, f.EndOfEvents().Count, "nothing before the first tick");
                f.Step();
                PaAssert.Equal(1, f.Voices.All.Count, "one voice");
                PaAssert.Equal(100u, f.Voices.All[0].Spec.NodeId);
                PaAssert.Equal(pid, f.Voices.All[0].Spec.PlayingId);
                PaAssert.Equal(EngineFixture.Go, f.Voices.All[0].Spec.GameObject);
                f.StepUntil(1.95);
                PaAssert.Equal(0, f.EndOfEvents().Count, "still playing at 1.95 s");
                f.StepUntil(2.0);
                List<CallbackRecord> cb = f.Callbacks();
                PaAssert.Equal(1, cb.Count, "EndOfEvent at 2 s");
                PaAssert.Equal(pid, cb[0].PlayingId);
                PaAssert.Equal(PaIds.Hash("Play_A"), cb[0].EventId);
                PaAssert.Equal(EngineFixture.Go, cb[0].GameObject);
                PaAssert.Equal(EngineFixture.Cookie, cb[0].Cookie);
                f.Step(0.5);
                PaAssert.Equal(0, f.EndOfEvents().Count, "exactly once");
                PaAssert.Equal(0u, PaEngine.GetEventIDFromPlayingID(pid), "instance gone");
            }
        }

        [PaTest]
        public static void EndOfEventFollowsAStopFade()
        {
            using (EngineFixture f = TwoSounds(10.0))
            {
                f.Start();
                uint pid = f.Post("Play_A");
                f.Step();
                f.StepUntil(1.0);
                PaAssert.NotEqual(0u, f.Post("Stop_Mixer10", EngineFixture.Go, false));
                f.Step();
                PaAssert.Equal("stop 500", f.Voices.All[0].Ops[0], "stopped with the action's fade");
                f.StepUntil(1.45);
                PaAssert.Equal(0, f.EndOfEvents().Count, "fade still running");
                f.StepUntil(1.55);
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents(), "EndOfEvent after the fade");
            }
        }

        [PaTest]
        public static void EndOfEventOnTheNextTickWhenNothingPlays()
        {
            using (EngineFixture f = TwoSounds())
            {
                f.Start();
                uint a = f.Post("Nothing");
                uint b = f.Post("Play_Missing");
                PaAssert.NotEqual(0u, a);
                PaAssert.NotEqual(0u, b);
                PaAssert.Equal(0, f.EndOfEvents().Count);
                f.Step();
                PaAssert.SequenceEqual(new[] { a, b }, f.EndOfEvents(), "both end on the first tick, in post order");
            }
        }

        [PaTest]
        public static void NoCallbackWithoutCookieOrFlag()
        {
            using (EngineFixture f = TwoSounds())
            {
                f.Start();
                PaEngine.PostEvent("Nothing", EngineFixture.Go, 0u, EngineFixture.Callback, EngineFixture.Cookie, 0, IntPtr.Zero, 0);
                PaEngine.PostEvent("Nothing", EngineFixture.Go, 1u, IntPtr.Zero, EngineFixture.Cookie, 0, IntPtr.Zero, 0);
                PaEngine.PostEvent("Nothing", EngineFixture.Go, 1u, EngineFixture.Callback, IntPtr.Zero, 0, IntPtr.Zero, 0);
                f.Step();
                f.Step();
                PaAssert.Equal(0, f.Callbacks().Count);
            }
        }

        [PaTest]
        public static void UnknownEventsFail()
        {
            using (EngineFixture f = TwoSounds())
            {
                f.Start();
                PaAssert.Equal(0u, f.Post("Play_Does_Not_Exist"), "unknown event");
                PaAssert.Equal(0u, f.Post("Play_A "), "trailing space hashes to nothing (original bug kept)");
                PaAssert.Equal(0u, PaEngine.PostEvent(12345u, EngineFixture.Go, 1u, EngineFixture.Callback, EngineFixture.Cookie, 0, IntPtr.Zero, 0));
                PaAssert.Equal(PaResult.IDNotFound, PaEngine.SeekOnEvent("Play_Does_Not_Exist", EngineFixture.Go, 0f, true, false, 0));
                PaAssert.Equal(PaResult.Success, PaEngine.SeekOnEvent("Play_A", EngineFixture.Go, 0f, true, false, 0));
                PaAssert.Equal(PaResult.Success, PaEngine.SeekOnEvent(PaIds.Hash("Play_A"), EngineFixture.Go, 0f, false, false, 0));
                for (int i = 0; i < 3; i++)
                    f.Step(0.1);
                PaAssert.Equal(0, f.Callbacks().Count, "no callback for unknown events");
                PaAssert.True(f.Trace.Count("post") >= 3);
                PaAssert.True(f.Trace.Lines.Exists(l => l.StartsWith("post") && l.Contains("result=unknown")), "traced as unknown");
            }
        }

        [PaTest]
        public static void UnloadedEventIsUnknown()
        {
            using (EngineFixture f = TwoSounds())
            {
                f.Start();
                PaAssert.NotEqual(0u, f.Post("Play_A", EngineFixture.Go, false));
                PaAssert.Equal(PaResult.Success, PaEngine.UnloadBank("Test"));
                PaAssert.Equal(0u, f.Post("Play_A"));
                PaAssert.Equal(PaResult.IDNotFound, PaEngine.SeekOnEvent("Play_A", EngineFixture.Go, 0f, true, false, 0));
            }
        }

        [PaTest]
        public static void PauseDelaysEndOfEvent()
        {
            using (EngineFixture f = TwoSounds())
            {
                f.Start();
                uint pid = f.Post("Play_A");
                f.Step();
                f.StepUntil(0.5);
                PaAssert.Equal(PaResult.Success, PaEngine.ExecuteActionOnEvent("Play_A", 1, EngineFixture.Go, 0, 4, 0));
                f.Step();
                f.StepUntil(3.0);
                PaAssert.Equal(0, f.EndOfEvents().Count, "paused: no EndOfEvent");
                PaEngine.ExecuteActionOnEvent("Play_A", 2, EngineFixture.Go, 0, 4, 0);
                f.Step();
                f.StepUntil(4.4);
                PaAssert.Equal(0, f.EndOfEvents().Count, "1.5 s left after the resume");
                f.StepUntil(4.6);
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents());
            }
        }

        [PaTest]
        public static void PauseAllActionPausesEverythingAndResumeAllResumes()
        {
            using (EngineFixture f = TwoSounds())
            {
                BankBuilder b = new BankBuilder("Pauses");
                f.Banks.Add(b.Bank);
                PaAction pause = b.Action(PaActionType.Pause, 0);
                pause.TargetMode = PaTargetMode.All;
                b.Event("Pause_All", pause);
                PaAction resume = b.Action(PaActionType.Resume, 0);
                resume.TargetMode = PaTargetMode.All;
                b.Event("Resume_All", resume);
                f.Start();
                uint a = f.Post("Play_A");
                uint other = f.Post("Play_B", 77);
                f.Step();
                f.StepUntil(1.0);
                f.Post("Pause_All", 5, false);
                f.Step();
                PaAssert.Equal(PaVoiceState.Paused, f.Voices.All[0].State);
                PaAssert.Equal(PaVoiceState.Paused, f.Voices.All[1].State, "all objects (not object scoped)");
                f.StepUntil(5.0);
                PaAssert.Equal(0, f.EndOfEvents().Count);
                f.Post("Resume_All", 5, false);
                f.Step();
                f.StepUntil(6.05);
                List<uint> ends = f.EndOfEvents();
                ends.Sort();
                List<uint> want = new List<uint> { a, other };
                want.Sort();
                PaAssert.SequenceEqual(want, ends);
            }
        }

        [PaTest]
        public static void StopByAncestorActorMixer()
        {
            using (EngineFixture f = TwoSounds(10.0, 10.0))
            {
                f.Start();
                uint a = f.Post("Play_A");
                uint b = f.Post("Play_B");
                f.Step();
                f.Post("Stop_Mixer10", 999, false);
                f.Step();
                f.StepUntil(1.0);
                PaAssert.SequenceEqual(new[] { a }, f.EndOfEvents(), "only the voice under actor mixer 10 stopped");
                PaAssert.Equal(PaVoiceState.Playing, f.Voices.All[1].State);
                f.StepUntil(10.0);
                PaAssert.SequenceEqual(new[] { b }, f.EndOfEvents());
            }
        }

        [PaTest]
        public static void ObjectScopedStopOnlyStopsThePostingObject()
        {
            using (EngineFixture f = TwoSounds(10.0, 10.0))
            {
                BankBuilder b = new BankBuilder("Scoped");
                f.Banks.Add(b.Bank);
                PaAction stop = b.Action(PaActionType.Stop, 10);
                stop.GameObjectScoped = true;
                b.Event("Stop_Mixer10_O", stop);
                f.Start();
                uint mine = f.Post("Play_A", 1);
                uint theirs = f.Post("Play_A", 2);
                f.Step();
                f.Post("Stop_Mixer10_O", 1, false);
                f.Step();
                f.Step(0.01);
                PaAssert.SequenceEqual(new[] { mine }, f.EndOfEvents());
                PaAssert.NotEqual(0u, theirs);
                PaAssert.Equal(PaVoiceState.Playing, f.Voices.All[1].State);
            }
        }

        [PaTest]
        public static void PauseAllExceptSkipsTheExceptions()
        {
            using (EngineFixture f = TwoSounds(10.0, 10.0))
            {
                BankBuilder b = new BankBuilder("Except");
                f.Banks.Add(b.Bank);
                b.Bank.Busses.Add(new PaBus { Id = 1, Name = "Master Audio Bus", Props = new PaProps() });
                b.Bank.Busses.Add(new PaBus { Id = 2, Parent = 1, Name = "Speech", Props = new PaProps() });
                b.Node(30, PaNodeType.ActorMixer, 0, 102).Bus = 2;
                b.Sound(102, 30, 502);
                f.MediaLi.Add(Media.Entry(502, 10.0));
                b.Event("Play_C", b.Action(PaActionType.Play, 102));
                PaAction pause = b.Action(PaActionType.Pause, 0);
                pause.TargetMode = PaTargetMode.AllExcept;
                pause.Exceptions = new[] { new PaObjectRef { Id = 20 }, new PaObjectRef { Id = 2, IsBus = true } };
                b.Event("Pause_AE", pause);
                f.Start();
                f.Post("Play_A");
                f.Post("Play_B");
                f.Post("Play_C");
                f.Step();
                f.Post("Pause_AE", EngineFixture.Go, false);
                f.Step();
                PaAssert.Equal(PaVoiceState.Paused, f.Voices.All[0].State, "node 100 paused");
                PaAssert.Equal(PaVoiceState.Playing, f.Voices.All[1].State, "node exception (actor mixer 20)");
                PaAssert.Equal(PaVoiceState.Playing, f.Voices.All[2].State, "bus exception (Speech)");
            }
        }

        [PaTest]
        public static void SetGameParameterRamps()
        {
            using (EngineFixture f = TwoSounds())
            {
                BankBuilder b = new BankBuilder("Init");
                f.Banks.Add(b.Bank);
                uint gp = PaIds.Hash("FADEVOLUME");
                b.Bank.GameParameters.Add(new PaGameParameter { Id = gp, Name = "FADEVOLUME", Default = 2f });
                PaAction set = b.Action(PaActionType.SetGameParameter, gp);
                set.Value = 12f;
                set.TransitionMs = 1000;
                b.Event("Fade_Up", set);
                PaAction setO = b.Action(PaActionType.SetGameParameter, gp);
                setO.Value = -4f;
                setO.ValueRelative = true;
                setO.GameObjectScoped = true;
                setO.BypassTransition = true;
                setO.TransitionMs = 1000;
                b.Event("Fade_Down_O", setO);
                f.Start();
                float v;
                int type = 0;
                PaEngine.GetRTPCValue(gp, PaIds.GlobalObject, out v, ref type);
                PaAssert.Near(2.0, v, 1e-4, "Init default");
                f.Post("Fade_Up", 5, false);
                f.Step();
                f.StepUntil(0.5);
                type = 1;
                PaEngine.GetRTPCValue(gp, PaIds.GlobalObject, out v, ref type);
                PaAssert.Near(7.0, v, 0.11, "half way along the linear ramp");
                f.StepUntil(1.1);
                PaEngine.GetRTPCValue(gp, PaIds.GlobalObject, out v, ref type);
                PaAssert.Near(12.0, v, 1e-4, "ramp done");
                f.Post("Fade_Down_O", 9, false);
                f.Step();
                type = 2;
                PaEngine.GetRTPCValue(gp, 9, out v, ref type);
                PaAssert.Near(8.0, v, 1e-4, "relative, object scoped, no transition");
                PaAssert.Equal(2, type, "object value");
                PaEngine.GetRTPCValue(gp, 10, out v, ref type);
                PaAssert.Near(12.0, v, 1e-4, "other objects see the global value");
            }
        }

        [PaTest]
        public static void SetRtpcValueRampsAndResets()
        {
            using (EngineFixture f = TwoSounds())
            {
                f.Start();
                uint id = PaIds.Hash("LowPassFilter");
                PaEngine.SetRTPCValue(id, 50f, 3, 0, 4, false);
                f.Step();
                float v;
                int type = 2;
                PaEngine.GetRTPCValue(id, 3, out v, ref type);
                PaAssert.Near(50, v, 1e-4);
                PaEngine.SetRTPCValue(id, 100f, 3, 2000, 4, false);
                f.Step();
                f.StepUntil(1.0);
                PaEngine.GetRTPCValue(id, 3, out v, ref type);
                PaAssert.Near(75, v, 1.5, "ramping");
                PaEngine.SetRTPCValue(id, 10f, 3, 2000, 4, true);
                f.Step();
                PaEngine.GetRTPCValue(id, 3, out v, ref type);
                PaAssert.Near(10, v, 1e-4, "bypassInterpolation jumps");
                PaEngine.ResetRTPCValue(id, 3, 0, 4, false);
                f.Step();
                type = 2;
                PaEngine.GetRTPCValue(id, 3, out v, ref type);
                PaAssert.Near(0, v, 1e-4, "reset to the default");
                PaAssert.Equal(0, type, "no object value any more");
            }
        }

        [PaTest]
        public static void PlayEventWithDelayPostsTheTargetLater()
        {
            using (EngineFixture f = TwoSounds())
            {
                BankBuilder b = new BankBuilder("Record");
                f.Banks.Add(b.Bank);
                PaAction pe = b.Action(PaActionType.PlayEvent, PaIds.Hash("Play_A"));
                pe.DelayMs = 1000;
                b.Event("Play_RecordPlayer", pe);
                f.Start();
                uint pid = f.Post("Play_RecordPlayer");
                f.Step();
                f.StepUntil(0.95);
                PaAssert.Equal(0, f.Voices.All.Count, "delayed");
                PaAssert.Equal(0, f.EndOfEvents().Count, "a pending action keeps the instance alive");
                f.StepUntil(1.0);
                PaAssert.Equal(1, f.Voices.All.Count, "posted after 1 s");
                PaAssert.Equal(pid, f.Voices.All[0].Spec.PlayingId, "the voice belongs to the posting instance");
                f.StepUntil(2.9);
                PaAssert.Equal(0, f.EndOfEvents().Count);
                f.StepUntil(3.05);
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents(), "one EndOfEvent for the outer instance");
            }
        }

        [PaTest]
        public static void StopCancelsPendingDelayedPlay()
        {
            using (EngineFixture f = TwoSounds())
            {
                BankBuilder b = new BankBuilder("Delayed");
                f.Banks.Add(b.Bank);
                PaAction play = b.Action(PaActionType.Play, 100);
                play.DelayMs = 2000;
                b.Event("Play_A_Later", play);
                f.Start();
                uint pid = f.Post("Play_A_Later");
                f.Step();
                f.StepUntil(0.5);
                f.Post("Stop_Mixer10", EngineFixture.Go, false);
                f.Step();
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents(), "the cancelled instance ends");
                f.StepUntil(3.0);
                PaAssert.Equal(0, f.Voices.All.Count, "the delayed play never ran");
            }
        }

        [PaTest]
        public static void AsyncBankCallbacksArriveInRequestOrder()
        {
            using (EngineFixture f = new EngineFixture())
            {
                BankBuilder a = f.Bank("BankA");
                a.Sound(200, 0, 600);
                f.MediaLi.Add(Media.Entry(600, 1));
                a.Event("Play_InA", a.Action(PaActionType.Play, 200));
                BankBuilder b = f.Bank("BankB");
                b.Sound(201, 0, 601);
                f.MediaLi.Add(Media.Entry(601, 1));
                b.Event("Play_InB", b.Action(PaActionType.Play, 201));
                f.MissingBankFiles.Add("BankMissing");
                f.Start(false);
                f.Voices.NotReady.Add(601);
                IntPtr ca = new IntPtr(0x100), cb = new IntPtr(0x200), cm = new IntPtr(0x250), cu = new IntPtr(0x300);
                uint ida, idb, idm;
                PaAssert.Equal(PaResult.Success, PaEngine.LoadBankAsync("BankA.bnk", ca, out ida));
                PaAssert.Equal(PaResult.Success, PaEngine.LoadBankAsync("BankB", cb, out idb));
                PaAssert.Equal(PaResult.Success, PaEngine.LoadBankAsync("BankMissing", cm, out idm));
                PaAssert.Equal(PaResult.Success, PaEngine.UnloadBankAsync("BankA", cu));
                PaAssert.Equal(PaIds.Hash("BankA"), ida);
                PaAssert.Equal(0u, f.Post("Play_InA"), "not registered before a tick");

                List<CallbackRecord> got = new List<CallbackRecord>();
                Stopwatch sw = Stopwatch.StartNew();
                while (got.Count < 1 && sw.ElapsedMilliseconds < 5000)
                {
                    f.Step(0.01);
                    got.AddRange(f.Callbacks());
                    Thread.Sleep(1);
                }
                PaAssert.Equal(1, got.Count, "BankA ready, BankB waits for its media");
                PaAssert.Equal(ida, got[0].BankId);
                PaAssert.Equal(ca, got[0].Cookie);
                PaAssert.Equal(PaResult.Success, got[0].Result);
                f.Step(0.5);
                PaAssert.Equal(0, f.Callbacks().Count, "B still waiting, so the missing bank and the unload wait too");
                PaAssert.True(f.Voices.Preloaded.Exists(p => p.Value.Id == 601), "B's embedded media was preloaded");
                PaAssert.True(f.Voices.Prefetched.Exists(p => p.Id == 601), "and prefetched, since the caller waits for it");
                f.Voices.NotReady.Clear();
                f.Step(0.01);
                f.Step(0.01);
                got = f.Callbacks();
                PaAssert.Equal(3, got.Count, "B, missing, unload");
                PaAssert.Equal(idb, got[0].BankId);
                PaAssert.Equal(idm, got[1].BankId);
                PaAssert.Equal(PaResult.Success, got[1].Result, "a bank without JSON loads as empty");
                PaAssert.Equal(cu, got[2].Cookie);
                PaAssert.Equal(0u, f.Post("Play_InA"), "A was unloaded");
                PaAssert.NotEqual(0u, f.Post("Play_InB"));
            }
        }

        [PaTest]
        public static void AsyncBankGivesUpWaitingForMediaAfterTheCap()
        {
            using (EngineFixture f = new EngineFixture())
            {
                BankBuilder b = f.Bank("Slow");
                b.Sound(201, 0, 601);
                f.MediaLi.Add(Media.Entry(601, 1));
                f.Start(false);
                f.Voices.NotReady.Add(601);
                uint id;
                PaEngine.LoadBankAsync("Slow", new IntPtr(0x10), out id);
                Stopwatch sw = Stopwatch.StartNew();
                while (PaCallbacks.PendingCount == 0 && sw.ElapsedMilliseconds < 3000 && f.Platform.Clock < PaCore.PreloadWaitSec - 0.1)
                {
                    f.Step(0.05);
                    Thread.Sleep(1);
                }
                PaAssert.Equal(0, f.Callbacks().Count, "within the cap: still waiting");
                for (int i = 0; i < 20; i++)
                    f.Step(0.05);
                List<CallbackRecord> got = f.Callbacks();
                PaAssert.Equal(1, got.Count, "reported after the cap");
                PaAssert.Equal(id, got[0].BankId);
                PaAssert.True(f.Platform.Clock < PaCore.PreloadWaitSec + 1.1, "the cap is short");
                PaAssert.True(f.Platform.Logs.Exists(l => l.Contains("bank Slow: media not ready after " + PaCore.PreloadWaitSec + " s")), "warned");
            }
        }

        [PaTest]
        public static void AsyncBankMediaJumpsAheadOfOtherBanksPreloads()
        {
            using (EngineFixture f = new EngineFixture())
            {
                BankBuilder big = f.Bank("Effects");
                for (uint i = 0; i < 300; i++)
                {
                    big.Sound(1000 + i, 0, 5000 + i);
                    f.MediaLi.Add(Media.Entry(5000 + i, 1));
                }
                BankBuilder small = f.Bank("Commentary");
                small.Sound(300, 0, 700);
                small.Sound(301, 0, 701);
                f.MediaLi.Add(Media.Entry(700, 1));
                f.MediaLi.Add(Media.Entry(701, 1));
                f.Start(false);
                f.Voices.QueueLoads = true;
                for (uint i = 0; i < 300; i++)
                    f.Voices.NotReady.Add(5000 + i);
                f.Voices.NotReady.Add(700);
                f.Voices.NotReady.Add(701);

                uint bigId, smallId;
                PaAssert.Equal(PaResult.Success, PaEngine.LoadBank("Effects", out bigId));
                f.Step(0.01);
                PaAssert.True(f.Voices.QueuedLoads > 250, "the sync bank queued its preloads");
                IntPtr cookie = new IntPtr(0x4242);
                PaAssert.Equal(PaResult.Success, PaEngine.LoadBankAsync("Commentary", cookie, out smallId));

                List<CallbackRecord> got = new List<CallbackRecord>();
                Stopwatch sw = Stopwatch.StartNew();
                int ticks = 0;
                while (got.Count == 0 && sw.ElapsedMilliseconds < 5000 && f.Platform.Clock < 5.0)
                {
                    f.Step(0.01);
                    ticks++;
                    got.AddRange(f.Callbacks());
                    if (got.Count == 0)
                        Thread.Sleep(1);
                }
                PaAssert.Equal(1, got.Count, "AK_Bank arrived");
                PaAssert.Equal(smallId, got[0].BankId);
                PaAssert.Equal(cookie, got[0].Cookie);
                PaAssert.Equal(PaResult.Success, got[0].Result);
                PaAssert.True(f.Voices.IsReady(Media.Entry(700, 1)) && f.Voices.IsReady(Media.Entry(701, 1)), "its media is ready");
                PaAssert.True(f.Voices.QueuedLoads > 200, "it did not wait for the other bank's preloads (" + f.Voices.QueuedLoads + " left)");
                PaAssert.False(f.Platform.Logs.Exists(l => l.Contains("media not ready")), "no timeout");
            }
        }

        [PaTest]
        public static void BanksAreReferenceCounted()
        {
            using (EngineFixture f = TwoSounds())
            {
                f.Start();
                uint id;
                PaAssert.Equal(PaResult.Success, PaEngine.LoadBank("Test", out id));
                PaAssert.Equal(PaResult.Success, PaEngine.UnloadBank(id));
                PaAssert.NotEqual(0u, f.Post("Play_A", 1, false), "one reference left");
                PaAssert.Equal(PaResult.Success, PaEngine.UnloadBank(id));
                PaAssert.Equal(0u, f.Post("Play_A", 1, false));
                PaAssert.Equal(PaResult.UnknownBankID, PaEngine.UnloadBank(id));
            }
        }

        [PaTest]
        public static void UnloadStopsVoicesOfRemovedNodes()
        {
            using (EngineFixture f = TwoSounds(10, 10))
            {
                f.Start();
                uint pid = f.Post("Play_A");
                f.Step();
                PaEngine.UnloadBank("Test");
                f.Step();
                PaAssert.Equal("stop 50", f.Voices.All[0].Ops[0]);
                f.Step(0.06);
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents());
                PaAssert.True(f.Voices.Released.Count > 0, "bank preloads released");
            }
        }

        [PaTest]
        public static void UnregisterFromAFinalizerThreadIsQueued()
        {
            using (EngineFixture f = TwoSounds())
            {
                BankBuilder b = new BankBuilder("Loop");
                f.Banks.Add(b.Bank);
                b.Sound(110, 0, 500).Props.Loop = 0;
                b.Event("Play_Loop", b.Action(PaActionType.Play, 110));
                f.Start();
                PaEngine.RegisterGameObject(7, "emitter", 1);
                PaEngine.SetSwitch(PaIds.Hash("SURFACE"), PaIds.Hash("WOOD"), 7);
                uint pid = f.Post("Play_Loop", 7);
                f.Step();
                PaAssert.True(double.IsPositiveInfinity(f.Voices.All[0].EndTime), "infinite loop");
                int result = -1;
                Exception error = null;
                Thread t = new Thread(() =>
                {
                    try { result = PaEngine.UnregisterGameObject(7); }
                    catch (Exception e) { error = e; }
                });
                t.Start();
                t.Join();
                PaAssert.Null(error);
                PaAssert.Equal(PaResult.Success, result);
                PaAssert.Equal(0, f.Voices.All[0].Ops.Count, "nothing touched off the main thread");
                f.Step();
                PaAssert.Equal("stop 0", f.Voices.All[0].Ops[0], "stopped at once on the next tick");
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents());
                uint value;
                PaEngine.GetSwitch(PaIds.Hash("SURFACE"), 7, out value);
                PaAssert.Equal(0u, value, "per-object switches dropped");
            }
        }

        [PaTest]
        public static void EntryPointsAreThreadSafe()
        {
            using (EngineFixture f = TwoSounds(0.05, 0.05))
            {
                f.Start();
                Exception error = null;
                int stop = 0;
                List<Thread> threads = new List<Thread>();
                for (int k = 0; k < 4; k++)
                {
                    int seed = k;
                    Thread t = new Thread(() =>
                    {
                        try
                        {
                            for (int i = 0; i < 300 && Volatile.Read(ref stop) == 0; i++)
                            {
                                PaEngine.RegisterGameObject(1000 + seed * 1000 + i, "x", 1);
                                PaEngine.PostEvent("Play_A", (ulong)(1000 + seed * 1000 + i), 1u, EngineFixture.Callback, EngineFixture.Cookie, 0, IntPtr.Zero, 0);
                                PaEngine.SetRTPCValue(PaIds.Hash("X"), i, PaIds.GlobalObject, 10, 4, false);
                                PaEngine.SetState(PaIds.Hash("G"), (uint)i);
                                PaEngine.UnregisterGameObject(1000 + seed * 1000 + i);
                                PaEngine.Tick();   // off the main thread: ignored
                            }
                        }
                        catch (Exception e) { error = e; }
                    });
                    threads.Add(t);
                    t.Start();
                }
                for (int i = 0; i < 200; i++)
                {
                    f.Step(0.01);
                    f.Callbacks();
                }
                Volatile.Write(ref stop, 1);
                foreach (Thread t in threads)
                    t.Join();
                for (int i = 0; i < 20; i++)
                    f.Step(0.05);
                f.Callbacks();
                PaAssert.Null(error, "no exception from concurrent calls");
                PaAssert.Equal(0, PaEngine.InstanceCount, "every instance completed");
            }
        }

        [PaTest]
        public static void TickRunsAtMostOncePerFrame()
        {
            using (EngineFixture f = TwoSounds())
            {
                f.Start();
                f.Step();
                int ticks = f.Voices.Ticks;
                PaEngine.Tick();
                PaEngine.RenderAudio();
                PaAssert.Equal(ticks, f.Voices.Ticks, "same frame");
                f.Platform.Frame++;
                PaEngine.RenderAudio();
                PaAssert.Equal(ticks + 1, f.Voices.Ticks, "RenderAudio ticks a new frame");
                PaAssert.NotNull(f.Platform.DriverTick, "driver started");
            }
        }

        [PaTest]
        public static void SuspendIsANoOpByDefault()
        {
            using (EngineFixture f = TwoSounds())
            {
                f.Start();
                f.Post("Play_A");
                f.Step();
                PaEngine.Suspend(false);
                f.Step(0.1);
                PaAssert.Equal(PaVoiceState.Playing, f.Voices.All[0].State);
                PaEngine.WakeupFromSuspend();
            }
            using (EngineFixture f = TwoSounds())
            {
                f.Platform.settings.SuspendOnFocusLoss = true;
                f.Start();
                f.Post("Play_A");
                f.Step();
                PaEngine.Suspend(false);
                f.Step(0.1);
                PaAssert.Equal(PaVoiceState.Paused, f.Voices.All[0].State, "-portAudioSuspendOnFocusLoss");
                PaEngine.WakeupFromSuspend();
                f.Step(0.1);
                PaAssert.Equal(PaVoiceState.Playing, f.Voices.All[0].State);
            }
        }

        [PaTest]
        public static void SilentModeWithoutDataMatchesTheOldBackend()
        {
            using (EngineFixture f = new EngineFixture())
            {
                PaRuntime.Platform = f.Platform;   // no manifest written
                PaAssert.Equal(PaResult.Success, PaEngine.Init());
                CheckSilent(f);
            }
        }

        [PaTest]
        public static void SilentModeWithPortAudioOff()
        {
            using (EngineFixture f = TwoSounds())
            {
                f.Platform.settings.Off = true;
                f.Start(false);
                PaAssert.Null(f.Voices, "no voices created");
                CheckSilent(f);
            }
        }

        [PaTest]
        public static void SilentModeWithoutPlatform()
        {
            using (EngineFixture f = new EngineFixture())
            {
                PaRuntime.Platform = null;
                PaAssert.Equal(PaResult.Success, PaEngine.Init());
                CheckSilent(f);
            }
        }

        static void CheckSilent(EngineFixture f)
        {
            PaAssert.True(PaEngine.IsSilent, "silent");
            PaAssert.True(PaEngine.IsInitialized());
            uint a = f.Post("Anything_At_All");
            PaAssert.NotEqual(0u, a, "a fresh id for every post");
            uint b = f.Post("Anything_At_All");
            PaAssert.NotEqual(a, b);
            PaAssert.Equal(PaIds.Hash("Anything_At_All"), PaEngine.GetEventIDFromPlayingID(a));
            PaAssert.Equal(77u, PaEngine.PostEvent(5u, 1, 0u, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, 77u), "requested id kept");
            PaAssert.Equal(PaResult.Success, PaEngine.SeekOnEvent("Anything_At_All", 1, 0f, true, false, 0));
            uint bank;
            PaAssert.Equal(PaResult.Success, PaEngine.LoadBank("Whatever.bnk", out bank));
            PaAssert.Equal(PaIds.Hash("Whatever"), bank);
            PaAssert.Equal(PaResult.Success, PaEngine.LoadBankAsync("Other", new IntPtr(0x55), out bank));
            PaAssert.Equal(PaResult.Success, PaEngine.UnloadBank("Whatever"));
            PaAssert.Equal(PaResult.Success, PaEngine.PrepareRequest(0, new IntPtr(0x66)));
            PaAssert.Equal(PaResult.Success, PaEngine.SetState(1, 2));
            uint state;
            PaEngine.GetState(1, out state);
            PaAssert.Equal(0u, state, "silent mode keeps no state, like PortAudioNull");
            PaAssert.True(PaEngine.AudioInputPlay(new byte[100], 100));
            PaAssert.False(PaEngine.AudioInputIsPlaying());
            PaAssert.Equal(0, PaEngine.AudioInputPosition());
            List<CallbackRecord> cb = f.Callbacks();
            PaAssert.Equal(4, cb.Count, "2 EndOfEvent + 2 bank records (async load, prepare), queued at once");
            PaAssert.Equal(a, cb[0].PlayingId);
            PaAssert.Equal(b, cb[1].PlayingId);
            PaAssert.Equal(PaCallbackType.Bank, cb[2].Type);
            PaAssert.Equal(PaIds.Hash("Other"), cb[2].BankId);
            PaEngine.Tick();
            f.Step(1.0);
            PaAssert.Equal(0, f.Callbacks().Count);
        }
    }
}
