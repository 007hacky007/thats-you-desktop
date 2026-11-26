// Unity side of the PortAudio runtime: the IPaPlatform the engine core runs on.
//
// Installed before the first scene loads, so it is in place when AkInitializer calls
// AkSoundEngine.Init (which reaches PaEngine.Init through the PINVOKE seam). It provides:
// - settings from the player command line (-portAudio... flags, PaOptions),
// - the StreamingAssets path, a thread-safe realtime clock and frame counter, the main thread
//   check, and log routing to the Unity console,
// - factories for the trace file, the Unity voice backend and the music player,
// - the per-frame driver: a hidden DontDestroyOnLoad PaDriver whose LateUpdate (latest
//   execution order, after AkInitializer.LateUpdate) runs the engine tick,
// - the mix recorder.

using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace PortAudio.Unity
{
    public sealed class PaUnityPlatform : IPaPlatform
    {
        static PaUnityPlatform instance;

        readonly int mainThreadId;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly string streamingAssets;
        readonly string[] args;
        PaSettings settings = new PaSettings();
        volatile int frame;
        Action tick;
        PaDriver driver;

        PaUnityPlatform()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            streamingAssets = Application.streamingAssetsPath;
            string[] a;
            try
            {
                a = Environment.GetCommandLineArgs();
            }
            catch (Exception)
            {
                a = new string[0];
            }
            args = a ?? new string[0];
            frame = Time.frameCount;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (instance != null && PaRuntime.Platform == instance)
                return;
            instance = new PaUnityPlatform();
            PaRuntime.Platform = instance;
            // Parsed after the platform is installed so option warnings reach the console.
            instance.settings = PaOptions.Parse(instance.args) ?? new PaSettings();
            instance.EnsureDriver();
        }

        // ---- IPaPlatform ----

        public string StreamingAssetsPath
        {
            get { return streamingAssets; }
        }

        public string[] CommandLineArgs
        {
            get { return args; }
        }

        public double RealtimeSeconds
        {
            get { return clock.Elapsed.TotalSeconds; }
        }

        public int FrameCount
        {
            get
            {
                if (IsMainThread)
                    frame = Time.frameCount;
                return frame;
            }
        }

        public bool IsMainThread
        {
            get { return Thread.CurrentThread.ManagedThreadId == mainThreadId; }
        }

        public PaSettings Settings
        {
            get { return settings; }
        }

        public IPaTraceSink CreateTraceSink(string path)
        {
            return PaTraceFile.Open(path);
        }

        public IPaVoices CreateVoices(PaMixerConfig config)
        {
            return new PaUnityVoices(config);
        }

        public IPaMusic CreateMusic()
        {
            return new PaMusicPlayer();
        }

        public void StartDriver(Action tick)
        {
            this.tick = tick;
            EnsureDriver();
        }

        public void StartRecorder(string wavPath)
        {
            PaRecorder.Start(wavPath);
        }

        public void StopRecorder()
        {
            PaRecorder.Stop();
        }

        public void Log(int level, string message)
        {
            // Debug.Log* may be called from any thread.
            if (level >= 2)
                UnityEngine.Debug.LogError(message);
            else if (level == 1)
                UnityEngine.Debug.LogWarning(message);
            else
                UnityEngine.Debug.Log(message);
        }

        // ---- driver ----

        void EnsureDriver()
        {
            if (!IsMainThread || driver != null)
                return;
            GameObject go = new GameObject("PortAudioDriver");
            go.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            UnityEngine.Object.DontDestroyOnLoad(go);
            driver = go.AddComponent<PaDriver>();
            driver.Platform = this;
        }

        internal void OnFrame()
        {
            frame = Time.frameCount;
            Action t = tick;
            if (t == null)
                return;
            try
            {
                t();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[PortAudio] tick failed: " + e);
            }
        }

        internal void OnDriverDestroyed(PaDriver d)
        {
            if (driver == d)
                driver = null;
        }
    }

    // Runs the engine tick once per frame, after every other LateUpdate.
    [DefaultExecutionOrder(32000)]
    [AddComponentMenu("")]
    public sealed class PaDriver : MonoBehaviour
    {
        internal PaUnityPlatform Platform;

        void LateUpdate()
        {
            if (Platform != null)
                Platform.OnFrame();
        }

        void OnDestroy()
        {
            if (Platform != null)
                Platform.OnDriverDestroyed(this);
        }
    }
}
