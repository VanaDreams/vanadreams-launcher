using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public sealed class ProfileRow
    {
        public Profile Profile { get; set; }
        public string Name => Profile.IsExample ? Profile.Id : Profile.Name;
        public double Opacity => Profile.IsExample ? 0.45 : 1.0;
    }

    public partial class ProfilesPage : UserControl
    {
        private readonly MainWindow _win;
        private readonly List<ProfileRow> _rows = new List<ProfileRow>();
        private Profile Current => (List.SelectedItem as ProfileRow)?.Profile;

        public ProfilesPage(MainWindow win, string selectId = null)
        {
            InitializeComponent();
            _win = win;
            Reload(selectId);
        }

        private void Reload(string selectId)
        {
            _rows.Clear();
            foreach (var p in App.State.Profiles.LoadAll()) _rows.Add(new ProfileRow { Profile = p });
            List.ItemsSource = null;
            List.ItemsSource = _rows;
            var sel = _rows.FirstOrDefault(r => string.Equals(r.Profile.Id, selectId, StringComparison.OrdinalIgnoreCase)) ?? _rows.FirstOrDefault(r => !r.Profile.IsExample) ?? _rows.FirstOrDefault();
            List.SelectedItem = sel;
        }

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => Show(Current);

        private void Show(Profile p)
        {
            var enabled = p != null;
            Form.IsEnabled = enabled;
            Note.Text = "";
            if (!enabled) return;
            NameBox.Text = p.Name;
            ServerBox.Text = p.Command.Server;
            _filling = true;   // setting the box while the form is filled must not rewrite the boxes beside it
            TailscaleBox.IsChecked = p.Command.IsTailscale;
            _filling = false;
            var cred = App.State.Credentials.Get(p.Id);
            UserBox.Text = cred?.User ?? p.Command.User;
            PassBox.Password = cred?.Password ?? p.Command.Password;
            BootBox.Text = p.BootFile;
            ShowGameFolder(p);
            ExtraBox.Text = p.Command.Extra + (p.Command.Hairpin ? (p.Command.Extra.Length > 0 ? " " : "") + "--hairpin" : "");
            ShowMenuScale(p);
            BgWBox.Text = p.BackgroundWidth > 0 ? p.BackgroundWidth.ToString() : ""; BgHBox.Text = p.BackgroundHeight > 0 ? p.BackgroundHeight.ToString() : "";
            AutoCloseBox.IsChecked = p.AutoClose;
            AdminBox.IsChecked = p.RunAsAdmin;
            if (p.IsExample) Note.Text = UiText.T("A shipped example. Duplicate it, then edit the copy.");
            else if (!string.IsNullOrEmpty(p.BootFile))
            {
                var bf = Path.IsPathRooted(p.BootFile) ? p.BootFile : Path.Combine(App.State.AshitaRoot, p.BootFile);
                if (!File.Exists(bf)) Note.Text = UiText.T("Boot file not found. Run Setup or browse to xiloader.");
            }
        }

        /// <summary>One row of the Menu scale box. Scale 0 is a menu size typed on an older launcher, kept as it is.</summary>
        private sealed class MenuScaleChoice
        {
            public double Scale;
            public string Text;
            public override string ToString() => Text;
        }

        /// <summary>
        /// 18 Oct 2026: the box was "Menu size", and a player who typed their window size there (2560 x 1600) got the
        /// menus at 1:1, tiny. The game stretches the menu to the window, so the player picks how big, not a size.
        /// </summary>
        private void ShowMenuScale(Profile p)
        {
            MenuScaleBox.Items.Clear();
            var current = p.MenuScale;
            MenuScaleChoice picked = null;
            foreach (var s in Profile.MenuScales)
            {
                var c = new MenuScaleChoice { Scale = s, Text = s + "x" + (s == 1 ? UiText.T(" (smallest)") : "") + (p.Height > 0 ? UiText.T("   menu ") + Math.Round(p.Width / s) + " × " + Math.Round(p.Height / s) : "") };
                MenuScaleBox.Items.Add(c);
                if (Math.Abs(s - current) < 0.02) picked = c;
            }
            if (picked == null)
            {
                picked = new MenuScaleChoice { Scale = 0, Text = UiText.T("Custom   menu ") + p.MenuWidth + " × " + p.MenuHeight };
                MenuScaleBox.Items.Add(picked);
            }
            MenuScaleBox.SelectedItem = picked;
        }

        /// <summary>
        /// Vanadreams profiles share one game folder, kept in Settings (FfxiFolderOverride). Left blank, Play looks
        /// across every copy on the PC for the one that fits the server; a folder picked here is always the one
        /// started. Any other server's profile names the copy that works with that server.
        /// 7 Oct 2026: the box was locked for Vanadreams, so a player whose other copy kept being picked could not fix it.
        /// </summary>
        private void ShowGameFolder(Profile p)
        {
            var state = App.State;
            GameBox.IsReadOnly = false;
            GameBrowse.IsEnabled = true;
            if (p.IsVanadreams)
            {
                GameBox.Text = state.Settings.FfxiFolderOverride;
                var auto = state.VanadreamsGameRoot();
                GameBox.ToolTip = UiText.T("The copy of Final Fantasy XI every Vanadreams profile plays. Windows is pointed at it when you press Play.") +
                    (auto != null ? UiText.T("\nLeft blank, the launcher picks the copy that fits the server; right now that is ") + ClientInstall.GameFolder(auto) + "."
                                  : UiText.T("\nLeft blank, the launcher picks the copy that fits the server."));
                return;
            }
            GameBox.Text = p.GameFolder;
            var fallback = GameRegistration.HasGame(state.Settings.OtherGameRoot) ? ClientInstall.GameFolder(state.Settings.OtherGameRoot) : null;
            GameBox.ToolTip = UiText.T("The copy of Final Fantasy XI that works with this server. Windows is pointed at it when you press Play.") +
                (fallback != null ? UiText.T("\nLeft blank, this profile plays ") + fallback + UiText.T(", the copy Windows had before Vanadreams.")
                                  : UiText.T("\nLeft blank, the game Windows already points at is used."));
        }

        private void GameBrowse_Click(object sender, RoutedEventArgs e)
        {
            var d = new System.Windows.Forms.FolderBrowserDialog { Description = UiText.T("Pick this server's FINAL FANTASY XI folder, or the folder that holds it and PlayOnlineViewer"), SelectedPath = GameBox.Text };
            if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) GameBox.Text = d.SelectedPath;
        }

        private bool _filling;

        /// <summary>
        /// Ticked: the server's Tailscale address and --hairpin go into the boxes. Unticked: the public name comes
        /// back and --hairpin goes. Only the boxes change here; Save writes them, the same as any other edit.
        /// </summary>
        private void Tailscale_Changed(object sender, RoutedEventArgs e)
        {
            if (_filling) return;
            var c = LoaderCommand.Parse(ExtraBox.Text);
            c.Server = ServerBox.Text.Trim();
            c.UseTailscale(TailscaleBox.IsChecked == true);
            ServerBox.Text = c.Server;
            ExtraBox.Text = c.Extra + (c.Hairpin ? (c.Extra.Length > 0 ? " " : "") + "--hairpin" : "");
            Note.Text = c.IsTailscale ? UiText.T("Over Tailscale: needs Tailscale running and the Vanadreams server shared with you. Press Save.") : UiText.T("Back to the public address. Press Save.");
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var p = Current;
            if (p == null) return;
            if (p.IsExample) { Note.Text = UiText.T("Duplicate this example first."); return; }
            p.Name = NameBox.Text.Trim();
            var extra = LoaderCommand.Parse(ExtraBox.Text);
            p.Command = new LoaderCommand { Server = ServerBox.Text.Trim(), Hairpin = extra.Hairpin, Extra = extra.Extra };
            p.BootFile = BootBox.Text.Trim();
            if (p.IsVanadreams) App.State.Settings.FfxiFolderOverride = GameBox.Text.Trim();
            else p.GameFolder = GameBox.Text.Trim();
            // the window's mode and size belong to the Graphics page, which saves them as they are picked
            // a size typed on an older launcher that matches no scale stays as it was until another scale is picked
            var scale = MenuScaleBox.SelectedItem as MenuScaleChoice;
            if (scale != null && scale.Scale > 0) p.SetMenuScale(scale.Scale);
            // blank or not a number: the game's own default
            int bw, bh;
            p.BackgroundWidth = int.TryParse(BgWBox.Text, out bw) && bw > 0 ? bw : -1;
            p.BackgroundHeight = int.TryParse(BgHBox.Text, out bh) && bh > 0 ? bh : -1;
            p.AutoClose = AutoCloseBox.IsChecked == true;
            p.RunAsAdmin = AdminBox.IsChecked == true;
            p.Save();
            App.State.Credentials.Set(p.Id, UserBox.Text.Trim(), PassBox.Password);
            App.State.Settings.LastProfile = p.Id;
            App.State.Settings.Save();
            Reload(p.Id);
            var picked = p.IsVanadreams ? App.State.Settings.FfxiFolderOverride : p.GameFolder;
            Note.Text = !string.IsNullOrWhiteSpace(picked) && !GameRegistration.HasGame(GameRegistration.RootOf(picked))
                ? UiText.T("Saved, but there is no game in that folder: pick FINAL FANTASY XI, with PlayOnlineViewer beside it.")
                : UiText.T("Saved.");
            App.State.Notify();
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            var template = App.State.Profiles.TemplatePath();
            if (template == null) { Note.Text = UiText.T("No profile to copy from; run Setup first."); return; }
            CreateFrom(template, UiText.T("New profile"));
        }

        private void Retail_Click(object sender, RoutedEventArgs e)
        {
            var retail = App.State.Profiles.PathFor("example-retail");
            if (!File.Exists(retail)) { Note.Text = UiText.T("Ashita's retail example is missing; run Repair."); return; }
            CreateFrom(retail, UiText.T("New retail profile"), retail: true);
        }

        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            if (Current == null) return;
            CreateFrom(Current.Path, UiText.T("Copy profile"));
        }

        private void CreateFrom(string sourcePath, string title, bool retail = false)
        {
            var id = Prompt.Ask(_win, title, UiText.T("Profile name:"), "");
            if (string.IsNullOrWhiteSpace(id)) return;
            id = id.Trim();
            if (!ProfileStore.IsValidId(id)) { Note.Text = UiText.T("That name has characters a file name can't take."); return; }
            var store = App.State.Profiles;
            if (store.Exists(id)) { Note.Text = id + UiText.T(" already exists."); return; }
            var copy = Profile.Load(sourcePath).DuplicateTo(store.PathFor(id), id);
            if (retail)
            {
                // Retail runs through PlayOnline Viewer's own pol.exe with its quick-play command, as
                // Ashita's example shows. The bootloader folder's pol.exe is for private servers only.
                var viewer = ClientVersion.FindPlayOnlineViewer();
                copy.BootFile = viewer ?? "";
                copy.Command = new LoaderCommand { Extra = "/game eAZcFcB" };
                copy.Script = "vanadreams.txt";
                copy.Save();
                App.State.Settings.LastProfile = id;
                App.State.Settings.Save();
                App.State.Notify();
                if (viewer == null)
                {
                    Reload(id);
                    Note.Text = UiText.T("PlayOnline Viewer was not found. Install the retail client (Guide) and browse to its pol.exe here.");
                    return;
                }
                _win.Navigate(new MenuPage(_win));   // it is the current profile now: Play is one press away
                return;
            }
            else if (Path.GetFileName(sourcePath).StartsWith("example", StringComparison.OrdinalIgnoreCase))
            {
                copy.Command = new LoaderCommand { Server = "" };
                copy.BootFile = Path.Combine(App.State.AshitaRoot, "bootloader", "xiloader.exe");
                copy.Script = "vanadreams.txt";
                copy.Save();
            }
            Reload(id);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var p = Current;
            if (p == null) return;
            if (p.IsExample) { Note.Text = UiText.T("The shipped examples stay."); return; }
            if (MessageBox.Show(UiText.T("Send ") + p.FileName + UiText.T(" to the Recycle Bin?"), UiText.T("Delete profile"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            App.State.Credentials.Remove(p.Id);
            Reload(null);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*", InitialDirectory = Path.Combine(App.State.AshitaRoot, "bootloader") };
            if (d.ShowDialog() == true) BootBox.Text = d.FileName;
        }
    }
}
