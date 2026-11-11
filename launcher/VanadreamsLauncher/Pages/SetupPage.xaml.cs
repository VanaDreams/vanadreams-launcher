using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public partial class SetupPage : UserControl, IBusyPage
    {
        private const string AshitaZipUrl = "https://github.com/AshitaXI/Ashita-v4beta/archive/refs/heads/main.zip";
        private readonly MainWindow _win;
        private string _v3;
        private bool _busy;

        public SetupPage(MainWindow win)
        {
            InitializeComponent();
            _win = win;
            var state = App.State;
            FolderBox.Text = string.IsNullOrWhiteSpace(state.AshitaRoot) ? @"C:\Games\Vanadreams" : state.AshitaRoot;
            if (state.HasAshita) { Title.Text = UiText.T("Repair or update Ashita"); GoButton.Content = UiText.T("Check and update"); }
            // a snapshot for the website never shows the machine's own v3 folder, which carries a Windows user name
            _v3 = string.IsNullOrEmpty(App.SnapshotPath) ? V3Import.FindInstalls().FirstOrDefault() : null;
            if (_v3 != null)
            {
                ImportPanel.Visibility = Visibility.Visible;
                ImportLabel.Text = UiText.T("Ashita v3 found at ") + _v3;
            }
            FolderBox.TextChanged += (s, e) => UpdateAdopt();
            UpdateAdopt();
        }

        /// <summary>The typed folder as a usable root: trailing slash off, except that a drive root keeps its slash.</summary>
        internal static string RootFrom(string text)
        {
            var t = (text ?? "").Trim().TrimEnd('\\', '/');
            if (t.Length == 2 && t[1] == ':') t += "\\";   // "D:" alone is drive-relative, which is never what was meant
            return t;
        }

        private void UpdateAdopt()
        {
            var root = RootFrom(FolderBox.Text);
            var existing = root.Length > 0 && File.Exists(Path.Combine(root, "Ashita-cli.exe"));
            AdoptButton.Visibility = existing && !string.Equals(root, App.State.AshitaRoot, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            if (existing) Say(UiText.T("An Ashita v4 install is already in that folder. Update it, or use it as it is."), "GoldSoft");
        }

        /// <summary>Register an Ashita v4 folder the player already has, write the profile, download nothing.</summary>
        private void Adopt_Click(object sender, RoutedEventArgs e)
        {
            var root = RootFrom(FolderBox.Text);
            if (!File.Exists(Path.Combine(root, "Ashita-cli.exe"))) return;
            var state = App.State;
            try
            {
                state.Settings.AshitaRoot = root;
                state.Settings.Save();
                var store = state.Profiles;
                var vdPath = store.PathFor("vanadreams");
                if (!File.Exists(vdPath))
                {
                    var template = store.TemplatePath();
                    if (template == null) throw new InvalidOperationException("No example profile in " + store.BootDir + " to copy from.");
                    var p = Profile.Load(template).DuplicateTo(vdPath, "Vanadreams");
                    p.Command = new LoaderCommand { Server = LoaderCommand.VanadreamsServer };
                    var loader = Path.Combine(root, "bootloader", "xiloader.exe");
                    p.BootFile = File.Exists(loader) ? loader : p.BootFile;
                    p.Script = "vanadreams.txt";
                    p.Save();
                    if (!File.Exists(loader)) Say(UiText.T("Profile written. No xiloader in bootloader\\ yet: press Check and update to fetch it, or browse to yours on the Profiles page."), "Warn");
                    else Say(UiText.T("Using ") + root + UiText.T(". Vanadreams profile written."), "Ok");
                }
                else Say(UiText.T("Using ") + root + UiText.T(". Vanadreams profile already there."), "Ok");
                state.Settings.LastProfile = "vanadreams";
                state.Settings.SetupDone = true;
                state.Settings.Save();
                state.ApplyEnabledAddons();
                state.CheckVersion();
                state.Notify();
                _win.RefreshStrip();
                Title.Text = UiText.T("Repair or update Ashita"); GoButton.Content = UiText.T("Check and update");
                UpdateAdopt();
            }
            catch (Exception ex) { Log.Error("adopt", ex); Say(ex.Message, "Bad"); }
        }

        private void Choose_Click(object sender, RoutedEventArgs e)
        {
            var d = new System.Windows.Forms.FolderBrowserDialog { Description = UiText.T("Pick or make the folder Ashita v4 lives in"), SelectedPath = FolderBox.Text };
            if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) FolderBox.Text = d.SelectedPath;
        }

        private async void Go_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var root = RootFrom(FolderBox.Text);
            if (string.IsNullOrWhiteSpace(root)) { Say(UiText.T("Choose a folder first."), "Warn"); return; }
            if (ClientVersion.IsUnderProgramFiles(root)) { Say(UiText.T("Not under Program Files: pick a folder you own, like C:\\Games\\Vanadreams."), "Warn"); return; }
            _busy = true; GoButton.IsEnabled = false;
            var state = App.State;
            try
            {
                Directory.CreateDirectory(root);
                var progress = new Progress<DownloadProgress>(p => { Progress.Visibility = Visibility.Visible; Progress.IsIndeterminate = false; Progress.Value = p.Fraction * 100; Status.Text = UiText.F("{0}: {1:0.0} of {2:0.0} MB", p.Label, p.Done / 1048576.0, p.Total > 0 ? p.Total / 1048576.0 : 0); });

                // 1. Ashita v4 beta
                Mark(Step1);
                var ashitaZip = Path.Combine(state.Settings.DownloadsFolder, "Ashita-v4beta-main.zip");
                Say(UiText.T("Downloading Ashita v4 beta…"));
                await state.Downloader.DownloadFileAsync(AshitaZipUrl, ashitaZip, progress, UiText.T("Ashita v4 beta"));
                Say(UiText.T("Unpacking Ashita…"));
                await Task.Run(() => UnpackStrippingTopFolder(ashitaZip, root));
                if (!File.Exists(Path.Combine(root, "Ashita-cli.exe"))) throw new InvalidOperationException(UiText.T("Ashita-cli.exe did not appear after unpacking; the archive layout changed."));
                Done(Step1);

                // 2. xiloader - the one the server accepts, never "the latest" (Services\Loader.cs says why)
                Mark(Step2);
                Say(UiText.T("Fetching xiloader ") + Loader.Tag + "…");
                await Loader.InstallAsync(state.Downloader, state.Settings.DownloadsFolder, root, true, progress);
                // and the Microsoft runtime it and Ashita need, without which Ashita's console hangs on its banner
                Say(UiText.T("Checking Microsoft's Visual C++ runtime…"));
                await VcRuntime.EnsureAsync(state.Downloader, state.Settings.DownloadsFolder, progress);
                Done(Step2);

                // 3. profile
                Mark(Step3);
                state.Settings.AshitaRoot = root;
                state.Settings.Save();
                var store = state.Profiles;
                var vdPath = store.PathFor("vanadreams");
                if (!File.Exists(vdPath))
                {
                    var template = store.TemplatePath();
                    if (template == null) throw new InvalidOperationException(UiText.T("Ashita's example profiles are missing from ") + store.BootDir + ".");
                    var p = Profile.Load(template).DuplicateTo(vdPath, "Vanadreams");
                    p.Command = new LoaderCommand { Server = LoaderCommand.VanadreamsServer };
                    p.BootFile = Path.Combine(root, "bootloader", "xiloader.exe");
                    p.Script = "vanadreams.txt";
                    p.AutoClose = true;
                    // a new player starts borderless at their own screen's size, never fullscreen
                    if (p.Width <= 0) { var s = ScreenSize.PrimaryPixels(); p.Width = s.Width; p.Height = s.Height; p.SetMenuScale(Profile.DefaultMenuScale(s.Height)); }
                    if (p.Mode == WindowMode.Registry || p.Mode == WindowMode.Fullscreen) p.Mode = WindowMode.Borderless;
                    p.Save();
                }
                state.Settings.LastProfile = "vanadreams";
                Done(Step3);

                // 4. v3 import
                Mark(Step4);
                if (_v3 != null && (ImportProfiles.IsChecked == true || ImportConfigs.IsChecked == true))
                {
                    var lines = new List<string>();
                    if (ImportProfiles.IsChecked == true)
                    {
                        List<Credential> logins; List<string> ids;
                        var rep = V3Import.ImportProfiles(_v3, root, out logins, out ids);
                        lines.AddRange(rep.Lines);
                        for (var i = 0; i < ids.Count && i < logins.Count; i++) if (logins[i] != null) state.Credentials.Set(ids[i], logins[i].User, logins[i].Password);
                    }
                    if (ImportConfigs.IsChecked == true) lines.AddRange(V3Import.ImportConfigs(_v3, root).Lines);
                    Log.Info("v3 import: " + string.Join(" | ", lines));
                    Step4.Text = UiText.T("4. From v3: ") + string.Join(" ", lines.Take(4));
                }
                else Step4.Text = UiText.T("4. No v3 install found, nothing to bring over.");
                Done(Step4);

                // 5. addons: a new install starts with none switched on, and a repair run leaves the
                // player's ticks alone. Setup used to tick every addon that ships inside Ashita, all 86,
                // on every run - and some of those are wrong for this server (chatfix rewrites chat,
                // tells and menus for an older packet layout; ime makes typed text need Enter twice).
                // Players tick what they want on the Addons page. The launcher's own music is a
                // setting (MusicOn, on by default), not an addon, so it is not touched here.
                // The exception is whatever the catalogue marks onByDefault (the Vanadreams playlist):
                // given once, then the player's to untick.
                Mark(Step5);
                if (state.Catalog.Items.Count == 0) await state.RefreshCatalogAsync();
                await state.GiveDefaultsAsync();
                state.Settings.SetupDone = true;
                state.Settings.Save();
                state.ApplyEnabledAddons();
                state.CheckVersion();
                Done(Step5);
                Say(UiText.T("Done. Ashita is at ") + root + ".", "Ok");
                state.Notify();
                _win.RefreshStrip();
                Title.Text = UiText.T("Repair or update Ashita"); GoButton.Content = UiText.T("Check and update");
            }
            catch (Exception ex)
            {
                Log.Error("setup", ex);
                Say(ex.Message + UiText.T(" Press the button again to retry; nothing already fetched is fetched twice."), "Bad");
            }
            finally
            {
                _busy = false; GoButton.IsEnabled = true; Progress.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>GitHub's branch zip nests everything under Ashita-v4beta-main\; drop that level.</summary>
        private static void UnpackStrippingTopFolder(string zip, string root)
        {
            using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
            {
                var fullRoot = Path.GetFullPath(root).TrimEnd('\\') + "\\";
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    var parts = entry.FullName.Split('/');
                    var rel = string.Join("\\", parts.Skip(1));
                    if (rel.Length == 0) continue;
                    var target = Path.GetFullPath(Path.Combine(fullRoot, rel));
                    if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) continue;
                    // never overwrite a player's own profiles or scripts on repair
                    if ((rel.StartsWith("config\\boot\\", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(rel).StartsWith("example", StringComparison.OrdinalIgnoreCase)) ||
                        rel.Equals("scripts\\vanadreams.txt", StringComparison.OrdinalIgnoreCase))
                        if (File.Exists(target)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
        }

        private void Say(string text, string brush = "Cream") { Status.Text = text; Status.Foreground = (Brush)FindResource(brush); }
        private void Mark(TextBlock step) { step.Foreground = (Brush)FindResource("GoldSoft"); }
        private void Done(TextBlock step) { step.Foreground = (Brush)FindResource("Ok"); if (!step.Text.StartsWith("✓")) step.Text = "✓ " + step.Text; }

        public bool CanLeave()
        {
            if (!_busy) return true;
            Say(UiText.T("Setup is still running. It finishes on its own; wait for it here."), "Warn");
            return false;
        }

        private void InstallGame_Click(object sender, RoutedEventArgs e) => _win.Navigate(new InstallPage(_win));
    }
}
