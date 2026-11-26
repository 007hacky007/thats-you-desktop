using System;

namespace PortAudio.Tests
{
    public static class PaVoiceCoreTests
    {
        static void Check(bool ok, string what) { MixFix.Check(ok, what); }
        static void Near(double w, double g, double t, string what) { MixFix.Near(w, g, t, what); }

        sealed class Clock { public double T; public double Now() { return T; } }

        // 2 s clip at 48 kHz
        static PaVoiceSpec Spec2s()
        {
            return new PaVoiceSpec { NodeId = 1, PlayingId = 7,
                Media = new PaMediaEntry { Id = 3, Rate = 48000, Channels = 2, Samples = 96000, Format = "ogg" } };
        }

        static PaVoiceCore Started(Clock c, PaVoiceSpec s, bool isVirtual)
        {
            var v = new PaVoiceCore(1, s, c.Now);
            v.MarkReady();
            v.Begin(Math.Max(c.T, v.TargetStart), isVirtual);
            return v;
        }

        [PaTest]

        public static void StatesAndNoMedia()
        {
            var c = new Clock { T = 1 };
            var v = new PaVoiceCore(1, new PaVoiceSpec(), c.Now);
            Check(v.State == PaVoiceState.Ended && v.EndReason == PaVoiceEnd.NoMedia, "no media ends at once");
            v = new PaVoiceCore(2, Spec2s(), c.Now);
            Check(v.State == PaVoiceState.Loading, "loading");
            Check(double.IsNaN(v.StartTime) && double.IsNaN(v.Position), "unknown before start");
            v.MarkReady();
            Check(v.State == PaVoiceState.Scheduled, "scheduled when ready");
            double seek = v.Begin(1, false);
            Near(0, seek, 1e-9, "no offset");
            Check(v.State == PaVoiceState.Playing, "playing");
            Near(1, v.StartTime, 1e-9, "start time");
            var pcm = new PaVoiceSpec { Pcm = new float[48000], PcmRate = 24000, PcmChannels = 2 };
            Near(1.0, PaVoiceCore.ClipLength(pcm), 1e-9, "pcm clip length");
        }

        [PaTest]

        public static void EndTimeWithLoopsAndPitch()
        {
            var c = new Clock { T = 0 };
            var s = Spec2s();
            s.LoopCount = 3; s.SourceOffset = 0.5; s.StartAt = 10;
            var v = new PaVoiceCore(1, s, c.Now);
            Near(5.5, v.TotalSeconds, 1e-9, "1.5 + 2 * 2");
            Near(15.5, v.EndTime, 1e-9, "predicted before load");
            v.MarkReady();
            Near(0.5, v.Begin(10, false), 1e-9, "seek to the source offset");
            Check(v.State == PaVoiceState.Scheduled && v.Armed, "armed for the future");
            Near(15.5, v.EndTime, 1e-9, "armed end");
            c.T = 10.5; v.Advance(c.T, 1.0);
            Check(v.State == PaVoiceState.Playing, "starts at StartAt");
            c.T = 11.0; v.Advance(c.T, 2.0);
            Near(0.5 + 1.0, v.ContentPosition, 1e-9, "0.5 s at 1x, 0.5 s at 2x");
            Near(11.0 + 4.0 / 2.0, v.EndTime, 1e-9, "remaining at the new pitch");
            Near(0.0, v.Position, 1e-9, "position wraps into the second pass (0.5 + 1.5 -> 0)");
            c.T = 11.25; v.Advance(c.T, 2.0);
            Near(0.5, v.Position, 1e-9, "second pass runs from 0");

            s = Spec2s(); s.LoopCount = 0;
            v = new PaVoiceCore(2, s, c.Now);
            Check(double.IsPositiveInfinity(v.EndTime), "infinite loop");
            s.PlayLength = 3;
            Near(3.0, PaVoiceCore.Total(s, 2.0), 1e-9, "PlayLength cuts an infinite loop");
            s = Spec2s(); s.PlayLength = 1.25;
            Near(1.25, PaVoiceCore.Total(s, 2.0), 1e-9, "PlayLength cut");
            s.PlayLength = 5;
            Near(2.0, PaVoiceCore.Total(s, 2.0), 1e-9, "PlayLength longer than the clip");
            s = Spec2s(); s.PitchCents = 1200; c.T = 0;
            v = new PaVoiceCore(3, s, c.Now);
            Near(1.0, v.EndTime, 1e-9, "spec pitch halves the duration");
            Check(double.IsNaN(new PaVoiceCore(4, new PaVoiceSpec { Media = new PaMediaEntry() }, c.Now).EndTime), "unknown length");
        }

        [PaTest]

        public static void FadeShapes()
        {
            var c = new Clock { T = 0 };
            var v = Started(c, Spec2s(), false);
            v.FadeTo(1, -200f, 1000, PaCurve.Linear);
            Near(0.0, v.SlotsDb(0), 1e-5, "fade starts at the current value");
            Near(20 * Math.Log10(0.5), v.SlotsDb(0.5), 1e-4, "linear amplitude at half time");
            Check(v.SlotsDb(1.0) <= -200f, "silent at the end");
            c.T = 1;
            v.FadeTo(1, 0f, 1000, PaCurve.SCurve);
            Near(-200, v.SlotsDb(1.0), 1e-3, "from silence");
            Near(20 * Math.Log10(0.5), v.SlotsDb(1.5), 1e-4, "S-curve half way");
            v.SetGainDb(1, 0f);
            c.T = 2;
            v.FadeTo(2, -200f, 1000, PaCurve.SCurve);
            Near(20 * Math.Log10(1 - (1 - Math.Cos(Math.PI * 0.25)) / 2), v.SlotsDb(2.25), 1e-4, "S-curve fade out");
            v.SetGainDb(2, -6f);
            v.SetGainDb(0, -3f);
            v.SetGainDb(9, -50f);
            Near(-9.0, v.SlotsDb(2.25), 1e-4, "slots add, bad slot ignored");
            v.FadeTo(0, -9f, 0, PaCurve.Linear);
            Near(-15.0, v.SlotsDb(2.25), 1e-4, "FadeTo 0 ms sets at once");

            c.T = 0;
            var s = Spec2s(); s.FadeInMs = 1000; s.FadeInCurve = PaCurve.Log3;
            v = Started(c, s, false);
            Near(1 - Math.Pow(0.5, 3), v.FadeLinear(0.5), 1e-5, "Log3 fade-in");
            Near(1.0, v.FadeLinear(1.5), 1e-6, "fade-in done");
        }

        [PaTest]

        public static void StopWithFadeAndBeforeStart()
        {
            var c = new Clock { T = 0 };
            var v = Started(c, Spec2s(), false);
            c.T = 0.5; v.Advance(c.T, 1);
            v.Stop(400, PaCurve.Linear);
            Check(v.State == PaVoiceState.Stopping, "stopping");
            Near(0.9, v.EndTime, 1e-9, "end at the fade end");
            Near(0.5, v.FadeLinear(0.7), 1e-5, "stop fade");
            c.T = 0.8; v.Advance(c.T, 1);
            Check(v.State == PaVoiceState.Stopping, "still fading");
            c.T = 0.92; v.Advance(c.T, 1);
            Check(v.State == PaVoiceState.Ended && v.EndReason == PaVoiceEnd.Stopped, "ended stopped");
            Near(0.9, v.EndTime, 1e-9, "ended at the fade end");

            var s = Spec2s(); s.StartAt = 5;
            v = new PaVoiceCore(2, s, c.Now);
            v.MarkReady(); v.Begin(5, false);
            v.Stop(1000, PaCurve.Linear);
            Check(v.State == PaVoiceState.Ended && v.EndReason == PaVoiceEnd.Stopped, "a voice that has not started stops at once");
            v = new PaVoiceCore(3, Spec2s(), c.Now);
            v.Stop(100, PaCurve.Linear);
            Check(v.IsEnded, "loading voice stops at once");
            v.End(PaVoiceEnd.Stolen);
            Check(v.EndReason == PaVoiceEnd.Stopped, "first reason wins");
        }

        [PaTest]

        public static void StopAtSchedulesTheFade()
        {
            var c = new Clock { T = 0 };
            var v = Started(c, Spec2s(), true);
            v.StopAt(1.0, 200, PaCurve.Linear);
            Near(1.2, v.EndTime, 1e-9, "EndTime honours StopAt");
            c.T = 0.9; v.Advance(c.T, 1);
            Check(v.State == PaVoiceState.Playing, "not yet");
            c.T = 1.1; v.Advance(c.T, 1);
            Check(v.State == PaVoiceState.Stopping, "fade began at 1.0");
            Near(0.5, v.FadeLinear(1.1), 1e-5, "half way at 1.1");
            c.T = 1.25; v.Advance(c.T, 1);
            Check(v.IsEnded && v.EndReason == PaVoiceEnd.Stopped, "stopped");
            var s = Spec2s(); s.StartAt = 3;
            v = new PaVoiceCore(2, s, c.Now);
            v.MarkReady(); v.Begin(3, false);
            v.StopAt(2.0, 0, PaCurve.Linear);
            c.T = 2.5; v.Advance(c.T, 1);
            Check(v.IsEnded && v.EndReason == PaVoiceEnd.Stopped, "stopped before its start");
        }

        [PaTest]

        public static void VirtualTimingEndsOnPrediction()
        {
            var c = new Clock { T = 100 };
            var s = Spec2s(); s.LoopCount = 2;
            var v = Started(c, s, true);
            Check(v.IsVirtual, "virtual");
            Near(104, v.EndTime, 1e-9, "two passes");
            int frames = 0;
            while (!v.IsEnded && frames < 1000) { c.T += 1.0 / 30; frames++; v.Advance(c.T, 1); }
            Check(v.EndReason == PaVoiceEnd.Finished, "finished");
            Near(4.0, frames / 30.0, 1.0 / 30 + 1e-9, "ended within a frame of the prediction");

            c.T = 0;
            s = Spec2s(); s.PitchCents = -1200;
            v = Started(c, s, true);
            c.T = 3.9; v.Advance(c.T, 0.5);
            Check(!v.IsEnded, "half speed: not yet");
            c.T = 4.01; v.Advance(c.T, 0.5);
            Check(v.IsEnded, "half speed: 4 s");

            c.T = 0;
            v = Started(c, Spec2s(), false);
            c.T = 10; v.Advance(c.T, 1);
            Check(!v.IsEnded, "real voices wait for the source to stop");
        }

        [PaTest]

        public static void PauseResumeShiftsTheEnd()
        {
            var c = new Clock { T = 0 };
            var s = Spec2s(); s.Media.Samples = 3 * 48000;
            var v = Started(c, s, true);
            c.T = 1; v.Advance(c.T, 1);
            v.Pause(0, PaCurve.Linear);
            Check(v.State == PaVoiceState.Paused && v.SourcePaused, "paused at once");
            Check(double.IsPositiveInfinity(v.EndTime), "no end while paused");
            c.T = 3; v.Advance(c.T, 1);
            Near(1.0, v.ContentPosition, 1e-9, "paused time does not count");
            v.Resume(500, PaCurve.Linear);
            Near(0.0, v.FadeLinear(3.0), 1e-6, "resume fades in");
            Near(0.5, v.FadeLinear(3.25), 1e-6, "resume fade half way");
            Near(5.0, v.EndTime, 1e-9, "end shifted by the pause");
            while (!v.IsEnded && c.T < 10) { c.T += 0.05; v.Advance(c.T, 1); }
            Near(5.0, c.T, 0.051, "ended at 5 s");

            c.T = 0;
            v = Started(c, Spec2s(), true);
            v.Pause(1000, PaCurve.Linear);
            Check(!v.SourcePaused, "pause fade running");
            Near(0.5, v.FadeLinear(0.5), 1e-6, "pause fade");
            c.T = 1.0; v.Advance(c.T, 1);
            Check(v.SourcePaused, "source paused after the fade");

            c.T = 0;
            s = Spec2s(); s.StartAt = 2;
            v = new PaVoiceCore(9, s, c.Now);
            v.MarkReady(); v.Begin(2, false);
            c.T = 1; v.Pause(0, PaCurve.Linear);
            Check(v.PausedBeforeStart && !v.Armed, "paused before start disarms");
            c.T = 4; v.Resume(0, PaCurve.Linear);
            Check(v.State == PaVoiceState.Scheduled, "back to scheduled");
            Near(5.0, v.TargetStart, 1e-9, "start shifted by the pause");
        }

        [PaTest]

        public static void MusicLateStartSeeks()
        {
            var c = new Clock { T = 0 };
            var s = Spec2s(); s.StartAt = 10; s.SourceOffset = 0.25; s.Music = true;
            var v = new PaVoiceCore(1, s, c.Now);
            v.MarkReady();
            c.T = 10.2;
            double seek = v.Begin(c.T, false);
            Near(10.0, v.StartTime, 1e-9, "music keeps its timeline");
            Near(0.2, v.LateSeconds, 1e-9, "lateness");
            Near(0.45, seek, 1e-9, "seeked by the lateness");
            Near(11.75, v.EndTime, 1e-9, "end unchanged");

            s = Spec2s(); s.StartAt = 10;
            v = new PaVoiceCore(2, s, c.Now);
            v.MarkReady();
            seek = v.Begin(c.T, false);
            Near(10.2, v.StartTime, 1e-9, "sfx starts late");
            Near(0.0, seek, 1e-9, "from the beginning");

            s = Spec2s(); s.StartAt = 1; s.Music = true;
            v = new PaVoiceCore(3, s, () => 0.0);
            v.MarkReady();
            Check(double.IsNaN(v.Begin(5.0, false)) && v.IsEnded && v.EndReason == PaVoiceEnd.Finished, "too late: nothing left");
        }
    }
}
