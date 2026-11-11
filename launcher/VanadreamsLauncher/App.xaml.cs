using System;
using System.Linq;
using System.Windows;
using Vanadreams.Services;

namespace Vanadreams
{
    public partial class App : Application
    {
        public static AppState State { get; private set; }
        public static string SnapshotPath { get; private set; }
        public static string SnapshotPage { get; private set; }
        /// <summary>The window switcher; made with the main window, running only when the player turned it on.</summary>
        public static WindowSwitcher Switcher { get; set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // test hook: VanadreamsLauncher.exe --snapshot <png> [page] renders the window to a file and exits
            var args = e.Args;
            // test hook: --data <folder> must be read before anything opens the settings
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--data") LauncherSettings.DataOverride = args[i + 1];
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--snapshot" && i + 1 < args.Length) { SnapshotPath = args[i + 1]; if (i + 2 < args.Length) SnapshotPage = args[i + 2]; }
                if (args[i] == "--ashita" && i + 1 < args.Length) { State = State ?? new AppState(); State.Settings.AshitaRoot = args[i + 1]; }
            }
            State = State ?? new AppState();
            UiText.Initialize(State.Settings.UiLanguage);
            // Snapshot-only language override; it never writes the player settings.
            if (!string.IsNullOrEmpty(SnapshotPath))
                for (var i = 0; i + 1 < args.Length; i++)
                    if (args[i] == "--ui-language") UiText.Initialize(args[i + 1]);
            Log.Info("Launcher " + typeof(App).Assembly.GetName().Version + " starting from " + SelfInstall.CurrentExe);
            Updater.SweepOld();

            // A download clicked while a launcher is installed: refresh the installed one and run that instead.
            if (string.IsNullOrEmpty(SnapshotPath) && Updater.HandOverToInstalled()) { Shutdown(); return; }

            // Installing is asked for, never automatic: VanadreamsLauncher.exe --install
            var wantsInstall = Array.IndexOf(args, "--install") >= 0;
            if (wantsInstall && string.IsNullOrEmpty(SnapshotPath))
            {
                var answer = MessageBox.Show(
                    UiText.T("Install the Vanadreams Launcher?\n\nIt copies itself to your apps folder and adds Start menu and desktop shortcuts. Nothing else changes. Choose No to run it from here just this once."),
                    UiText.T("Vanadreams Launcher"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) { Shutdown(); return; }
                if (answer == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (SelfInstall.IsInstalledCopy) { SelfInstall.MakeShortcuts(); }   // already here: just the shortcuts
                        else { SelfInstall.Install(); SelfInstall.HandOver(); Shutdown(); return; }
                    }
                    catch (Exception ex) { Log.Error("self-install", ex); MessageBox.Show(UiText.T("Couldn't install: ") + ex.Message + UiText.T("\n\nRunning from here instead."), UiText.T("Vanadreams Launcher")); }
                }
            }
            DispatcherUnhandledException += (s, ex) =>
            {
                Log.Error("unhandled", ex.Exception);
                MessageBox.Show(ex.Exception.Message, UiText.T("Vanadreams Launcher"), MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Music.Stop();
            Switcher?.Dispose();
            GameLauncher.Sweep(State?.AshitaRoot);   // a launch copy must not outlive the launcher
            base.OnExit(e);
        }
    }
}
