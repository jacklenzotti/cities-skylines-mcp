using System;
using System.Collections.Generic;
using System.Threading;
using ColossalFramework;

namespace CS1McpBridge
{
    public enum RunOn { Sim, Main }

    /// <summary>
    /// Marshals work from socket worker threads onto Cities: Skylines' threads and
    /// blocks the caller until the result (or an exception) is ready.
    ///
    /// Two destinations:
    ///   RunOn.Sim  -> simulation thread, via SimulationManager.AddAction.
    ///                 Use for anything that reads/writes simulation state
    ///                 (buildings, citizens, economy, disasters, sim speed).
    ///   RunOn.Main -> main/render thread, via the queue pumped in Threading.OnUpdate.
    ///                 Use for camera and screenshots (render-thread sensitive).
    /// </summary>
    public static class Dispatch
    {
        static readonly object _gate = new object();
        static readonly Queue<Action> _mainQueue = new Queue<Action>();

        /// <summary>Drains the main-thread queue. Called every frame from Threading.OnUpdate.</summary>
        public static void PumpMainThread()
        {
            while (true)
            {
                Action job;
                lock (_gate)
                {
                    if (_mainQueue.Count == 0) return;
                    job = _mainQueue.Dequeue();
                }
                try { job(); }
                catch (Exception e) { Log.Error(e); }
            }
        }

        public static T Run<T>(RunOn thread, Func<T> work, int timeoutMs = 5000)
        {
            T result = default(T);
            Exception captured = null;
            var done = new ManualResetEvent(false);

            Action job = () =>
            {
                try { result = work(); }
                catch (Exception e) { captured = e; }
                finally { done.Set(); }
            };

            if (thread == RunOn.Sim)
                Singleton<SimulationManager>.instance.AddAction(job);
            else
                lock (_gate) _mainQueue.Enqueue(job);

            if (!done.WaitOne(timeoutMs))
                throw new TimeoutException("Command timed out on the " + thread + " thread.");
            if (captured != null) throw captured;
            return result;
        }
    }
}
