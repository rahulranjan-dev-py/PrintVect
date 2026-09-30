using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace PrintVect.Core.Printing
{
    /// <summary>
    /// Runs a piece of work on a thread of the required COM apartment. The XPS Print API refuses
    /// its interfaces (E_NOINTERFACE) from a single-threaded apartment, while System.Printing's
    /// XPS path needs a single-threaded one, so each engine asks for what it needs.
    /// </summary>
    public static class ApartmentRunner
    {
        public static T Run<T>(ApartmentState apartment, string threadName, Func<T> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (Thread.CurrentThread.GetApartmentState() == apartment)
            {
                return work();
            }

            T result = default(T);
            ExceptionDispatchInfo failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = work();
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
            })
            {
                IsBackground = true,
                Name = threadName
            };
            thread.SetApartmentState(apartment);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                failure.Throw();
            }
            return result;
        }
    }
}
