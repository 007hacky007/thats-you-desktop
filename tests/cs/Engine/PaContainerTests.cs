// Container selection: switch and state containers (with continuous validation), random
// weights, avoid repeat, shuffle, sequence ping-pong, continuous containers and layers.

using System;
using System.Collections.Generic;

namespace PortAudio.Tests
{
    public static class PaContainerTests
    {
        static readonly uint Group = PaIds.Hash("SCENARIO");
        static readonly uint V1 = PaIds.Hash("WILD");
        static readonly uint V2 = PaIds.Hash("HOME");

        static EngineFixture SwitchFixture(bool isState, bool continuous, double len = 10.0)
        {
            EngineFixture f = new EngineFixture();
            BankBuilder b = f.Bank("Switches");
            PaNode sw = b.Node(30, PaNodeType.Switch, 0, 101, 102);
            sw.Switch = new PaSwitch
            {
                IsState = isState, Continuous = continuous, Group = Group, Default = V1,
                Assignments = new[] { new PaSwitchAssign { Value = V1, Nodes = new uint[] { 101 } }, new PaSwitchAssign { Value = V2, Nodes = new uint[] { 102 } } },
                ChildParams = new[]
                {
                    new PaSwitchChild { Node = 101, OnSwitchMode = 1, FadeOutMs = 200, FadeInMs = 0 },
                    new PaSwitchChild { Node = 102, OnSwitchMode = 0, FadeInMs = 300 },
                },
            };
            b.Sound(101, 30, 501);
            b.Sound(102, 30, 502);
            f.MediaLi.Add(Media.Entry(501, len));
            f.MediaLi.Add(Media.Entry(502, len));
            b.Event("Play_Switch", b.Action(PaActionType.Play, 30));
            return f;
        }

        [PaTest]
        public static void SwitchContainerPlaysTheSelectedChild()
        {
            using (EngineFixture f = SwitchFixture(false, false))
            {
                f.Start();
                f.Post("Play_Switch", 1, false);
                f.Step();
                PaAssert.SequenceEqual(new uint[] { 101 }, f.Voices.Nodes(), "unset: the default value");
                PaEngine.SetSwitch(Group, V2, 1);
                f.Post("Play_Switch", 1, false);
                f.Post("Play_Switch", 2, false);
                f.Step();
                PaAssert.SequenceEqual(new uint[] { 101, 102, 101 }, f.Voices.Nodes(), "per object");
                PaEngine.SetSwitch(Group, V2, PaIds.GlobalObject);
                f.Post("Play_Switch", 3, false);
                f.Step();
                PaAssert.Equal(102u, f.Voices.All[3].Spec.NodeId, "global switch value");
            }
        }

        [PaTest]
        public static void StateContainerFollowsTheState()
        {
            using (EngineFixture f = SwitchFixture(true, false))
            {
                f.Start();
                PaEngine.SetState(Group, V2);
                uint state;
                PaEngine.GetState(Group, out state);
                PaAssert.Equal(V2, state, "state stored at once");
                f.Post("Play_Switch", 1, false);
                f.Step();
                PaAssert.SequenceEqual(new uint[] { 102 }, f.Voices.Nodes());
                PaEngine.SetSwitch(Group, V1, 1);
                f.Post("Play_Switch", 1, false);
                f.Step();
                PaAssert.Equal(102u, f.Voices.All[1].Spec.NodeId, "a switch of the same id does not drive a state container");
            }
        }

        [PaTest]
        public static void ContinuousSwitchFollowsChanges()
        {
            using (EngineFixture f = SwitchFixture(false, true))
            {
                f.Start();
                uint pid = f.Post("Play_Switch", 1);
                f.Step();
                PaAssert.SequenceEqual(new uint[] { 101 }, f.Voices.Nodes());
                PaEngine.SetSwitch(Group, V1, 2);   // another object: no effect
                f.Step();
                PaAssert.Equal(1, f.Voices.All.Count);
                PaEngine.SetSwitch(Group, V2, 1);
                f.Step();
                PaAssert.Equal(2, f.Voices.All.Count, "the new child started");
                PaAssert.Equal("stop 200", f.Voices.All[0].Ops[0], "the old child stops with its fade-out");
                PaAssert.Equal(102u, f.Voices.All[1].Spec.NodeId);
                PaAssert.Equal(300, f.Voices.All[1].Spec.FadeInMs, "with its fade-in");
                PaAssert.Equal(pid, f.Voices.All[1].Spec.PlayingId, "same instance");
                PaEngine.SetSwitch(Group, V1, 1);
                f.Step();
                PaAssert.Equal(0, f.Voices.All[1].Ops.Count, "102 plays to the end (OnSwitchMode 0)");
                PaAssert.Equal(3, f.Voices.All.Count);
                PaAssert.Equal(101u, f.Voices.All[2].Spec.NodeId);
                PaAssert.Equal(0, f.EndOfEvents().Count, "instance alive");
            }
        }

        [PaTest]
        public static void ContinuousStateSwitchFollowsStates()
        {
            using (EngineFixture f = SwitchFixture(true, true))
            {
                f.Start();
                f.Post("Play_Switch", 1, false);
                f.Step();
                PaEngine.SetState(Group, V2);
                f.Step();
                PaAssert.SequenceEqual(new uint[] { 101, 102 }, f.Voices.Nodes());
                PaAssert.True(f.Music.Calls.Contains("state " + Group + " " + V2), "music is told about the state");
            }
        }

        [PaTest]
        public static void SwitchWithNothingAssignedPlaysNothing()
        {
            using (EngineFixture f = SwitchFixture(false, false))
            {
                f.Start();
                PaEngine.SetSwitch(Group, PaIds.Hash("UNASSIGNED"), 1);
                uint pid = f.Post("Play_Switch", 1);
                f.Step();
                PaAssert.Equal(0, f.Voices.All.Count);
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents(), "empty switch path ends on the next tick");
            }
        }

        static EngineFixture RanSeqFixture(PaRanSeq rs, int children, long seed = 12345, double len = 0.1)
        {
            EngineFixture f = new EngineFixture();
            f.Platform.settings.Seed = seed;
            BankBuilder b = f.Bank("RanSeq");
            uint[] ids = new uint[children];
            for (int i = 0; i < children; i++)
                ids[i] = (uint)(101 + i);
            b.RanSeq(40, 0, rs, ids);
            for (int i = 0; i < children; i++)
            {
                b.Sound(ids[i], 40, (uint)(501 + i));
                f.MediaLi.Add(Media.Entry((uint)(501 + i), len));
            }
            b.Event("Play_RS", b.Action(PaActionType.Play, 40));
            return f;
        }

        static List<uint> Picks(EngineFixture f, int count)
        {
            for (int i = 0; i < count; i++)
                f.Post("Play_RS", 1, false);
            f.Step();
            return f.Voices.Nodes();
        }

        [PaTest]
        public static void RandomWeights()
        {
            PaRanSeq rs = new PaRanSeq { UseWeights = true };
            using (EngineFixture f = RanSeqFixture(rs, 2))
            {
                rs.Playlist = new[] { new PaWeighted { Id = 101, Weight = 90 }, new PaWeighted { Id = 102, Weight = 10 } };
                f.Start();
                List<uint> picks = Picks(f, 1000);
                int a = picks.FindAll(x => x == 101).Count;
                PaAssert.True(a > 850 && a < 950, "about 90% for weight 90, got " + a);
            }
        }

        [PaTest]
        public static void RandomAvoidsRepeats()
        {
            using (EngineFixture f = RanSeqFixture(new PaRanSeq { AvoidRepeat = 2 }, 3))
            {
                f.Start();
                List<uint> picks = Picks(f, 300);
                for (int i = 2; i < picks.Count; i++)
                    PaAssert.True(picks[i] != picks[i - 1] && picks[i] != picks[i - 2], "no repeat of the last 2 at " + i);
                PaAssert.True(picks.Contains(101) && picks.Contains(102) && picks.Contains(103));
            }
            using (EngineFixture f = RanSeqFixture(new PaRanSeq { AvoidRepeat = 5 }, 2))
            {
                f.Start();
                List<uint> picks = Picks(f, 20);
                for (int i = 1; i < picks.Count; i++)
                    PaAssert.True(picks[i] != picks[i - 1], "avoid repeat is capped at count-1");
            }
        }

        [PaTest]
        public static void ShuffleIsAPermutationAndDeterministicPerSeed()
        {
            List<uint> first, second, other;
            using (EngineFixture f = RanSeqFixture(new PaRanSeq { Shuffle = true }, 4, 777))
            {
                f.Start();
                first = Picks(f, 40);
            }
            using (EngineFixture f = RanSeqFixture(new PaRanSeq { Shuffle = true }, 4, 777))
            {
                f.Start();
                second = Picks(f, 40);
            }
            using (EngineFixture f = RanSeqFixture(new PaRanSeq { Shuffle = true }, 4, 778))
            {
                f.Start();
                other = Picks(f, 40);
            }
            PaAssert.SequenceEqual(first, second, "same seed, same order");
            PaAssert.False(string.Join(",", first) == string.Join(",", other), "another seed, another order");
            for (int block = 0; block < 10; block++)
            {
                List<uint> part = first.GetRange(block * 4, 4);
                part.Sort();
                PaAssert.SequenceEqual(new uint[] { 101, 102, 103, 104 }, part, "every block of 4 plays each item once");
            }
        }

        [PaTest]
        public static void SetRandomSeedReseeds()
        {
            List<uint> a, b;
            using (EngineFixture f = RanSeqFixture(new PaRanSeq { Global = true }, 5))
            {
                f.Start();
                PaEngine.SetRandomSeed(99);
                a = Picks(f, 20);
            }
            using (EngineFixture f = RanSeqFixture(new PaRanSeq { Global = true }, 5, 4242))
            {
                f.Start();
                PaEngine.SetRandomSeed(99);
                b = Picks(f, 20);
            }
            PaAssert.SequenceEqual(a, b);
        }

        [PaTest]
        public static void SequencePingPong()
        {
            using (EngineFixture f = RanSeqFixture(new PaRanSeq { Sequence = true, RestartBackward = true }, 3))
            {
                f.Start();
                PaAssert.SequenceEqual(new uint[] { 101, 102, 103, 102, 101, 102, 103, 102 }, Picks(f, 8));
            }
            using (EngineFixture f = RanSeqFixture(new PaRanSeq { Sequence = true }, 3))
            {
                f.Start();
                PaAssert.SequenceEqual(new uint[] { 101, 102, 103, 101, 102 }, Picks(f, 5), "restart");
            }
        }

        [PaTest]
        public static void SequenceStateIsPerObjectUnlessGlobal()
        {
            using (EngineFixture f = RanSeqFixture(new PaRanSeq { Sequence = true }, 3))
            {
                f.Start();
                f.Post("Play_RS", 1, false);
                f.Post("Play_RS", 2, false);
                f.Post("Play_RS", 1, false);
                f.Step();
                PaAssert.SequenceEqual(new uint[] { 101, 101, 102 }, f.Voices.Nodes());
            }
            using (EngineFixture f = RanSeqFixture(new PaRanSeq { Sequence = true, Global = true }, 3))
            {
                f.Start();
                f.Post("Play_RS", 1, false);
                f.Post("Play_RS", 2, false);
                f.Post("Play_RS", 1, false);
                f.Step();
                PaAssert.SequenceEqual(new uint[] { 101, 102, 103 }, f.Voices.Nodes());
            }
        }

        [PaTest]
        public static void ContinuousDelayGenerator()
        {
            PaRanSeq rs = new PaRanSeq { Continuous = true, LoopCount = 0, Transition = "delay", TransitionSec = 1.5f };
            using (EngineFixture f = RanSeqFixture(rs, 1, 12345, 2.0))
            {
                f.Start();
                uint pid = f.Post("Play_RS", 1);
                f.Step();
                f.StepUntil(12.0, 0.05);
                List<double> starts = new List<double>();
                foreach (EngineVoice v in f.Voices.All)
                    starts.Add(v.StartTime);
                PaAssert.True(starts.Count >= 4, "an endless generator, got " + starts.Count);
                PaAssert.Near(0.0, starts[0], 1e-6);
                PaAssert.Near(3.5, starts[1], 1e-6, "end + delay");
                PaAssert.Near(7.0, starts[2], 1e-6);
                PaAssert.Equal(0, f.EndOfEvents().Count, "infinite loop keeps the instance alive");
                PaEngine.StopPlayingID(pid, 0, 4);
                f.Step();
                f.Step(0.01);
                int count = f.Voices.All.Count;
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents(), "stop ends the generator");
                f.StepUntil(20.0, 0.5);
                PaAssert.Equal(count, f.Voices.All.Count, "no more items after the stop");
            }
        }

        [PaTest]
        public static void ContinuousGaplessPlaysTheLoopsThenEnds()
        {
            PaRanSeq rs = new PaRanSeq { Continuous = true, Sequence = true, LoopCount = 2 };
            using (EngineFixture f = RanSeqFixture(rs, 2, 12345, 1.0))
            {
                f.Start();
                uint pid = f.Post("Play_RS", 1);
                f.Step();
                f.StepUntil(3.9, 0.05);
                PaAssert.Equal(4, f.Voices.All.Count, "2 loops of 2 items");
                PaAssert.SequenceEqual(new uint[] { 101, 102, 101, 102 }, f.Voices.Nodes());
                for (int i = 0; i < 4; i++)
                    PaAssert.Near(i * 1.0, f.Voices.All[i].StartTime, 1e-6, "gapless start " + i);
                PaAssert.Equal(0, f.EndOfEvents().Count);
                f.StepUntil(4.05, 0.05);
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents());
            }
        }

        [PaTest]
        public static void ContinuousTriggerRateOverlaps()
        {
            PaRanSeq rs = new PaRanSeq { Continuous = true, Sequence = true, LoopCount = 1, Transition = "triggerRate", TransitionSec = 0.5f };
            using (EngineFixture f = RanSeqFixture(rs, 3, 12345, 2.0))
            {
                f.Start();
                f.Post("Play_RS", 1, false);
                f.Step();
                f.StepUntil(1.5, 0.05);
                PaAssert.Equal(3, f.Voices.All.Count);
                PaAssert.Near(0.5, f.Voices.All[1].StartTime, 1e-6);
                PaAssert.Near(1.0, f.Voices.All[2].StartTime, 1e-6);
            }
        }

        [PaTest]
        public static void ContinuousCrossfadeOverlapsByTheTransition()
        {
            PaRanSeq rs = new PaRanSeq { Continuous = true, Sequence = true, LoopCount = 1, Transition = "crossfadeAmp", TransitionSec = 0.5f };
            using (EngineFixture f = RanSeqFixture(rs, 2, 12345, 2.0))
            {
                f.Start();
                f.Post("Play_RS", 1, false);
                f.Step();
                f.StepUntil(1.6, 0.05);
                PaAssert.Equal(2, f.Voices.All.Count);
                PaAssert.Near(1.5, f.Voices.All[1].StartTime, 1e-6, "starts TransitionSec before the end");
                PaAssert.Equal(500, f.Voices.All[1].Spec.FadeInMs);
                PaAssert.True(f.Voices.All[0].Ops.Exists(o => o.StartsWith("stopAt 1.5 500")), "the current item fades out");
            }
        }

        [PaTest]
        public static void LayerPlaysAllChildren()
        {
            using (EngineFixture f = new EngineFixture())
            {
                BankBuilder b = f.Bank("Layers");
                b.Node(50, PaNodeType.Layer, 0, 101, 102);
                b.Sound(101, 50, 501);
                b.Sound(102, 50, 502);
                f.MediaLi.Add(Media.Entry(501, 1));
                f.MediaLi.Add(Media.Entry(502, 2));
                b.Event("Play_Layer", b.Action(PaActionType.Play, 50));
                f.Start();
                uint pid = f.Post("Play_Layer");
                f.Step();
                PaAssert.SequenceEqual(new uint[] { 101, 102 }, f.Voices.Nodes());
                f.StepUntil(1.5);
                PaAssert.Equal(0, f.EndOfEvents().Count, "waits for the longest child");
                f.StepUntil(2.0);
                PaAssert.SequenceEqual(new[] { pid }, f.EndOfEvents());
            }
        }

        [PaTest]
        public static void SoundSpecSumsRandomizersAndAppliesLoopAndDelay()
        {
            using (EngineFixture f = new EngineFixture())
            {
                BankBuilder b = f.Bank("Spec");
                PaNode mixer = b.Node(10, PaNodeType.ActorMixer, 0, 100);
                mixer.Props.VolumeRand = new PaRange { Min = -3, Max = -3 };
                mixer.Props.InitialDelay = 0.25f;
                PaNode s = b.Sound(100, 10, 500);
                s.Props.VolumeRand = new PaRange { Min = -1, Max = -1 };
                s.Props.PitchRand = new PaRange { Min = 100, Max = 100 };
                s.Props.Loop = 3;
                f.MediaLi.Add(Media.Entry(500, 1));
                PaAction play = b.Action(PaActionType.Play, 100);
                play.TransitionMs = 150;
                b.Event("Play_Spec", play);
                f.Start();
                f.Post("Play_Spec");
                f.Step();
                PaVoiceSpec spec = f.Voices.All[0].Spec;
                PaAssert.Near(-4.0, spec.VolumeDb, 1e-4, "randomizers summed over the chain");
                PaAssert.Near(100.0, spec.PitchCents, 1e-4);
                PaAssert.Equal(3, spec.LoopCount);
                PaAssert.Near(0.25, spec.StartAt, 1e-6, "initial delay");
                PaAssert.Equal(150, spec.FadeInMs, "play fade-in");
                PaAssert.NotNull(spec.Media);
                PaAssert.Equal(500u, spec.Media.Id);
            }
        }
    }
}
