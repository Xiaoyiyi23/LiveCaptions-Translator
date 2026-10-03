using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    /// <summary>
    /// Owns the tray icon lifecycle: context menu, balloon hints, and window
    /// toggling. The app runs with ShutdownMode=OnExplicitShutdown, so closing
    /// windows never terminates the process — the tray menu's Exit is the only
    /// exit path (plus system logoff/shutdown).
    /// </summary>
    public static class TrayController
    {
        private static TaskbarIcon? trayIcon;
        private static MenuItem? pauseMenuItem;

        public static void Initialize()
        {
            if (trayIcon != null)
                return;

            string iconPath = Path.Combine(AppContext.BaseDirectory, "LiveCaptions-Translator.ico");
            trayIcon = new TaskbarIcon
            {
                ToolTipText = "LiveCaptions Translator",
                NoLeftClickDelay = true,
                IconSource = File.Exists(iconPath) ? new BitmapImage(new Uri(iconPath)) : null
            };
            trayIcon.TrayLeftMouseUp += (_, _) => ToggleMainWindow();
            trayIcon.TrayContextMenuOpen += (_, _) => SyncPauseState();

            LocalizationService.Instance.PropertyChanged += OnLanguageChanged;
            Translator.LogOnlyFlagChanged += SyncPauseState;
            BuildMenu();
        }

        private static void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Item[]")
                BuildMenu();
        }

        private static void BuildMenu()
        {
            if (trayIcon == null)
                return;

            var openItem = new MenuItem { Header = LocalizationService.Get("Tray.Open") };
            openItem.Click += (_, _) => ShowMainWindow();

            var overlayItem = new MenuItem { Header = LocalizationService.Get("Tray.Overlay") };
            overlayItem.Click += (_, _) => (Application.Current.MainWindow as MainWindow)?.ToggleOverlay();

            pauseMenuItem = new MenuItem
            {
                Header = LocalizationService.Get("Tray.Pause"),
                IsCheckable = true,
                IsChecked = Translator.LogOnlyFlag
            };
            pauseMenuItem.Click += (_, _) => Translator.LogOnlyFlag = !Translator.LogOnlyFlag;

            var exitItem = new MenuItem { Header = LocalizationService.Get("Tray.Exit") };
            exitItem.Click += (_, _) => App.ExitApplication();

            var menu = new ContextMenu();
            menu.Items.Add(openItem);
            menu.Items.Add(overlayItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(pauseMenuItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(exitItem);
            trayIcon.ContextMenu = menu;
        }

        private static void SyncPauseState()
        {
            if (pauseMenuItem != null)
                pauseMenuItem.IsChecked = Translator.LogOnlyFlag;
        }

        public static void ShowMainWindow()
        {
            var main = Application.Current.MainWindow as MainWindow;
            if (main == null)
                return;
            main.Show();
            if (main.WindowState == WindowState.Minimized)
                main.WindowState = WindowState.Normal;
            main.Activate();
        }

        private static void ToggleMainWindow()
        {
            var main = Application.Current.MainWindow as MainWindow;
            if (main == null)
                return;
            if (main.IsVisible)
                main.Hide();
            else
                ShowMainWindow();
        }

        public static void ShowFirstMinimizeHint()
        {
            trayIcon?.ShowBalloonTip(
                LocalizationService.Get("Tray.Hint.Title"),
                LocalizationService.Get("Tray.Hint.Message"),
                BalloonIcon.Info);
        }

        public static void Dispose()
        {
            if (trayIcon == null)
                return;
            LocalizationService.Instance.PropertyChanged -= OnLanguageChanged;
            Translator.LogOnlyFlagChanged -= SyncPauseState;
            trayIcon.Dispose();
            trayIcon = null;
        }
    }
}
