// Typed model of the converted Wwise graph (manifest, bank JSON, media tables).
//
// The field declarations are binding (audio architecture section 6.2): the pipeline writes one JSON
// key per field, named like the field with the first letter lowercased, and omits keys at their
// default. PaModelReader fills these classes; after reading, arrays and lists are never null,
// Props is always present and the other nested objects are null unless present in the JSON.

using System;
using System.Collections.Generic;

namespace PortAudio
{
    public sealed class PaDataException : Exception { public PaDataException(string m) : base(m) {} }

    public sealed class PaLanguage { public string Name, Key; public int BankLanguageId; }

    public sealed class PaBankInfo { public string Name, File; public uint Id; public bool Localized; public List<string> Languages; }

    public sealed class PaManifest
    {
        public int Format, WwiseBankVersion;
        public string DefaultLanguage;
        public List<PaLanguage> Languages;
        public List<PaBankInfo> Banks;
        public Dictionary<uint, string> Names;

        // forced (if known) > requested (if known) > DefaultLanguage > first. A language is known when
        // its Wwise name or its key matches, ignoring case. null when the manifest lists no language.
        public PaLanguage ResolveLanguage(string requested, string forced)
        {
            PaLanguage found = Lookup(forced);
            if (found == null)
                found = Lookup(requested);
            if (found == null)
                found = Lookup(DefaultLanguage);
            if (found == null && Languages != null && Languages.Count > 0)
                found = Languages[0];
            return found;
        }

        PaLanguage Lookup(string language)
        {
            if (string.IsNullOrEmpty(language) || Languages == null)
                return null;
            string trimmed = language.Trim();
            foreach (PaLanguage l in Languages)
            {
                if (l == null)
                    continue;
                if (string.Equals(l.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.Key, trimmed, StringComparison.OrdinalIgnoreCase))
                    return l;
            }
            return null;
        }
    }

    public sealed class PaBank
    {
        public int Format;
        public string Name, Language, LanguageKey;
        public uint Id;
        public List<PaEvent> Events;
        public List<PaAction> Actions;
        public List<PaNode> Nodes;
        public List<PaBus> Busses;
        public List<PaAttenuation> Attenuations;
        public List<PaGameParameter> GameParameters;
        public List<PaStateGroup> StateGroups;
        public List<PaEffect> Effects;
    }

    public sealed class PaEvent { public uint Id; public string Name; public uint[] Actions; }

    public enum PaActionType
    {
        Unknown, Play, PlayEvent, Stop, Pause, Resume, Break, Seek, SetState, SetSwitch, SetGameParameter, ResetGameParameter,
        Trigger, Mute, UnMute, SetVolume, ResetVolume, SetPitch, ResetPitch, SetLPF, ResetLPF, SetBusVolume, ResetBusVolume
    }

    public enum PaTargetMode { Element, All, AllExcept }

    public struct PaObjectRef { public uint Id; public bool IsBus; }

    public sealed class PaAction
    {
        // SetGameParameter: Target = game parameter id, Value(+range) absolute or relative, TransitionMs = ramp.
        // PlayEvent: Target = event id.
        public uint Id, Target, StateGroup, State, SwitchGroup, SwitchValue, BankId;
        public PaActionType Type;
        public int RawType;
        public PaTargetMode TargetMode;
        public bool GameObjectScoped, TargetIsBus, ValueRelative, BypassTransition, SeekRelative, SeekToMarker;
        public int DelayMs, DelayMinMs, DelayMaxMs, TransitionMs, TransitionMinMs, TransitionMaxMs;
        public PaCurve Curve = PaCurve.Linear;
        public float Probability = 100f, Value, ValueMin, ValueMax, SeekValue;
        public PaObjectRef[] Exceptions;
    }

    public enum PaNodeType { Sound, RanSeq, Switch, Layer, ActorMixer, MusicSegment, MusicTrack, MusicRanSeq, MusicSwitch }

    public struct PaRange { public float Min, Max; }

    public sealed class PaProps
    {
        public float Volume, Pitch, Lpf, BusVolume, OutputBusVolume, MakeUpGain, Lfe, PanLR, PanFR, CenterPct, InitialDelay;
        public float Probability = 100f, Priority = 50f;
        public int Loop = -1;                                             // -1 unset (=1), 0 infinite, n plays
        public PaRange VolumeRand, PitchRand, LpfRand, InitialDelayRand;
        public Dictionary<int, double> Unknown;
    }

    public sealed class PaPositioning { public bool ThreeD, Spatialize, UsePanner; public uint Attenuation; public float PanLR, PanFR, CenterPct; }

    public sealed class PaRtpc { public uint Rtpc; public string Param, Accumulation, Scaling; public int ParamId; public PaPoint[] Curve; }

    public sealed class PaAdvanced { public int MaxInstances, VirtualBehavior, BelowThreshold; public bool KillNewest, UseVirtual, GlobalLimit; }

    public sealed class PaSource { public uint Plugin, Media, InMemorySize; public string Kind, Stream; public bool LanguageSpecific; } // external: Media = cookie

    public struct PaWeighted { public uint Id; public int Weight; }

    public sealed class PaRanSeq
    {
        public bool Sequence, Shuffle, Continuous, ResetEachPlay, RestartBackward, Global, UseWeights;
        public int AvoidRepeat;
        public int LoopCount = 1, LoopMin, LoopMax;
        public string Transition = "none";
        public float TransitionSec, TransitionMinSec, TransitionMaxSec;
        public PaWeighted[] Playlist;
    }

    public sealed class PaSwitchAssign { public uint Value; public uint[] Nodes; }

    public sealed class PaSwitchChild { public uint Node; public bool FirstOnly, ContinuePlayback; public int OnSwitchMode, FadeOutMs, FadeInMs; } // OnSwitchMode 0 PlayToEnd, 1 Stop

    public sealed class PaSwitch { public bool IsState, Continuous; public uint Group, Default; public PaSwitchAssign[] Assignments; public PaSwitchChild[] ChildParams; }

    public sealed class PaMarker { public uint Id; public double PosMs; public string Name, Kind; }

    public sealed class PaClip { public int SubTrack; public uint Media; public double PlayAtMs, BeginTrimMs, EndTrimMs, SrcDurationMs; }

    public sealed class PaAutomation { public int Clip; public string Type; public PaPoint[] Points; }

    public sealed class PaPlaylistItem { public uint Id, Segment; public string Type; public int Loop = 1, Weight, AvoidRepeat; public bool Shuffle; public PaPlaylistItem[] Children; }

    public sealed class PaMusicArg { public uint Group; public bool IsState; }

    public sealed class PaMusicPath { public uint[] Keys; public uint Node; public int Weight; }          // key 0 = any; Node 0 = nothing

    public sealed class PaStinger { public uint Trigger, Segment, CueFilter; public string Sync; public int DontRepeatMs, LookAheadSegments; }

    public sealed class PaTransitionSrc { public int FadeMs, FadeOffsetMs, SyncRaw; public PaCurve Curve = PaCurve.Linear; public string Sync; public uint CueFilter; public bool PlayPostExit; }

    public sealed class PaTransitionDst
    {
        public int FadeMs, FadeOffsetMs, EntryRaw;
        public PaCurve Curve = PaCurve.Linear;
        public uint CueFilter, JumpTo;
        public string Entry;
        public bool PlayPreEntry, MatchSourceCueName;
    }

    public sealed class PaTransitionSegment { public uint Segment; public int FadeInMs, FadeOutMs; public PaCurve FadeInCurve, FadeOutCurve; public bool PlayPreEntry, PlayPostExit; }

    public sealed class PaTransitionRule { public int[] Src, Dst; public PaTransitionSrc SrcRule; public PaTransitionDst DstRule; public PaTransitionSegment Segment; }

    public sealed class PaMusic
    {
        public float Tempo;
        public int TimeSigNum, TimeSigDen, LookAheadMs;
        public double GridPeriodMs, GridOffsetMs, DurationMs, EntryMs, ExitMs;
        public PaStinger[] Stingers; public PaTransitionRule[] Transitions; public PaMarker[] Markers;          // segment: Duration/Entry/Exit/Markers
        public string TrackType, Mode; public PaSource[] Sources; public PaClip[] Clips; public PaAutomation[] Automation; // track
        public PaPlaylistItem Playlist;                                                                       // music ranseq
        public bool ContinuePlaying; public PaMusicArg[] Arguments; public PaMusicPath[] Paths;               // music switch
    }

    public sealed class PaNode
    {
        public uint Id, Parent, Bus;                                      // Bus != 0 only where the node overrides the output bus
        public PaNodeType Type;
        public string Name;
        public uint[] Children;
        public PaProps Props;
        public PaPositioning Positioning;
        public PaRtpc[] Rtpcs;
        public PaAdvanced Advanced;
        public PaSource Source;
        public PaRanSeq RanSeq;
        public PaSwitch Switch;
        public PaMusic Music;
    }

    public sealed class PaDuck { public uint Bus; public float VolumeDb; public int FadeOutMs, FadeInMs; public PaCurve Curve; public string TargetProp; }

    public sealed class PaBusFx { public int Index; public uint Effect; public bool Bypass; }

    public sealed class PaBus
    {
        public uint Id, Parent;
        public string Name;
        public bool IsAux, BackgroundMusic;
        public PaProps Props;
        public PaRtpc[] Rtpcs;
        public PaDuck[] Ducks;
        public int DuckRecoveryMs;
        public float MaxDuckDb;
        public PaBusFx[] Fx;
    }

    public sealed class PaAttenuationCurve { public string Use, Scaling; public PaPoint[] Points; }               // x = distance

    public sealed class PaAttenuation
    {
        public uint Id;
        public bool ConeUse;
        public float ConeInnerDeg, ConeOuterDeg, ConeMaxDb, ConeLpf;
        public PaAttenuationCurve[] Curves;
        public PaRtpc[] Rtpcs;
    }

    public sealed class PaGameParameter { public uint Id; public string Name, RampType; public float Default, RampUp, RampDown; }

    public sealed class PaStateGroup { public uint Id; public string Name; public int DefaultTransitionMs; }

    public sealed class PaMeter { public float AttackSec, ReleaseSec, HoldSec, MinDb, MaxDb; public string Mode; public uint GameParameter; }

    public sealed class PaEffect
    {
        public uint Id, Plugin;
        public string PluginName, RawParams;                              // PluginName e.g. meter|gain|matrixReverb|compressor|peakLimiter|parametricEq|other
        public bool ShareSet;
        public PaMeter Meter;
        public float GainDb;
        public PaRtpc[] Rtpcs;
    }

    public sealed class PaCue { public long Pos; public string Label; }

    public sealed class PaMediaEntry
    {
        public uint Id;
        public string Name, Scope, File, Format, Label, FullPath;          // FullPath = root + File
        public int Rate, Channels;
        public long Samples, Bytes;
        public PaCue[] Cues;

        // Seconds: Samples / Rate (0 when the rate is unknown).
        public double Duration
        {
            get { return Rate > 0 ? Samples / (double)Rate : 0.0; }
        }
    }
}
