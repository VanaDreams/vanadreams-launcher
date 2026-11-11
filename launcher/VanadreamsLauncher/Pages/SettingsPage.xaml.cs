using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public partial class SettingsPage : UserControl
    {
        private readonly MainWindow _win;

        public SettingsPage(MainWindow win)
        {
            InitializeComponent();
            _win = win;
            var s = App.State.Settings;
            AshitaBox.Text = s.AshitaRoot; FfxiBox.Text = s.FfxiFolderOverride; CatalogBox.Text = s.CatalogUrl; StatusBox.Text = s.StatusUrl;
            // read-only: it comes from the status route, never typed here
            var st = App.State.Status;
            ExpectedBox.Text = string.IsNullOrEmpty(st.ClientVer) ? UiText.T("not published by the server yet") : st.ClientVer + UiText.T(" · lock ") + (st.Lock.HasValue ? ((int)st.Lock.Value).ToString() : "2") + UiText.T(" · from vanadreams.org");
            ShowPlays();
            foreach (ComboBoxItem choice in LanguageBox.Items)
                if ((string)choice.Tag == UiLanguage.Normalize(s.UiLanguage)) LanguageBox.SelectedItem = choice;
            MusicBox.IsChecked = s.MusicOn;
            RestoreBox.IsChecked = s.RestoreOtherGame;
            MusicCredit.Text = UiText.T("made for Vanadreams with SoundBreak");
            About.Text = UiText.T("Vanadreams Launcher ") + typeof(App).Assembly.GetName().Version + UiText.T(" · github.com/VanaDreams/vanadreams-launcher · no telemetry");
            ShowLayout();
        }

        /// <summary>The layout in use is the gold button.</summary>
        private void ShowLayout()
        {
            var h = _win.Horizontal;
            VerticalButton.Style = (Style)FindResource(h ? "GhostButton" : "GoldButton");
            HorizontalButton.Style = (Style)FindResource(h ? "GoldButton" : "GhostButton");
        }

        /// <summary>The menu moves the moment the player picks; it is kept at once, without Save.</summary>
        private void Layout_Click(object sender, RoutedEventArgs e)
        {
            var s = App.State.Settings;
            s.MenuLayout = (string)((FrameworkElement)sender).Tag;
            s.Save();
            _win.ApplyLayout();
            ShowLayout();
        }

        /// <summary>The copy Play starts for Vanadreams, which is not always the one Windows starts now: show the first, tip the second.</summary>
        private void ShowPlays()
        {
            var plays = App.State.VanadreamsGameRoot();
            DetectedBox.Text = plays != null ? ClientInstall.GameFolder(plays) : UiText.T("no game found yet; Play starts what Windows has");
            DetectedBox.ToolTip = UiText.T("Windows starts now: ") + (ClientVersion.FindFfxiFolder() ?? UiText.T("nothing registered by PlayOnline")) +
                                  UiText.T("\nTo always play one copy, put its FINAL FANTASY XI folder in Vanadreams game folder above.");
        }

        /// <summary>The corner mute changed the setting; show it.</summary>
        public void SyncMusic() => MusicBox.IsChecked = App.State.Settings.MusicOn;

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var s = App.State.Settings;
            s.AshitaRoot = AshitaBox.Text.Trim(); s.FfxiFolderOverride = FfxiBox.Text.Trim(); s.CatalogUrl = CatalogBox.Text.Trim(); s.StatusUrl = StatusBox.Text.Trim();
            var language = UiLanguage.Normalize((LanguageBox.SelectedItem as ComboBoxItem)?.Tag as string);
            var languageChanged = language != UiLanguage.Normalize(s.UiLanguage);
            s.UiLanguage = language;
            s.MusicOn = MusicBox.IsChecked == true;
            s.RestoreOtherGame = RestoreBox.IsChecked == true;
            s.Save();
            if (s.MusicOn) Music.Start(); else Music.Stop();
            _win.RefreshMute();
            App.State.CheckVersion();
            ShowPlays();
            Note.Text = !string.IsNullOrWhiteSpace(s.FfxiFolderOverride) && !GameRegistration.HasGame(GameRegistration.RootOf(s.FfxiFolderOverride))
                ? UiText.T("Saved, but there is no game in that Vanadreams game folder: pick FINAL FANTASY XI, with PlayOnlineViewer beside it.")
                : languageChanged ? UiText.T("Language saved. Restart the launcher to apply it.") : UiText.T("Saved.");
            App.State.Notify();
            await App.State.RefreshStatusAsync();
        }

        /// <summary>Register the copy Windows had before ours, now: for a player whose Windower or PlayOnline is starting Vanadreams' game.</summary>
        private void GiveBack_Click(object sender, RoutedEventArgs e)
        {
            var s = App.State.Settings;
            var lang = 1;
            var current = GameRegistration.CurrentRoot(GameRegistration.Read(lang));
            var back = GameRegistration.RestoreTarget(null, current, s.OtherGameRoot);
            if (back == null)
            {
                Note.Text = UiText.T("No other game is remembered. Install or play the other client once, or pick its FINAL FANTASY XI folder on the Profiles page.");
                return;
            }
            try
            {
                Note.Text = ClientInstall.RunRegister(back, s.DownloadsFolder, lang)
                    ? UiText.T("Windows now points at ") + ClientInstall.GameFolder(back) + UiText.T(". Play moves it back to Vanadreams when you press it.")
                    : UiText.T("Windows did not get permission; nothing changed.");
            }
            catch (Exception ex) { Note.Text = ex.Message; }
        }

        private async void Catalog_Click(object sender, RoutedEventArgs e)
        {
            Note.Text = UiText.T("Fetching…");
            await App.State.RefreshCatalogAsync();
            Note.Text = App.State.CatalogFromCache ? UiText.T("Could not fetch; using the cached copy.") : UiText.T("Catalogue updated: ") + App.State.Catalog.Items.Count + UiText.T(" items.");
        }

        private void Shortcuts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!SelfInstall.IsInstalledCopy) { SelfInstall.Install(); Note.Text = UiText.T("Installed to ") + SelfInstall.InstallDir + UiText.T(" with shortcuts. Use the shortcut from now on."); }
                else { SelfInstall.MakeShortcuts(); Note.Text = UiText.T("Shortcuts made."); }
            }
            catch (Exception ex) { Note.Text = ex.Message; }
        }

        private void Data_Click(object sender, RoutedEventArgs e) => Open(LauncherSettings.DataFolder);
        private void Log_Click(object sender, RoutedEventArgs e) => Open(App.State.Settings.LogPath);
        private void Open(string path)
        {
            try { if (!File.Exists(path) && !Directory.Exists(path)) Directory.CreateDirectory(LauncherSettings.DataFolder); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Note.Text = ex.Message; }
        }
    }
}
