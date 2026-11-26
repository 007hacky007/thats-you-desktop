// PaEngine: the managed replacement for the Wwise sound engine behind AkSoundEnginePINVOKE.
//
// Modes
//   real   converted data (StreamingAssets/PortAudio/manifest.json or -portAudioRoot) loaded:
//          events, actions, containers, game syncs, voices (PaRuntime.Voices) and interactive
//          music (PaRuntime.Music) behave like Wwise 2015.1 for what the game uses.
//   silent no platform, no manifest or -portAudioOff: exactly the old PortAudioNull behaviour
//          (every call succeeds, PostEvent hands out a fresh id and EndOfEvent follows on the
//          next callback flush, bank requests succeed and report AK_Bank).
//
// Threading
//   Every entry point takes one engine lock, records what was asked (a state change and/or a
//   command in an ordered queue) and returns; synchronous answers (PostEvent's playing id or 0,
//   SeekOnEvent's existence check, GetState...) come from data under that lock. Finalizers
//   (UnregisterGameObject from ~AkAutoObject) only queue work. Voices, music and anything
//   Unity are touched only in Tick, on the main thread, once per frame (PaDriver.LateUpdate, or
//   RenderAudio if it comes first in a frame), in this order:
//     1 drain commands   2 RTPC ramps   3 delayed actions and continuous containers
//     4 music tick       5 voices tick (voice ends)   6 completed instances -> EndOfEvent
//     7 stats trace every 10 s
//   Wwise applies posted commands at RenderAudio too, so the one-frame latency is faithful.

using System;
using System.Collections.Generic;
using System.IO;

namespace PortAudio
{
    internal sealed class PaStats
    {
        public long Posts, PostsUnknown, VoicesStarted, VoicesFinished, VoicesStopped, VoicesFailed;
        public long InstancesCompleted, EndOfEvent, BankCallbacks, BanksLoaded, MusicPlays, Unsupported, Ticks;
    }

    internal sealed partial class PaCore
    {
        public readonly IPaPlatform platform;
        public readonly PaSettings settings;
        public readonly PaBankStore banks;
        public readonly PaMediaIndex media;
        public readonly PaWorld world;
        public readonly IPaVoices voices;
        public readonly IPaMusic music;
        public readonly PaRandom random;
        public readonly PaCommandQueue commands = new PaCommandQueue();
        public readonly PaStats stats = new PaStats();
        readonly HashSet<string> warned = new HashSet<string>();
        readonly List<KeyValuePair<uint, ulong>> switchChanges = new List<KeyValuePair<uint, ulong>>();
        readonly List<uint> stateChanges = new List<uint>();
        double lastStats = double.NaN;

        public PaCore(IPaPlatform platform, PaSettings settings, PaBankStore banks, PaMediaIndex media, PaWorld world, IPaVoices voices, IPaMusic music)
        {
            this.platform = platform;
            this.settings = settings;
            this.banks = banks;
            this.media = media;
            this.world = world;
            this.voices = voices;
            this.music = music;
            random = world.Random;
        }

        public void WarnOnce(string key, string message)
        {
            if (warned.Add(key))
                PaLog.Warn(message);
        }

        public void Tick()
        {
            stats.Ticks++;
            commands.Drain(e => WarnOnce("cmd:" + e.GetType().Name + e.Message, "command failed: " + e));
            FlushSyncChanges();
            double now = voices.Now;
            world.Advance(now);
            AdvanceBankOps();
            RunPreloads();
            RunDueActions();
            AdvanceSequencers();
            if (music != null)
                music.Tick();
            voices.Tick();
            CompleteInstances();
            double real = platform.RealtimeSeconds;
            if (double.IsNaN(lastStats))
                lastStats = real;
            else if (real - lastStats >= 10.0)
            {
                lastStats = real;
                TraceStats();
            }
        }

        void TraceStats()
        {
            if (!PaLog.Tracing)
                return;
            int live = 0;
            foreach (IPaVoice v in voices.Active)
                live++;
            int contexts = 0;
            if (music != null)
                foreach (IPaMusicContext c in music.Active)
                    contexts++;
            PaLog.Trace("stats", "voices", live, "instances", instances.Count, "music", contexts);
        }

        public void TraceSummary()
        {
            if (!PaLog.Tracing)
                return;
            PaLog.Trace("summary", "posts", stats.Posts, "postsUnknown", stats.PostsUnknown, "voicesStarted", stats.VoicesStarted,
                "voicesFinished", stats.VoicesFinished, "voicesStopped", stats.VoicesStopped, "voicesFailed", stats.VoicesFailed,
                "instancesCompleted", stats.InstancesCompleted, "endOfEvent", stats.EndOfEvent, "bankCallbacks", stats.BankCallbacks,
                "banksLoaded", stats.BanksLoaded, "musicPlays", stats.MusicPlays, "unsupportedActions", stats.Unsupported, "ticks", stats.Ticks);
        }

        // ---- game syncs (store now, notify music and switch containers on the next tick) ----

        public void SetStateNow(uint group, uint state)
        {
            world.SetState(group, state);
            if (!stateChanges.Contains(group))
                stateChanges.Add(group);
            TraceSync("state", group, state, state, PaIds.GlobalObject);
        }

        public void SetSwitchNow(uint group, uint value, ulong go)
        {
            world.SetSwitch(group, value, go);
            KeyValuePair<uint, ulong> key = new KeyValuePair<uint, ulong>(group, go);
            if (!switchChanges.Contains(key))
                switchChanges.Add(key);
            TraceSync("switch", group, value, value, go);
        }

        void FlushSyncChanges()
        {
            for (int pass = 0; pass < 4 && (stateChanges.Count > 0 || switchChanges.Count > 0); pass++)
            {
                uint[] states = stateChanges.ToArray();
                KeyValuePair<uint, ulong>[] switches = switchChanges.ToArray();
                stateChanges.Clear();
                switchChanges.Clear();
                foreach (uint group in states)
                {
                    if (music != null)
                        music.OnStateChanged(group, world.GetState(group));
                    RecheckSwitches(group, true, PaIds.GlobalObject);
                }
                foreach (KeyValuePair<uint, ulong> kv in switches)
                {
                    if (music != null)
                        music.OnSwitchChanged(kv.Key, world.GetSwitch(kv.Key, kv.Value), kv.Value);
                    RecheckSwitches(kv.Key, false, kv.Value);
                }
            }
        }

        public void TraceSync(string op, uint group, object value, uint valueId, ulong go)
        {
            if (!PaLog.Tracing)
                return;
            PaLog.Trace("sync", "op", op, "group", group, "gname", banks.NameOf(group), "value", value,
                "vname", valueId != 0 ? banks.NameOf(valueId) : null, "go", go);
        }

        // ---- posting ----

        public void Start(PaInstance inst)
        {
            inst.Started = true;
            if (inst.Completed)
                return;
            world.Touch(inst.GameObject);
            StartEvent(inst, banks.Event(inst.EventId), 0);
        }

        // External voice: the language folder of szFile resolves like SetCurrentLanguage.
        public string ExternalLanguageKey(string file)
        {
            string folder = PaMediaIndex.LanguageFolderOf(file ?? "");
            PaManifest m = banks.Manifest;
            PaLanguage lang = m != null ? m.ResolveLanguage(folder, settings.VoiceLanguage) : null;
            if (lang == null)
                return world.VoiceLanguageKey;
            if (!string.IsNullOrEmpty(folder) && string.IsNullOrEmpty(settings.VoiceLanguage)
                && !string.Equals(lang.Name, folder, StringComparison.OrdinalIgnoreCase))
                WarnOnce("extlang:" + folder, "no voice for language " + folder + "; external voice falls back to " + lang.Name);
            return lang.Key;
        }

        // SeekOnEvent on playing instances: their non-music voices restart at the position.
        public void Seek(uint eventId, ulong go, float position, bool isPercent, uint playingId)
        {
            foreach (PaInstance inst in instanceOrder.ToArray())
            {
                if (inst.EventId != eventId || (!PaIds.IsGlobal(go) && inst.GameObject != go))
                    continue;
                if (playingId != 0 && inst.PlayingId != playingId)
                    continue;
                foreach (PaVoiceRef v in inst.Voices.ToArray())
                {
                    if (v.Ended || v.Stopping || v.Voice == null || v.Voice.Spec.Music)
                        continue;
                    PaVoiceSpec old = v.Voice.Spec;
                    double length = old.Media != null ? old.Media.Duration
                        : (old.Pcm != null && old.PcmRate > 0 ? (double)old.Pcm.Length / Math.Max(1, old.PcmChannels) / old.PcmRate : 0.0);
                    double offset = isPercent ? Math.Max(0f, Math.Min(1f, position)) * length : Math.Max(0f, position) / 1000.0;
                    PaVoiceSpec spec = new PaVoiceSpec
                    {
                        PlayingId = old.PlayingId, GameObject = old.GameObject, NodeId = old.NodeId, Media = old.Media,
                        Pcm = old.Pcm, PcmRate = old.PcmRate, PcmChannels = old.PcmChannels, VolumeDb = old.VolumeDb,
                        PitchCents = old.PitchCents, Lpf = old.Lpf, LoopCount = old.LoopCount, SourceOffset = offset,
                        StartAt = voices.Now, PlayLength = old.PlayLength,
                    };
                    PaVoiceRef replacement = new PaVoiceRef(this, inst, v.NodeId, ++voiceCounter);
                    replacement.Item = v.Item;
                    replacement.Switches = v.Switches;
                    spec.Tag = replacement;
                    IPaVoice voice = voices.Play(spec, replacement);
                    if (voice == null)
                        continue;
                    replacement.Voice = voice;
                    inst.Voices.Add(replacement);
                    liveVoices.Add(replacement);
                    if (v.Item != null)
                        v.Item.Voices.Add(replacement);
                    if (v.Switches != null)
                        foreach (KeyValuePair<PaSwitchPlay, uint> sw in v.Switches)
                        {
                            List<PaVoiceRef> list;
                            if (sw.Key.Children.TryGetValue(sw.Value, out list))
                                list.Add(replacement);
                        }
                    v.Stop(0, PaCurve.Linear);
                }
            }
        }

        // PinEventInStreamCache: prefetch the media under the event's Play targets.
        public void Pin(uint eventId)
        {
            PaEvent ev = banks.Event(eventId);
            if (ev == null || ev.Actions == null)
                return;
            HashSet<uint> seen = new HashSet<uint>();
            foreach (uint actionId in ev.Actions)
            {
                PaAction a = banks.Action(actionId);
                if (a == null || a.Type != PaActionType.Play)
                    continue;
                foreach (PaSource src in banks.SourcesUnder(a.Target))
                {
                    if (src == null || !seen.Add(src.Media))
                        continue;
                    string kind = src.Kind ?? "";
                    if (kind == "external" || kind == "audioInput")
                        continue;
                    PaMediaEntry e = media.Find(src.Media, src.LanguageSpecific, world.VoiceLanguageKey);
                    if (e != null)
                        voices.Prefetch(e);
                }
            }
        }

        public int PlayingIdsOf(ulong go, List<uint> into)
        {
            foreach (PaInstance inst in instanceOrder)
                if (!inst.Completed && (PaIds.IsGlobal(go) || inst.GameObject == go))
                    into.Add(inst.PlayingId);
            return into.Count;
        }

        public void Shutdown()
        {
            commands.Clear();
            pending.Clear();
            bankOps.Clear();
            try
            {
                if (music != null)
                    music.StopAll();
            }
            catch (Exception e)
            {
                PaLog.Error("music shutdown failed: " + e.Message);
            }
            try
            {
                voices.StopAll(0);
                voices.Shutdown();
            }
            catch (Exception e)
            {
                PaLog.Error("voice shutdown failed: " + e.Message);
            }
            instances.Clear();
            instanceOrder.Clear();
            liveVoices.Clear();
        }
    }

    public static class PaEngine
    {
        static readonly object sync = new object();
        static PaCore core;                 // null in silent mode
        static bool initialized;
        static bool suspended;
        static int lastTickFrame = int.MinValue;
        static string requestedLanguage;
        static string basePath;
        static bool ticking;

        // Silent-mode state, exactly as PortAudioNull kept it.
        static readonly Dictionary<uint, uint> silentEventOfPlaying = new Dictionary<uint, uint>();

        // ---- lifecycle ----

        public static bool IsInitialized()
        {
            lock (sync) return initialized;
        }

        public static bool IsSilent
        {
            get { lock (sync) return core == null; }
        }

        public static int Init()
        {
            lock (sync)
            {
                if (initialized)
                    return PaResult.Success;
                IPaPlatform platform = PaRuntime.Platform;
                PaSettings settings = platform != null && platform.Settings != null ? platform.Settings : new PaSettings();
                if (platform != null && !string.IsNullOrEmpty(settings.TracePath) && PaLog.Sink == null)
                {
                    try
                    {
                        PaLog.Sink = platform.CreateTraceSink(settings.TracePath);
                    }
                    catch (Exception e)
                    {
                        PaLog.Error("trace " + settings.TracePath + " could not be opened: " + e.Message);
                    }
                }
                string root = !string.IsNullOrEmpty(settings.Root) ? settings.Root
                    : (platform != null && platform.StreamingAssetsPath != null ? Path.Combine(platform.StreamingAssetsPath, "PortAudio") : null);
                string reason = null;
                PaManifest manifest = null;
                if (platform == null)
                    reason = "no platform";
                else if (settings.Off)
                    reason = "-portAudioOff";
                else
                {
                    try
                    {
                        manifest = root != null ? PaManifestLoader.Load(root) : null;
                        if (manifest == null)
                            reason = "no converted audio at " + root;
                    }
                    catch (Exception e)
                    {
                        reason = "manifest unreadable: " + e.Message;
                    }
                }

                if (manifest != null)
                {
                    try
                    {
                        core = CreateCore(platform, settings, root, manifest);
                    }
                    catch (Exception e)
                    {
                        core = null;
                        reason = "engine start failed: " + e.Message;
                        PaLog.Error(reason);
                    }
                }

                initialized = true;
                suspended = false;
                lastTickFrame = int.MinValue;
                if (core != null)
                {
                    PaLog.Info("real mode, data at " + root + ", voice language " + core.world.VoiceLanguageKey);
                    if (PaLog.Tracing)
                        PaLog.Trace("init", "mode", "real", "root", root, "language", requestedLanguage, "voiceLanguage", core.world.VoiceLanguageKey,
                            "banks", manifest.Banks != null ? manifest.Banks.Count : 0, "audioRunning", core.voices.AudioRunning, "realVoices", settings.RealVoices);
                }
                else
                {
                    PaLog.Warn("silent mode (" + reason + "): no audio output, events end on the next frame");
                    if (PaLog.Tracing)
                        PaLog.Trace("init", "mode", "silent", "root", root, "language", requestedLanguage, "voiceLanguage", null,
                            "banks", 0, "audioRunning", false, "realVoices", settings.RealVoices, "reason", reason);
                }
                return PaResult.Success;
            }
        }

        static PaCore CreateCore(IPaPlatform platform, PaSettings settings, string root, PaManifest manifest)
        {
            PaBankStore banks = new PaBankStore(root, manifest);
            PaMediaIndex media = new PaMediaIndex(root, manifest);
            uint seed = settings.Seed >= 0 ? unchecked((uint)settings.Seed) : unchecked((uint)Environment.TickCount ^ 0x5A17u);
            PaWorld world = new PaWorld(banks, media, seed);
            PaLanguage lang = manifest.ResolveLanguage(requestedLanguage ?? manifest.DefaultLanguage, settings.VoiceLanguage);
            world.VoiceLanguageKey = lang != null ? lang.Key : "";
            PaMixerConfig cfg = new PaMixerConfig { RealVoices = settings.RealVoices, TrimDb = settings.TrimDb };
            PaRuntime.World = world;
            IPaVoices voices = platform.CreateVoices(cfg);
            if (voices == null)
                throw new InvalidOperationException("the platform created no voices");
            IPaMusic music = platform.CreateMusic();
            PaRuntime.Voices = voices;
            PaRuntime.Music = music;
            PaCore c = new PaCore(platform, settings, banks, media, world, voices, music);
            platform.StartDriver(Tick);
            if (!string.IsNullOrEmpty(settings.RecordPath))
            {
                try
                {
                    platform.StartRecorder(settings.RecordPath);
                }
                catch (Exception e)
                {
                    PaLog.Error("recorder failed to start: " + e.Message);
                }
            }
            return c;
        }

        public static void Term()
        {
            lock (sync)
            {
                if (core != null)
                {
                    PaCore c = core;
                    core = null;
                    c.Shutdown();
                    try
                    {
                        c.platform.StopRecorder();
                    }
                    catch (Exception e)
                    {
                        PaLog.Error("recorder failed to stop: " + e.Message);
                    }
                    c.TraceSummary();
                    PaRuntime.World = null;
                    PaRuntime.Voices = null;
                    PaRuntime.Music = null;
                }
                initialized = false;
                silentEventOfPlaying.Clear();
                IPaTraceSink sink = PaLog.Sink;
                PaLog.Sink = null;
                if (sink != null)
                {
                    try
                    {
                        sink.Flush();
                        sink.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        public static int SetBasePath(string path)
        {
            lock (sync) basePath = path;
            return PaResult.Success;
        }

        internal static string BasePath
        {
            get { lock (sync) return basePath; }
        }

        public static int SetCurrentLanguage(string language)
        {
            lock (sync)
            {
                requestedLanguage = language;
                if (core == null)
                    return PaResult.Success;
                PaManifest m = core.banks.Manifest;
                PaLanguage lang = m.ResolveLanguage(language, core.settings.VoiceLanguage);
                if (lang == null)
                    return PaResult.Success;
                if (!string.IsNullOrEmpty(language) && string.IsNullOrEmpty(core.settings.VoiceLanguage)
                    && !string.Equals(lang.Name, language, StringComparison.OrdinalIgnoreCase))
                    core.WarnOnce("lang:" + language, "no voice for language " + language + "; using " + lang.Name);
                core.world.VoiceLanguageKey = lang.Key;
                return PaResult.Success;
            }
        }

        public static int RenderAudio()
        {
            IPaPlatform p = PaRuntime.Platform;
            if (p != null && p.IsMainThread)
                Tick();
            return PaResult.Success;
        }

        // Suspend/WakeupFromSuspend meant system sleep on PS4; on desktop they would silence the
        // game whenever the window loses focus, so they do nothing unless
        // -portAudioSuspendOnFocusLoss asks for the original behaviour.
        public static int Suspend(bool renderAnyway)
        {
            lock (sync)
            {
                if (core == null || !core.settings.SuspendOnFocusLoss || suspended)
                    return PaResult.Success;
                suspended = true;
                PaCore c = core;
                c.commands.Enqueue(() =>
                {
                    foreach (PaInstance inst in c.Instances)
                        c.PauseInstance(inst, 0, PaCurve.Linear, true);
                });
                return PaResult.Success;
            }
        }

        public static int WakeupFromSuspend()
        {
            lock (sync)
            {
                if (core == null || !suspended)
                    return PaResult.Success;
                suspended = false;
                PaCore c = core;
                c.commands.Enqueue(() =>
                {
                    foreach (PaInstance inst in c.Instances)
                        c.PauseInstance(inst, 0, PaCurve.Linear, false);
                });
                return PaResult.Success;
            }
        }

        // Main thread, at most once per frame.
        public static void Tick()
        {
            lock (sync)
            {
                if (core == null || ticking)
                    return;
                IPaPlatform p = core.platform;
                if (!p.IsMainThread)
                    return;
                int frame = p.FrameCount;
                if (frame == lastTickFrame)
                    return;
                lastTickFrame = frame;
                ticking = true;
                try
                {
                    core.Tick();
                }
                catch (Exception e)
                {
                    core.WarnOnce("tick:" + e.GetType().Name + e.Message, "tick failed: " + e);
                }
                finally
                {
                    ticking = false;
                }
            }
        }

        // ---- banks ----

        public static int LoadBank(string name, out uint bankId)
        {
            bankId = PaCore.BankIdFromName(name);
            PaCore c;
            lock (sync) c = core;
            if (c == null)
                return PaResult.Success;
            return c.LoadBankSync(name, bankId, sync);
        }

        public static int LoadBank(uint bankId)
        {
            PaCore c;
            lock (sync) c = core;
            if (c == null)
                return PaResult.Success;
            return c.LoadBankSync(null, bankId, sync);
        }

        public static int LoadBankAsync(string name, IntPtr cookie, out uint bankId)
        {
            bankId = PaCore.BankIdFromName(name);
            lock (sync)
            {
                if (core == null)
                    return SilentBank(bankId, cookie);
                core.LoadBankAsync(name, bankId, cookie, sync);
                return PaResult.Success;
            }
        }

        public static int LoadBankAsync(uint bankId, IntPtr cookie)
        {
            lock (sync)
            {
                if (core == null)
                    return SilentBank(bankId, cookie);
                core.LoadBankAsync(null, bankId, cookie, sync);
                return PaResult.Success;
            }
        }

        // In-memory banks are not used by the game; they succeed with a fresh id.
        public static int LoadBankFromMemory(IntPtr cookie, out uint bankId)
        {
            bankId = PaHandles.NewId();
            lock (sync)
            {
                if (core == null)
                    return SilentBank(bankId, cookie);
                core.WarnOnce("bankmem", "LoadBank from memory is not supported; reported as loaded");
                core.PrepareRequest(bankId, cookie);
                return PaResult.Success;
            }
        }

        public static int UnloadBank(string name)
        {
            return UnloadBank(PaCore.BankIdFromName(name));
        }

        public static int UnloadBank(uint bankId)
        {
            lock (sync)
            {
                if (core == null)
                    return PaResult.Success;
                return core.UnloadBankSync(bankId);
            }
        }

        public static int UnloadBankAsync(string name, IntPtr cookie)
        {
            return UnloadBankAsync(PaCore.BankIdFromName(name), cookie);
        }

        public static int UnloadBankAsync(uint bankId, IntPtr cookie)
        {
            lock (sync)
            {
                if (core == null)
                    return SilentBank(bankId, cookie);
                core.UnloadBankAsync(bankId, cookie);
                return PaResult.Success;
            }
        }

        public static int ClearBanks()
        {
            lock (sync)
            {
                if (core == null)
                    return PaResult.Success;
                return core.ClearAllBanks();
            }
        }

        public static int PrepareRequest(uint bankId, IntPtr cookie)
        {
            lock (sync)
            {
                if (core == null)
                    return SilentBank(bankId, cookie);
                core.PrepareRequest(bankId, cookie);
                return PaResult.Success;
            }
        }

        static int SilentBank(uint bankId, IntPtr cookie)
        {
            if (cookie != IntPtr.Zero)
                PaCallbacks.QueueBank(cookie, bankId, PaResult.Success);
            return PaResult.Success;
        }

        // ---- events ----

        public static uint PostEvent(uint eventId, ulong go, uint flags, IntPtr callback, IntPtr cookie, uint externalCount, IntPtr externals, uint playingId)
        {
            return Post(eventId, null, go, flags, callback, cookie, externalCount, externals, playingId);
        }

        public static uint PostEvent(string eventName, ulong go, uint flags, IntPtr callback, IntPtr cookie, uint externalCount, IntPtr externals, uint playingId)
        {
            return Post(PaIds.Hash(eventName), eventName, go, flags, callback, cookie, externalCount, externals, playingId);
        }

        static uint Post(uint eventId, string name, ulong go, uint flags, IntPtr callback, IntPtr cookie, uint externalCount, IntPtr externals, uint requestedPlayingId)
        {
            lock (sync)
            {
                if (core == null)
                {
                    uint pid = requestedPlayingId != 0 ? requestedPlayingId : PaHandles.NewId();
                    if (silentEventOfPlaying.Count > 4096)
                        silentEventOfPlaying.Clear();
                    silentEventOfPlaying[pid] = eventId;
                    if (flags != 0 && callback != IntPtr.Zero && cookie != IntPtr.Zero)
                        PaCallbacks.QueueEndOfEvent(cookie, go, pid, eventId);
                    if (PaLog.Tracing)
                        PaLog.Trace("post", "id", eventId, "name", name, "go", go, "playingId", pid, "result", "silent", "externals", externalCount);
                    return pid;
                }
                PaCore c = core;
                c.stats.Posts++;
                PaEvent ev = eventId != 0 ? c.banks.Event(eventId) : null;
                if (ev == null)
                {
                    c.stats.PostsUnknown++;
                    if (PaLog.Tracing)
                        PaLog.Trace("post", "id", eventId, "name", name ?? c.banks.NameOf(eventId), "go", go, "playingId", 0u, "result", "unknown", "externals", externalCount);
                    return 0;
                }
                PaInstance inst = c.CreateInstance(eventId, go, flags, callback, cookie, externalCount, externals, requestedPlayingId);
                if (PaLog.Tracing)
                    PaLog.Trace("post", "id", eventId, "name", name ?? ev.Name ?? c.banks.NameOf(eventId), "go", go, "playingId", inst.PlayingId, "result", "ok", "externals", externalCount);
                c.commands.Enqueue(() => c.Start(inst));
                return inst.PlayingId;
            }
        }

        public static int ExecuteActionOnEvent(uint eventId, int actionType, ulong go, int transitionMs, int curve, uint playingId)
        {
            lock (sync)
            {
                if (core == null)
                    return PaResult.Success;
                PaCore c = core;
                if (c.banks.Event(eventId) == null)
                    return PaResult.IDNotFound;
                PaCurve cv = Curve(curve);
                c.commands.Enqueue(() => c.ExecuteActionOnEvent(eventId, actionType, go, transitionMs, cv, playingId));
                return PaResult.Success;
            }
        }

        public static int ExecuteActionOnEvent(string eventName, int actionType, ulong go, int transitionMs, int curve, uint playingId)
        {
            return ExecuteActionOnEvent(PaIds.Hash(eventName), actionType, go, transitionMs, curve, playingId);
        }

        // The game's existence check before posting: Success when the event is loaded.
        public static int SeekOnEvent(uint eventId, ulong go, float position, bool isPercent, bool seekToNearestMarker, uint playingId)
        {
            return Seek(eventId, null, go, position, isPercent, playingId);
        }

        public static int SeekOnEvent(string eventName, ulong go, float position, bool isPercent, bool seekToNearestMarker, uint playingId)
        {
            return Seek(PaIds.Hash(eventName), eventName, go, position, isPercent, playingId);
        }

        static int Seek(uint eventId, string name, ulong go, float position, bool isPercent, uint playingId)
        {
            lock (sync)
            {
                if (core == null)
                    return PaResult.Success;
                PaCore c = core;
                bool known = eventId != 0 && c.banks.Event(eventId) != null;
                int result = known ? PaResult.Success : PaResult.IDNotFound;
                if (PaLog.Tracing)
                    PaLog.Trace("seek", "id", eventId, "name", name ?? c.banks.NameOf(eventId), "go", go, "result", known ? "ok" : "unknown");
                if (known)
                    c.commands.Enqueue(() => c.Seek(eventId, go, position, isPercent, playingId));
                return result;
            }
        }

        public static void StopPlayingID(uint playingId, int transitionMs, int curve)
        {
            lock (sync)
            {
                if (core == null)
                    return;
                PaCore c = core;
                PaCurve cv = Curve(curve);
                c.commands.Enqueue(() => c.StopPlayingId(playingId, transitionMs, cv));
            }
        }

        public static void StopAll(ulong go)
        {
            lock (sync)
            {
                if (core == null)
                    return;
                PaCore c = core;
                c.commands.Enqueue(() => c.StopAllOn(go));
            }
        }

        public static int PostTrigger(uint triggerId, ulong go)
        {
            lock (sync)
            {
                if (core == null)
                    return PaResult.Success;
                PaCore c = core;
                c.TraceSync("trigger", triggerId, null, 0, go);
                if (c.music != null)
                    c.commands.Enqueue(() => c.music.PostTrigger(triggerId, go));
                return PaResult.Success;
            }
        }

        public static int PostTrigger(string trigger, ulong go)
        {
            return PostTrigger(PaIds.Hash(trigger), go);
        }

        public static int PinEventInStreamCache(uint eventId)
        {
            lock (sync)
            {
                if (core == null)
                    return PaResult.Success;
                PaCore c = core;
                c.commands.Enqueue(() => c.Pin(eventId));
                return PaResult.Success;
            }
        }

        public static int PinEventInStreamCache(string name)
        {
            return PinEventInStreamCache(PaIds.Hash(name));
        }

        public static int UnpinEventInStreamCache(uint eventId)
        {
            return PaResult.Success;
        }

        public static int UnpinEventInStreamCache(string name)
        {
            return PaResult.Success;
        }

        public static void CancelEventCallbackCookie(IntPtr cookie)
        {
            PaCallbacks.CancelCookie(cookie);
            lock (sync)
            {
                if (core != null)
                    core.CancelCookie(cookie);
            }
        }

        public static void CancelEventCallback(uint playingId)
        {
            lock (sync)
            {
                if (core != null)
                    core.CancelCallback(playingId);
            }
        }

        public static uint GetEventIDFromPlayingID(uint playingId)
        {
            lock (sync)
            {
                if (core == null)
                {
                    uint eventId;
                    return silentEventOfPlaying.TryGetValue(playingId, out eventId) ? eventId : 0u;
                }
                PaInstance inst = core.Instance(playingId);
                return inst != null ? inst.EventId : 0u;
            }
        }

        public static ulong GetGameObjectFromPlayingID(uint playingId)
        {
            lock (sync)
            {
                if (core == null)
                    return 0UL;
                PaInstance inst = core.Instance(playingId);
                return inst != null ? inst.GameObject : PaIds.GlobalObject;
            }
        }

        // count in: capacity of ids (0 or null ids: just count); count out: number of ids.
        public static int GetPlayingIDsFromGameObject(ulong go, ref uint count, uint[] ids)
        {
            lock (sync)
            {
                if (core == null)
                {
                    count = 0;
                    return PaResult.Success;
                }
                List<uint> list = new List<uint>();
                core.PlayingIdsOf(go, list);
                if (ids == null || count == 0)
                {
                    count = (uint)list.Count;
                    return PaResult.Success;
                }
                int n = (int)Math.Min(Math.Min(count, (uint)ids.Length), (uint)list.Count);
                for (int i = 0; i < n; i++)
                    ids[i] = list[i];
                count = (uint)n;
                return PaResult.Success;
            }
        }

        public static int GetSourcePlayPosition(uint playingId, out int positionMs)
        {
            positionMs = 0;
            lock (sync)
            {
                if (core == null)
                    return PaResult.Success;
                PaInstance inst = core.Instance(playingId);
                if (inst == null)
                    return PaResult.Fail;
                foreach (PaVoiceRef v in inst.Voices)
                {
                    if (v.Ended || v.Voice == null)
                        continue;
                    double pos = v.Voice.Position;
                    positionMs = double.IsNaN(pos) ? 0 : (int)Math.Round(Math.Max(0.0, pos) * 1000.0);
                    return PaResult.Success;
                }
                return PaResult.Success;
            }
        }

        public static uint GetIDFromString(string name)
        {
            return PaIds.Hash(name);
        }

        // ---- game syncs ----

        public static int SetState(uint group, uint state)
        {
            lock (sync)
            {
                if (core != null)
                    core.SetStateNow(group, state);
                return PaResult.Success;
            }
        }

        public static int SetState(string group, string state)
        {
            return SetState(PaIds.Hash(group), PaIds.Hash(state));
        }

        public static int GetState(uint group, out uint state)
        {
            lock (sync)
            {
                state = core != null ? core.world.GetState(group) : 0u;
                return PaResult.Success;
            }
        }

        public static int SetSwitch(uint group, uint value, ulong go)
        {
            lock (sync)
            {
                if (core != null)
                    core.SetSwitchNow(group, value, go);
                return PaResult.Success;
            }
        }

        public static int SetSwitch(string group, string value, ulong go)
        {
            return SetSwitch(PaIds.Hash(group), PaIds.Hash(value), go);
        }

        public static int GetSwitch(uint group, ulong go, out uint value)
        {
            lock (sync)
            {
                value = core != null ? core.world.GetSwitch(group, go) : 0u;
                return PaResult.Success;
            }
        }

        public static int SetRTPCValue(uint rtpc, float value, ulong go, int durationMs, int curve, bool bypassInterpolation)
        {
            lock (sync)
            {
                if (core == null)
                    return PaResult.Success;
                PaCore c = core;
                PaCurve cv = Curve(curve);
                c.TraceSync("rtpc", rtpc, value, 0, go);
                c.commands.Enqueue(() => c.world.SetRtpc(rtpc, value, go, c.voices.Now, durationMs, cv, bypassInterpolation));
                return PaResult.Success;
            }
        }

        public static int SetRTPCValue(string rtpc, float value, ulong go, int durationMs, int curve, bool bypassInterpolation)
        {
            return SetRTPCValue(PaIds.Hash(rtpc), value, go, durationMs, curve, bypassInterpolation);
        }

        // Wwise scopes this to the playing instance; here it applies to the instance's object.
        public static int SetRTPCValueByPlayingID(uint rtpc, float value, uint playingId, int durationMs, int curve, bool bypass)
        {
            lock (sync)
            {
                if (core == null)
                    return PaResult.Success;
                PaInstance inst = core.Instance(playingId);
                if (inst == null)
                    return PaResult.Success;
                ulong go = inst.GameObject;
                PaCore c = core;
                PaCurve cv = Curve(curve);
                c.TraceSync("rtpc", rtpc, value, 0, go);
                c.commands.Enqueue(() => c.world.SetRtpc(rtpc, value, go, c.voices.Now, durationMs, cv, bypass));
                return PaResult.Success;
            }
        }

        public static int SetRTPCValueByPlayingID(string rtpc, float value, uint playingId, int durationMs, int curve, bool bypass)
        {
            return SetRTPCValueByPlayingID(PaIds.Hash(rtpc), value, playingId, durationMs, curve, bypass);
        }

        public static int ResetRTPCValue(uint rtpc, ulong go, int durationMs, int curve, bool bypass)
        {
            lock (sync)
            {
                if (core == null)
                    return PaResult.Success;
                PaCore c = core;
                PaCurve cv = Curve(curve);
                c.commands.Enqueue(() => c.world.ResetRtpc(rtpc, go, c.voices.Now, durationMs, cv, bypass));
                return PaResult.Success;
            }
        }

        public static int ResetRTPCValue(string rtpc, ulong go, int durationMs, int curve, bool bypass)
        {
            return ResetRTPCValue(PaIds.Hash(rtpc), go, durationMs, curve, bypass);
        }

        // valueType (AkRTPCValue_type): 0 default, 1 global, 2 game object, 3 playing id,
        // 4 unavailable. The reply says where the value came from.
        public static int GetRTPCValue(uint rtpc, ulong go, out float value, ref int valueType)
        {
            lock (sync)
            {
                if (core == null)
                {
                    value = 0f;
                    return PaResult.Success;
                }
                PaWorld w = core.world;
                if (valueType == 1 || PaIds.IsGlobal(go))
                {
                    value = w.GetRtpc(rtpc, PaIds.GlobalObject);
                    valueType = w.HasGlobalValue(rtpc) ? 1 : 0;
                    return PaResult.Success;
                }
                value = w.GetRtpc(rtpc, go);
                valueType = w.HasObjectValue(rtpc, go) ? 2 : (w.HasGlobalValue(rtpc) ? 1 : 0);
                return PaResult.Success;
            }
        }

        public static int GetRTPCValue(string rtpc, ulong go, out float value, ref int valueType)
        {
            return GetRTPCValue(PaIds.Hash(rtpc), go, out value, ref valueType);
        }

        public static void SetRandomSeed(uint seed)
        {
            lock (sync)
            {
                if (core != null)
                    core.random.Reseed(seed);
            }
        }

        public static void MuteBackgroundMusic(bool mute)
        {
            lock (sync)
            {
                if (core != null)
                    core.world.BackgroundMusicMuted = mute;
            }
        }

        // ---- objects and listener ----

        public static int RegisterGameObject(int id, string name, uint listenerMask)
        {
            lock (sync)
            {
                if (core != null)
                    core.world.Register(PaIds.FromRegisteredId(id), name, listenerMask);
                return PaResult.Success;
            }
        }

        // Also called from finalizers (~AkAutoObject): only queued.
        public static int UnregisterGameObject(int id)
        {
            lock (sync)
            {
                if (core == null)
                    return PaResult.Success;
                PaCore c = core;
                ulong go = PaIds.FromRegisteredId(id);
                c.commands.Enqueue(() => c.UnregisterObject(go));
                return PaResult.Success;
            }
        }

        public static int UnregisterAllGameObjects()
        {
            lock (sync)
            {
                if (core == null)
                    return PaResult.Success;
                PaCore c = core;
                c.commands.Enqueue(() =>
                {
                    foreach (PaInstance inst in new List<PaInstance>(c.Instances))
                        if (!PaIds.IsGlobal(inst.GameObject) && inst.GameObject != PaCore.AudioInputObject)
                            c.StopInstance(inst, 0, PaCurve.Linear);
                    c.world.UnregisterAll();
                });
                return PaResult.Success;
            }
        }

        public static int SetObjectPosition(ulong go, float px, float py, float pz, float fx, float fy, float fz)
        {
            lock (sync)
            {
                if (core != null)
                    core.world.SetPosition(go, new PaVec3(px, py, pz), new PaVec3(fx, fy, fz));
                return PaResult.Success;
            }
        }

        // Multi-position emitters (AkAmbient): the position arrays are opaque seam handles, so the
        // object keeps its last single position.
        public static int SetMultiplePositions(ulong go, IntPtr positions, ushort count, int type)
        {
            lock (sync)
            {
                if (core != null)
                    core.WarnOnce("multipos", "SetMultiplePositions is not reproduced; emitters keep their single position");
                return PaResult.Success;
            }
        }

        public static int SetListenerPosition(float fx, float fy, float fz, float tx, float ty, float tz, float px, float py, float pz, uint listener)
        {
            lock (sync)
            {
                if (core != null && listener == 0)
                    core.world.SetListener(new PaVec3(px, py, pz), new PaVec3(fx, fy, fz), new PaVec3(tx, ty, tz));
                return PaResult.Success;
            }
        }

        public static int SetActiveListeners(ulong go, uint mask)
        {
            lock (sync)
            {
                if (core != null)
                    core.world.SetListenerMask(go, mask);
                return PaResult.Success;
            }
        }

        public static int SetGameObjectOutputBusVolume(ulong go, float linear)
        {
            lock (sync)
            {
                if (core != null)
                    core.world.SetOutputBusVolume(go, linear);
                return PaResult.Success;
            }
        }

        public static int SetAttenuationScalingFactor(ulong go, float factor)
        {
            lock (sync)
            {
                if (core != null)
                    core.world.SetAttenuationScaling(go, factor);
                return PaResult.Success;
            }
        }

        // ---- audio input (phone clips; PortAudioNull forwards here) ----

        public static void AudioInputSetCallbacks()
        {
        }

        public static bool AudioInputIsPlaying()
        {
            lock (sync) return core != null && core.AudioInputIsPlaying;
        }

        public static bool AudioInputPlay(byte[] pcm16Mono48k, int bytes)
        {
            lock (sync)
            {
                if (core == null)
                    return true;
                return core.AudioInputPlay(pcm16Mono48k, bytes);
            }
        }

        public static int AudioInputPosition()
        {
            lock (sync) return core != null ? core.AudioInputPosition : 0;
        }

        public static void AudioInputStop()
        {
            lock (sync)
            {
                if (core != null)
                    core.AudioInputStop();
            }
        }

        // ---- helpers ----

        static PaCurve Curve(int curve)
        {
            return curve >= 0 && curve <= 9 ? (PaCurve)curve : PaCurve.Linear;
        }

        // For tests and diagnostics.
        internal static int InstanceCount
        {
            get { lock (sync) return core != null ? core.InstanceCount : 0; }
        }
    }
}
