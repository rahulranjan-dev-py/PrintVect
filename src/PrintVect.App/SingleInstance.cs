using System;
using System.Threading;
using PrintVect.Core.Logging;

namespace PrintVect.App
{
    /// <summary>
    /// One PrintVect per logged-in session. A second start simply asks the running one to show
    /// its window. "Local\" keeps the names per session, so two users on one PC do not collide.
    /// </summary>
    internal static class SingleInstance
    {
        private const string MutexName = @"Local\PrintVect.App.Instance";
        private const string ShowEventName = @"Local\PrintVect.App.ShowWindow";

        /// <summary>Returns the mutex (dispose it at exit) and whether this process is the first one.</summary>
        public static Mutex TryAcquire(out bool isFirstInstance)
        {
            var mutex = new Mutex(false, MutexName);
            try
            {
                isFirstInstance = mutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                // The previous instance died without releasing; we own it now.
                isFirstInstance = true;
            }
            return mutex;
        }

        /// <summary>Called by the running instance so a later start can wake it up.</summary>
        public static EventWaitHandle CreateShowEvent()
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        }

        /// <summary>Called by the second instance: tells the running one to show its window.</summary>
        public static void SignalShowWindow()
        {
            try
            {
                using (EventWaitHandle handle = EventWaitHandle.OpenExisting(ShowEventName))
                {
                    handle.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Log.Warn("The running PrintVect did not answer (it may still be starting). Please start it again in a moment.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not ask the running PrintVect to show its window: " + ex.Message);
            }
        }
    }
}
