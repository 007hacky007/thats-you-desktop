// Transition rules between music objects: which rule applies, when the switch happens and where
// the destination starts.
//
// Rules live on music containers; a container without a matching rule defers to its music
// ancestors. Inside one container the last rule whose Src and Dst match wins. Src matches -1
// or any object on the source path (switch child down to the playing segment); Dst matches -1
// or any object on the destination path. Exact Wwise sync is not the goal; the results are
// musically sensible and deterministic.

using System;
using System.Collections.Generic;

namespace PortAudio
{
    public static class PaMusicTransitions
    {
        static bool Matches(int[] ids, IList<uint> path)
        {
            if (ids == null || ids.Length == 0)
                return true;
            foreach (int id in ids)
            {
                if (id == -1)
                    return true;
                if (path != null)
                    foreach (uint p in path)
                        if (unchecked((uint)id) == p)
                            return true;
            }
            return false;
        }

        // Last matching rule of one container, or null.
        public static PaTransitionRule Select(PaTransitionRule[] rules, IList<uint> srcPath, IList<uint> dstPath)
        {
            if (rules == null)
                return null;
            PaTransitionRule found = null;
            foreach (var r in rules)
                if (r != null && Matches(r.Src, srcPath) && Matches(r.Dst, dstPath))
                    found = r;
            return found;
        }

        // Rule lookup starting at container `owner`, then its music ancestors.
        public static PaTransitionRule Find(PaBankStore banks, uint owner, IList<uint> srcPath, IList<uint> dstPath)
        {
            if (banks == null || owner == 0)
                return null;
            foreach (uint id in banks.Chain(owner))
            {
                var n = banks.Node(id);
                if (n == null || n.Music == null || !IsMusicContainer(n.Type))
                    continue;
                var r = Select(n.Music.Transitions, srcPath, dstPath);
                if (r != null)
                    return r;
            }
            return null;
        }

        public static bool IsMusicContainer(PaNodeType t)
        {
            return t == PaNodeType.MusicSwitch || t == PaNodeType.MusicRanSeq || t == PaNodeType.MusicSegment;
        }

        // Sync point inside one segment instance at or after t; NaN when the segment has none.
        public static double SyncIn(string sync, uint cueFilter, PaSegmentInfo s, double t0, double t)
        {
            double entry = PaMusicTimeline.EntryTime(s, t0), exit = PaMusicTimeline.ExitTime(s, t0);
            double at;
            switch (sync ?? "exitMarker")
            {
                case "immediate":
                    return t <= exit + 1e-6 ? t : double.NaN;
                case "nextGrid":
                    at = PaMusicTimeline.NextGrid(s, t0, t);
                    break;
                case "nextBar":
                    at = PaMusicTimeline.NextBar(s, t0, t);
                    break;
                case "nextBeat":
                    at = PaMusicTimeline.NextBeat(s, t0, t);
                    break;
                case "nextMarker":
                    at = PaMusicTimeline.NextMarker(s, t0, t, false, cueFilter);
                    break;
                case "nextUserMarker":
                    at = PaMusicTimeline.NextMarker(s, t0, t, true, cueFilter);
                    break;
                case "entryMarker":
                    at = entry >= t - 1e-6 ? entry : double.NaN;
                    break;
                default:   // exitMarker, exitNever, lastExitPosition
                    at = exit >= t - 1e-6 ? exit : double.NaN;
                    break;
            }
            if (double.IsNaN(at) || at > exit + 1e-6)
                return double.NaN;
            return at;
        }

        // Earliest sync point for a rule. For grid and marker syncs the search starts late enough that the
        // source fade-out (which ends at the sync) fits after `now`; immediate and exit syncs are not moved
        // and clamp the fade start instead (SourceFadeStart).
        public static double EarliestSync(PaTransitionSrc src, double t, double now)
        {
            if (src == null || src.FadeMs <= 0 || src.FadeOffsetMs <= 0)
                return t;
            switch (src.Sync)
            {
                case "nextGrid":
                case "nextBar":
                case "nextBeat":
                case "nextMarker":
                case "nextUserMarker":
                    return Math.Max(t, now + src.FadeOffsetMs / 1000.0);
                default:
                    return t;
            }
        }

        // Source fade-out start. A positive offset is time before the sync point, so offset == fade time
        // (the authoring default, and every faded source rule in the banks) ends the fade at the sync.
        // A start in the past is clamped to `now` and keeps the full fade length.
        public static double SourceFadeStart(PaTransitionSrc src, double sync, double now)
        {
            double at = sync - (src != null ? src.FadeOffsetMs : 0) / 1000.0;
            return at < now ? now : at;
        }

        // Destination fade-in start. A positive offset is time before the sync point; the fade never
        // starts before the destination can sound (`earliest`), so it is always heard in full.
        public static double DestinationFadeStart(PaTransitionDst dst, double sync, double earliest)
        {
            double at = sync - (dst != null ? dst.FadeOffsetMs : 0) / 1000.0;
            return at < earliest ? earliest : at;
        }

        // Where the destination segment starts: T0 for a destination that begins sounding at `sync`.
        // srcOffset is the source position after its entry at the sync point (for sameTime).
        public static double DestinationT0(PaTransitionDst dst, PaSegmentInfo d, double sync, double srcOffset, PaRandom random)
        {
            string entry = dst != null && dst.Entry != null ? dst.Entry : "entryMarker";
            uint cue = dst != null ? dst.CueFilter : 0;
            switch (entry)
            {
                case "sameTime":
                {
                    double off = Math.Max(0, srcOffset);
                    double len = (d.ExitMs - d.EntryMs) / 1000.0;
                    if (len > 0 && off >= len)
                        off = 0;
                    return sync - d.EntryMs / 1000.0 - off;
                }
                case "randomMarker":
                case "randomUserMarker":
                {
                    var markers = PaMusicTimeline.MarkersOf(d, entry == "randomUserMarker", cue);
                    if (markers.Count == 0 && cue != 0)
                        markers = PaMusicTimeline.MarkersOf(d, entry == "randomUserMarker", 0);
                    if (markers.Count == 0)
                        return sync - d.EntryMs / 1000.0;
                    var m = markers[random != null ? random.Range(0, markers.Count) : 0];
                    return sync - m.PosMs / 1000.0;
                }
                default:   // entryMarker, lastExitTime
                    return sync - d.EntryMs / 1000.0;
            }
        }
    }
}
