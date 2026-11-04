using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    /// <summary>
    /// The Play page: the current profile's login and the Play button on the left, Vana'News on the right. When a game
    /// update is waiting the button reads Patch, and the update runs here, under the news, before Play is possible.
    /// </summary>
    public partial class MenuPage : UserControl, IBusyPage
    {
        private readonly MainWindow _win;
        private Profile _profile;
        private string _loginFor;   // the profile whose login the boxes hold, so a refresh never wipes what is being typed
        private System.Threading.CancellationTokenSource _patching;
        private bool _patchedNow;

        public MenuPage(MainWindow win)
        {
            InitializeComponent();
            _win = win;
            _onChanged = OnStateChanged;
            foreach (var who in new[] { "mandragora", "moogle", "chocobo", "goblin", "orc" })
            {
                var img = new Image { Source = CrewGuide.Picture(who), Height = 72, Margin = new Thickness(1, 0, 1, 0), Stretch = Stretch.Uniform };
                if (who == "chocobo") { img.RenderTransformOrigin = new Point(0.5, 0.5); img.RenderTransform = new ScaleTransform(-1, 1); }
                img.Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 4, Direction = 270, BlurRadius = 4, Opacity = 0.6, Color = Colors.Black };
                Crew.Children.Add(img);
            }
            ShowNews(News.ReadCache(App.State.Settings.NewsCachePath));
            // listen only while on screen; a page that has been left must not keep reloading profiles
            Loaded += async (s, e) =>
            {
                LoadProfile(); Keyboard.Focus(this); App.State.Changed += _onChanged;
                if (string.IsNullOrEmpty(App.SnapshotPath))
                    ShowNews(await News.FetchAsync(App.State.Downloader, App.State.Settings.NewsUrl, App.State.Settings.NewsCachePath));
            };
            Unloaded += (s, e) => App.State.Changed -= _onChanged;
        }

        private readonly Action _onChanged;
        private void OnStateChanged() => Dispatcher.BeginInvoke(new Action(LoadProfile));

        private void ShowNews(List<NewsItem> items)
        {
            NewsList.ItemsSource = items;
            NoNews.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadProfile()
        {
            var state = App.State;
            var all = state.Profiles.LoadAll();
            _profile = all.FirstOrDefault(p => string.Equals(p.Id, state.Settings.LastProfile, StringComparison.OrdinalIgnoreCase))
                       ?? all.FirstOrDefault(p => !p.IsExample) ?? all.FirstOrDefault();
            if (_profile == null)
            {
                ProfileLine.Text = "No profile yet · run Setup";
                PlayButton.IsEnabled = false;
                UserBox.IsEnabled = PassBox.IsEnabled = RememberBox.IsEnabled = false;
                ShowPatch();
                return;
            }
            PlayButton.IsEnabled = true;
            UserBox.IsEnabled = PassBox.IsEnabled = RememberBox.IsEnabled = true;
            ProfileLine.Text = "Profile: " + _profile.Name;
            if (!string.Equals(_loginFor, _profile.Id, StringComparison.OrdinalIgnoreCase))
            {
                _loginFor = _profile.Id;
                var cred = state.Credentials.Get(_profile.Id);
                UserBox.Text = cred?.User ?? "";
                PassBox.Password = cred?.Password ?? "";
                RememberBox.IsChecked = true;
            }
            ShowPatch();
        }

        /// <summary>The update waits for this profile when it plays Vanadreams; another server's profile plays as it is.</summary>
        /// The look and title packs' move is about the game copy, not the server, so it is offered on any profile:
        /// 16 Oct 2026, Ferrin's profile pointed at the server's LAN address and the Patch for the move never showed.
        private bool PatchFirst => _profile != null && !_patchedNow &&
                                   ((_profile.IsVanadreams && App.State.PatchNeeded) || App.State.OverlayMigrationDue);

        /// <summary>Play or Patch on the button, and the line and bar under the news.</summary>
        private void ShowPatch()
        {
            if (_patching != null) return;   // the patch keeps its own words until it ends
            var pending = App.State.PendingClientUpdate;
            if (PatchFirst)
            {
                PlayButton.Content = "⟳  Patch";
                PatchPanel.Visibility = Visibility.Visible;
                PatchBar.Visibility = Visibility.Visible;
                PatchBar.Value = 0;
                PatchText.Foreground = (Brush)FindResource("GoldSoft");
                PatchText.Text = pending != null
                    ? "A game update is ready: " + pending.Words + ". Press Patch to install it."
                    : App.State.OverlayMigrationDue
                    ? "The look and title packs are moving into packs you can switch on and off (about 400 MB). Press Patch."
                    : "The server runs " + App.State.Version.Expected + " and this game is " + (App.State.Version.Installed ?? "older") + ". Press Patch to bring it up to date.";
                return;
            }
            PlayButton.Content = "▶  Play";
            if (!_patchedNow) PatchPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Snapshot hook (menu-patching): the page part way through a patch, nothing downloaded.</summary>
        public void ShowSamplePatch(double fraction)
        {
            PlayButton.Content = "Patching…";
            PlayButton.IsEnabled = false;
            PatchPanel.Visibility = Visibility.Visible;
            PatchBar.Visibility = Visibility.Visible;
            PatchBar.Value = fraction * 100;
            PatchText.Text = "Patch in progress, please wait.";
        }

        public bool CanLeave()
        {
            if (_patching == null) return true;
            PatchText.Text = "Patch in progress, please wait.";
            return false;
        }

        private async void Patch()
        {
            var state = App.State;
            _patching = new System.Threading.CancellationTokenSource();
            PlayButton.Content = "Patching…";
            PlayButton.IsEnabled = false;
            PatchPanel.Visibility = Visibility.Visible;
            PatchBar.Visibility = Visibility.Visible;
            PatchBar.Value = 0;
            PatchText.Foreground = (Brush)FindResource("GoldSoft");
            PatchText.Text = "Patch in progress, please wait.";
            var progress = new Progress<InstallProgress>(p =>
            {
                PatchBar.Value = 100.0 * p.Done / Math.Max(1, p.Total);
                PatchBar.ToolTip = $"{p.Stage} part {p.Part} of {p.Parts} · {p.Done / 1073741824.0:0.0} of {p.Total / 1073741824.0:0.0} GB";
            });
            try
            {
                var said = await state.PatchClientAsync(progress, _patching.Token);
                _patching = null;
                if (said != null) { PatchText.Text = said; PatchText.Foreground = (Brush)FindResource("Warn"); return; }
                _patchedNow = !state.PatchNeeded;
                if (!_patchedNow) { PatchText.Text = "Part of the patch is still to come. Press Patch again."; return; }
                PatchBar.Value = 100;
                PatchBar.Visibility = Visibility.Collapsed;
                PatchText.Text = "Patch finished. You're up to date.";
                Log.Info("patch finished from the Play page");
                Music.Jingle();
            }
            catch (Exception ex)
            {
                Log.Error("patch", ex);
                PatchText.Text = ex.Message;
                PatchText.Foreground = (Brush)FindResource("Bad");
            }
            finally
            {
                _patching = null;
                PlayButton.IsEnabled = _profile != null;
                PlayButton.Content = PatchFirst ? "⟳  Patch" : "▶  Play";   // the line under the news keeps what happened
                _win.RefreshStrip();
            }
        }

        /// <summary>The login in the boxes: kept for this profile when Remember me is ticked, forgotten when it is not.</summary>
        private Credential TakeLogin()
        {
            var user = UserBox.Text.Trim();
            var pass = PassBox.Password;
            var state = App.State;
            if (RememberBox.IsChecked == true) state.Credentials.Set(_profile.Id, user, pass);
            else state.Credentials.Remove(_profile.Id);
            return user.Length == 0 && pass.Length == 0 ? null : new Credential { User = user, Password = pass };
        }

        private void Change_Click(object sender, RoutedEventArgs e) => _win.Navigate(new ProfilesPage(_win, _profile?.Id));

        /// <summary>A long folder as drive, "…" and its last folders, so it fits the facts column.</summary>
        public static string ShortPath(string path, int max)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= max) return path ?? "";
            var parts = path.TrimEnd('\\').Split('\\');
            var tail = parts[parts.Length - 1];
            for (var i = parts.Length - 2; i > 0 && parts[0].Length + 3 + parts[i].Length + 1 + tail.Length <= max; i--) tail = parts[i] + "\\" + tail;
            return parts[0] + "\\…\\" + tail;
        }

        /// <summary>Patch while an update waits; Play once there is none.</summary>
        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (_profile == null || _patching != null) return;
            if (PatchFirst) { Patch(); return; }
            Play();
        }

        /// <summary>The Vanadreams Discord's permanent invite, the same one vanadreams.org carries.</summary>
        public const string DiscordInvite = "https://discord.gg/tBpNVBwKqT";

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { Log.Warn("open url: " + ex.Message); }
        }

        /// <summary>
        /// Ashita runs as the player unless something here needs administrator: the profile asks for it, it is a retail
        /// profile (PlayOnline updates itself), or Ashita or the game sits under Program Files, where only an
        /// administrator can write the game's settings and screenshots.
        /// </summary>
        private bool NeedsAdmin(Profile profile, string gameRoot) =>
            profile.RunAsAdmin || profile.IsRetail ||
            ClientVersion.IsUnderProgramFiles(App.State.AshitaRoot) || ClientVersion.IsUnderProgramFiles(gameRoot);

        /// <summary>
        /// The copy of the game this profile should start. Vanadreams looks across every copy on the PC for the one
        /// that fits the server (AppState.VanadreamsGameRoot); any other server, retail included, plays the profile's
        /// own game folder or the copy Windows had before we took it.
        /// Null when there is nothing known to point at, and the registration is left alone.
        /// </summary>
        private string GameRootFor(Profile profile, RegisteredGame reg)
        {
            var state = App.State;
            if (!profile.IsVanadreams) return state.OtherGameRootFor(profile);

            var registered = GameRegistration.CurrentRoot(reg);
            var root = state.VanadreamsGameRoot(registered ?? "");
            Log.Info("play: the copy to start is " + (root ?? "whatever Windows has") +
                     (root == null ? "" : ", version " + (state.InstalledVersion(root) ?? "unknown")) +
                     "; Windows starts " + (registered ?? "nothing") + "; installed here " +
                     (string.IsNullOrWhiteSpace(state.Settings.GameInstallRoot) ? "not recorded" : state.Settings.GameInstallRoot));
            // Nothing recorded? Anyone who installed before the folder was remembered has this empty, and his game
            // may be sitting beside his Ashita. Record it so the Install page opens on the real folder.
            if (root != null && !GameRegistration.HasGame(state.Settings.GameInstallRoot) &&
                (GameRegistration.SameFolder(root, state.Settings.AshitaRoot) || GameRegistration.SameFolder(root, ClientInstall.DefaultRoot)))
            {
                state.Settings.GameInstallRoot = root;
                state.Settings.Save();
                Log.Info("found the game beside " + root + " and recorded it");
            }
            // Null: nothing found to point at - which is not the same as nothing being there. A game put somewhere
            // this launcher does not know to look is still a game, so the launch goes ahead with what Windows has.
            return root;
        }

        /// <summary>Roots whose registration did not take even after the prompt; asked once per run, not on every Play.</summary>
        private static readonly HashSet<string> _registerDidNotTake = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Make sure Windows starts the copy of the game in this root, fixing it in place when it does not.
        /// False only when it could not be put right and the player has been told why.
        /// </summary>
        private bool EnsureGameIsRegistered(string root, RegisteredGame reg, int lang = 1)
        {
            var state = App.State;
            if (GameRegistration.PointsAt(reg, root) || _registerDidNotTake.Contains(root))
                return true;   // nothing to do, which is almost every launch

            // Remember the copy we are taking it from, so a profile for its server can have it back.
            var current = GameRegistration.CurrentRoot(reg);
            if (GameRegistration.HasGame(current) &&
                !GameRegistration.SameFolder(current, root) &&
                !GameRegistration.SameFolder(current, state.Settings.GameInstallRoot))
            {
                state.Settings.OtherGameRoot = current;
                state.Settings.Save();
                Log.Info("the registration was on " + current + "; remembered for other servers");
            }

            try
            {
                if (ClientInstall.RunRegister(root, state.Settings.DownloadsFolder, lang))
                {
                    Log.Info("registration pointed at " + ClientInstall.GameFolder(root));
                    var after = GameRegistration.Read(lang);
                    if (!GameRegistration.PointsAt(after, root))
                    {
                        _registerDidNotTake.Add(root);
                        Log.Warn("registration still differs after registering " + root + ": game " + after.GameFolder +
                                 ", viewer " + after.ViewerFolder + ", COM " + string.Join("; ", after.ComServers));
                    }
                    return true;
                }
                MessageBox.Show(
                    "Windows needs permission to point at the game in " + ClientInstall.GameFolder(root) + " before it can start.\n\n" +
                    "Press Play again and choose Yes.",
                    "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("register on play", ex);
                MessageBox.Show(ex.Message, "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }

        private async void Play()
        {
            if (_profile == null) return;
            var state = App.State;
            var login = TakeLogin();
            // Ashita reads the profile's file itself, so one still on an address the server moved from is put right on disk
            state.RepointMovedProfiles();
            if (LoaderCommand.MovedTo(_profile.Command.Server) != null && File.Exists(_profile.Path)) _profile = Profile.Load(_profile.Path);
            // what the facts panel used to show beside Play is said when Play is pressed
            PlayNote.Visibility = login == null ? Visibility.Visible : Visibility.Collapsed;
            PlayNote.Text = login == null ? "No login typed: the loader's window will ask for your username and password." : "";
            if (_profile.IsRetail && !File.Exists(_profile.BootFile))
            {
                MessageBox.Show("This profile plays retail through PlayOnline Viewer, and PlayOnline Viewer is not on this PC" +
                                (string.IsNullOrWhiteSpace(_profile.BootFile) ? "." : " (" + _profile.BootFile + ").") +
                                "\n\nInstall the retail client, then browse to its pol.exe on the Profiles page.",
                    "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // The loader has to be one the Vanadreams server accepts, and the newest one is not (Services\Loader.cs).
            // A player whose Setup ran on or after 27 Sep 2026 had v2.2.0 in bootloader\ and was told
            // "Your xiloader is too old" by a server that wanted the older one. Put it right here, once,
            // so the fix reaches a player who only ever presses Play. Other servers keep the loader they have.
            if (_profile.IsVanadreams)
            {
                try
                {
                    if (await Loader.InstallAsync(state.Downloader, state.Settings.DownloadsFolder, state.AshitaRoot, false))
                        Log.Info("loader replaced before play");
                }
                catch (Exception ex) { Log.Warn("loader check on play: " + ex.Message); }
                // 11 Oct 2026: a player's install sat on B: while her profile still booted a loader elsewhere,
                // and Ashita closed the moment it opened. Every check on the menu read the folder, so all looked right.
                try
                {
                    var was = _profile.BootFile;
                    if (_profile.PointLoaderAt(state.AshitaRoot)) { _profile.Save(); Log.Info("profile loader moved from " + was + " to " + _profile.BootFile); }
                }
                catch (Exception ex) { Log.Warn("loader path on play: " + ex.Message); }
            }
            // Ashita and xiloader need a newer 32-bit Visual C++ runtime than many PCs have, and without it
            // Ashita's console shows its banner and waits forever (Services\VcRuntime.cs). Put it right here.
            try { await VcRuntime.EnsureAsync(state.Downloader, state.Settings.DownloadsFolder); }
            catch (Exception ex)
            {
                Log.Error("vc runtime on play", ex);
                MessageBox.Show(ex.Message, "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // The effects are only a rename in bootloader\, so they are put right on every Play, in case an update put a
            // wrapper back; with the profile's frame rate and display from the Graphics page (Services\Graphics.cs)
            if (state.HasAshita) Graphics.ApplyForLaunch(state.AshitaRoot, _profile, state.Settings);
            // The game reads its own location from Windows, not from this launcher, and Windows holds one
            // such location for the whole machine. When it does not point at the copy this profile belongs to,
            // the wrong game starts: an older copy kept for another server, which Vanadreams turns away, or
            // nothing at all, so the console closes moments after "Successfully logged in".
            //
            // We know which copy each profile needs, so this is ours to keep right rather than ours to report.
            // Point it at that copy and carry on. The only thing the player sees is Windows' own permission
            // prompt, and only on the launch after it drifted or when they switch between servers.
            // 26 Sept 2026: a player installed twice, to C: and then to D:, and the registration stayed on
            // the first. He spent an hour on it and nothing anywhere said why.
            var lang = _profile.Command.Lang;
            var reg = GameRegistration.Read(lang);
            var root = GameRootFor(_profile, reg);
            var previousRoot = GameRegistration.CurrentRoot(reg);
            if (root != null && !EnsureGameIsRegistered(root, reg, lang)) return;
            // 12 Oct 2026: Mogsy's loader logged in and closed, because neither pol.exe nor FFXiMain.dll was where
            // Windows pointed. When no copy we know of is a whole game, the launch is all but sure to do the same.
            if (_profile.IsVanadreams && root == null &&
                MessageBox.Show(PlayCheck.NoGameSentence, "Vanadreams Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            state.CheckVersion();
            if (_profile.IsVanadreams && state.Version.BlocksPlay)   // the Vanadreams version rule has no say over other servers
            {
                MessageBox.Show(state.Version.Sentence + "\n\nInstall game, on the menu, fetches the version the server runs.", "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                _win.RefreshStrip();
                return;
            }

            // The launcher can sit in the tray for days, and the catalogue was only read at start, so an addon update
            // pushed after that never reached the game (16 Oct 2026). Read it again and bring installed addons up to date.
            if (state.HasAshita)
            {
                try
                {
                    await state.RefreshCatalogAsync();
                    var overruled = await state.OverruleOldVisionAsync("before play");
                    if (await state.UpdateInstalledAddonsAsync("before play") || overruled) state.Settings.Save();
                    if (overruled) state.ApplyEnabledAddons();
                }
                catch (Exception ex) { Log.Warn("addon update on play: " + ex.Message); }
            }

            // macros and config live only in the game's USER folder; a copy before every launch lets a lost set come back.
            // The launcher only reads USER. 19 Oct 2026: carrying files newer-wins between game copies (0.2.48, including
            // the retail copy) mixed one copy's book names (mcr.ttl) with another's books, so Ferrin's and Terry's macro
            // books came out renamed and shuffled. Macros are the player's own files, so nothing here ever writes them.
            try
            {
                var gameFolder = root != null ? ClientInstall.GameFolder(root) : reg.GameFolder;
                var copy = UserFolderBackup.Take(gameFolder, UserFolderBackup.BackupFolder, DateTime.Now);
                if (copy != null) Log.Info("USER folder copied to " + copy);
            }
            catch (Exception ex) { Log.Warn("USER backup on play: " + ex.Message); }

            // Antivirus takes Ashita or the loader away, and without them Ashita closes before anything shows (Services\PlayCheck.cs)
            if (state.HasAshita && !FilesAreThere(state)) return;
            // A network that cannot reach the server looks exactly like every other failure once the loader's window closes
            // Hairpin profiles play from the server's own network, where the public address may not loop back, so they skip it
            if (_profile.IsVanadreams && !_profile.Command.Hairpin && !await ServerAnswers(state)) return;

            var loaderName = PlayCheck.LoaderProcessName(_profile.BootFile);
            var before = PlayCheck.RunningIds(loaderName);
            var launchedAt = DateTime.UtcNow;
            try
            {
                GameLauncher.Launch(state.AshitaRoot, _profile, login, NeedsAdmin(_profile, root ?? GameRegistration.CurrentRoot(reg)));
                Music.FadeOut(3);
                state.Settings.LastProfile = _profile.Id;
                state.Settings.LastPlayed[_profile.Id] = DateTime.Now.ToString("o");
                state.Settings.Save();
                if (!_profile.AutoClose) LoadProfile();
                // the launcher waits to close until the game is open, so it can still say why if it never opens
                var autoClose = _profile.AutoClose;
                var opened = await WatchLaunch(state, _profile, loaderName, before, launchedAt, root, lang);
                GiveOtherGameBack(state, root, previousRoot, loaderName, before, lang);
                // with the window switcher on, "close" means the tray: the switcher has to keep running for the game
                if (autoClose && opened && !_win.HideToTray()) Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Error("launch failed", ex);
                MessageBox.Show(ex.Message, "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Windows' one Final Fantasy XI location was moved to the copy this profile starts. Windower and PlayOnline read
        /// that same location, so a player who keeps an older client elsewhere would find them starting ours (18 Oct 2026).
        /// Once the loader closes, a hidden PowerShell registers the copy Windows had before; Play moves it back next time.
        /// </summary>
        private void GiveOtherGameBack(AppState state, string root, string previousRoot, string loaderName, HashSet<int> before, int lang)
        {
            if (!state.Settings.RestoreOtherGame) return;
            try
            {
                var back = GameRegistration.RestoreTarget(previousRoot, root, state.Settings.OtherGameRoot);
                if (back == null) return;
                var ids = PlayCheck.RunningIds(loaderName);
                ids.ExceptWith(before ?? new HashSet<int>());
                var downloads = state.Settings.DownloadsFolder;
                Directory.CreateDirectory(downloads);
                var cmd = Path.Combine(downloads, "restore-other-game.cmd");
                var ps1 = Path.Combine(downloads, "restore-other-game.ps1");
                File.WriteAllText(cmd, ClientInstall.RegisterScript(back, lang));
                File.WriteAllText(ps1, GameRegistration.WatcherScript(ids, cmd));
                Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\"")
                    { UseShellExecute = false, CreateNoWindow = true });
                Log.Info("when the loader closes, Windows is pointed back at " + back);
            }
            catch (Exception ex) { Log.Warn("restore of the other game not set up: " + ex.Message); }
        }

        /// <summary>False, after telling the player, when antivirus or anything else has taken a file the launch needs.</summary>
        private bool FilesAreThere(AppState state)
        {
            var missing = PlayCheck.Missing(state.AshitaRoot, _profile.BootFile);
            if (missing.Count == 0) return true;
            Log.Warn("before play, missing: " + string.Join("; ", missing));
            ExplainMissing(state, missing);
            return false;
        }

        private void ExplainMissing(AppState state, List<string> missing)
        {
            var av = PlayCheck.Antivirus();
            var text = PlayCheck.MissingSentence(missing, av, state.AshitaRoot);
            // Windows Security alone can be told to leave the folders alone from here; any other antivirus only by hand
            if (PlayCheck.OtherAntivirus(av).Count == 0)
            {
                var ask = MessageBox.Show(text + "\n\nAdd the Ashita and game folders to Windows Security's exclusions now? Windows will ask for permission.",
                    "Vanadreams Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (ask == MessageBoxResult.Yes)
                {
                    var ok = PlayCheck.AddDefenderExclusions(new[] { state.AshitaRoot, state.Settings.GameInstallRoot });
                    MessageBox.Show(ok ? "Done. Now open Setup and press Check and update to put the missing files back, then press Play."
                                       : "The exclusions were not added. Add the folders by hand in Windows Security, Virus & threat protection, Exclusions.",
                        "Vanadreams Launcher", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                return;
            }
            MessageBox.Show(text, "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>False, after telling the player, when the server's login ports do not answer from this PC. A failed check never blocks Play on its own.</summary>
        private async System.Threading.Tasks.Task<bool> ServerAnswers(AppState state)
        {
            PlayCheck.Reach reach;
            try { reach = await PlayCheck.TryPortsAsync(_profile.Command.Server, PlayCheck.LoginPorts, TimeSpan.FromSeconds(4)); }
            catch (Exception ex) { Log.Warn("port check: " + ex.Message); return true; }
            Log.Info("port check " + reach.Host + ": open " + string.Join(",", reach.Open) + " closed " + string.Join(",", reach.Closed) + (reach.NoName ? " (no name)" : ""));
            var text = PlayCheck.ReachSentence(reach, _profile.Command.IsTailscale, state.Status.State);
            if (text == null) return true;
            return MessageBox.Show(text + "\n\nPlay anyway?", "Vanadreams Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Watch the loader after Play. True once the game's window opens. While it runs, the loader's own window is read,
        /// so its last words survive it closing. When it closes first, say why: a file taken, a crash Windows recorded,
        /// the loader's own error line, the game quitting as it started (with what this PC shows about the game), or the
        /// server out of reach. Every early close leaves last-launch.txt beside the log for the bug report.
        /// </summary>
        private async System.Threading.Tasks.Task<bool> WatchLaunch(AppState state, Profile profile, string loaderName, HashSet<int> before, DateTime launchedAt, string root, int lang)
        {
            LaunchOutcome outcome;
            var watch = new LoaderWatch();
            try { outcome = await PlayCheck.WatchAsync(loaderName, before, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(15), ids => watch.Look(ids)); }
            catch (Exception ex) { Log.Warn("launch watch: " + ex.Message); return true; }
            Log.Info("launch " + profile.Name + ": " + outcome);
            if (outcome == LaunchOutcome.GameOpened || outcome == LaunchOutcome.StillWaiting) return true;

            List<string> loaderErrors;
            var stop = LaunchDiagnosis.Classify(watch.LastText, out loaderErrors);
            var ashita = state.HasAshita ? state.AshitaRoot : null;
            List<LaunchDiagnosis.Finding> findings;
            try { findings = await System.Threading.Tasks.Task.Run(() => LaunchDiagnosis.Gather(root, ashita, lang, loaderName)); }
            catch (Exception ex) { findings = new List<LaunchDiagnosis.Finding>(); Log.Warn("launch checks: " + ex.Message); }
            var record = LaunchDiagnosis.Record(DateTime.Now, profile.Name, outcome, stop, watch.LastText, watch.Problem, findings);
            Log.Warn("launch closed early\n" + record);
            try { File.WriteAllText(LaunchDiagnosis.LastLaunchPath(LauncherSettings.DataFolder), record); }
            catch (Exception ex) { Log.Warn("last-launch.txt not written: " + ex.Message); }

            var missing = state.HasAshita ? PlayCheck.Missing(state.AshitaRoot, profile.BootFile) : new List<string>();
            if (missing.Count > 0) { Log.Warn("after play, missing: " + string.Join("; ", missing)); ExplainMissing(state, missing); return false; }

            var crash = PlayCheck.LastCrash(new[] { loaderName, "Ashita-cli" }, launchedAt.AddSeconds(-5));
            if (crash != null)
            {
                Log.Warn("crash after play: " + crash.App + " in " + crash.Module + " " + crash.ModuleVersion);
                MessageBox.Show(PlayCheck.CrashSentence(crash), "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // the loader said why, or got as far as starting the game: that is the answer, the port check below cannot add to it
            if (stop == LoaderStop.LoaderError || stop == LoaderStop.GameQuit)
            {
                MessageBox.Show(LaunchDiagnosis.Sentence(stop, loaderErrors, findings, watch.LastText), "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (profile.IsVanadreams && !profile.Command.Hairpin)
            {
                try
                {
                    var reach = await PlayCheck.TryPortsAsync(profile.Command.Server, PlayCheck.LoginPorts, TimeSpan.FromSeconds(4));
                    var text = PlayCheck.ReachSentence(reach, profile.Command.IsTailscale, state.Status.State);
                    if (text != null) { MessageBox.Show(text, "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
                }
                catch (Exception ex) { Log.Warn("port check after play: " + ex.Message); }
            }

            var said = outcome == LaunchOutcome.NeverStarted ? PlayCheck.NeverStartedNoCause
                     : stop == LoaderStop.Unknown ? PlayCheck.ClosedNoCause
                     : LaunchDiagnosis.Sentence(stop, loaderErrors, findings, watch.LastText);
            MessageBox.Show(said, "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}
