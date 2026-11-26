// Pool of Unity voices for the PortAudio mixer: one hidden DontDestroyOnLoad root with pooled
// child GameObjects, each carrying a 2D AudioSource and an AudioLowPassFilter. The pool never
// grows past Limit sources; PaUnityVoices steals the quietest voice when it is full.
// Main thread only.

using System.Collections.Generic;
using UnityEngine;

namespace PortAudio.Unity
{
    /// <summary>One pooled Unity voice.</summary>
    public sealed class PaVoiceSlot
    {
        public GameObject Go;
        public AudioSource Source;
        public AudioLowPassFilter Filter;
        public bool InUse;
        public int Index;
    }

    public sealed class PaVoicePool
    {
        public const int DefaultLimit = 160;

        readonly List<PaVoiceSlot> all = new List<PaVoiceSlot>();
        readonly Stack<PaVoiceSlot> free = new Stack<PaVoiceSlot>();
        GameObject root;

        public PaVoicePool(int limit)
        {
            Limit = limit > 0 ? limit : DefaultLimit;
        }

        public int Limit { get; private set; }
        public int InUse { get { return all.Count - free.Count; } }
        public int Created { get { return all.Count; } }
        public bool Full { get { return free.Count == 0 && all.Count >= Limit; } }

        GameObject Root
        {
            get
            {
                if (root == null)
                {
                    root = new GameObject("PortAudioVoices");
                    root.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                    Object.DontDestroyOnLoad(root);
                    all.Clear();
                    free.Clear();
                }
                return root;
            }
        }

        /// <summary>A free slot, or null when Limit slots are in use.</summary>
        public PaVoiceSlot Acquire()
        {
            var r = Root;
            PaVoiceSlot s = null;
            while (free.Count > 0 && s == null)
            {
                s = free.Pop();
                if (s.Go == null || s.Source == null) { all.Remove(s); s = null; }   // destroyed behind our back
            }
            if (s == null)
            {
                if (all.Count >= Limit) return null;
                s = Create(r, all.Count);
                all.Add(s);
            }
            s.InUse = true;
            Reset(s);
            return s;
        }

        public void Release(PaVoiceSlot s)
        {
            if (s == null || !s.InUse) return;
            s.InUse = false;
            if (s.Source != null)
            {
                s.Source.Stop();
                s.Source.clip = null;
            }
            free.Push(s);
        }

        static PaVoiceSlot Create(GameObject parent, int index)
        {
            var go = new GameObject("PaVoice" + index);
            go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            go.transform.SetParent(parent.transform, false);
            var src = go.AddComponent<AudioSource>();
            var lpf = go.AddComponent<AudioLowPassFilter>();
            var s = new PaVoiceSlot { Go = go, Source = src, Filter = lpf, Index = index };
            Reset(s);
            return s;
        }

        static void Reset(PaVoiceSlot s)
        {
            var src = s.Source;
            if (src != null)
            {
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.dopplerLevel = 0f;
                src.bypassReverbZones = true;
                src.loop = false;
                src.mute = false;
                src.volume = 0f;
                src.pitch = 1f;
                src.panStereo = 0f;
                src.priority = 128;
                src.clip = null;
            }
            if (s.Filter != null)
            {
                s.Filter.cutoffFrequency = PaGainModel.MaxCutoffHz;
                s.Filter.lowpassResonanceQ = 1f;
                s.Filter.enabled = false;
            }
        }

        /// <summary>Destroy every pooled object.</summary>
        public void Destroy()
        {
            foreach (var s in all)
                if (s.Source != null) s.Source.Stop();
            all.Clear();
            free.Clear();
            if (root != null) Object.Destroy(root);
            root = null;
        }
    }
}
