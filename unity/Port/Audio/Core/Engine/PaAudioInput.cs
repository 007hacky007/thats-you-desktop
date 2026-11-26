// Audio input: voice clips sent from the players' phones.
//
// The game's native module had extra entry points (kept as PortAudioNull's five static
// methods) that take a decoded clip as 16-bit mono PCM at 48 kHz and play it through a Wwise
// audio input source. Here the clip is converted to float samples and played by a voice: the
// first Sound with source kind "audioInput" in the loaded banks gives it its bus and
// properties; without one the clip still plays on the master bus. Any audio input Sound posted
// through a normal event plays the latest clip too.

using System;

namespace PortAudio
{
    internal sealed class PaAudioInput
    {
        public const int Rate = 48000;

        public float[] Pcm;
        public bool StartRequested;
        public bool Playing;
        public int Generation;
        public PaVoiceRef Voice;
        public PaInstance Instance;
        int nodeCacheVersion = -1;
        uint nodeCache;

        public static float[] ToFloat(byte[] pcm16, int bytes)
        {
            if (pcm16 == null)
                return new float[0];
            int count = Math.Max(0, Math.Min(bytes, pcm16.Length)) / 2;
            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                short s = (short)(pcm16[2 * i] | (pcm16[2 * i + 1] << 8));
                samples[i] = s / 32768f;
            }
            return samples;
        }

        // The audio input Sound of the loaded banks (cached per bank store version).
        public uint FindNode(PaBankStore banks)
        {
            if (banks.Version == nodeCacheVersion)
                return nodeCache;
            nodeCacheVersion = banks.Version;
            nodeCache = 0;
            foreach (PaBank b in banks.Loaded)
            {
                foreach (PaNode n in b.Nodes)
                {
                    if (n.Type == PaNodeType.Sound && n.Source != null && string.Equals(n.Source.Kind, "audioInput", StringComparison.OrdinalIgnoreCase))
                    {
                        nodeCache = n.Id;
                        return nodeCache;
                    }
                }
            }
            return 0;
        }
    }

    internal sealed partial class PaCore
    {
        readonly PaAudioInput audioInput = new PaAudioInput();

        public const ulong AudioInputObject = 0xA0D10000UL;

        // Under the engine lock: keep the clip and start it on the next tick.
        public bool AudioInputPlay(byte[] pcm16, int bytes)
        {
            audioInput.Pcm = PaAudioInput.ToFloat(pcm16, bytes);
            audioInput.Generation++;
            audioInput.StartRequested = true;
            audioInput.Playing = audioInput.Pcm.Length > 0;
            int generation = audioInput.Generation;
            commands.Enqueue(() => StartAudioInput(generation));
            return true;
        }

        public void AudioInputStop()
        {
            audioInput.StartRequested = false;
            audioInput.Playing = false;
            PaInstance inst = audioInput.Instance;
            audioInput.Instance = null;
            audioInput.Voice = null;
            if (inst != null)
                commands.Enqueue(() => StopInstance(inst, 0, PaCurve.Linear));
        }

        public bool AudioInputIsPlaying
        {
            get
            {
                if (audioInput.StartRequested)
                    return audioInput.Playing;
                return audioInput.Voice != null && !audioInput.Voice.Ended;
            }
        }

        // Playback position in samples of the clip.
        public int AudioInputPosition
        {
            get
            {
                PaVoiceRef v = audioInput.Voice;
                if (v == null || v.Ended || v.Voice == null)
                    return 0;
                double pos = v.Voice.Position;
                if (double.IsNaN(pos) || pos < 0)
                    return 0;
                return (int)Math.Min(int.MaxValue, pos * PaAudioInput.Rate);
            }
        }

        void StartAudioInput(int generation)
        {
            if (generation != audioInput.Generation || !audioInput.StartRequested)
                return;
            StopAudioInputVoice();
            audioInput.StartRequested = false;
            if (audioInput.Pcm == null || audioInput.Pcm.Length == 0)
            {
                audioInput.Playing = false;
                return;
            }
            PaInstance inst = CreateInstance(0, AudioInputObject, 0, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, 0);
            inst.Started = true;
            audioInput.Instance = inst;
            uint node = audioInput.FindNode(banks);
            int before = inst.Voices.Count;
            if (node != 0)
                PlayNode(inst, node, new PaPlayContext());
            if (inst.Voices.Count == before)
            {
                PaVoiceSpec spec = new PaVoiceSpec
                {
                    PlayingId = inst.PlayingId, GameObject = AudioInputObject, NodeId = 0,
                    Pcm = audioInput.Pcm, PcmRate = PaAudioInput.Rate, PcmChannels = 1, StartAt = voices.Now,
                };
                PaVoiceRef v = new PaVoiceRef(this, inst, 0, ++voiceCounter);
                spec.Tag = v;
                IPaVoice voice = voices.Play(spec, v);
                if (voice != null)
                {
                    v.Voice = voice;
                    inst.Voices.Add(v);
                    liveVoices.Add(v);
                    stats.VoicesStarted++;
                }
            }
            audioInput.Voice = inst.Voices.Count > before ? inst.Voices[inst.Voices.Count - 1] : null;
            audioInput.Playing = audioInput.Voice != null;
            if (PaLog.Tracing)
                PaLog.Trace("play", "playingId", inst.PlayingId, "node", node, "nname", "audioInput", "result", audioInput.Voice != null ? "voices" : "nothing", "reason", null);
        }

        void StopAudioInputVoice()
        {
            if (audioInput.Instance != null)
                StopInstance(audioInput.Instance, 0, PaCurve.Linear);
            audioInput.Instance = null;
            audioInput.Voice = null;
        }
    }
}
