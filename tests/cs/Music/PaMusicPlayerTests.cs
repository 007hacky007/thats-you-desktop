using System.Linq;
using static PortAudio.Tests.MusicData;

namespace PortAudio.Tests
{
    public sealed class PaMusicPlayerTests
    {
        const uint LevelGroup = 500, Level1 = 1, Level2 = 2, Level2Stop = 3;

        // A looping (infinite) playlist of one 4 s segment with one clip of `media`.
        static void Looping(MusicFixture f, uint ranseq, uint parent, uint seg, uint media, double lengthMs = 4000)
        {
            f.Add(RanSeq(ranseq, parent, Group("sequenceContinuous", 0, Leaf(seg)), seg),
                  Segment(seg, ranseq, lengthMs, 0, lengthMs, seg + 1),
                  Track(seg + 1, seg, Clip(media, 0, 0, 0, lengthMs)))
             .Media(media, lengthMs / 1000.0);
        }

        static MusicFixture LevelSwitch(params PaTransitionRule[] rules)
        {
            var f = new MusicFixture();
            var sw = Switch(4000, 0, new[] { new PaMusicArg { Group = LevelGroup, IsState = true } },
                new[] { Path(4100, Level1), Path(4200, Level2), Path(0, Level2Stop) }, 4100, 4200);
            sw.Music.Transitions = rules;
            f.Add(sw);
            Looping(f, 4100, 4000, 4101, 41);
            Looping(f, 4200, 4000, 4201, 42);
            f.World.States[LevelGroup] = Level1;
            return f;
        }

        [PaTest]
        public void GaplessPlaylistWithEntryAndExit()
        {
            using (var f = new MusicFixture())
            {
                var rs = RanSeq(3000, 0, Group("sequenceContinuous", 1, Leaf(2001), Leaf(2002)), 2001, 2002);
                rs.Music.Transitions = new[] { Rule(-1, -1, "exitMarker", "entryMarker", postExit: false, preEntry: true) };
                f.Add(rs,
                    Segment(2001, 3000, 6000, 1000, 5000, 2101), Track(2101, 2001, Clip(11, 0, 0, 0, 6000)),
                    Segment(2002, 3000, 5000, 500, 4500, 2102), Track(2102, 2002, Clip(12, 0, 0, 0, 5000)))
                 .Media(11, 6).Media(12, 5);
                var ctx = f.Play(3000);
                Check.True(ctx != null, "context");
                f.Run(0.05);
                var a = f.Voices.ForMedia(11).Single();
                Check.Near(100.3, a.Spec.StartAt, 1e-6, "first segment starts at its entry");
                Check.Near(1.0, a.Spec.SourceOffset, 1e-6, "pre-entry of the first segment skipped");
                Check.Near(4.0, a.Spec.PlayLength, 1e-6, "post-exit cut (rule)");
                Check.True(a.Spec.Music, "music voice");
                Check.Equal(2101u, a.Spec.NodeId, "node is the track");
                Check.Equal(77u, a.Spec.PlayingId, "playing id");
                f.Run(4.0);
                var b = f.Voices.ForMedia(12).Single();
                Check.Near(104.3 - 0.5, b.Spec.StartAt, 1e-6, "next entry on previous exit, pre-entry played");
                Check.Near(0.0, b.Spec.SourceOffset, 1e-6, "from the clip start");
                Check.Near(5.0, b.Spec.PlayLength, 1e-6, "final segment rings out");
                Check.True(f.Sink.Count("music", "segment") == 2, "segment traces");
            }
        }

        [PaTest]
        public void FinitePlaylistEndCallsOnMusicEnded()
        {
            using (var f = new MusicFixture())
            {
                f.Add(Segment(1000, 0, 2000, 0, 2000, 1100), Track(1100, 1000, Clip(1, 0, 0, 0, 2000))).Media(1, 2);
                var ctx = f.Play(1000);
                f.Run(1.0);
                Check.Equal(0, f.EndedContexts.Count, "still playing");
                Check.True(!ctx.Ended, "not ended");
                f.Run(2.0);
                Check.Equal(1, f.EndedContexts.Count, "ended once");
                Check.True(ctx.Ended, "ended");
                Check.Equal(0, f.Player.Active.Count(), "no active contexts");
                f.Run(1.0);
                Check.Equal(1, f.EndedContexts.Count, "only once");
                Check.Equal(1, f.Sink.Count("music", "end"), "end trace");
            }
        }

        [PaTest]
        public void LevelStateChanges()
        {
            using (var f = LevelSwitch(Rule(-1, -1, "exitMarker", "entryMarker")))
            {
                var ctx = f.Play(4000);
                f.Run(1.0);
                Check.Near(100.3, f.Voices.ForMedia(41)[0].Spec.StartAt, 1e-6, "LEVEL1 plays");
                f.SetState(LevelGroup, Level2);
                f.Run(5.0);
                var l2 = f.Voices.Sounding(42);
                Check.True(l2.Count >= 1, "LEVEL2 plays");
                Check.Near(104.3, l2[0].Spec.StartAt, 1e-6, "switched at the exit marker");
                Check.True(f.Voices.Sounding(41).All(v => v.StartTime < 104.3 - 1e-6), "no LEVEL1 after the switch");
                f.SetState(LevelGroup, Level2Stop);   // now 106: current LEVEL2 instance exits at 108.3
                f.Run(4.0);
                Check.True(f.Voices.Sounding(42).All(v => v.StartTime < 108.3 - 1e-6), "LEVEL2_STOP: nothing after the exit");
                Check.True(f.Voices.All.All(v => v.State == PaVoiceState.Ended), "silence");
                Check.True(!ctx.Ended, "a switch on nothing keeps listening");
                f.SetState(LevelGroup, Level1);   // now 110
                f.Run(1.0);
                var again = f.Voices.Sounding(41).Where(v => v.StartTime > 109).ToList();
                Check.True(again.Count == 1, "LEVEL1 again");
                Check.Near(110.3, again[0].Spec.StartAt, 0.02, "from silence right away");
                Check.Equal(3, f.Sink.Count("music", "transition"), "transition traces");
                Check.True(ctx.Involves(4000) && ctx.Involves(4101) && ctx.Involves(4202 - 1) && ctx.Involves(4102), "involves played nodes");
            }
        }

        [PaTest]
        public void SameStateValueDoesNothing()
        {
            using (var f = LevelSwitch(Rule(-1, -1, "immediate", "entryMarker")))
            {
                f.Play(4000);
                f.Run(1.0);
                f.SetState(LevelGroup, Level1);
                f.Player.OnStateChanged(999, 1);
                f.Run(1.0);
                Check.Equal(0, f.Sink.Count("music", "transition"), "no transition");
            }
        }

        [PaTest]
        public void ContinuePlayingKeepsSelectedChild()
        {
            var f = new MusicFixture();
            using (f)
            {
                var sw = Switch(4000, 0, new[] { new PaMusicArg { Group = LevelGroup, IsState = true } },
                    new[] { Path(4100, Level1), Path(4100, Level2) }, 4100);
                sw.Music.ContinuePlaying = true;
                sw.Music.Transitions = new[] { Rule(-1, -1, "immediate", "entryMarker") };
                f.Add(sw);
                Looping(f, 4100, 4000, 4101, 41);
                f.World.States[LevelGroup] = Level1;
                f.Play(4000);
                f.Run(1.0);
                var v = f.Voices.ForMedia(41)[0];
                f.SetState(LevelGroup, Level2);
                f.Run(1.0);
                Check.Equal(0, f.Sink.Count("music", "transition"), "kept playing");
                Check.True(v.State == PaVoiceState.Playing, "voice untouched");
            }
        }

        [PaTest]
        public void ExitMarkerTransitionWithFades()
        {
            using (var f = LevelSwitch(Rule(-1, -1, "exitMarker", "entryMarker", srcFadeMs: 500, srcFadeOffsetMs: 500, dstFadeMs: 200)))
            {
                f.Play(4000);
                f.Run(1.0);
                f.SetState(LevelGroup, Level2);
                f.Run(4.0);
                var src = f.Voices.ForMedia(41)[0];
                var fade = src.Find("fade", 1);
                Check.True(fade != null, "source fade on slot 1");
                Check.Near(-200, fade.Db, 0.01, "to silence");
                Check.Equal(500, fade.Ms, "source fade length");
                Check.Near(103.8, fade.Now, 0.011, "fade starts 500 ms before the exit");
                Check.Near(104.3, fade.Now + fade.Ms / 1000.0, 0.011, "fade ends at the sync point");
                var stop = src.Ops.First(o => o.Kind == "stopAt");
                Check.Near(104.3, stop.At, 1e-6, "source stops at the exit");
                var dst = f.Voices.Sounding(42)[0];
                Check.Near(104.3, dst.Spec.StartAt, 1e-6, "destination at the exit");
                Check.True(dst.Has("gain", 1, -200), "destination starts silent");
                var din = dst.Find("fade", 1);
                Check.True(din != null && din.Ms <= 200 && din.Ms >= 190 && din.Db == 0f, "destination fades in over 200 ms");
                Check.Near(104.3, din.Now, 0.011, "fade-in at the sync point");
            }
        }

        [PaTest]
        public void NextBeatFadeOutEndsOnTheBeat()
        {
            // real-data shape: nextBeat, fade 1000 ms with offset 1000 ms
            using (var f = LevelSwitch(Rule(-1, -1, "nextBeat", "entryMarker", srcFadeMs: 1000, srcFadeOffsetMs: 1000)))
            {
                f.Play(4000);
                f.Run(1.1);                         // now 101.1; the fade needs 1 s, so the first beat >= 102.1
                f.SetState(LevelGroup, Level2);
                f.Run(2.5);
                var dst = f.Voices.Sounding(42)[0];
                Check.Near(102.3, dst.Spec.StartAt, 1e-6, "sync pushed to the beat the fade fits before");
                var src = f.Voices.ForMedia(41)[0];
                var fade = src.Find("fade", 1);
                Check.True(fade != null, "source fade on slot 1");
                Check.Near(101.3, fade.Now, 0.011, "fade starts 1 s before the beat");
                Check.Near(102.3, fade.Now + fade.Ms / 1000.0, 0.011, "fade ends on the beat");
                Check.Near(102.3, src.Ops.Last(o => o.Kind == "stopAt").At, 1e-6, "source stops at the sync");
            }
        }

        [PaTest]
        public void ImmediateFadeOutStartsNow()
        {
            // real-data shape: immediate, fade 2000 ms with offset 2000 ms; the sync is not delayed
            using (var f = LevelSwitch(Rule(-1, -1, "immediate", "entryMarker", srcFadeMs: 2000, srcFadeOffsetMs: 2000)))
            {
                f.Play(4000);
                f.Run(1.0);                         // now 101.0, sync 101.3
                f.SetState(LevelGroup, Level2);
                f.Run(3.0);
                var dst = f.Voices.Sounding(42)[0];
                Check.Near(101.3, dst.Spec.StartAt, 1e-6, "destination after the lead");
                var src = f.Voices.ForMedia(41)[0];
                var fade = src.Find("fade", 1);
                Check.True(fade != null, "source fade on slot 1");
                Check.Near(101.0, fade.Now, 0.011, "fade start clamped to now, never after the sync");
                Check.True(fade.Ms >= 1990 && fade.Ms <= 2000, "full fade length");
                Check.Near(103.0, src.Ops.Last(o => o.Kind == "stopAt").At, 1e-6, "source stops when the fade ends");
            }
        }

        [PaTest]
        public void DestinationFadeOffsetDoesNotDelayFadeIn()
        {
            using (var f = LevelSwitch(Rule(-1, -1, "exitMarker", "entryMarker", dstFadeMs: 500, dstFadeOffsetMs: 500)))
            {
                f.Play(4000);
                f.Run(1.0);
                f.SetState(LevelGroup, Level2);
                f.Run(4.0);
                var dst = f.Voices.Sounding(42)[0];
                Check.Near(104.3, dst.Spec.StartAt, 1e-6, "destination at the exit");
                Check.True(dst.Has("gain", 1, -200), "destination starts silent");
                var din = dst.Find("fade", 1);
                Check.True(din != null && din.Db == 0f, "destination fades in");
                Check.Near(104.3, din.Now, 0.011, "fade-in starts when the destination starts");
                Check.True(din.Ms >= 490 && din.Ms <= 500, "fade-in heard in full");
            }
        }

        [PaTest]
        public void FadeStartHelpers()
        {
            var src = new PaTransitionSrc { Sync = "nextBeat", FadeMs = 1000, FadeOffsetMs = 1000 };
            Check.Near(9.0, PaMusicTransitions.SourceFadeStart(src, 10.0, 5.0), 1e-9, "offset before the sync");
            Check.Near(9.5, PaMusicTransitions.SourceFadeStart(src, 10.0, 9.5), 1e-9, "clamped to now");
            Check.Near(10.0, PaMusicTransitions.SourceFadeStart(null, 10.0, 5.0), 1e-9, "no rule: at the sync");
            Check.Near(6.0, PaMusicTransitions.EarliestSync(src, 5.3, 5.0), 1e-9, "grid sync leaves room for the fade");
            Check.Near(5.3, PaMusicTransitions.EarliestSync(new PaTransitionSrc { Sync = "immediate", FadeMs = 1000, FadeOffsetMs = 1000 }, 5.3, 5.0), 1e-9, "immediate not moved");
            Check.Near(5.3, PaMusicTransitions.EarliestSync(new PaTransitionSrc { Sync = "exitMarker", FadeMs = 1000, FadeOffsetMs = 1000 }, 5.3, 5.0), 1e-9, "exit not moved");
            var dst = new PaTransitionDst { FadeMs = 500, FadeOffsetMs = 500 };
            Check.Near(10.0, PaMusicTransitions.DestinationFadeStart(dst, 10.0, 10.0), 1e-9, "not before the destination sounds");
            Check.Near(9.5, PaMusicTransitions.DestinationFadeStart(dst, 10.0, 8.0), 1e-9, "into pre-entry");
        }

        [PaTest]
        public void NextBeatTransitionSync()
        {
            using (var f = LevelSwitch(Rule(-1, -1, "nextBeat", "entryMarker")))
            {
                f.Play(4000);
                f.Run(1.1);                         // now 101.1, earliest sync 101.4
                f.SetState(LevelGroup, Level2);
                f.Run(2.0);
                var dst = f.Voices.Sounding(42)[0];
                Check.Near(101.8, dst.Spec.StartAt, 1e-6, "next beat after the lead (120 bpm from entry 100.3)");
                var src = f.Voices.ForMedia(41)[0];
                Check.Near(101.8, src.Ops.First(o => o.Kind == "stopAt").At, 1e-6, "source cut on the beat");
                Check.Near(101.8 - 0.01, src.EndTime, 0.011, "source ended on the beat");
            }
        }

        [PaTest]
        public void NextBarTransitionSync()
        {
            using (var f = LevelSwitch(Rule(-1, -1, "nextBar", "entryMarker")))
            {
                f.Play(4000);
                f.Run(0.6);                         // earliest 100.9, next bar 102.3
                f.SetState(LevelGroup, Level2);
                f.Run(2.5);
                Check.Near(102.3, f.Voices.Sounding(42)[0].Spec.StartAt, 1e-6, "next bar");
            }
        }

        [PaTest]
        public void SameTimeEntryKeepsOffset()
        {
            using (var f = LevelSwitch(Rule(-1, -1, "immediate", "sameTime")))
            {
                f.Play(4000);
                f.Run(2.0);                         // now 102.0
                f.SetState(LevelGroup, Level2);
                f.Run(1.0);
                var dst = f.Voices.Sounding(42)[0];
                Check.Near(102.3, dst.Spec.StartAt, 1e-6, "immediate (plus lead)");
                Check.Near(2.0, dst.Spec.SourceOffset, 1e-6, "same position as the source");
                Check.Near(2.0, dst.Spec.PlayLength, 1e-6, "rest of the segment");
            }
        }

        [PaTest]
        public void StingerOnNextBeatAndDontRepeat()
        {
            using (var f = new MusicFixture())
            {
                Looping(f, 3000, 0, 3001, 31);
                f.Add(Segment(6100, 0, 1000, 0, 1000, 6101), Track(6101, 6100, Clip(61, 0, 0, 0, 1000))).Media(61, 1);
                f.Get(3000).Music.Stingers = new[] { new PaStinger { Trigger = 777, Segment = 6100, Sync = "nextBeat", DontRepeatMs = 2000 } };
                f.Play(3000);
                f.Run(1.1);                          // now 101.1
                Check.Equal(1, f.Player.PostTrigger(777, 1), "stinger scheduled");
                Check.Equal(0, f.Player.PostTrigger(778, 1), "unknown trigger");
                Check.Equal(0, f.Player.PostTrigger(777, 2), "other object");
                Check.Near(101.8, f.Voices.ForMedia(61).Single().Spec.StartAt, 1e-6, "on the next beat");
                f.Run(0.4);
                Check.Equal(0, f.Player.PostTrigger(777, PaIds.GlobalObject), "do not repeat within 2 s");
                f.Run(1.7);                          // now 103.2
                Check.Equal(1, f.Player.PostTrigger(777, PaIds.GlobalObject), "fires again after 2 s");
                Check.Equal(2, f.Voices.ForMedia(61).Count, "two stinger voices");
                Check.Near(103.8, f.Voices.ForMedia(61)[1].Spec.StartAt, 1e-6, "next beat again");
            }
        }

        [PaTest]
        public void PauseAndResumeShiftTheTimeline()
        {
            using (var f = new MusicFixture())
            {
                f.Add(RanSeq(3000, 0, Group("sequenceContinuous", 0, Leaf(3001)), 3001),
                      Segment(3001, 3000, 4000, 0, 4000, 3002),
                      Track(3002, 3001, Clip(71, 0, 0, 0, 4000), Clip(72, 3000, 0, 0, 1000)))
                 .Media(71, 4).Media(72, 1);
                var ctx = f.Play(3000);
                f.Run(1.0);                          // now 101
                var first = f.Voices.ForMedia(71)[0];
                var late = f.Voices.ForMedia(72)[0];
                Check.Near(103.3, late.Spec.StartAt, 1e-6, "second clip scheduled");
                f.Player.Pause(ctx, 0, PaCurve.Linear);
                f.Run(2.0);                          // now 103
                Check.True(first.State == PaVoiceState.Paused, "playing voice paused");
                Check.True(late.State == PaVoiceState.Ended && !late.Started, "unstarted voice cancelled");
                f.Player.Resume(ctx, 0, PaCurve.Linear);
                Check.True(first.State == PaVoiceState.Playing, "resumed");
                var again = f.Voices.ForMedia(72);
                Check.Equal(2, again.Count, "rescheduled");
                Check.Near(105.3, again[1].Spec.StartAt, 1e-6, "shifted by the paused time");
                f.Run(3.5);                          // now 106.5
                var next = f.Voices.ForMedia(71);
                Check.True(next.Count >= 2, "next loop");
                Check.Near(106.3, next[1].Spec.StartAt, 1e-6, "next segment shifted too");
                Check.Near(106.3, first.EndTime, 0.011, "first voice ended late by the pause");
            }
        }

        [PaTest]
        public void StopFadesAndEnds()
        {
            using (var f = new MusicFixture())
            {
                Looping(f, 3000, 0, 3001, 31);
                var ctx = f.Play(3000);
                f.Run(1.0);
                f.Player.Stop(ctx, 300, PaCurve.Linear);
                var v = f.Voices.ForMedia(31)[0];
                Check.Equal(300, v.Ops.First(o => o.Kind == "stop").Ms, "stop fade");
                f.Run(0.2);
                Check.Equal(0, f.EndedContexts.Count, "fading");
                f.Run(0.3);
                Check.Equal(1, f.EndedContexts.Count, "ended after the fade");
                Check.True(f.Voices.All.All(x => x.State == PaVoiceState.Ended), "all voices ended");
            }
        }

        [PaTest]
        public void FirstSegmentWaitsForClips()
        {
            using (var f = new MusicFixture())
            {
                Looping(f, 3000, 0, 3001, 31);
                f.Voices.NotReady.Add(31);
                f.Play(3000);
                f.Run(0.5);
                Check.Equal(0, f.Voices.All.Count, "waiting for media");
                Check.True(f.Voices.Prefetched.Contains(31), "prefetched");
                f.Voices.NotReady.Clear();
                f.Run(0.1);                          // ready at 100.51
                Check.Near(100.81, f.Voices.ForMedia(31)[0].Spec.StartAt, 1e-6, "starts once ready");
            }
            using (var f = new MusicFixture())
            {
                Looping(f, 3000, 0, 3001, 31);
                f.Voices.NotReady.Add(31);
                f.Play(3000);
                f.Run(2.5);
                Check.Equal(1, f.Voices.All.Count, "gave up waiting after 2 s");
                Check.Near(102.3, f.Voices.All[0].Spec.StartAt, 0.011, "timeout start");
            }
        }

        [PaTest]
        public void PlayFadeInAndClipAutomation()
        {
            using (var f = new MusicFixture())
            {
                f.Add(Segment(1000, 0, 4000, 0, 4000, 1100), Track(1100, 1000, Clip(1, 0, 0, 0, 4000))).Media(1, 4);
                f.Get(1100).Music.Automation = new[]
                {
                    new PaAutomation { Clip = 0, Type = "fadeIn", Points = new[] { new PaPoint(0, 0, PaCurve.Linear), new PaPoint(500, 1, PaCurve.Linear) } },
                    new PaAutomation { Clip = 0, Type = "fadeOut", Points = new[] { new PaPoint(0, 1, PaCurve.Linear), new PaPoint(1000, 0, PaCurve.Linear) } },
                };
                f.Play(1000, fadeInMs: 1000);
                f.Run(4.5);
                var v = f.Voices.ForMedia(1)[0];
                Check.True(v.Has("gain", 1, -200), "play fade-in starts silent (slot 1)");
                Check.True(v.Ops.Any(o => o.Kind == "fade" && o.Slot == 1 && o.Db == 0f && o.Ms >= 990), "play fade-in");
                Check.True(v.Has("gain", 2, -200), "clip fade-in starts silent (slot 2)");
                var fin = v.Ops.First(o => o.Kind == "fade" && o.Slot == 2 && o.Db == 0f);
                Check.Near(100.3, fin.Now, 0.011, "clip fade-in at the clip start");
                var fout = v.Ops.Last(o => o.Kind == "fade" && o.Slot == 2);
                Check.Near(-200, fout.Db, 0.01, "clip fade-out to silence");
                Check.Near(103.3, fout.Now, 0.011, "clip fade-out 1 s before the clip end");
            }
        }

        [PaTest]
        public void SwitchOnOtherObjectIgnored()
        {
            using (var f = new MusicFixture())
            {
                var sw = Switch(4000, 0, new[] { new PaMusicArg { Group = 600 } }, new[] { Path(4100, 1), Path(4200, 2) }, 4100, 4200);
                sw.Music.Transitions = new[] { Rule(-1, -1, "immediate", "entryMarker") };
                f.Add(sw);
                Looping(f, 4100, 4000, 4101, 41);
                Looping(f, 4200, 4000, 4201, 42);
                f.World.Switches["600/5"] = 1;
                f.Play(4000, go: 5);
                f.Run(0.5);
                f.SetSwitch(600, 2, 6);
                f.Run(0.5);
                Check.Equal(0, f.Voices.ForMedia(42).Count, "other object");
                f.SetSwitch(600, 2, 5);
                f.Run(0.5);
                Check.Equal(1, f.Voices.ForMedia(42).Count, "own object switches");
            }
        }

        // An 8 s intro, then a 155 s segment that loops forever (longer than HorizonSec + 60 s).
        static MusicFixture IntroThenLongLoop()
        {
            var f = new MusicFixture();
            var rs = RanSeq(5000, 0, Group("sequenceContinuous", 1, Leaf(5001), Leaf(5002, 0)), 5001, 5002);
            rs.Music.Transitions = new[] { Rule(-1, -1, "exitMarker", "entryMarker", postExit: false, preEntry: true) };
            f.Add(rs,
                Segment(5001, 5000, 8000, 0, 8000, 5101), Track(5101, 5001, Clip(51, 0, 0, 0, 8000)),
                Segment(5002, 5000, 155000, 0, 155000, 5102), Track(5102, 5002, Clip(52, 0, 0, 0, 155000)))
             .Media(51, 8).Media(52, 155);
            return f;
        }

        [PaTest]
        public void LongSegmentAfterShortOneStartsOnTime()
        {
            using (var f = IntroThenLongLoop())
            {
                f.Play(5000);
                f.Run(0.05);
                double a = f.Voices.ForMedia(51).Single().Spec.StartAt;
                f.Run(9.0, 0.05);
                var b = f.Voices.ForMedia(52);
                Check.Equal(1, b.Count, "long segment realized before its entry");
                Check.Near(a + 8.0, b[0].Spec.StartAt, 1e-6, "long segment starts at its entry");
                Check.Near(0.0, b[0].Spec.SourceOffset, 1e-6, "from the clip start");
                f.Run(160.0, 0.05);
                var loops = f.Voices.ForMedia(52);
                Check.True(loops.Count >= 2, "second loop iteration realized");
                Check.Near(a + 8.0 + 155.0, loops[1].Spec.StartAt, 1e-6, "second loop iteration starts on time");
                Check.Near(0.0, loops[1].Spec.SourceOffset, 1e-6, "second loop from the clip start");
                Check.Equal(0, f.Sink.Count("music", "late"), "nothing late");
            }
        }

        [PaTest]
        public void LateRealizeIsTraced()
        {
            using (var f = IntroThenLongLoop())
            {
                f.Play(5000);
                f.Run(5.0, 5.0);   // first tick at 105: the intro is placed
                f.Run(10.0, 5.0);  // ticks at 110 and 115: the long segment (entry 108.x) is realized late
                var b = f.Voices.ForMedia(52);
                Check.Equal(1, b.Count, "long segment realized");
                Check.True(b[0].Spec.SourceOffset > 0.5, "joined mid-clip");
                Check.True(f.Sink.Count("music", "late") >= 1, "late realize traced");
            }
        }

        [PaTest]
        public void PlayRejectsNonMusicNodes()
        {
            using (var f = new MusicFixture())
            {
                f.Add(Node(9, 0, PaNodeType.Sound, null));
                Check.True(f.Play(9) == null, "sound node");
                Check.True(f.Play(12345) == null, "unknown node");
                Check.True(!f.Player.IsMusicNode(9), "not music");
            }
        }
    }
}
