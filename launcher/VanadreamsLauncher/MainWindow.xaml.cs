using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Vanadreams.Pages;
using Vanadreams.Services;

namespace Vanadreams
{
    /// <summary>One entry of the launcher's menu, shown both in the rail (vertical) and as a tab (horizontal).</summary>
    public sealed class NavItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _selected, _current;
        public string Label { get; set; }
        public string Key { get; set; }
        public bool Dim { get; set; }
        /// <summary>The hand: on the page showing, or wherever the arrow keys have moved it.</summary>
        public bool Selected { get { return _selected; } set { _selected = value; Raise(); } }
        /// <summary>The page showing now.</summary>
        public bool Current { get { return _current; } set { _current = value; Raise(); } }
        public Visibility HandVisibility => Selected ? Visibility.Visible : Visibility.Hidden;
        public Visibility UnderlineVisibility => Current ? Visibility.Visible : Visibility.Collapsed;
        public double Opacity => Dim ? 0.55 : Selected || Current ? 1.0 : 0.9;
        public Brush TabBrush => (Brush)Application.Current.FindResource(Current ? "GoldSoft" : "Cream");
        /// <summary>A tab makes room on its left for the hand while it has it.</summary>
        public Thickness TabMargin => new Thickness(Selected ? 21 : 8, 0, 0, 0);
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void Raise()
        {
            foreach (var n in new[] { "Selected", "Current", "HandVisibility", "UnderlineVisibility", "Opacity", "TabBrush", "TabMargin" })
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
        }
    }

    /// <summary>A page that is busy (an install, a patch) and must not be left halfway: it says why and returns false.</summary>
    public interface IBusyPage
    {
        bool CanLeave();
    }

    public partial class MainWindow : Window
    {
        public AppState State => App.State;

        private readonly List<NavItem> _nav = new List<NavItem>
        {
            new NavItem { Label = UiText.T("Play"), Key = "play" },
            new NavItem { Label = UiText.T("Graphics"), Key = "graphics" },
            new NavItem { Label = UiText.T("Profiles"), Key = "profiles" },
            new NavItem { Label = UiText.T("Addons"), Key = "addons" },
            new NavItem { Label = UiText.T("Fishing"), Key = "fishing" },
            new NavItem { Label = "Vanatunes", Key = "vanatunes" },
            new NavItem { Label = UiText.T("Capture"), Key = "capture" },
            new NavItem { Label = UiText.T("Setup"), Key = "setup" },
            new NavItem { Label = UiText.T("Install game"), Key = "install" },
            new NavItem { Label = UiText.T("Settings"), Key = "settings" },
            new NavItem { Label = "Discord", Key = "discord" },
            new NavItem { Label = UiText.T("Exit"), Key = "exit", Dim = true },
        };
        private const int MainCount = 10;   // Discord and Exit sit apart, at the foot of the rail or past the divider

        /// <summary>Snapshot hook: the layout a snapshot asked for, without touching the player's setting.</summary>
        private bool? _forcedHorizontal;
        public bool Horizontal => _forcedHorizontal ?? State.Settings.HorizontalMenu;

        public MainWindow()
        {
            InitializeComponent();
            DrawStars();
            RailMain.ItemsSource = _nav.Take(MainCount).ToList();
            RailEnd.ItemsSource = _nav.Skip(MainCount).ToList();
            BarMain.ItemsSource = _nav.Take(MainCount).ToList();
            BarEnd.ItemsSource = _nav.Skip(MainCount).ToList();
            LauncherVersion.Text = Updater.Normalise(Updater.Current).ToString();
            ApplyLayout();
            State.Changed += () => Dispatcher.BeginInvoke(new Action(RefreshStrip));
            Loaded += OnLoaded;
            KeyDown += OnNavKey;
        }

        /// <summary>The menu down the left or across the top, as Settings says; the page and footer make room for it.</summary>
        public void ApplyLayout()
        {
            var h = Horizontal;
            Rail.Visibility = h ? Visibility.Collapsed : Visibility.Visible;
            Bar.Visibility = h ? Visibility.Visible : Visibility.Collapsed;
            Page.Margin = h ? new Thickness(24, 104, 24, 48) : new Thickness(254, 24, 24, 48);
            Footer.Margin = h ? new Thickness(24, 0, 24, 10) : new Thickness(254, 0, 24, 10);
        }

        // ---- the menu ----

        private void Nav_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var item = ((FrameworkElement)sender).Tag as NavItem;
            if (item != null) Go(item.Key);
        }

        /// <summary>Up and Down (Left and Right on the tabs) move the hand; Enter goes there. Typing in a box keeps its keys.</summary>
        private void OnNavKey(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Handled) return;
            var src = e.OriginalSource as DependencyObject;
            if (src is TextBox || src is PasswordBox || src is ComboBox || src is ComboBoxItem || src is ListBox || src is ListBoxItem || src is Slider) return;
            var h = Horizontal;
            var back = h ? System.Windows.Input.Key.Left : System.Windows.Input.Key.Up;
            var next = h ? System.Windows.Input.Key.Right : System.Windows.Input.Key.Down;
            var at = _nav.FindIndex(n => n.Selected);
            if (e.Key == next || e.Key == back)
            {
                var to = ((at < 0 ? 0 : at) + (e.Key == next ? 1 : -1) + _nav.Count) % _nav.Count;
                foreach (var n in _nav) n.Selected = false;
                _nav[to].Selected = true;
                e.Handled = true;
            }
            else if ((e.Key == System.Windows.Input.Key.Enter) && at >= 0)
            {
                Go(_nav[at].Key);
                e.Handled = true;
            }
        }

        /// <summary>Open a page from the menu.</summary>
        public void Go(string key)
        {
            switch (key)
            {
                case "discord": MenuPage.OpenUrl(MenuPage.DiscordInvite); MarkCurrent(Page.Content); return;
                case "exit": Application.Current.Shutdown(); return;
            }
            var page = MakePage(key);
            if (page != null) Navigate(page);
        }

        private UserControl MakePage(string key)
        {
            switch (key)
            {
                case "play": return new MenuPage(this);
                case "graphics": return new GraphicsPage(this);
                case "profiles": return new ProfilesPage(this, State.Settings.LastProfile);
                case "addons": return new AddonsPage(this);
                case "fishing": return new FishingPage(this);
                case "vanatunes": return new VanatunesPage(this);
                case "capture": return new CapturePage(this);
                case "setup": return new SetupPage(this);
                case "install": return new InstallPage(this);
                case "settings": return new SettingsPage(this);
                case "windower": return new WindowerPage(this);
                case "switcher": return new SwitcherPage(this);
            }
            return null;
        }

        /// <summary>The menu entry a page belongs to: the switcher and Windower pages live under Addons.</summary>
        private static string KeyOf(object page)
        {
            if (page is MenuPage) return "play";
            if (page is GraphicsPage) return "graphics";
            if (page is ProfilesPage) return "profiles";
            if (page is AddonsPage || page is SwitcherPage || page is WindowerPage) return "addons";
            if (page is FishingPage) return "fishing";
            if (page is VanatunesPage) return "vanatunes";
            if (page is CapturePage) return "capture";
            if (page is SetupPage) return "setup";
            if (page is InstallPage) return "install";
            if (page is SettingsPage) return "settings";
            return null;
        }

        private void MarkCurrent(object page)
        {
            var key = KeyOf(page);
            foreach (var n in _nav) { n.Current = n.Key == key; n.Selected = n.Key == key; }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;   // startup runs once, even if WPF raises Loaded again
            if (State.HasAshita) GameLauncher.Sweep(State.AshitaRoot);
            State.RepointMovedProfiles();   // a profile still on the server's old address, before the menu reads it
            State.CheckVersion();
            RefreshStrip();
            RefreshMute();
            if (!string.IsNullOrEmpty(App.SnapshotPath)) { await SnapshotAndExit(); return; }
            if (State.HasAshita) Navigate(new MenuPage(this)); else Navigate(new SetupPage(this));
            App.Switcher = new WindowSwitcher(State.Settings, Dispatcher);
            // the game windows all closed while the launcher sat in the tray: nothing is left to switch, so it goes
            App.Switcher.Changed += () => { if (_tray != null && _tray.Visible && App.Switcher.Windows.Count == 0) Quit(); };
            App.Switcher.Apply();
            // music after the first frame, so the unpack on a first run never delays the window
            if (State.Settings.MusicOn) Dispatcher.BeginInvoke(new Action(Music.Start), System.Windows.Threading.DispatcherPriority.Background);
            // first, and alongside the rest: the update window has to be up before anyone reaches Play
            var update = CheckForUpdateAsync();
            // Keep this status and client-version policy until the launcher is restarted.
            await State.RefreshStatusAsync();
            await State.CheckClientUpdateAsync();
            await EnsureLoaderAsync();
            await State.RefreshCatalogAsync();
            await State.GiveDefaultsAsync();
            await update;
        }

        /// <summary>The loader in bootloader\ is one the server accepts; when it is not, it is swapped for the pinned one (Services\Loader.cs).</summary>
        private async Task EnsureLoaderAsync()
        {
            if (!State.HasAshita) return;
            try
            {
                if (await Loader.InstallAsync(State.Downloader, State.Settings.DownloadsFolder, State.AshitaRoot, false)) State.Notify();
            }
            catch (Exception ex) { Log.Warn("loader: " + ex.Message); }
        }

        private string _updatedExe;

        /// <summary>
        /// Once per start: a newer release puts the update window in front of the player while it downloads
        /// (UpdateWindow). The signed exe is swapped in whatever they choose; after Later, the strip keeps
        /// the restart button.
        /// </summary>
        private async Task CheckForUpdateAsync()
        {
            try
            {
                var asset = await Updater.CheckAsync(State.Downloader);
                if (asset == null) return;
                NewsLine.Text = UiText.T("·  Launcher ") + asset.Tag + UiText.T(" downloading…");
                var progress = new Progress<DownloadProgress>();
                var ready = FetchAndApplyAsync(asset, progress);
                var box = new UpdateWindow(asset.Tag, Updater.Normalise(Updater.Current), ready, progress) { Owner = this };
                if (box.ShowDialog() == true && _updatedExe != null) { Updater.Restart(_updatedExe); return; }
                await ready;
                if (_updatedExe == null) { NewsLine.Text = UiText.T("·  Launcher ") + asset.Tag + UiText.T(" is on vanadreams.org"); return; }
                NewsLine.Text = "";   // the button says it, and the footer has room for one of them
                UpdateButton.Content = UiText.T("Restart into ") + asset.Tag;
                UpdateButton.Visibility = Visibility.Visible;
                RefreshStrip();
            }
            catch (Exception ex) { Log.Warn("update: " + ex.Message); }
        }

        /// <summary>Download, check the signature, swap into place. The path of the new exe, or null.</summary>
        private async Task<string> FetchAndApplyAsync(ReleaseAsset asset, IProgress<DownloadProgress> progress)
        {
            var exe = await Updater.FetchAsync(State.Downloader, asset, State.Settings.DownloadsFolder, progress);
            if (exe == null) return null;
            _updatedExe = Updater.Apply(exe);
            return _updatedExe;
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            if (_updatedExe == null) return;
            Updater.Restart(_updatedExe);
        }

        // ---- the tray, for the window switcher ----

        private System.Windows.Forms.NotifyIcon _tray;
        private bool _trayTold;

        /// <summary>
        /// The close button (and Alt+F4), while the switcher runs and a game is open, hides the launcher to the tray instead.
        /// Caught as the window's Close command rather than in Closing, so Exit and an update restart still close for good.
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            source?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                const int WM_SYSCOMMAND = 0x0112, SC_CLOSE = 0xF060;
                if (msg == WM_SYSCOMMAND && ((int)wParam & 0xFFF0) == SC_CLOSE && HideToTray()) handled = true;
                return IntPtr.Zero;
            });
        }

        /// <summary>Hide to the tray when the switcher has a game to look after. False when there is none, so the caller closes.</summary>
        public bool HideToTray()
        {
            var sw = App.Switcher;
            if (sw == null || !sw.Running) return false;
            sw.Scan();
            if (sw.Windows.Count == 0) return false;
            if (_tray == null)
            {
                _tray = new System.Windows.Forms.NotifyIcon { Text = UiText.T("Vanadreams Launcher · window switcher"), Visible = false };
                try
                {
                    using (var s = Application.GetResourceStream(new Uri("pack://application:,,,/assets/moon.ico")).Stream)
                        _tray.Icon = new System.Drawing.Icon(s);
                }
                catch (Exception ex) { Log.Warn("tray icon: " + ex.Message); _tray.Icon = System.Drawing.SystemIcons.Application; }
                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add(UiText.T("Open the launcher"), null, (s, e) => Dispatcher.BeginInvoke(new Action(ShowFromTray)));
                menu.Items.Add(UiText.T("Quit (the window switcher stops)"), null, (s, e) => Dispatcher.BeginInvoke(new Action(Quit)));
                _tray.ContextMenuStrip = menu;
                _tray.DoubleClick += (s, e) => Dispatcher.BeginInvoke(new Action(ShowFromTray));
            }
            _tray.Visible = true;
            Hide();
            if (!_trayTold)
            {
                _trayTold = true;
                _tray.ShowBalloonTip(4000, UiText.T("Window switcher still on"), UiText.T("The launcher is in the tray so your switch keys keep working. Right-click the moon to quit."), System.Windows.Forms.ToolTipIcon.None);
            }
            Log.Info("hidden to the tray for the window switcher");
            return true;
        }

        private void ShowFromTray()
        {
            if (_tray != null) _tray.Visible = false;
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        private void Quit()
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            Application.Current.Shutdown();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            base.OnClosed(e);
        }

        public void Navigate(UserControl page)
        {
            var busy = Page.Content as IBusyPage;
            if (busy != null && !ReferenceEquals(busy, page) && !busy.CanLeave()) { MarkCurrent(Page.Content); return; }
            Page.Content = page;
            MarkCurrent(page);
            page.Focus();
            RefreshMute();
        }

        /// <summary>The corner button: music on or off, remembered in settings, and the Settings page follows it.</summary>
        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            var s = State.Settings;
            if (s.MusicOn && !Music.Playing) { Music.Start(); RefreshMute(); return; }   // faded out after Play: bring it back
            s.MusicOn = !s.MusicOn;
            s.Save();
            if (s.MusicOn) Music.Start(); else Music.Stop();
            RefreshMute();
            var settings = Page.Content as Pages.SettingsPage;
            if (settings != null) settings.SyncMusic();
        }

        public void RefreshMute()
        {
            var on = State.Settings.MusicOn;
            MuteButton.Content = !on ? UiText.T("♪ music off") : Music.Playing ? UiText.T("♪ mute") : UiText.T("♪ play");
            MuteButton.Opacity = on && Music.Playing ? 0.75 : 1.0;
        }

        /// <summary>The footer: server state and players, then the launcher's and the game's versions for troubleshooting.</summary>
        public void RefreshStrip()
        {
            var s = State.Status;
            StatusWord.Text = UiText.T(s.StateWord);
            StatusWhen.Text = s.Online.HasValue && !s.FromCache ? " · " + s.Online.Value + UiText.T(" online") : s.FromCache && s.CheckedAt.HasValue ? " · " + CheckedWord(s) + UiText.T(" (last seen)") : "";
            var note = string.IsNullOrWhiteSpace(s.Note) ? (s.Error != null ? UiText.T("Couldn't reach vanadreams.org.") : null) : s.Note;
            StatusWord.ToolTip = note == null ? s.CheckedWord : note + "\n" + s.CheckedWord;   // the server's whole note, however long
            Brush dot;
            switch (s.State)
            {
                case ServerState.Online: dot = (Brush)FindResource("Ok"); break;
                case ServerState.Maintenance: dot = (Brush)FindResource("Warn"); break;
                case ServerState.Offline: dot = (Brush)FindResource("Bad"); break;
                default: dot = (Brush)FindResource("Mist"); break;
            }
            StatusDot.Fill = dot;
            StatusDot.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = ((SolidColorBrush)dot).Color, BlurRadius = 8, ShadowDepth = 0 };
            // a game update is the Play page's Patch button now; the footer only names the version installed
            var v = State.Version;
            GameVersion.Text = string.IsNullOrEmpty(v.Installed) ? UiText.T("not found") : v.Installed;
            GameVersion.Foreground = (Brush)FindResource(!v.ExpectedIsPublished || v.Verdict == VersionVerdict.Ready ? "Cream" : v.Verdict == VersionVerdict.Unknown ? "Warn" : "Bad");
            GameVersion.ToolTip = v.Sentence;
        }

        private static string CheckedWord(StatusInfo status) => status.CheckedAt.HasValue
            ? UiText.T("checked ") + status.CheckedAt.Value.ToLocalTime().ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant() : "";

        private void DrawStars()
        {
            var rnd = new Random(1313);
            for (var i = 0; i < 90; i++)
            {
                var size = rnd.NextDouble() < 0.2 ? 2.0 : 1.0;
                var star = new Ellipse { Width = size, Height = size, Fill = Brushes.White, Opacity = 0.35 + rnd.NextDouble() * 0.5 };
                Canvas.SetLeft(star, rnd.NextDouble() * 1000);
                Canvas.SetTop(star, rnd.NextDouble() * 600);
                Stars.Children.Add(star);
            }
        }

        private async Task SnapshotAndExit()
        {
            UserControl page;
            var key = (App.SnapshotPage ?? "menu").ToLowerInvariant();
            // "<page>@h" or "<page>@v": that menu layout for this picture only; the player's setting is left alone
            var at = key.IndexOf('@');
            if (at >= 0) { _forcedHorizontal = key.Substring(at + 1).StartsWith("h"); key = key.Substring(0, at); ApplyLayout(); }
            // "addons#<id>": the Addons page with that addon selected
            string item = null;
            var hash = key.IndexOf('#');
            if (hash >= 0) { item = key.Substring(hash + 1); key = key.Substring(0, hash); }
            // "<page>-update": the same page with a game update waiting, so a layout change can be checked with it showing
            // "menu-patching": the Play page part way through a patch
            var patching = key == "menu-patching";
            if (patching) key = "menu-update";
            if (key.EndsWith("-update")) { key = key.Substring(0, key.Length - "-update".Length); State.ShowSampleClientUpdate(); }
            RefreshStrip();
            switch (key)
            {
                case "install-new": page = new InstallPage(this, asNewPlayer: true); break;
                case "menu": case "play": page = new MenuPage(this); break;
                default: page = MakePage(key) ?? new MenuPage(this); break;
            }
            Navigate(page);
            await Task.Delay(700);
            if (patching) { (page as MenuPage)?.ShowSamplePatch(0.42); await Task.Delay(300); }
            if (item != null) { (page as AddonsPage)?.ShowItem(item); await Task.Delay(400); }
            var bmp = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(this);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(App.SnapshotPath)) enc.Save(f);
            if (App.SnapshotPage == "guide")
            {
                var g = new GuideWindow(State); g.Show(); await Task.Delay(500);
                var gb = new RenderTargetBitmap((int)g.ActualWidth, (int)g.ActualHeight, 96, 96, PixelFormats.Pbgra32); gb.Render(g);
                var ge = new PngBitmapEncoder(); ge.Frames.Add(BitmapFrame.Create(gb));
                using (var f = File.Create(System.IO.Path.ChangeExtension(App.SnapshotPath, ".guide.png"))) ge.Save(f);
            }
            Application.Current.Shutdown();
        }
    }
}
