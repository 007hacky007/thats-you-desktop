// Event actions: what an event does when it is posted.
//
// Each action of the event runs in order, either at once or after its delay (DelayMs plus a
// random offset in DelayMinMs..DelayMaxMs, on the voice clock). Its probability is rolled once
// per run. Implemented: Play, PlayEvent, Stop, Pause, Resume, SetGameParameter,
// ResetGameParameter, SetState and SetSwitch. Other types are traced as unsupported; the
// game's banks contain none of them.
//
// Stop, Pause and Resume select what they act on by target (the node and everything under it,
// or a bus and everything routed under it), by scope (GameObjectScoped: only the posting
// object) and by mode (one element, all, or all except a list of exceptions).

using System;
using System.Collections.Generic;

namespace PortAudio
{
    internal sealed partial class PaCore
    {
        const int MaxEventDepth = 8;

        // Runs (or schedules) every action of the event for this instance. Used by PostEvent
        // and by PlayEvent actions, whose voices belong to the posting instance.
        void StartEvent(PaInstance inst, PaEvent ev, int depth)
        {
            if (ev == null || ev.Actions == null)
                return;
            for (int i = 0; i < ev.Actions.Length; i++)
            {
                PaAction a = banks.Action(ev.Actions[i]);
                if (a == null)
                {
                    if (PaLog.Tracing)
                        PaLog.Trace("action", "playingId", inst.PlayingId, "type", "missing", "target", ev.Actions[i], "tname", null, "delayMs", 0);
                    continue;
                }
                int delayMs = a.DelayMs + RollMs(a.DelayMinMs, a.DelayMaxMs);
                if (PaLog.Tracing)
                    PaLog.Trace("action", "playingId", inst.PlayingId, "type", a.Type.ToString(), "target", a.Target,
                        "tname", banks.NameOf(a.Target), "delayMs", delayMs);
                if (delayMs <= 0)
                {
                    RunAction(inst, a, depth);
                    continue;
                }
                PaPending p = new PaPending { Instance = inst, Action = a, Depth = depth, Due = voices.Now + delayMs / 1000.0 };
                pending.Add(p);
                inst.PendingActions++;
            }
        }

        // Step 3 of Tick: delayed actions that are due run in due order.
        void RunDueActions()
        {
            if (pending.Count == 0)
                return;
            double now = voices.Now;
            for (int guard = 0; guard < 4096; guard++)
            {
                PaPending next = null;
                foreach (PaPending p in pending)
                    if (!p.Cancelled && !p.Paused && p.Due <= now && (next == null || p.Due < next.Due))
                        next = p;
                if (next == null)
                    break;
                next.Cancelled = true;
                next.Instance.PendingActions--;
                if (!next.Instance.Completed)
                    RunAction(next.Instance, next.Action, next.Depth);
            }
            pending.RemoveAll(p => p.Cancelled);
        }

        void RunAction(PaInstance inst, PaAction a, int depth)
        {
            if (a.Probability < 100f && random.NextDouble() * 100.0 >= a.Probability)
            {
                if (PaLog.Tracing)
                    PaLog.Trace("play", "playingId", inst.PlayingId, "node", a.Target, "nname", banks.NameOf(a.Target), "result", "skipped", "reason", "probability");
                return;
            }
            int transitionMs = Math.Max(0, a.TransitionMs + RollMs(a.TransitionMinMs, a.TransitionMaxMs));
            switch (a.Type)
            {
                case PaActionType.Play:
                    PlayTarget(inst, a.Target, transitionMs, a.Curve);
                    break;
                case PaActionType.PlayEvent:
                    PaEvent ev = banks.Event(a.Target);
                    if (ev == null)
                        WarnOnce("playevent:" + a.Target, "PlayEvent action " + a.Id + " targets unknown event " + a.Target);
                    else if (depth >= MaxEventDepth)
                        WarnOnce("playevent-depth:" + a.Target, "PlayEvent nesting too deep at event " + a.Target);
                    else
                        StartEvent(inst, ev, depth + 1);
                    break;
                case PaActionType.Stop:
                    ApplyTargeted(inst, a, TargetOp.Stop, transitionMs);
                    break;
                case PaActionType.Pause:
                    ApplyTargeted(inst, a, TargetOp.Pause, transitionMs);
                    break;
                case PaActionType.Resume:
                    ApplyTargeted(inst, a, TargetOp.Resume, transitionMs);
                    break;
                case PaActionType.SetGameParameter:
                    SetGameParameterAction(inst, a, transitionMs);
                    break;
                case PaActionType.ResetGameParameter:
                    {
                        ulong scope = a.GameObjectScoped ? inst.GameObject : PaIds.GlobalObject;
                        world.ResetRtpc(a.Target, scope, voices.Now, transitionMs, a.Curve, a.BypassTransition);
                        TraceSync("rtpc", a.Target, world.GetRtpc(a.Target, scope), 0, scope);
                    }
                    break;
                case PaActionType.SetState:
                    SetStateNow(a.StateGroup, a.State);
                    break;
                case PaActionType.SetSwitch:
                    SetSwitchNow(a.SwitchGroup, a.SwitchValue, inst.GameObject);
                    break;
                default:
                    stats.Unsupported++;
                    WarnOnce("action:" + a.Type, "unsupported action type " + a.Type + " (raw 0x" + a.RawType.ToString("X4") + ") traced and ignored");
                    break;
            }
        }

        void SetGameParameterAction(PaInstance inst, PaAction a, int transitionMs)
        {
            ulong scope = a.GameObjectScoped ? inst.GameObject : PaIds.GlobalObject;
            float value = a.Value;
            if (a.ValueMin != 0f || a.ValueMax != 0f)
                value += random.Range(Math.Min(a.ValueMin, a.ValueMax), Math.Max(a.ValueMin, a.ValueMax));
            if (a.ValueRelative)
                value += world.GetRtpc(a.Target, scope);
            world.SetRtpc(a.Target, value, scope, voices.Now, a.BypassTransition ? 0 : transitionMs, a.Curve, a.BypassTransition);
            TraceSync("rtpc", a.Target, value, 0, scope);
        }

        internal enum TargetOp { Stop, Pause, Resume }

        // Stop / Pause / Resume of an action, on every instance it reaches.
        void ApplyTargeted(PaInstance from, PaAction a, TargetOp op, int fadeMs)
        {
            double now = voices.Now;
            foreach (PaInstance inst in instanceOrder.ToArray())
            {
                if (a.GameObjectScoped && inst.GameObject != from.GameObject)
                    continue;
                foreach (PaVoiceRef v in inst.Voices.ToArray())
                {
                    if (v.Ended || !MatchesNode(a, v.NodeId))
                        continue;
                    if (op == TargetOp.Stop)
                        v.Stop(fadeMs, a.Curve);
                    else if (v.Voice != null && op == TargetOp.Pause)
                        v.Voice.Pause(fadeMs, a.Curve);
                    else if (v.Voice != null)
                        v.Voice.Resume(fadeMs, a.Curve);
                }
                if (music != null)
                {
                    foreach (PaMusicRef m in inst.Music.ToArray())
                    {
                        if (m.Ended || !MatchesMusic(a, m.Context))
                            continue;
                        if (op == TargetOp.Stop)
                            music.Stop(m.Context, fadeMs, a.Curve);
                        else if (op == TargetOp.Pause)
                            music.Pause(m.Context, fadeMs, a.Curve);
                        else
                            music.Resume(m.Context, fadeMs, a.Curve);
                    }
                }
                foreach (PaSequencer s in inst.Sequencers)
                {
                    if (s.Done || !MatchesNode(a, s.ContainerId))
                        continue;
                    if (op == TargetOp.Stop)
                        s.Stop();
                    else
                        s.Paused = op == TargetOp.Pause;
                }
                if (op == TargetOp.Stop)
                    foreach (PaSwitchPlay sw in inst.Switches)
                        if (MatchesNode(a, sw.Node.Id))
                            sw.Stopped = true;
                foreach (PaPending p in pending)
                {
                    if (p.Cancelled || p.Instance != inst || p.Action == a)
                        continue;
                    if (p.Action.Type != PaActionType.Play && p.Action.Type != PaActionType.PlayEvent)
                        continue;
                    if (p.Action.Type == PaActionType.Play && !MatchesNode(a, p.Action.Target))
                        continue;
                    if (p.Action.Type == PaActionType.PlayEvent && a.TargetMode == PaTargetMode.Element)
                        continue;
                    if (op == TargetOp.Stop)
                        CancelPending(p);
                    else
                        PausePending(p, op == TargetOp.Pause, now);
                }
            }
        }

        bool MatchesNode(PaAction a, uint nodeId)
        {
            switch (a.TargetMode)
            {
                case PaTargetMode.All:
                    return true;
                case PaTargetMode.AllExcept:
                    if (a.Exceptions != null)
                        foreach (PaObjectRef e in a.Exceptions)
                            if (e.IsBus ? banks.IsUnderBus(banks.OutputBus(nodeId), e.Id) : banks.IsUnder(nodeId, e.Id))
                                return false;
                    return true;
                default:
                    return a.TargetIsBus ? banks.IsUnderBus(banks.OutputBus(nodeId), a.Target) : banks.IsUnder(nodeId, a.Target);
            }
        }

        bool MatchesMusic(PaAction a, IPaMusicContext c)
        {
            if (c == null)
                return false;
            switch (a.TargetMode)
            {
                case PaTargetMode.All:
                    return true;
                case PaTargetMode.AllExcept:
                    if (a.Exceptions != null)
                        foreach (PaObjectRef e in a.Exceptions)
                            if (e.IsBus ? banks.IsUnderBus(banks.OutputBus(c.RootNode), e.Id) : c.Involves(e.Id))
                                return false;
                    return true;
                default:
                    return a.TargetIsBus ? banks.IsUnderBus(banks.OutputBus(c.RootNode), a.Target) : c.Involves(a.Target);
            }
        }

        int RollMs(int min, int max)
        {
            if (min == 0 && max == 0)
                return 0;
            if (max < min)
            {
                int t = min;
                min = max;
                max = t;
            }
            return (int)Math.Round(random.Range((float)min, (float)max));
        }

        // ---- ExecuteActionOnEvent / StopPlayingID / StopAll ----

        // actionType 0 Stop, 1 Pause, 2 Resume (AkActionOnEventType). Returns false when the
        // event is not loaded.
        public void ExecuteActionOnEvent(uint eventId, int actionType, ulong go, int transitionMs, PaCurve curve, uint playingId)
        {
            foreach (PaInstance inst in instanceOrder.ToArray())
            {
                if (inst.EventId != eventId)
                    continue;
                if (!PaIds.IsGlobal(go) && inst.GameObject != go)
                    continue;
                if (playingId != 0 && inst.PlayingId != playingId)
                    continue;
                if (actionType == 0)
                    StopInstance(inst, transitionMs, curve);
                else if (actionType == 1)
                    PauseInstance(inst, transitionMs, curve, true);
                else if (actionType == 2)
                    PauseInstance(inst, transitionMs, curve, false);
                else
                    WarnOnce("execaction:" + actionType, "ExecuteActionOnEvent type " + actionType + " is not supported");
            }
        }

        public void StopPlayingId(uint playingId, int transitionMs, PaCurve curve)
        {
            PaInstance inst = Instance(playingId);
            if (inst != null)
                StopInstance(inst, transitionMs, curve);
        }

        public void StopAllOn(ulong go)
        {
            foreach (PaInstance inst in instanceOrder.ToArray())
                if (PaIds.IsGlobal(go) || inst.GameObject == go)
                    StopInstance(inst, 0, PaCurve.Linear);
        }

        // UnregisterGameObject: the object's voices and music stop at once, its instances end
        // and its own switch and RTPC values are dropped.
        public void UnregisterObject(ulong go)
        {
            foreach (PaInstance inst in instanceOrder.ToArray())
                if (inst.GameObject == go)
                    StopInstance(inst, 0, PaCurve.Linear);
            world.Unregister(go);
        }
    }
}
