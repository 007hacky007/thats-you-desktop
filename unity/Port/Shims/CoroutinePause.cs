// Compile shim for the coroutine pause API that Sony's PS4 build of Unity 5.3 added to
// MonoBehaviour. Stock Unity (5.x through 6) never had it.
//
// The PS4 UnityEngine.dll exposed these public instance methods on MonoBehaviour:
//   void PauseCoroutine(string methodName, bool pause)
//   void PauseCoroutine(IEnumerator routine, bool pause)
//   void PauseCoroutine(Coroutine routine, bool pause)
//   void PauseAllCoroutines(bool pause)
// (plus internal PauseCoroutine_Auto / PauseCoroutineViaEnumerator_Auto native bindings).
// The game uses them for the pause menu: it freezes the coroutines of the flow, question,
// scenario and wiggle behaviours while the menu is open and resumes them afterwards.
//
// Unity 6 cannot suspend a running coroutine from outside the behaviour that owns it, so
// these are extension methods with the same names and parameters. Instance syntax at the
// call sites binds to them unchanged.
//
// Behaviour: the requested paused state is recorded per behaviour (and per routine), and
// can be queried with IsCoroutinePaused / AreAllCoroutinesPaused, but the Unity scheduler
// keeps running those coroutines. Pause-menu coroutine freezing is therefore lost until a
// real implementation lands (for example a pausable coroutine runner, or driving the
// affected timings from a scaled clock).

using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public static class CoroutinePauseExtensions
    {
        sealed class PauseState
        {
            public bool all;
            public readonly HashSet<object> routines = new HashSet<object>();
        }

        static readonly ConditionalWeakTable<MonoBehaviour, PauseState> s_State =
            new ConditionalWeakTable<MonoBehaviour, PauseState>();

        static PauseState State(MonoBehaviour behaviour)
        {
            return s_State.GetValue(behaviour, _ => new PauseState());
        }

        static void SetRoutine(MonoBehaviour behaviour, object routine, bool pause)
        {
            if (behaviour == null || routine == null)
                return;
            PauseState state = State(behaviour);
            if (pause)
                state.routines.Add(routine);
            else
                state.routines.Remove(routine);
        }

        /// <summary>Records the paused state of all coroutines on this behaviour. No actual suspension on Unity 6.</summary>
        public static void PauseAllCoroutines(this MonoBehaviour behaviour, bool pause)
        {
            if (behaviour == null)
                return;
            PauseState state = State(behaviour);
            state.all = pause;
            if (!pause)
                state.routines.Clear();
        }

        /// <summary>Records the paused state of a coroutine started by method name. No actual suspension on Unity 6.</summary>
        public static void PauseCoroutine(this MonoBehaviour behaviour, string methodName, bool pause)
        {
            SetRoutine(behaviour, methodName, pause);
        }

        /// <summary>Records the paused state of a coroutine by its enumerator. No actual suspension on Unity 6.</summary>
        public static void PauseCoroutine(this MonoBehaviour behaviour, IEnumerator routine, bool pause)
        {
            SetRoutine(behaviour, routine, pause);
        }

        /// <summary>Records the paused state of a coroutine by its handle. No actual suspension on Unity 6.</summary>
        public static void PauseCoroutine(this MonoBehaviour behaviour, Coroutine routine, bool pause)
        {
            SetRoutine(behaviour, routine, pause);
        }

        /// <summary>True when PauseAllCoroutines(true) is in effect for this behaviour.</summary>
        public static bool AreAllCoroutinesPaused(this MonoBehaviour behaviour)
        {
            PauseState state;
            return behaviour != null && s_State.TryGetValue(behaviour, out state) && state.all;
        }

        /// <summary>True when the given routine (name, enumerator or Coroutine) was requested paused, individually or via PauseAllCoroutines.</summary>
        public static bool IsCoroutinePaused(this MonoBehaviour behaviour, object routine)
        {
            PauseState state;
            if (behaviour == null || !s_State.TryGetValue(behaviour, out state))
                return false;
            return state.all || (routine != null && state.routines.Contains(routine));
        }
    }
}
