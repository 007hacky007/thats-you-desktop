// Ordered command queue of the managed audio engine.
//
// Engine entry points can run on any thread (game code on the main thread, finalizers on the
// GC thread, bank reads on pool threads). They only record what has to happen; the work that
// touches voices, music or Unity runs in PaEngine.Tick on the main thread, in the order the
// calls were made. That order matters: a Stop posted right after a Play must see the Play.

using System;
using System.Collections.Generic;

namespace PortAudio
{
    internal sealed class PaCommandQueue
    {
        readonly object sync = new object();
        List<Action> queued = new List<Action>();
        List<Action> draining = new List<Action>();

        public int Count
        {
            get { lock (sync) return queued.Count; }
        }

        public void Enqueue(Action command)
        {
            if (command == null)
                return;
            lock (sync) queued.Add(command);
        }

        // Runs every queued command in order. Commands queued while draining (for example a
        // PlayEvent action posting another event) run in the same drain, after the current ones.
        // A failing command is reported and does not stop the ones after it.
        public int Drain(Action<Exception> onError)
        {
            int ran = 0;
            for (int pass = 0; pass < 64; pass++)
            {
                lock (sync)
                {
                    if (queued.Count == 0)
                        break;
                    List<Action> swap = draining;
                    draining = queued;
                    queued = swap;
                }
                for (int i = 0; i < draining.Count; i++)
                {
                    try
                    {
                        draining[i]();
                    }
                    catch (Exception e)
                    {
                        if (onError != null)
                            onError(e);
                    }
                    ran++;
                }
                draining.Clear();
            }
            return ran;
        }

        public void Clear()
        {
            lock (sync)
            {
                queued.Clear();
                draining.Clear();
            }
        }
    }
}
