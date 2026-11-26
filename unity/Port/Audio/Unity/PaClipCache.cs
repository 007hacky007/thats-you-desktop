// AudioClip cache for the PortAudio mixer. Converted media (Ogg Vorbis remux or WAV fallback)
// is loaded asynchronously from StreamingAssets with UnityWebRequestMultimedia, kept compressed
// in memory, and shared by every voice that plays it.
//
// - Bank-owned preloads (Preload/ReleaseOwner, owner = the bank) cover embedded SFX and music.
//   They are reference counted per owner and destroyed when the last owner and voice let go.
// - Language media and external voice load on demand (or through Prefetch) into an LRU that is
//   trimmed to StreamBudgetMb; clips in use by a voice are never evicted.
// - At most MaxConcurrentLoads requests run at once; voice requests go before preloads.
// Main thread only.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace PortAudio.Unity
{
    public sealed class PaClipCache
    {
        public enum LoadState { Queued, Loading, Decoding, Ready, Failed }

        public sealed class Entry
        {
            public string Key;
            public PaMediaEntry Media;
            public AudioClip Clip;
            public LoadState State;
            public string Error;
            public readonly HashSet<object> Owners = new HashSet<object>();
            public bool EverOwned;
            public int Users;
            public long LastUse;
            public long Bytes;
            public bool Urgent;
            public UnityWebRequest Request;
            public bool Finished { get { return State == LoadState.Ready || State == LoadState.Failed; } }
        }

        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly List<Entry> queue = new List<Entry>();
        readonly List<Entry> loading = new List<Entry>();
        readonly int maxLoads;
        readonly long budgetBytes;
        long useCounter;

        public PaClipCache(PaMixerConfig config)
        {
            config = config ?? new PaMixerConfig();
            maxLoads = Math.Max(1, config.MaxConcurrentLoads);
            budgetBytes = Math.Max(1, config.StreamBudgetMb) * 1024L * 1024L;
        }

        public int LoadedCount { get; private set; }
        public int LoadingCount { get { return loading.Count + queue.Count; } }
        /// <summary>Bytes held by on-demand (not bank-owned) clips.</summary>
        public long StreamBytes { get; private set; }

        public static string KeyOf(PaMediaEntry m)
        {
            if (m == null) return null;
            if (!string.IsNullOrEmpty(m.FullPath)) return m.FullPath;
            return (m.Scope ?? "") + "/" + m.Id + "/" + (m.File ?? "");
        }

        public static AudioType TypeOf(PaMediaEntry m)
        {
            string f = (m.Format ?? "").ToLowerInvariant();
            if (f == "wav") return AudioType.WAV;
            if (f == "ogg") return AudioType.OGGVORBIS;
            string path = (m.FullPath ?? m.File ?? "").ToLowerInvariant();
            return path.EndsWith(".wav", StringComparison.Ordinal) ? AudioType.WAV : AudioType.OGGVORBIS;
        }

        static long EstimateBytes(PaMediaEntry m)
        {
            if (m.Bytes > 0) return m.Bytes;
            return Math.Max(1024L, m.Samples * Math.Max(1, m.Channels) / 4);   // Vorbis is roughly 1/8 of 16-bit PCM
        }

        Entry GetOrCreate(PaMediaEntry media, bool urgent)
        {
            string key = KeyOf(media);
            if (key == null) return null;
            Entry e;
            if (!entries.TryGetValue(key, out e))
            {
                e = new Entry { Key = key, Media = media, State = LoadState.Queued, Bytes = EstimateBytes(media), Urgent = urgent };
                entries[key] = e;
                if (string.IsNullOrEmpty(media.FullPath)) { e.State = LoadState.Failed; e.Error = "no file path"; }
                else queue.Add(e);
            }
            else if (urgent && e.State == LoadState.Queued) e.Urgent = true;
            e.LastUse = ++useCounter;
            return e;
        }

        /// <summary>Entry for a voice: queued at high priority and pinned while the voice uses it.</summary>
        public Entry Acquire(PaMediaEntry media)
        {
            var e = GetOrCreate(media, true);
            if (e != null) e.Users++;
            return e;
        }

        public void ReleaseUse(Entry e)
        {
            if (e == null) return;
            if (e.Users > 0) e.Users--;
            e.LastUse = ++useCounter;
            if (e.Users == 0 && e.EverOwned && e.Owners.Count == 0) Drop(e);
        }

        public void Preload(IEnumerable<PaMediaEntry> media, object owner)
        {
            if (media == null) return;
            foreach (var m in media)
            {
                var e = GetOrCreate(m, false);
                if (e == null) continue;
                if (owner != null) { e.Owners.Add(owner); e.EverOwned = true; }
            }
        }

        public void ReleaseOwner(object owner)
        {
            if (owner == null) return;
            var drop = new List<Entry>();
            foreach (var e in entries.Values)
                if (e.Owners.Remove(owner) && e.Owners.Count == 0 && e.Users == 0) drop.Add(e);
            foreach (var e in drop) Drop(e);
        }

        public void Prefetch(PaMediaEntry media) { GetOrCreate(media, true); }

        /// <summary>True when the clip's load has finished (successfully or not), or there is nothing to load.</summary>
        public bool IsReady(PaMediaEntry media)
        {
            string key = KeyOf(media);
            if (key == null) return true;
            Entry e;
            return entries.TryGetValue(key, out e) ? e.Finished : false;
        }

        public Entry Find(PaMediaEntry media)
        {
            string key = KeyOf(media);
            Entry e;
            return key != null && entries.TryGetValue(key, out e) ? e : null;
        }

        public void Tick()
        {
            // finish running requests
            for (int i = loading.Count - 1; i >= 0; i--)
            {
                var e = loading[i];
                if (e.State == LoadState.Loading)
                {
                    if (e.Request == null) { Fail(e, "request lost"); loading.RemoveAt(i); continue; }
                    if (!e.Request.isDone) continue;
                    if (e.Request.result != UnityWebRequest.Result.Success)
                    {
                        Fail(e, e.Request.error ?? "load failed");
                        DisposeRequest(e);
                        loading.RemoveAt(i);
                        continue;
                    }
                    AudioClip clip = null;
                    try { clip = DownloadHandlerAudioClip.GetContent(e.Request); }
                    catch (Exception ex) { Fail(e, ex.Message); }
                    DisposeRequest(e);
                    if (clip == null) { if (e.State != LoadState.Failed) Fail(e, "no clip"); loading.RemoveAt(i); continue; }
                    clip.name = "pa:" + (e.Media.Label ?? e.Media.Id.ToString());
                    e.Clip = clip;
                    e.State = LoadState.Decoding;
                    if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                }
                if (e.State == LoadState.Decoding)
                {
                    var ls = e.Clip == null ? AudioDataLoadState.Failed : e.Clip.loadState;
                    if (ls == AudioDataLoadState.Loading) continue;
                    if (ls == AudioDataLoadState.Unloaded) { e.Clip.LoadAudioData(); continue; }
                    loading.RemoveAt(i);
                    if (ls == AudioDataLoadState.Failed) { DestroyClip(e); Fail(e, "decode failed"); continue; }
                    e.State = LoadState.Ready;
                    LoadedCount++;
                }
            }

            // start queued requests, voices first
            if (queue.Count > 0 && loading.Count < maxLoads)
            {
                queue.Sort((a, b) => a.Urgent != b.Urgent ? (a.Urgent ? -1 : 1) : a.LastUse.CompareTo(b.LastUse));
                int n = 0;
                while (n < queue.Count && loading.Count < maxLoads)
                {
                    var e = queue[n];
                    queue.RemoveAt(n);
                    if (e.State != LoadState.Queued) continue;
                    Start(e);
                }
            }

            Trim();
        }

        void Start(Entry e)
        {
            try
            {
                string uri = new Uri(e.Media.FullPath).AbsoluteUri;
                var req = UnityWebRequestMultimedia.GetAudioClip(uri, TypeOf(e.Media));
                var dh = req.downloadHandler as DownloadHandlerAudioClip;
                if (dh != null) { dh.compressed = true; dh.streamAudio = false; }
                e.Request = req;
                e.State = LoadState.Loading;
                req.SendWebRequest();
                loading.Add(e);
            }
            catch (Exception ex)
            {
                DisposeRequest(e);
                Fail(e, ex.Message);
            }
        }

        void Fail(Entry e, string error)
        {
            e.State = LoadState.Failed;
            e.Error = error;
            PaLog.Trace("warn", "msg", "clip load failed", "media", e.Media.Id, "file", e.Media.File, "reason", error);
        }

        static void DisposeRequest(Entry e)
        {
            if (e.Request == null) return;
            try { e.Request.Dispose(); } catch (Exception) { }
            e.Request = null;
        }

        static void DestroyClip(Entry e)
        {
            if (e.Clip != null) UnityEngine.Object.Destroy(e.Clip);
            e.Clip = null;
        }

        void Drop(Entry e)
        {
            if (e.State == LoadState.Loading || e.State == LoadState.Decoding) loading.Remove(e);
            if (e.State == LoadState.Queued) queue.Remove(e);
            if (e.State == LoadState.Ready) LoadedCount = Math.Max(0, LoadedCount - 1);
            if (e.Request != null) { try { e.Request.Abort(); } catch (Exception) { } }
            DisposeRequest(e);
            DestroyClip(e);
            entries.Remove(e.Key);
        }

        /// <summary>Evict least recently used on-demand clips (no owner, no voice) over the budget.</summary>
        void Trim()
        {
            long total = 0;
            List<Entry> candidates = null;
            foreach (var e in entries.Values)
            {
                if (e.Owners.Count > 0 || e.State != LoadState.Ready && e.State != LoadState.Failed) continue;
                if (e.State == LoadState.Ready) total += e.Bytes;
                if (e.Users == 0)
                {
                    if (candidates == null) candidates = new List<Entry>();
                    candidates.Add(e);
                }
            }
            StreamBytes = total;
            if (candidates == null) return;
            // failed on-demand entries are forgotten once unused, so a later post retries
            foreach (var e in candidates)
                if (e.State == LoadState.Failed && !e.EverOwned) Drop(e);
            if (total <= budgetBytes) return;
            candidates.RemoveAll(e => e.State != LoadState.Ready);
            candidates.Sort((a, b) => a.LastUse.CompareTo(b.LastUse));
            foreach (var e in candidates)
            {
                if (total <= budgetBytes) break;
                total -= e.Bytes;
                Drop(e);
            }
            StreamBytes = total;
        }

        /// <summary>Clip for raw PCM (audio input), owned by the caller.</summary>
        public static AudioClip CreatePcmClip(float[] pcm, int rate, int channels, string name)
        {
            if (pcm == null || rate <= 0) return null;
            int ch = Math.Max(1, channels);
            int frames = pcm.Length / ch;
            if (frames <= 0) return null;
            var clip = AudioClip.Create(name ?? "pa:pcm", frames, ch, rate, false);
            clip.SetData(pcm, 0);
            return clip;
        }

        public void Clear()
        {
            foreach (var e in new List<Entry>(entries.Values)) Drop(e);
            entries.Clear();
            queue.Clear();
            loading.Clear();
            LoadedCount = 0;
            StreamBytes = 0;
        }
    }
}
