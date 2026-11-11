using System;
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

        private readonly bool _asNewPlayer;

        /// <param name="asNewPlayer">Snapshot hook: show the page as a PC with no game on it sees it.</param>
        public InstallPage(MainWindow win, bool asNewPlayer = false)
        {
            InitializeComponent();
            _win = win;
            _asNewPlayer = asNewPlayer;
            Loaded += async (s, e) => { Refresh(); await LoadManifestAsync(); };
        }

        private static string Gb(long bytes) => (bytes / 1073741824.0).ToString("0.0") + " GB";
        private string Expected => App.State.Version.ExpectedIsPublished ? App.State.Version.Expected : null;
        /// <summary>The update found at start, when it is for the folder this page installs to.</summary>
        private ClientUpdate Pending
        {
            get
            {
                var u = App.State.PendingClientUpdate;
                return u != null && !_asNewPlayer && GameRegistration.SameFolder(u.Root, _root) ? u : null;
            }
        }
        private string AshitaFolder => App.State.HasAshita ? App.State.AshitaRoot : null;

        private void Refresh()
        {
            var found = _asNewPlayer ? null : App.State.FfxiFolder;   // the copy Vanadreams plays, not whatever Windows has registered
            var have = ClientVersion.ReadInstalled(found);
            var want = Expected;
            FolderText.Text = ClientInstall.GameFolder(_root);
            VersionText.Text = want == null ? UiText.T("the server hasn't published its version yet") : want + UiText.T(" · the version the server runs");
            FoundText.Text = found == null ? UiText.T("none on this PC") : (have ?? UiText.T("version unknown")) + " · " + found.TrimEnd('\\');
            var matches = found != null && have != null && want != null && string.CompareOrdinal(have.Substring(0, 6), want.Substring(0, 6)) >= 0;
            State.Text = found == null ? UiText.T("Not installed yet.") : matches ? UiText.T("✓ Installed · matches the server") : UiText.T("Installed, but older than the server needs. Install game fetches the right one.");
            State.Foreground = (Brush)FindResource(found == null ? "Warn" : matches ? "Ok" : "Warn");
            if (Pending != null && _cancel == null)
            {
                State.Text = UiText.T("Update ready · ") + Pending.Words + UiText.T(". Only these are downloaded.");
                State.Foreground = (Brush)FindResource("Warn");
            }
            InstallButton.Content = _cancel != null ? UiText.T("Stop") : Pending != null ? UiText.T("Update game") : found == null ? UiText.T("Install game") : UiText.T("Install a fresh copy");

        }

        private async Task LoadManifestAsync()
        {
            if (Expected == null) { SizeText.Text = UiText.T("known once the server's version is published"); return; }
            SizeText.Text = UiText.T("checking…");
            try
            {
                _manifest = ClientManifest.Parse(await App.State.Downloader.GetStringAsync(ClientInstall.ManifestUrl(Expected)));
                var left = ClientInstall.Remaining(_manifest, ClientInstall.Done(_root, _manifest.Version, AshitaFolder));
                SizeText.Text = left.Count > 0 && left.Count < _manifest.Chunks.Count
                    ? Gb(left.Sum(c => c.Size)) + UiText.T(" still to fetch, of ") + Gb(_manifest.TotalSize)
                    : Gb(_manifest.TotalSize) + " · " + Gb(_manifest.TotalUnpacked) + UiText.T(" on disk");
            }
            catch (Exception ex)
            {
                Log.Warn("client manifest: " + ex.Message);
                SizeText.Text = UiText.T("the download isn't available right now");
            }
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (_cancel != null) { _cancel.Cancel(); return; }
            if (_manifest == null) { await LoadManifestAsync(); if (_manifest == null) { ProgressText.Text = UiText.T("The game download isn't available right now. Try again in a while."); return; } }

            var drive = new DriveInfo(Path.GetPathRoot(_root));
            var todo = ClientInstall.Remaining(_manifest, ClientInstall.Done(_root, _manifest.Version, AshitaFolder));
            var needed = todo.Sum(c => c.Size + c.Unpacked);
            // only new parts for a copy that is already registered: no permission prompt at the end
            var updateOnly = todo.Count < _manifest.Chunks.Count && ClientInstall.IsRegistered(_root);
            if (drive.AvailableFreeSpace < needed) { ProgressText.Text = UiText.F("{0} has {1} free and the install needs {2}. Pick another folder.", drive.Name, Gb(drive.AvailableFreeSpace), Gb(needed)); return; }

            var state = App.State;
            _cancel = new CancellationTokenSource();
            FolderButton.IsEnabled = false; Refresh();
            Progress.Visibility = Visibility.Visible; Progress.IsIndeterminate = false;
            var started = DateTime.UtcNow; long startedAt = -1;
            var progress = new Progress<InstallProgress>(p =>
            {
                Progress.Value = 100.0 * p.Done / Math.Max(1, p.Total);
                if (startedAt < 0) startedAt = p.Done;
                var secs = (DateTime.UtcNow - started).TotalSeconds;
                var rate = secs > 5 ? (p.Done - startedAt) / secs : 0;
                var left = rate > 0 ? UiText.T(" · about ") + Math.Max(1, (int)Math.Ceiling((p.Total - p.Done) / rate / 60)) + UiText.T(" min left") : "";
                ProgressText.Text = UiText.F("{0} part {1} of {2} · {3} of {4}{5}", UiText.T(p.Stage), p.Part, p.Parts, Gb(p.Done), Gb(p.Total), left);
            });
            try
            {
                await ClientInstall.InstallFilesAsync(state.Downloader, _manifest, _root, state.Settings.DownloadsFolder, progress, _cancel.Token,
                    AshitaFolder, state.Settings.LowSpec);
                state.Settings.GameInstallRoot = _root;
                state.Settings.Save();
                // the look and title packs land as XIPivot overlays: XIPivot in place and the overlays listed, once
                XIPivotBundle.Ensure(AshitaFolder);
                if (state.GiveOverlays()) state.ApplyEnabledAddons();
                if (updateOnly) ProgressText.Text = UiText.T("Updated. Press Play.");
                else
                {
                    ProgressText.Text = UiText.T("Files in place. Windows will ask for permission to register the game…");
                    var ok = await Task.Run(() => ClientInstall.RunRegister(_root, state.Settings.DownloadsFolder));
                    ProgressText.Text = ok ? UiText.T("Installed. Run Setup if you haven't, then press Play.")
                                           : UiText.T("The permission prompt was refused, so the game isn't registered yet. Press Install game again to finish; nothing downloads twice.");
                }
                await state.CheckClientUpdateAsync();
                state.CheckVersion();
                state.Notify();
            }
            catch (OperationCanceledException) { ProgressText.Text = UiText.T("Stopped. Press Install game to carry on where it left off."); }
            catch (Exception ex) { Log.Error("client install", ex); ProgressText.Text = ex.Message; }
            finally
            {
                _cancel = null; FolderButton.IsEnabled = true; Progress.Visibility = Visibility.Collapsed;
                Refresh();
                Packs.Reload();   // an install brings the look and title packs
            }
        }




        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            var d = new System.Windows.Forms.FolderBrowserDialog { Description = UiText.T("Pick the folder the game goes in (Final Fantasy XI and PlayOnlineViewer are made inside it)"), SelectedPath = _root };
            if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            _root = d.SelectedPath;
            App.State.Settings.GameInstallRoot = _root;
            App.State.Settings.Save();
            Refresh();
        }

        public bool CanLeave()
        {
            if (_cancel == null) return true;
            ProgressText.Text = UiText.T("Stop the install first.");
            return false;
        }
    }
}
