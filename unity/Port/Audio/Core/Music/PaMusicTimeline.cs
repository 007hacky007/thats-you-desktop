// Segment and clip math for interactive music, in seconds on the voice clock.
//
// A segment instance starts at T0 (its time 0). Entry and exit are EntryMs and ExitMs after T0;
// beats, bars and the grid count from the entry. A clip of a track sounds from
// T0 + (PlayAtMs + BeginTrimMs)/1000 for (SrcDurationMs - BeginTrimMs - EndTrimMs)/1000 seconds,
// starting BeginTrimMs/1000 into its media. Anything before cutBefore is skipped by seeking,
// anything after cutAfter is cut.

using System;
using System.Collections.Generic;

namespace PortAudio
{
    // Immutable view of what a segment needs at runtime. A MusicTrack played on its own becomes a
    // one-track segment with entry 0 and exit at the end of its last clip.
    public sealed class PaSegmentInfo
    {
        const double MinSegmentMs = 100;
        public uint Id;
        public PaNode Node;
        public PaMusic Music;      // may be null for a bare track
        public double EntryMs, ExitMs, DurationMs;
        public PaNode[] Tracks;

        public static PaSegmentInfo From(PaNode node, PaBankStore banks)
        {
            if (node == null)
                return null;
            var info = new PaSegmentInfo { Id = node.Id, Node = node };
            if (node.Type == PaNodeType.MusicTrack)
            {
                info.Music = null;
                info.Tracks = new[] { node };
                double end = 0;
                if (node.Music != null && node.Music.Clips != null)
                    foreach (var c in node.Music.Clips)
                        if (c != null)
                            end = Math.Max(end, c.PlayAtMs + c.SrcDurationMs - c.EndTrimMs);
                info.EntryMs = 0;
                info.ExitMs = end;
                info.DurationMs = end;
                return info;
            }
            if (node.Type != PaNodeType.MusicSegment)
                return null;
            var m = node.Music;
            info.Music = m;
            var tracks = new List<PaNode>();
            if (node.Children != null && banks != null)
                foreach (var id in node.Children)
                {
                    var t = banks.Node(id);
                    if (t != null && t.Type == PaNodeType.MusicTrack)
                        tracks.Add(t);
                }
            info.Tracks = tracks.ToArray();
            if (m != null)
            {
                info.DurationMs = m.DurationMs;
                info.EntryMs = m.EntryMs;
                info.ExitMs = m.ExitMs;
                if (m.Markers != null)
                    foreach (var mk in m.Markers)
                    {
                        if (mk == null)
                            continue;
                        if (mk.Kind == "entry" && info.EntryMs == 0)
                            info.EntryMs = mk.PosMs;
                        else if (mk.Kind == "exit" && info.ExitMs == 0)
                            info.ExitMs = mk.PosMs;
                    }
            }
            if (info.ExitMs <= info.EntryMs)
                info.ExitMs = info.DurationMs > info.EntryMs ? info.DurationMs : info.EntryMs;
            if (info.ExitMs <= info.EntryMs)
            {
                // no usable exit: end with the last clip, and never chain zero-length instances
                foreach (var t in info.Tracks)
                    if (t.Music != null && t.Music.Clips != null)
                        foreach (var c in t.Music.Clips)
                            if (c != null)
                                info.ExitMs = Math.Max(info.ExitMs, c.PlayAtMs + c.SrcDurationMs - c.EndTrimMs);
                if (info.ExitMs < info.EntryMs + MinSegmentMs)
                    info.ExitMs = info.EntryMs + MinSegmentMs;
            }
            return info;
        }

        public PaMarker[] Markers
        {
            get { return Music != null && Music.Markers != null ? Music.Markers : new PaMarker[0]; }
        }
    }

    public struct PaClipPlacement
    {
        public int ClipIndex;
        public double StartAt;        // voice clock
        public double SourceOffset;   // seconds into the media
        public double PlayLength;     // seconds, 0 = natural length
        public double ClipStart;      // voice clock time of the untrimmed clip start (for automation)
        public double ClipEnd;        // voice clock time where the clip ends, +Inf when unknown
    }

    public static class PaMusicTimeline
    {
        const double Eps = 1e-6;
        const double MinLength = 0.005;

        public static double EntryTime(PaSegmentInfo s, double t0) { return t0 + s.EntryMs / 1000.0; }
        public static double ExitTime(PaSegmentInfo s, double t0) { return t0 + s.ExitMs / 1000.0; }

        // T0 of the next segment so that its entry lines up with this segment's exit.
        public static double ChainT0(double t0, PaSegmentInfo current, PaSegmentInfo next)
        {
            return t0 + current.ExitMs / 1000.0 - next.EntryMs / 1000.0;
        }

        public static double BeatSec(PaMusic m)
        {
            float tempo = m != null ? m.Tempo : 0f;
            if (tempo <= 0.01f)
                tempo = 120f;
            return 60.0 / tempo;
        }

        public static double BarSec(PaMusic m)
        {
            int num = m != null && m.TimeSigNum > 0 ? m.TimeSigNum : 4;
            return BeatSec(m) * num;
        }

        public static double GridSec(PaMusic m)
        {
            if (m != null && m.GridPeriodMs > 0)
                return m.GridPeriodMs / 1000.0;
            return BarSec(m);
        }

        // First point >= t of the series origin + k*period (k >= 0), or NaN.
        public static double NextOnGrid(double origin, double period, double t)
        {
            if (period <= 0)
                return double.NaN;
            if (t <= origin + Eps)
                return origin;
            double k = Math.Ceiling((t - origin) / period - Eps);
            return origin + k * period;
        }

        public static double NextBeat(PaSegmentInfo s, double t0, double t)
        {
            return NextOnGrid(EntryTime(s, t0), BeatSec(s.Music), t);
        }

        public static double NextBar(PaSegmentInfo s, double t0, double t)
        {
            return NextOnGrid(EntryTime(s, t0), BarSec(s.Music), t);
        }

        public static double NextGrid(PaSegmentInfo s, double t0, double t)
        {
            double off = s.Music != null ? s.Music.GridOffsetMs / 1000.0 : 0;
            return NextOnGrid(EntryTime(s, t0) + off, GridSec(s.Music), t);
        }

        static bool CueMatches(PaMarker m, uint cueFilter)
        {
            return cueFilter == 0 || m.Id == cueFilter;
        }

        // Next marker at or after t (customOnly: user cues only), filtered by cue id; NaN if none.
        public static double NextMarker(PaSegmentInfo s, double t0, double t, bool customOnly, uint cueFilter)
        {
            double best = double.NaN;
            foreach (var m in s.Markers)
            {
                if (m == null || (customOnly && m.Kind != "custom") || !CueMatches(m, cueFilter))
                    continue;
                double at = t0 + m.PosMs / 1000.0;
                if (at + Eps >= t && (double.IsNaN(best) || at < best))
                    best = at;
            }
            return best;
        }

        public static List<PaMarker> MarkersOf(PaSegmentInfo s, bool customOnly, uint cueFilter)
        {
            var l = new List<PaMarker>();
            foreach (var m in s.Markers)
            {
                if (m == null || m.Kind == "exit")
                    continue;
                if (customOnly && m.Kind != "custom")
                    continue;
                if (!CueMatches(m, cueFilter))
                    continue;
                l.Add(m);
            }
            return l;
        }

        // Placement of one clip. False when nothing of it is left to play.
        public static bool PlaceClip(PaClip clip, double t0, double cutBefore, double cutAfter, out PaClipPlacement p)
        {
            p = new PaClipPlacement();
            if (clip == null)
                return false;
            double start = t0 + (clip.PlayAtMs + clip.BeginTrimMs) / 1000.0;
            double offset = clip.BeginTrimMs / 1000.0;
            double length = (clip.SrcDurationMs - clip.BeginTrimMs - clip.EndTrimMs) / 1000.0;
            bool known = clip.SrcDurationMs > 0;
            if (known && length < MinLength)
                return false;
            p.ClipStart = start;
            p.ClipEnd = known ? start + length : double.PositiveInfinity;
            if (start < cutBefore)
            {
                double d = cutBefore - start;
                if (known && length - d < MinLength)
                    return false;
                start = cutBefore;
                offset += d;
                if (known)
                    length -= d;
            }
            if (!double.IsInfinity(cutAfter))
            {
                if (start >= cutAfter - MinLength)
                    return false;
                if (!known || start + length > cutAfter)
                {
                    if (!known)
                        p.ClipEnd = cutAfter;
                    length = cutAfter - start;
                    known = true;
                }
            }
            p.StartAt = start;
            p.SourceOffset = offset;
            p.PlayLength = known ? length : 0;
            return true;
        }

        // Clips of a track that play this time. Normal tracks play every clip; random and sequence
        // tracks play the clips of one sub-track, chosen by the caller.
        public static List<int> ClipsFor(PaNode track, int subTrack)
        {
            var l = new List<int>();
            var m = track.Music;
            if (m == null || m.Clips == null)
                return l;
            bool all = m.TrackType == null || m.TrackType == "normal" || subTrack < 0;
            for (int i = 0; i < m.Clips.Length; i++)
                if (m.Clips[i] != null && (all || m.Clips[i].SubTrack == subTrack))
                    l.Add(i);
            return l;
        }

        public static int SubTrackCount(PaNode track)
        {
            int n = 0;
            if (track.Music != null && track.Music.Clips != null)
                foreach (var c in track.Music.Clips)
                    if (c != null && c.SubTrack + 1 > n)
                        n = c.SubTrack + 1;
            return n;
        }

        // Earliest start of anything this segment would play with these cuts (+Inf if nothing).
        public static double EarliestStart(PaSegmentInfo s, double t0, double cutBefore, double cutAfter)
        {
            double best = double.PositiveInfinity;
            foreach (var t in s.Tracks)
            {
                if (t.Music == null || t.Music.Clips == null)
                    continue;
                foreach (var c in t.Music.Clips)
                {
                    PaClipPlacement p;
                    if (PlaceClip(c, t0, cutBefore, cutAfter, out p) && p.StartAt < best)
                        best = p.StartAt;
                }
            }
            return best;
        }
    }
}
