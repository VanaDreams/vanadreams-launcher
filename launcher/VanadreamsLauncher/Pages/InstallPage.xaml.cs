using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public partial class InstallPage : UserControl, IBusyPage
    {
        private readonly MainWindow _win;
        // Where the game went last time, so the page opens on the real folder rather than the default.
        // Without this the launcher did not know where it had installed, and a player who moved the
        // install could not be put right without finding the folder himself.
        private string _root = string.IsNullOrWhiteSpace(App.State.Settings.GameInstallRoot)
            ? ClientInstall.DefaultRoot
            : App.State.Settings.GameInstallRoot;
        private ClientManifest _manifest;
        private CancellationTokenSource _cancel;
        private bool _cleaning;

        private readonly bool _asNewPlayer;

        /// <param name="asNewPlayer">Snapshot hook: show the page as a PC with no game on it sees it.</param>
        public InstallPage(MainWindow win, bool asNewPlayer = false)
        {
            InitializeComponent();
            _win = win;
            _asNewPlayer = asNewPlayer;
            CleanupButton.Content = UiText.T("Clean up failed installs");
            Loaded += async (s, e) => { Refresh(); await LoadManifestAsync(); };
        }

        private static string Gb(long bytes) => (bytes / 1073741824.0).ToString("0.0") + " GB";
        private string Expected => ManagedClient.Version;
        private void Refresh()
        {
            var existing = _asNewPlayer ? null : App.State.ExistingClientRoot(_root);
            var found = existing == null ? null : ClientInstall.GameFolder(existing);
            var have = ClientVersion.ReadInstalled(found);
            var want = Expected;
            FolderText.Text = ClientInstall.GameFolder(_root);
            VersionText.Text = want == null ? UiText.T("the server hasn't published its version yet") : want + UiText.T(" · the version the server runs");
            FoundText.Text = found == null ? UiText.T("none on this PC") : (have ?? UiText.T("version unknown")) + " · " + found.TrimEnd('\\');
            var ready = existing != null && _manifest != null && ManagedClient.HasManifest(existing, _manifest);
            State.Text = found == null ? UiText.T("Not installed yet.") : ready
                ? UiText.T("✓ Installed · matches the server")
                : UiText.T("The launcher will reuse matching files from this installation to prepare your VanaDreams copy.");
            State.Foreground = (Brush)FindResource(ready ? "Ok" : "Warn");
            InstallButton.Content = _cancel != null ? UiText.T("Stop") : found != null
                ? UiText.T("Patch game") : UiText.T("Install game");
            if (_manifest != null)
                SizeText.Text = (found == null ? Gb(_manifest.TotalSize) + UiText.T(" download; ")
                    : UiText.T("Download size depends on your existing files. ")) +
                    Gb(_manifest.TotalUnpacked) + UiText.T(" in a separate game folder.");
        }

        private async Task LoadManifestAsync(CancellationToken ct = default(CancellationToken))
        {
            if (Expected == null) { SizeText.Text = UiText.T("known once the server's version is published"); return; }
            SizeText.Text = UiText.T("checking…");
            try
            {
                _manifest = ClientManifest.Parse(await App.State.Downloader.GetStringAsync(ClientInstall.ManifestUrl(Expected), ct));
                Refresh();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Log.Warn("client manifest: " + ex.Message);
                SizeText.Text = UiText.T("the download isn't available right now");
            }
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (_cancel != null)
            {
                _cancel.Cancel();
                ProgressText.Text = UiText.T("Stopping…");
                return;
            }

            var state = App.State;
            var attempt = _cancel = new CancellationTokenSource();
            CleanupButton.IsEnabled = false;
            FolderButton.IsEnabled = false; Refresh();
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            ProgressText.Text = UiText.T("Checking for a game update…");
            var progress = new Progress<InstallProgress>(p =>
            {
                if (_cancel != attempt || attempt.IsCancellationRequested) return;
                Progress.IsIndeterminate = false;
                Progress.Value = 100.0 * p.Done / Math.Max(1, p.Total);
                ProgressText.Text = p.StatusText;
            });
            try
            {
                if (_manifest == null) await LoadManifestAsync(attempt.Token);
                if (_manifest == null)
                {
                    ProgressText.Text = UiText.T("The game download isn't available right now. Try again in a while.");
                    return;
                }
                await state.InstallManagedClientAsync(_manifest, _root, progress, attempt.Token);
                _root = state.Settings.GameInstallRoot;
                ProgressText.Text = UiText.T("Verified VanaDreams copy ready. Press Play.");

            }
            catch (OperationCanceledException) { ProgressText.Text = UiText.T("Stopped. Your download will resume when you try again."); }
            catch (Exception ex) { Log.Error("client install", ex); ProgressText.Text = ex.Message; }
            finally
            {
                _cancel = null;
                attempt.Dispose();
                CleanupButton.IsEnabled = true;
                FolderButton.IsEnabled = true; Progress.Visibility = Visibility.Collapsed;
                Refresh();
                Packs.Reload();   // an install brings the look and title packs
            }
        }




        private async void Cleanup_Click(object sender, RoutedEventArgs e)
        {
            if (_cancel != null || _cleaning) return;
            _cleaning = true;
            InstallButton.IsEnabled = FolderButton.IsEnabled = CleanupButton.IsEnabled = false;
            ProgressText.Text = UiText.T("Looking for unfinished installs and saved downloads…");
            try
            {
                var state = App.State;
                var targets = new[] { _root, state.Settings.GameInstallRoot, ClientInstall.DefaultRoot };
                var downloads = state.Settings.DownloadsFolder;
                var entries = await Task.Run(() => InstallCleanup.Find(targets, ProtectedInstallPaths(state), downloads));
                if (entries.Count == 0)
                {
                    ProgressText.Text = UiText.T("No unfinished installs or saved game downloads were found. Use Change folder to check another location.");
                    return;
                }

                var review = new InstallCleanupWindow(entries) { Owner = _win };
                if (review.ShowDialog() != true)
                {
                    ProgressText.Text = "";
                    return;
                }

                ProgressText.Text = UiText.T("Cleaning up install files…");
                var result = await Task.Run(() => InstallCleanup.Remove(review.Selected, targets, ProtectedInstallPaths(state), downloads));
                ProgressText.Text = UiText.F("Removed {0} of install files.", InstallCleanupWindow.FormatSize(result.Bytes));
                if (result.Skipped != 0)
                    ProgressText.Text += " " + UiText.T("Some files changed or were in use. Open cleanup again to review what remains.");
            }
            catch (Exception ex)
            {
                Log.Error("install cleanup", ex);
                ProgressText.Text = ex.Message;
            }
            finally
            {
                _cleaning = false;
                InstallButton.IsEnabled = FolderButton.IsEnabled = CleanupButton.IsEnabled = true;
            }
        }

        private static IEnumerable<string> ProtectedInstallPaths(AppState state)
        {
            var settings = state.Settings;
            var paths = new List<string> { settings.AshitaRoot, settings.FfxiFolderOverride };
            foreach (var root in new[] { settings.GameInstallRoot, settings.OtherGameRoot }.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                paths.Add(ClientInstall.GameFolder(root));
                paths.Add(ClientInstall.ViewerFolder(root));
            }
            foreach (var lang in new[] { 0, 1, 2 })
            {
                var registered = GameRegistration.Read(lang, exactVendor: true);
                paths.Add(registered.GameFolder);
                paths.Add(registered.ViewerFolder);
                paths.AddRange(registered.ComServers);
            }
            paths.AddRange(state.Profiles.LoadAll().Select(profile => profile.GameFolder));
            return paths;
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            var d = new System.Windows.Forms.FolderBrowserDialog { Description = UiText.T("Pick the folder the game goes in (Final Fantasy XI and PlayOnlineViewer are made inside it)"), SelectedPath = _root };
            if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            _root = d.SelectedPath;
            Refresh();
        }

        public bool CanLeave()
        {
            if (_cleaning) return false;
            if (_cancel == null) return true;
            ProgressText.Text = UiText.T("Stop the install first.");
            return false;
        }
    }
}
