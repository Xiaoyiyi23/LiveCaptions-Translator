using System.Threading;
using System.Windows;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class App : Application
    {
        private const string MUTEX_NAME = "LiveCaptionsTranslator_SingleInstance";
        private const string ACTIVATE_SIGNAL_NAME = "LiveCaptionsTranslator_ActivateSignal";

        private static Mutex? singleInstanceMutex;
        private static EventWaitHandle? activateSignal;
        private static bool isShuttingDown;

        public static bool IsShuttingDown => isShuttingDown;

        App()
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Single instance: a second launch just activates the running one.
            // This must run BEFORE touching `Translator`, whose static constructor
            // would otherwise kill and relaunch the first instance's LiveCaptions.
            singleInstanceMutex = new Mutex(true, MUTEX_NAME, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                ActivateRunningInstance();
                Shutdown();
                return;
            }
            StartActivationListener();

            // First (only) instance: initialize global state and background loops.
            // Resolves to the saved language, or the system UI language.
            LocalizationService.Instance.ApplyLanguage(Translator.Setting?.Language);
            Translator.EnsureLiveCaptions();
            Task.Run(() => Translator.SyncLoop());
            Task.Run(() => Translator.TranslateLoop());
            Task.Run(() => Translator.DisplayLoop());

            // `--minimized` (autostart) launches straight to the tray: the window
            // is created but not shown, so its Loaded handlers run on first show.
            bool startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            if (!startMinimized)
                mainWindow.Show();

            TrayController.Initialize();
        }

        private static void ActivateRunningInstance()
        {
            try
            {
                // Create-or-open: works no matter which instance created it first.
                using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ACTIVATE_SIGNAL_NAME);
                signal.Set();
            }
            catch (Exception ex)
            {
                FileLogger.Warn($"Failed to activate the running instance: {ex.Message}");
            }
        }

        private void StartActivationListener()
        {
            activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ACTIVATE_SIGNAL_NAME);
            var signal = activateSignal;
            Task.Run(() =>
            {
                while (signal.WaitOne())
                    Dispatcher.BeginInvoke(TrayController.ShowMainWindow);
            });
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            // Windows logoff/shutdown: bypass tray interception and exit cleanly.
            isShuttingDown = true;
            base.OnSessionEnding(e);
        }

        // The single supported exit path (tray menu, or closing with CloseToTray off).
        public static void ExitApplication()
        {
            isShuttingDown = true;
            Current.Shutdown();
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            // Flush any debounced setting changes before the process goes away.
            Translator.Setting?.Save();
            TrayController.Dispose();
            if (Translator.Window != null)
            {
                LiveCaptionsHandler.RestoreLiveCaptions(Translator.Window);
                LiveCaptionsHandler.KillLiveCaptions(Translator.Window);
            }
        }
    }
}
