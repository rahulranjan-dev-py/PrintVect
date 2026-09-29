using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using PrintVect.App.Tray;
using PrintVect.Core;
using PrintVect.Core.Config;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Logging;

namespace PrintVect.App
{
    internal static class Program
    {
        /// <summary>Command-line switch used by the autostart entry: start hidden in the tray.</summary>
        public const string TraySwitch = "/tray";

        [STAThread]
        private static int Main(string[] args)
        {
            bool startHidden = args.Any(a => string.Equals(a, TraySwitch, StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnUiThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            AppPaths paths = AppPaths.Default();
            try
            {
                paths.EnsureDirectories();
                Log.Initialize(paths.LogsDir, Log.DefaultPrefix);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Strings.StartupFolderError, paths.Root, ex.Message),
                    Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            bool isFirstInstance;
            using (Mutex mutex = SingleInstance.TryAcquire(out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    Log.Info("PrintVect is already running in this session; showing the existing window instead of starting again.");
                    SingleInstance.SignalShowWindow();
                    return 0;
                }

                try
                {
                    Log.Info(string.Format("{0} {1} starting. Command line: {2}", AppInfo.ProductName, AppInfo.Version,
                        args.Length == 0 ? "(none)" : string.Join(" ", args)));
                    Log.Info(WindowsInfo.OneLine());

                    var store = new ConfigStore(paths.ConfigFile);
                    AppConfig config = store.LoadOrCreate();

                    using (var context = new TrayApplicationContext(paths, store, config, startHidden))
                    {
                        Application.Run(context);
                    }
                    Log.Info(AppInfo.ProductName + " stopped.");
                }
                finally
                {
                    Log.Shutdown();
                    try { mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned; nothing to release */ }
                }
            }
            return 0;
        }

        private static void OnUiThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Log.Error("Unexpected error on the UI thread.", e.Exception);
            try
            {
                MessageBox.Show(string.Format(Strings.UnexpectedError, e.Exception.Message), Strings.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                Log.Error("Could not even show the error message box.", ex);
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error("Unexpected error; PrintVect will close." + (e.IsTerminating ? "" : " (not terminating)"),
                e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject)));
        }
    }
}
