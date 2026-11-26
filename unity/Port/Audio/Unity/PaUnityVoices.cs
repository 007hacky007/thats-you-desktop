// Unity voices for the PortAudio engine (design 6.1 IPaVoices, 6.6, 7.5): converted media
// plays through pooled AudioSources with the Wwise-like gain model of PaGainModel/PaBusState,
// on a voice clock taken from AudioSettings.dspTime.
//
// - Construction raises Unity's real voice count (AudioSettings.Reset) before any audio plays
//   when it is below the configured one.
// - AudioRunning: Unity audio may be disabled (ProjectSettings m_DisableAudio) or have no
//   device. If AudioSettings.unityAudioDisabled says so, or dspTime does not advance within 1 s,
//   the voices fall back to realtime "virtual" playback: nothing is audible, but every voice
//   lives for its known duration (media length, loops, pitch), so end-of-event timing holds.
//   Voices wait (Scheduled) while this is still undecided, which is at most 1 s after start.
// - Tick (main thread, once per frame from PaEngine.Tick): clip loads, voice starts
//   (PlayScheduled for future starts, Play otherwise, timeSamples for offsets, late music seeks),
//   per-frame mix parameters, end detection, then listener callbacks. Play may be called from
//   inside the callbacks; new voices join on the next Tick and listeners are never called from Play.
// - Clip progress: a "clips" trace line about once per second while clips are queued or loading
//   (with "gated" while preloads wait for a scene load), and one when the queue drains.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PortAudio.Unity
{
    public sealed class PaUnityVoices : IPaVoices
    {
        sealed class Voice
        {
            public PaVoiceCore Core;
            public PaGainModel Model;
            public IPaVoiceListener Listener;
            public PaClipCache.Entry Clip;
            public AudioClip PcmClip;
            public PaVoiceSlot Slot;
            public bool StartTraced, ClampTraced, SourcePaused, NeedsEndSchedule;
            public float LastGainDb = -200f;
            public double LastPitch = 1.0;
            public double ScheduledEnd = double.NaN;
        }

        const double DetectSeconds = 1.0;
        const double StartFudge = 0.002;         // starts closer than this count as "now"
        const double EndGraceSeconds = 0.5;      // predicted-end fallback for real voices
        const double LateTraceSeconds = 0.05;
        const double ClipTraceSeconds = 1.0;

        readonly PaMixerConfig config;
        readonly PaVoicePool pool;
        readonly PaClipCache cache;
        readonly List<Voice> voices = new List<Voice>();
        readonly List<Voice> added = new List<Voice>();
        readonly List<Voice> ended = new List<Voice>();
        readonly List<uint> liveBuses = new List<uint>();
        PaBusState buses;
        IPaWorld busWorld;
        int nextId;
        int realVoices;
        bool decided, running, shutdown;
        double dsp0, real0, virtBase, realBase;
        double lastTickNow = double.NaN;
        double lastClipTrace = double.NaN;
        bool clipsBusy;
        readonly double scheduleAhead;

        public PaUnityVoices(PaMixerConfig config)
        {
            this.config = config ?? new PaMixerConfig();
            pool = new PaVoicePool(PaVoicePool.DefaultLimit);
            cache = new PaClipCache(this.config);
            scheduleAhead = Math.Max(0.02, this.config.LookAheadSec) + 0.5;

            bool disabled = UnityAudioDisabled();
            if (!disabled) EnsureRealVoices(this.config.RealVoices);
            try { realVoices = AudioSettings.GetConfiguration().numRealVoices; } catch (Exception) { realVoices = 0; }

            dsp0 = AudioSettings.dspTime;
            real0 = Time.realtimeSinceStartupAsDouble;
            if (disabled) Decide(false, "Unity audio is disabled");
        }

        // ---- clock and audio detection ----

        static bool UnityAudioDisabled()
        {
            try
            {
                var p = typeof(AudioSettings).GetProperty("unityAudioDisabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(null, null);
            }
            catch (Exception) { }
            return false;
        }

        static void EnsureRealVoices(int wanted)
        {
            try
            {
                var ac = AudioSettings.GetConfiguration();
                if (wanted <= 0 || ac.numRealVoices >= wanted) return;
                ac.numRealVoices = wanted;
                if (ac.numVirtualVoices < Math.Max(512, wanted)) ac.numVirtualVoices = Math.Max(512, wanted);
                if (!AudioSettings.Reset(ac)) PaLog.Warn("AudioSettings.Reset failed; real voices stay at " + AudioSettings.GetConfiguration().numRealVoices);
            }
            catch (Exception e) { PaLog.Warn("could not raise the real voice count: " + e.Message); }
        }

        void Decide(bool audio, string why)
        {
            if (decided) return;
            decided = true;
            running = audio;
            if (!audio)
            {
                virtBase = AudioSettings.dspTime;
                realBase = Time.realtimeSinceStartupAsDouble;
                PaLog.Warn("Unity audio is not running (" + why + "); voices play virtually on the realtime clock");
            }
            PaLog.Trace("init", "audioRunning", audio, "realVoices", realVoices);
        }

        void Detect()
        {
            if (decided) return;
            double d = AudioSettings.dspTime;
            if (d > dsp0 + 1e-9) { Decide(true, null); return; }
            if (Time.realtimeSinceStartupAsDouble - real0 >= DetectSeconds) Decide(false, "dspTime did not advance");
        }

        public double Now
        {
            get
            {
                if (decided && !running) return virtBase + (Time.realtimeSinceStartupAsDouble - realBase);
                return AudioSettings.dspTime;
            }
        }

        public bool AudioRunning { get { return !decided || running; } }

        bool Virtual { get { return decided && !running; } }

        public int RealVoiceCount { get { return realVoices; } }
        public int ClipsLoaded { get { return cache.LoadedCount; } }
        public int ClipsLoading { get { return cache.LoadingCount; } }
        public double StreamMb { get { return cache.StreamBytes / (1024.0 * 1024.0); } }
        public int SourcesInUse { get { return pool.InUse; } }

        // ---- IPaVoices ----

        public IPaVoice Play(PaVoiceSpec spec, IPaVoiceListener listener)
        {
            if (spec == null) spec = new PaVoiceSpec();
            var core = new PaVoiceCore(++nextId, spec, () => Now);
            var v = new Voice { Core = core, Listener = listener, LastPitch = core.PitchRatioNow };
            if (shutdown) core.End(PaVoiceEnd.Shutdown);
            if (!core.IsEnded)
            {
                v.Model = new PaGainModel(PaRuntime.World, spec);
                if (spec.Pcm == null && spec.Media != null && !Virtual) v.Clip = cache.Acquire(spec.Media);
            }
            added.Add(v);
            return core;
        }

        public void Preload(IEnumerable<PaMediaEntry> media, object owner)
        {
            if (Virtual || shutdown) return;
            cache.Preload(media, owner);
        }

        public void ReleaseOwner(object owner) { cache.ReleaseOwner(owner); }

        public void Prefetch(PaMediaEntry media)
        {
            if (media == null || Virtual || shutdown) return;
            cache.Prefetch(media);
        }

        public bool IsReady(PaMediaEntry media)
        {
            if (media == null || Virtual) return true;
            return cache.IsReady(media);
        }

        public void StopAll(int fadeMs)
        {
            foreach (var v in voices) v.Core.Stop(fadeMs, PaCurve.Linear);
            foreach (var v in added) v.Core.Stop(fadeMs, PaCurve.Linear);
        }

        public IEnumerable<IPaVoice> Active
        {
            get
            {
                var list = new List<IPaVoice>(voices.Count + added.Count);
                foreach (var v in voices) if (!v.Core.IsEnded) list.Add(v.Core);
                foreach (var v in added) if (!v.Core.IsEnded) list.Add(v.Core);
                return list;
            }
        }

        public void Shutdown()
        {
            shutdown = true;
            voices.AddRange(added);
            added.Clear();
            foreach (var v in voices)
            {
                v.Core.End(PaVoiceEnd.Shutdown);
                Release(v);
            }
            voices.Clear();
            pool.Destroy();
            cache.Clear();
        }

        // ---- Tick ----

        public void Tick()
        {
            if (shutdown) return;
            Detect();
            if (!Virtual) { cache.Tick(); TraceClips(); }
            if (added.Count > 0) { voices.AddRange(added); added.Clear(); }

            double now = Now;
            double dt = double.IsNaN(lastTickNow) ? 0 : Math.Max(0, now - lastTickNow);
            lastTickNow = now;

            var world = PaRuntime.World;
            if (buses == null || !ReferenceEquals(busWorld, world)) { buses = new PaBusState(world); busWorld = world; }
            liveBuses.Clear();
            foreach (var v in voices)
                if (v.Model != null && Audible(v.Core)) liveBuses.Add(v.Model.OutputBus);
            buses.BeginFrame(now, liveBuses);

            for (int i = 0; i < voices.Count; i++)
            {
                var v = voices[i];
                try { Update(v, now); }
                catch (Exception e)
                {
                    PaLog.Error("voice " + v.Core.Id + " failed: " + e.Message);
                    v.Core.End(PaVoiceEnd.Stopped);
                }
            }
            buses.EndFrame(now, dt);

            // collect, then notify (listeners may Play or Stop; new voices join next Tick)
            ended.Clear();
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                var v = voices[i];
                if (!v.Core.IsEnded) continue;
                voices.RemoveAt(i);
                Release(v);
                TraceEnd(v);
                ended.Add(v);
            }
            for (int i = ended.Count - 1; i >= 0; i--)
            {
                var v = ended[i];
                if (v.Listener == null) continue;
                try { v.Listener.OnVoiceEnded(v.Core, v.Core.EndReason); }
                catch (Exception e) { PaLog.Error("voice listener failed: " + e); }
            }
            ended.Clear();
        }

        static bool Audible(PaVoiceCore c)
        {
            var s = c.State;
            if (s == PaVoiceState.Playing || s == PaVoiceState.Stopping) return true;
            return s == PaVoiceState.Paused && !c.PausedBeforeStart && !c.SourcePaused;
        }

        void Update(Voice v, double now)
        {
            var c = v.Core;
            if (c.IsEnded) return;

            // loading
            if (c.State == PaVoiceState.Loading || (c.State == PaVoiceState.Paused && c.PausedBeforeStart))
            {
                if (c.Spec.Pcm != null || Virtual) c.MarkReady();
                else if (v.Clip == null) { if (decided) Fail(v, PaVoiceEnd.LoadFailed, "no media path"); return; }
                else if (v.Clip.State == PaClipCache.LoadState.Ready) c.MarkReady();
                else if (v.Clip.State == PaClipCache.LoadState.Failed)
                {
                    if (!decided) return;           // may still turn out virtual
                    if (Virtual) c.MarkReady();
                    else { Fail(v, PaVoiceEnd.LoadFailed, v.Clip.Error); return; }
                }
                if (c.State == PaVoiceState.Loading) return;
            }

            // a voice paused before its audible start gives its source back
            if (c.PausedBeforeStart)
            {
                if (v.Slot != null) { pool.Release(v.Slot); v.Slot = null; v.ScheduledEnd = double.NaN; }
                return;
            }

            // starting
            if (c.State == PaVoiceState.Scheduled && !c.Armed)
            {
                if (!decided) return;
                if (Virtual)
                {
                    if (now + StartFudge < c.TargetStart) return;
                    c.Begin(now, true);
                    TraceStart(v);
                }
                else
                {
                    if (c.TargetStart - now > scheduleAhead) return;
                    if (!StartReal(v, now)) return;
                }
                if (c.IsEnded) return;
            }

            // per-frame parameters
            c.Advance(now, v.LastPitch);
            if (c.IsEnded) return;
            PaMix mix = v.Model != null ? v.Model.Evaluate(buses, c.VoiceDb(now), config.TrimDb) : Fallback(c, now);
            v.LastGainDb = mix.Muted ? -200f : mix.GainDb;
            v.LastPitch = mix.PitchRatio;
            c.SetPitchRatio(mix.PitchRatio);
            if (mix.Clamped && !v.ClampTraced)
            {
                v.ClampTraced = true;
                PaLog.Trace("voice", "op", "clamp", "voice", c.Id, "playingId", c.Spec.PlayingId, "node", c.Spec.NodeId, "gainDb", mix.GainDb);
            }
            if (v.Model != null && Audible(c) && !mix.Muted) buses.MeterInput(v.Model.OutputBus, mix.PreBusDb);

            if (v.Slot != null) Apply(v, mix, now);
        }

        PaMix Fallback(PaVoiceCore c, double now)
        {
            bool clamped;
            var m = new PaMix();
            m.GainDb = c.VoiceDb(now) + c.Spec.VolumeDb + config.TrimDb;
            m.Volume = PaGainModel.VolumeFromDb(m.GainDb, out clamped);
            m.Clamped = clamped;
            m.PitchCents = c.Spec.PitchCents;
            m.PitchRatio = PaGainModel.PitchRatio(m.PitchCents);
            m.Lpf = Math.Max(0f, Math.Min(100f, c.Spec.Lpf));
            m.CutoffHz = PaGainModel.CutoffHz(m.Lpf);
            m.LpfEnabled = m.Lpf >= PaGainModel.LpfOffBelow;
            return m;
        }

        bool StartReal(Voice v, double now)
        {
            var c = v.Core;
            AudioClip clip;
            if (c.Spec.Pcm != null)
            {
                if (v.PcmClip == null) v.PcmClip = PaClipCache.CreatePcmClip(c.Spec.Pcm, c.Spec.PcmRate, c.Spec.PcmChannels, "pa:input" + c.Id);
                clip = v.PcmClip;
            }
            else clip = v.Clip != null ? v.Clip.Clip : null;
            if (clip == null) { Fail(v, PaVoiceEnd.LoadFailed, "no clip"); return false; }

            if (v.Slot == null)
            {
                v.Slot = pool.Acquire();
                if (v.Slot == null && Steal(v)) v.Slot = pool.Acquire();
                if (v.Slot == null) return false;   // wait for a free source; the start will be late
            }

            var src = v.Slot.Source;
            src.clip = clip;
            // pitch first (a late music seek scales with it), then the gain with fades and slots
            var mix = v.Model != null ? v.Model.Evaluate(buses, 0f, config.TrimDb) : Fallback(c, now);
            v.LastPitch = mix.PitchRatio;
            c.SetPitchRatio(mix.PitchRatio);

            bool future = c.TargetStart > now + StartFudge;
            double seek = c.Begin(future ? c.TargetStart : now, false);
            if (double.IsNaN(seek)) return true;        // nothing left to play; ended Finished
            double at = Math.Max(now, c.StartTime);
            mix = v.Model != null ? v.Model.Evaluate(buses, c.VoiceDb(at), config.TrimDb) : Fallback(c, at);
            v.LastGainDb = mix.Muted ? -200f : mix.GainDb;

            int frames = Math.Max(1, clip.samples);
            int pos = (int)Math.Round(seek * clip.frequency);
            if (pos < 0) pos = 0;
            if (pos >= frames) pos = frames - 1;
            src.loop = c.Spec.LoopCount != 1;
            src.timeSamples = pos;
            src.volume = mix.Muted ? 0f : mix.Volume;
            src.pitch = mix.PitchRatio;
            src.panStereo = mix.Pan;
            src.priority = UnityPriority(v.Model);
            ApplyFilter(v.Slot, mix);
            if (future) src.PlayScheduled(c.TargetStart);
            else src.Play();

            v.NeedsEndSchedule = c.Spec.LoopCount != 1 || c.Spec.PlayLength > 0;
            v.ScheduledEnd = double.NaN;
            UpdateScheduledEnd(v);
            TraceStart(v);
            return true;
        }

        static int UnityPriority(PaGainModel model)
        {
            float p = model == null ? 50f : model.Priority;
            int u = (int)Math.Round(128.0 - (p - 50.0) * 2.56);
            return u < 0 ? 0 : (u > 256 ? 256 : u);
        }

        static void ApplyFilter(PaVoiceSlot slot, PaMix mix)
        {
            var f = slot.Filter;
            if (f == null) return;
            if (f.enabled != mix.LpfEnabled) f.enabled = mix.LpfEnabled;
            if (mix.LpfEnabled && Math.Abs(f.cutoffFrequency - mix.CutoffHz) > 0.5f) f.cutoffFrequency = mix.CutoffHz;
        }

        void UpdateScheduledEnd(Voice v)
        {
            if (!v.NeedsEndSchedule || v.Slot == null) return;
            double e = v.Core.EndTime;
            if (double.IsNaN(e) || double.IsInfinity(e)) return;
            if (!double.IsNaN(v.ScheduledEnd) && Math.Abs(e - v.ScheduledEnd) < 0.002) return;
            v.ScheduledEnd = e;
            v.Slot.Source.SetScheduledEndTime(e);
        }

        void Apply(Voice v, PaMix mix, double now)
        {
            var c = v.Core;
            var src = v.Slot.Source;
            if (src == null) { c.End(PaVoiceEnd.Stopped); return; }

            src.volume = mix.Muted ? 0f : mix.Volume;
            if (Math.Abs(src.pitch - mix.PitchRatio) > 1e-4f) src.pitch = mix.PitchRatio;
            if (Math.Abs(src.panStereo - mix.Pan) > 1e-4f) src.panStereo = mix.Pan;
            ApplyFilter(v.Slot, mix);

            if (c.SourcePaused)
            {
                if (!v.SourcePaused) { src.Pause(); v.SourcePaused = true; }
                return;
            }
            if (v.SourcePaused)
            {
                src.UnPause();
                v.SourcePaused = false;
                v.ScheduledEnd = double.NaN;    // the end moved by the paused time
            }
            UpdateScheduledEnd(v);

            // end detection: the source stopped by itself, with a predicted-end fallback
            if ((c.State == PaVoiceState.Playing || c.State == PaVoiceState.Stopping) && now >= c.StartTime + 0.05)
            {
                if (!src.isPlaying) { c.End(PaVoiceEnd.Finished); return; }
                double e = c.EndTime;
                if (!double.IsNaN(e) && !double.IsInfinity(e) && now > e + EndGraceSeconds) c.End(PaVoiceEnd.Finished);
            }
        }

        void Fail(Voice v, PaVoiceEnd reason, string why)
        {
            var c = v.Core;
            if (c.IsEnded) return;
            c.End(reason);
            var m = c.Spec.Media;
            PaLog.Trace("voice", "op", "fail", "voice", c.Id, "playingId", c.Spec.PlayingId, "node", c.Spec.NodeId,
                "media", m == null ? 0u : m.Id, "file", m == null ? null : m.File, "reason", why ?? reason.ToString());
        }

        bool Steal(Voice forVoice)
        {
            Voice victim = null;
            foreach (var v in voices)
            {
                if (v == forVoice || v.Slot == null || v.Core.IsEnded) continue;
                if (victim == null || v.LastGainDb < victim.LastGainDb) victim = v;
            }
            if (victim == null) return false;
            PaLog.Trace("voice", "op", "steal", "voice", victim.Core.Id, "playingId", victim.Core.Spec.PlayingId,
                "node", victim.Core.Spec.NodeId, "gainDb", victim.LastGainDb, "reason", "pool full, for voice " + forVoice.Core.Id);
            victim.Core.End(PaVoiceEnd.Stolen);
            pool.Release(victim.Slot);
            victim.Slot = null;
            return true;
        }

        void Release(Voice v)
        {
            if (v.Slot != null) { pool.Release(v.Slot); v.Slot = null; }
            if (v.Clip != null) { cache.ReleaseUse(v.Clip); v.Clip = null; }
            if (v.PcmClip != null) { UnityEngine.Object.Destroy(v.PcmClip); v.PcmClip = null; }
        }

        void TraceClips()
        {
            if (!PaLog.Tracing) return;
            int queued = cache.QueuedCount, inFlight = cache.InFlightCount;
            bool busy = queued + inFlight > 0;
            if (!busy && !clipsBusy) return;
            double t = Time.realtimeSinceStartupAsDouble;
            if (busy && clipsBusy && !double.IsNaN(lastClipTrace) && t - lastClipTrace < ClipTraceSeconds) return;
            clipsBusy = busy;
            lastClipTrace = t;
            PaLog.Trace("clips", "queued", queued, "loading", inFlight, "loaded", cache.LoadedCount, "gated", cache.Gated);
        }

        void TraceStart(Voice v)
        {
            if (v.StartTraced) return;
            v.StartTraced = true;
            if (!PaLog.Tracing) return;
            var c = v.Core;
            var m = c.Spec.Media;
            double lateMs = Math.Round(c.LateSeconds * 1000.0, 1);
            PaLog.Trace("voice", "op", "start", "voice", c.Id, "playingId", c.Spec.PlayingId, "node", c.Spec.NodeId,
                "media", m == null ? 0u : m.Id, "file", m == null ? (c.Spec.Pcm != null ? "pcm" : null) : m.File,
                "lateMs", lateMs, "gainDb", Math.Round(v.LastGainDb, 2), "virtual", c.IsVirtual);
            if (c.LateSeconds >= LateTraceSeconds)
                PaLog.Trace("voice", "op", "late", "voice", c.Id, "playingId", c.Spec.PlayingId, "node", c.Spec.NodeId,
                    "media", m == null ? 0u : m.Id, "lateMs", lateMs);
        }

        void TraceEnd(Voice v)
        {
            if (!PaLog.Tracing) return;
            var c = v.Core;
            var r = c.EndReason;
            if (r == PaVoiceEnd.NoMedia && !v.StartTraced)
            {
                PaLog.Trace("voice", "op", "fail", "voice", c.Id, "playingId", c.Spec.PlayingId, "node", c.Spec.NodeId,
                    "media", 0u, "file", null, "reason", "NoMedia");
            }
            PaLog.Trace("voice", "op", "end", "voice", c.Id, "playingId", c.Spec.PlayingId, "node", c.Spec.NodeId,
                "media", c.Spec.Media == null ? 0u : c.Spec.Media.Id, "reason", r.ToString());
        }
    }
}
