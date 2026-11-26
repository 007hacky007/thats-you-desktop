// Voice state machine of the PortAudio mixer (design 6.1 IPaVoice, 7.5). It is pure: the
// owner (PaUnityVoices, or a test) supplies the voice clock, reports load completion and the
// actual start, and calls Advance once per frame. The core keeps the state, the fade-in, the
// stop/pause/resume fades, the four gain slots with FadeTo ramps, the played content position
// and the predicted EndTime with loops and pitch. Virtual voices (Unity audio not running) end
// from that prediction alone, so end-of-event timing still holds without audio.
//
// States: Loading -> Scheduled -> Playing -> (Stopping) -> Ended, plus Paused from any live
// state. A Scheduled voice may already be "armed" (a source scheduled for a future StartTime).
// Fades interpolate linear amplitude with the PaCurves shape. StopAt(t) behaves like Stop
// called at t: the fade starts at t.

using System;

namespace PortAudio
{
    public sealed class PaVoiceCore : IPaVoice
    {
        public const int SlotCount = 4;

        /// <summary>Linear-amplitude ramp over the voice clock.</summary>
        struct Ramp
        {
            public float From, To; public double T0, Dur; public PaCurve Curve; public bool Active;
            public float Value(double now)
            {
                if (!Active || Dur <= 0 || now >= T0 + Dur) return To;
                if (now <= T0) return From;
                return From + (To - From) * (float)PaCurves.Shape(Curve, (now - T0) / Dur);
            }
            public bool Done(double now) { return !Active || Dur <= 0 || now >= T0 + Dur; }
            public static Ramp Start(float from, float to, double t0, int ms, PaCurve c)
            {
                return new Ramp { From = from, To = to, T0 = t0, Dur = Math.Max(0, ms) / 1000.0, Curve = c, Active = true };
            }
            public static Ramp Hold(float v) { return new Ramp { From = v, To = v, Active = false }; }
        }

        readonly int id;
        readonly PaVoiceSpec spec;
        readonly Func<double> clock;
        readonly Ramp[] slots = new Ramp[SlotCount];

        PaVoiceState state;
        PaVoiceState prePause;
        PaVoiceEnd endReason;
        double startTime = double.NaN, targetStart, lastAdvance = double.NaN, endedAt = double.NaN;
        double contentPos;          // seconds of source content played (in "total" space)
        double pitchRatio = 1.0;
        bool armed;                 // a start time is fixed (StartTime valid)
        bool isVirtual;
        double pausedRemaining;     // Paused before the audible start: delay left to StartTime
        Ramp stopRamp = Ramp.Hold(1f), pauseRamp = Ramp.Hold(1f);
        double stopEnd = double.PositiveInfinity;
        double pendingStopAt = double.NaN; int pendingStopMs; PaCurve pendingStopCurve = PaCurve.Linear;

        public PaVoiceCore(int id, PaVoiceSpec spec, Func<double> clock)
        {
            this.id = id;
            this.spec = spec ?? new PaVoiceSpec();
            this.clock = clock ?? (() => 0.0);
            for (int i = 0; i < SlotCount; i++) slots[i] = Ramp.Hold(1f);
            double now = Now;
            CreatedAt = now;
            targetStart = Math.Max(this.spec.StartAt, now);
            pitchRatio = PaGainModel.PitchRatio(this.spec.PitchCents);
            ClipSeconds = ClipLength(this.spec);
            TotalSeconds = Total(this.spec, ClipSeconds);
            if (this.spec.Media == null && this.spec.Pcm == null) { state = PaVoiceState.Loading; End(PaVoiceEnd.NoMedia); }
            else state = PaVoiceState.Loading;
        }

        double Now { get { return clock(); } }

        // ---- static helpers ----

        /// <summary>Clip length in seconds: Media.Duration, else PCM frames / rate, else NaN.</summary>
        public static double ClipLength(PaVoiceSpec s)
        {
            if (s == null) return double.NaN;
            if (s.Media != null) return s.Media.Rate > 0 && s.Media.Samples > 0 ? s.Media.Duration : double.NaN;
            if (s.Pcm != null && s.PcmRate > 0) return s.Pcm.Length / (double)Math.Max(1, s.PcmChannels) / s.PcmRate;
            return double.NaN;
        }

        /// <summary>Content seconds the voice plays: the first pass from SourceOffset plus
        /// (LoopCount - 1) full passes, +Inf for LoopCount 0; PlayLength > 0 cuts it.</summary>
        public static double Total(PaVoiceSpec s, double clip)
        {
            double natural;
            if (double.IsNaN(clip)) natural = double.NaN;
            else if (s.LoopCount <= 0) natural = clip > 0 ? double.PositiveInfinity : 0;
            else natural = Math.Max(0, clip - Math.Max(0, s.SourceOffset)) + (s.LoopCount - 1) * clip;
            if (s.PlayLength > 0) return double.IsNaN(natural) || double.IsInfinity(natural) ? s.PlayLength : Math.Min(s.PlayLength, natural);
            return natural;
        }

        // ---- IPaVoice ----

        public int Id { get { return id; } }
        public PaVoiceSpec Spec { get { return spec; } }
        public PaVoiceState State { get { return state; } }
        public double StartTime { get { return armed ? startTime : double.NaN; } }

        public double EndTime
        {
            get
            {
                if (state == PaVoiceState.Ended) return endedAt;
                if (state == PaVoiceState.Paused) return double.PositiveInfinity;
                double now = Now;
                double e;
                double rate = pitchRatio > 0 ? pitchRatio : 1.0;
                if (double.IsNaN(TotalSeconds)) e = double.NaN;
                else if (double.IsPositiveInfinity(TotalSeconds)) e = double.PositiveInfinity;
                else if (!armed) e = Math.Max(targetStart, now) + TotalSeconds / rate;
                else
                {
                    double from = double.IsNaN(lastAdvance) ? startTime : Math.Max(lastAdvance, startTime);
                    e = from + Math.Max(0, TotalSeconds - contentPos) / rate;
                }
                if (state == PaVoiceState.Stopping) e = double.IsNaN(e) ? stopEnd : Math.Min(e, stopEnd);
                if (!double.IsNaN(pendingStopAt))
                {
                    double ps = pendingStopAt + pendingStopMs / 1000.0;
                    e = double.IsNaN(e) ? ps : Math.Min(e, ps);
                }
                return e;
            }
        }

        public double Position
        {
            get
            {
                if (!armed || state == PaVoiceState.Loading) return double.NaN;
                double now = Now;
                if (now < startTime && state != PaVoiceState.Paused) return double.NaN;
                double c = contentPos;
                if ((state == PaVoiceState.Playing || state == PaVoiceState.Stopping) && !double.IsNaN(lastAdvance) && now > lastAdvance)
                    c += (now - Math.Max(lastAdvance, startTime)) * pitchRatio;
                if (!double.IsNaN(TotalSeconds) && c > TotalSeconds) c = TotalSeconds;
                return ClipPosition(c);
            }
        }

        public void Stop(int fadeMs, PaCurve curve)
        {
            if (state == PaVoiceState.Ended) return;
            double now = Now;
            bool audible = armed && now >= startTime && (state == PaVoiceState.Playing || state == PaVoiceState.Stopping);
            if (!audible || fadeMs <= 0) { End(PaVoiceEnd.Stopped); return; }
            BeginStop(now, fadeMs, curve);
        }

        public void StopAt(double time, int fadeMs, PaCurve curve)
        {
            if (state == PaVoiceState.Ended) return;
            if (time <= Now) { Stop(fadeMs, curve); return; }
            if (!double.IsNaN(pendingStopAt) && pendingStopAt <= time) return;
            pendingStopAt = time; pendingStopMs = Math.Max(0, fadeMs); pendingStopCurve = curve;
        }

        public void Pause(int fadeMs, PaCurve curve)
        {
            if (state == PaVoiceState.Ended || state == PaVoiceState.Paused || state == PaVoiceState.Stopping) return;
            double now = Now;
            prePause = state;
            if (state == PaVoiceState.Loading || state == PaVoiceState.Scheduled || (armed && now < startTime))
            {
                prePause = state == PaVoiceState.Loading ? PaVoiceState.Loading : PaVoiceState.Scheduled;
                pausedRemaining = Math.Max(0, (armed ? startTime : targetStart) - now);
                armed = false;                      // a scheduled source must be cancelled; re-armed on Resume
                state = PaVoiceState.Paused;
                return;
            }
            pauseRamp = Ramp.Start(pauseRamp.Value(now), 0f, now, fadeMs, curve);
            state = PaVoiceState.Paused;
        }

        public void Resume(int fadeMs, PaCurve curve)
        {
            if (state != PaVoiceState.Paused) return;
            double now = Now;
            if (prePause == PaVoiceState.Loading || prePause == PaVoiceState.Scheduled)
            {
                targetStart = now + pausedRemaining;
                state = prePause;
                return;
            }
            pauseRamp = Ramp.Start(pauseRamp.Value(now), 1f, now, fadeMs, curve);
            if (double.IsNaN(lastAdvance) || lastAdvance < now) lastAdvance = now; // paused time does not count
            state = PaVoiceState.Playing;
        }

        public void SetGainDb(int slot, float db)
        {
            if (slot < 0 || slot >= SlotCount) return;
            slots[slot] = Ramp.Hold(PaDb.ToLinear(db));
        }

        public void FadeTo(int slot, float db, int ms, PaCurve curve)
        {
            if (slot < 0 || slot >= SlotCount) return;
            if (ms <= 0) { SetGainDb(slot, db); return; }
            double now = Now;
            slots[slot] = Ramp.Start(slots[slot].Value(now), PaDb.ToLinear(db), now, ms, curve);
        }

        // ---- owner API ----

        public double CreatedAt { get; private set; }
        public double ClipSeconds { get; private set; }
        public double TotalSeconds { get; private set; }
        public PaVoiceEnd EndReason { get { return endReason; } }
        public bool IsEnded { get { return state == PaVoiceState.Ended; } }
        public bool IsVirtual { get { return isVirtual; } }
        public bool Armed { get { return armed; } }
        public double PitchRatioNow { get { return pitchRatio; } }
        /// <summary>When the voice should start on the voice clock (StartAt, or creation time for asap; shifted by pauses).</summary>
        public double TargetStart { get { return targetStart; } }
        /// <summary>Seconds the start was late against TargetStart (0 when on time).</summary>
        public double LateSeconds { get; private set; }
        /// <summary>Content seconds played so far.</summary>
        public double ContentPosition { get { return contentPos; } }
        /// <summary>True while the source must be paused (pause fade finished).</summary>
        public bool SourcePaused
        {
            get { return state == PaVoiceState.Paused && prePause != PaVoiceState.Loading && prePause != PaVoiceState.Scheduled && pauseRamp.Done(Now); }
        }
        /// <summary>True while a paused voice had not started yet (its scheduled source must be cancelled).</summary>
        public bool PausedBeforeStart
        {
            get { return state == PaVoiceState.Paused && (prePause == PaVoiceState.Loading || prePause == PaVoiceState.Scheduled); }
        }

        /// <summary>Loading -> Scheduled when the clip is ready (a paused voice stays paused).</summary>
        public void MarkReady()
        {
            if (state == PaVoiceState.Loading) state = PaVoiceState.Scheduled;
            else if (state == PaVoiceState.Paused && prePause == PaVoiceState.Loading) prePause = PaVoiceState.Scheduled;
        }

        /// <summary>Fix the start. actualStart is when the source starts on the voice clock (in the
        /// future for PlayScheduled). Music voices that start late keep their timeline and are
        /// seeked forward by the lateness. Returns the source position to seek to (seconds into
        /// the clip), or NaN when nothing is left to play (the voice then ends Finished).</summary>
        public double Begin(double actualStart, bool virtualVoice)
        {
            if (state != PaVoiceState.Scheduled && state != PaVoiceState.Loading) return double.NaN;
            isVirtual = virtualVoice;
            double late = actualStart - targetStart;
            LateSeconds = late > 0.0005 ? late : 0;
            contentPos = 0;
            if (spec.Music && LateSeconds > 0)
            {
                startTime = targetStart;
                contentPos = LateSeconds * pitchRatio;
            }
            else startTime = actualStart;
            armed = true;
            state = PaVoiceState.Scheduled;
            lastAdvance = contentPos > 0 ? actualStart : double.NaN;   // a late music seek is valid at actualStart
            if (contentPos > 0 && !double.IsNaN(TotalSeconds) && contentPos >= TotalSeconds)
            {
                End(PaVoiceEnd.Finished);
                return double.NaN;
            }
            if (Now >= startTime) state = PaVoiceState.Playing;
            return ClipPosition(contentPos);
        }

        /// <summary>Clip position (seconds) for a content position: the first pass runs from
        /// SourceOffset to the clip end, later passes from 0.</summary>
        public double ClipPosition(double content)
        {
            double p = Math.Max(0, spec.SourceOffset) + content;
            double clip = ClipSeconds;
            if (!double.IsNaN(clip) && clip > 0 && p >= clip)
            {
                p = (p - clip) % clip;
                if (spec.LoopCount == 1) p = clip;  // a single pass stays at the end
            }
            return p;
        }

        /// <summary>Advance to now with the current pitch ratio: pending StopAt, armed start,
        /// content integration, stop fade completion and the virtual end.</summary>
        public void Advance(double now, double pitch)
        {
            if (state == PaVoiceState.Ended) return;
            if (!(pitch > 0) || double.IsNaN(pitch)) pitch = 1.0;

            if (!double.IsNaN(pendingStopAt) && now >= pendingStopAt)
            {
                double at = pendingStopAt;
                pendingStopAt = double.NaN;
                bool audible = armed && at >= startTime && state != PaVoiceState.Paused;
                if (!audible || pendingStopMs <= 0) { Integrate(Math.Min(now, at), pitch); End(PaVoiceEnd.Stopped, at); return; }
                Integrate(at, pitch);
                BeginStop(at, pendingStopMs, pendingStopCurve);
            }

            if (state == PaVoiceState.Scheduled && armed && now >= startTime) state = PaVoiceState.Playing;
            Integrate(now, pitch);
            pitchRatio = pitch;

            if (state == PaVoiceState.Stopping && now >= stopEnd) { End(PaVoiceEnd.Stopped, stopEnd); return; }
            if (isVirtual && armed && (state == PaVoiceState.Playing || state == PaVoiceState.Stopping))
            {
                double total = double.IsNaN(TotalSeconds) ? 0 : TotalSeconds;
                if (contentPos >= total) End(PaVoiceEnd.Finished);
            }
        }

        /// <summary>Set the pitch ratio applied from now on (used by EndTime and the next Advance
        /// interval's caller); Advance integrates the elapsed interval with the ratio it is given.</summary>
        public void SetPitchRatio(double pitch)
        {
            if (pitch > 0 && !double.IsNaN(pitch) && !double.IsInfinity(pitch)) pitchRatio = pitch;
        }

        void Integrate(double now, double pitch)
        {
            bool running = armed && (state == PaVoiceState.Playing || state == PaVoiceState.Stopping
                || (state == PaVoiceState.Paused && prePause != PaVoiceState.Loading && prePause != PaVoiceState.Scheduled && !pauseRamp.Done(lastAdvance)));
            if (running)
            {
                double from = double.IsNaN(lastAdvance) ? startTime : Math.Max(lastAdvance, startTime);
                if (now > from) contentPos += (now - from) * pitch;
            }
            if (double.IsNaN(lastAdvance) || now > lastAdvance) lastAdvance = now;
        }

        void BeginStop(double t0, int ms, PaCurve curve)
        {
            double end = t0 + ms / 1000.0;
            if (state == PaVoiceState.Stopping && end >= stopEnd) return;
            stopRamp = Ramp.Start(stopRamp.Value(t0), 0f, t0, ms, curve);
            stopEnd = end;
            state = PaVoiceState.Stopping;
        }

        /// <summary>End the voice now (first reason wins).</summary>
        public void End(PaVoiceEnd reason) { End(reason, Now); }

        void End(PaVoiceEnd reason, double at)
        {
            if (state == PaVoiceState.Ended) return;
            endReason = reason;
            endedAt = at;
            state = PaVoiceState.Ended;
        }

        /// <summary>Sum of the gain slots in dB.</summary>
        public float SlotsDb(double now)
        {
            float db = 0f;
            for (int i = 0; i < SlotCount; i++) db += PaDb.FromLinear(slots[i].Value(now));
            return db;
        }

        /// <summary>Product of fade-in, stop and pause fades (linear, 0..1).</summary>
        public float FadeLinear(double now)
        {
            float g = stopRamp.Value(now) * pauseRamp.Value(now);
            if (spec.FadeInMs > 0 && armed)
            {
                double t = (now - startTime) / (spec.FadeInMs / 1000.0);
                g *= (float)PaCurves.Shape(spec.FadeInCurve, t);
            }
            return g;
        }

        /// <summary>Slots plus fades in dB (the gain model's voiceDb term).</summary>
        public float VoiceDb(double now) { return SlotsDb(now) + PaDb.FromLinear(FadeLinear(now)); }
    }
}
