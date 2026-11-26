using static PortAudio.Tests.MusicData;

namespace PortAudio.Tests
{
    public sealed class PaMusicTimelineTests
    {
        static PaSegmentInfo Seg(double dur, double entry, double exit)
        {
            var n = Segment(1, 0, dur, entry, exit);
            return PaSegmentInfo.From(n, null);
        }

        [PaTest]
        public void ClipPlacementFromPlayAtAndTrims()
        {
            PaClipPlacement p;
            Check.True(PaMusicTimeline.PlaceClip(Clip(5, 1000, 200, 300, 5000), 10.0, double.NegativeInfinity, double.PositiveInfinity, out p), "placed");
            Check.Near(11.2, p.StartAt, 1e-9, "start");
            Check.Near(0.2, p.SourceOffset, 1e-9, "offset");
            Check.Near(4.5, p.PlayLength, 1e-9, "length");
        }

        [PaTest]
        public void PreEntryIsSkippedAndPostExitCut()
        {
            var s = Seg(8000, 1000, 5000);
            double t0 = 20.0;
            PaClipPlacement p;
            // clip starts 500 ms before the segment (negative playAt) and runs past the exit
            Check.True(PaMusicTimeline.PlaceClip(Clip(5, -500, 0, 0, 7000), t0, PaMusicTimeline.EntryTime(s, t0), PaMusicTimeline.ExitTime(s, t0), out p), "placed");
            Check.Near(21.0, p.StartAt, 1e-9, "starts at entry");
            Check.Near(1.5, p.SourceOffset, 1e-9, "skipped 1.5 s of pre-entry");
            Check.Near(4.0, p.PlayLength, 1e-9, "cut at exit");
            Check.True(!PaMusicTimeline.PlaceClip(Clip(6, 5500, 0, 0, 1000), t0, double.NegativeInfinity, PaMusicTimeline.ExitTime(s, t0), out p), "post-exit clip dropped");
            Check.True(PaMusicTimeline.PlaceClip(Clip(6, 5500, 0, 0, 1000), t0, double.NegativeInfinity, double.PositiveInfinity, out p), "post-exit kept when allowed");
        }

        [PaTest]
        public void GaplessChain()
        {
            var a = Seg(10000, 1000, 9000);
            var b = Seg(6000, 500, 5000);
            double t0b = PaMusicTimeline.ChainT0(50.0, a, b);
            Check.Near(58.5, t0b, 1e-9, "next T0");
            Check.Near(PaMusicTimeline.ExitTime(a, 50.0), PaMusicTimeline.EntryTime(b, t0b), 1e-9, "entry meets exit");
        }

        [PaTest]
        public void BeatBarAndGridFromEntry()
        {
            var s = Seg(20000, 1000, 17000);   // 120 bpm 4/4: beat 0.5 s, bar 2 s
            double t0 = 10.0;                   // entry at 11.0
            Check.Near(11.5, PaMusicTimeline.NextBeat(s, t0, 11.3), 1e-9, "next beat");
            Check.Near(11.5, PaMusicTimeline.NextBeat(s, t0, 11.5), 1e-9, "on the beat");
            Check.Near(13.0, PaMusicTimeline.NextBar(s, t0, 11.3), 1e-9, "next bar");
            Check.Near(11.0, PaMusicTimeline.NextBar(s, t0, 10.2), 1e-9, "before entry: the entry");
            s.Music.GridPeriodMs = 750; s.Music.GridOffsetMs = 250;
            Check.Near(12.75, PaMusicTimeline.NextGrid(s, t0, 12.1), 1e-9, "grid with offset");
            s.Music.Tempo = 90; s.Music.TimeSigNum = 3;
            Check.Near(11.0 + 2.0, PaMusicTimeline.NextBar(s, t0, 11.1), 1e-9, "3/4 at 90 bpm: bar 2 s");
        }

        [PaTest]
        public void MarkerQueries()
        {
            var n = Segment(1, 0, 10000, 0, 8000);
            n.Music.Markers = new[]
            {
                new PaMarker { Id = 1, PosMs = 0, Kind = "entry" },
                new PaMarker { Id = 77, PosMs = 2000, Kind = "custom", Name = "1j" },
                new PaMarker { Id = 78, PosMs = 4000, Kind = "custom", Name = "2j" },
                new PaMarker { Id = 2, PosMs = 8000, Kind = "exit" },
            };
            var s = PaSegmentInfo.From(n, null);
            Check.Near(12.0, PaMusicTimeline.NextMarker(s, 10.0, 11.0, true, 0), 1e-9, "next custom");
            Check.Near(14.0, PaMusicTimeline.NextMarker(s, 10.0, 11.0, true, 78), 1e-9, "cue filter");
            Check.Near(18.0, PaMusicTimeline.NextMarker(s, 10.0, 14.5, false, 0), 1e-9, "any marker: exit");
            Check.True(double.IsNaN(PaMusicTimeline.NextMarker(s, 10.0, 14.5, true, 0)), "no more custom");
            Check.Equal(2, PaMusicTimeline.MarkersOf(s, true, 0).Count, "custom markers");
        }

        [PaTest]
        public void SyncPointsPerType()
        {
            var s = Seg(20000, 1000, 17000);
            double t0 = 10.0;
            Check.Near(11.5, PaMusicTransitions.SyncIn("nextBeat", 0, s, t0, 11.3), 1e-9, "beat");
            Check.Near(13.0, PaMusicTransitions.SyncIn("nextBar", 0, s, t0, 11.3), 1e-9, "bar");
            Check.Near(27.0, PaMusicTransitions.SyncIn("exitMarker", 0, s, t0, 11.3), 1e-9, "exit");
            Check.Near(11.3, PaMusicTransitions.SyncIn("immediate", 0, s, t0, 11.3), 1e-9, "immediate");
            Check.True(double.IsNaN(PaMusicTransitions.SyncIn("nextBar", 0, Seg(20000, 1000, 16500), t0, 25.5)), "no bar before exit");
            Check.True(double.IsNaN(PaMusicTransitions.SyncIn("entryMarker", 0, s, t0, 11.3)), "entry passed");
        }

        [PaTest]
        public void SameTimeDestinationKeepsOffset()
        {
            var d = Seg(20000, 1000, 17000);
            var dst = new PaTransitionDst { Entry = "sameTime" };
            double t0 = PaMusicTransitions.DestinationT0(dst, d, 50.0, 3.25, null);
            Check.Near(50.0 - 3.25, PaMusicTimeline.EntryTime(d, t0), 1e-9, "same distance from entry");
            dst.Entry = "entryMarker";
            Check.Near(50.0, PaMusicTimeline.EntryTime(d, PaMusicTransitions.DestinationT0(dst, d, 50.0, 3.25, null)), 1e-9, "entry at sync");
        }

        [PaTest]
        public void RuleSelectionTakesLastMatch()
        {
            var rules = new[]
            {
                Rule(-1, -1, "exitMarker", "entryMarker"),
                Rule(5, -1, "nextBar", "entryMarker"),
                Rule(-1, 9, "nextBeat", "sameTime"),
                Rule(6, -1, "immediate", "entryMarker"),
            };
            Check.Equal("nextBeat", PaMusicTransitions.Select(rules, new uint[] { 5, 50 }, new uint[] { 9, 90 }).SrcRule.Sync, "last match wins");
            Check.Equal("nextBar", PaMusicTransitions.Select(rules, new uint[] { 5 }, new uint[] { 8 }).SrcRule.Sync, "source specific");
            Check.Equal("exitMarker", PaMusicTransitions.Select(rules, new uint[] { 7 }, new uint[] { 8 }).SrcRule.Sync, "wildcard");
            Check.True(PaMusicTransitions.Select(new PaTransitionRule[0], new uint[] { 7 }, new uint[] { 8 }) == null, "no rules");
        }
    }
}
