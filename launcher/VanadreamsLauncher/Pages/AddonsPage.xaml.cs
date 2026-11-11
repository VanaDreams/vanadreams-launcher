using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public sealed class AddonRow : INotifyPropertyChanged
    {
        private bool _selected, _enabled;
        private CatalogItem _blockedBy;
        public CatalogItem Item { get; set; }
        /// <summary>The ticked item this one gives way to (catalogue "conflicts"): while it is ticked this row is
        /// unticked and greyed out, as LuAshitacast is while vanagear, which carries it, is ticked.</summary>
        public CatalogItem BlockedBy { get { return _blockedBy; } set { _blockedBy = value; Raise("BlockedBy"); Raise("CanTick"); Raise("Opacity"); } }
        public bool CanTick => Item.HasV4 && _blockedBy == null;
        /// <summary>The launcher's own window switcher, listed with the addons; its tick is LauncherSettings.SwitcherOn.</summary>
        public bool IsSwitcher { get; set; }
        public bool Installed { get; set; }
        public string InstalledVersion { get; set; }
        public bool HasV4 => Item.HasV4;
        public bool Enabled { get { return _enabled; } set { _enabled = value; Raise("Enabled"); } }
        public bool Selected { get { return _selected; } set { _selected = value; Raise("Selected"); Raise("HandVisibility"); } }
        public Visibility HandVisibility => Selected ? Visibility.Visible : Visibility.Hidden;
        public string Label => Item.Name + (string.IsNullOrEmpty(Item.Version) ? "" : " " + Item.Version) + (!Item.HasV4 && !string.IsNullOrEmpty(Item.Replacement) ? " → " + Item.Replacement : "");
        public double Opacity => Item.HasV4 && _blockedBy == null ? 1.0 : 0.5;
        public string Tag => IsSwitcher ? "launcher" : Item.SourceTag;
        public Brush TagBrush => IsSwitcher ? (Brush)Application.Current.FindResource("GoldSoft") : Item.Source == SourceType.None ? (Brush)Application.Current.FindResource("Bad") : Item.Source == SourceType.Bundled ? (Brush)Application.Current.FindResource("Mist") : (Brush)Application.Current.FindResource("GoldSoft");
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public sealed class AddonSection
    {
        public string Name { get; set; }
        public string DisplayName => UiText.T(Name);
        public List<AddonRow> Rows { get; set; }
    }

    public partial class AddonsPage : UserControl
    {
        private readonly MainWindow _win;
        private readonly List<AddonRow> _rows = new List<AddonRow>();
        private AddonRow _current;
        private bool _busy;

        public AddonsPage(MainWindow win)
        {
            InitializeComponent();
            _win = win;
            _onChanged = OnStateChanged;
            Build();
            // listen only while on screen; a page that has been left must not keep rebuilding
            Loaded += (s, e) => App.State.Changed += _onChanged;
            Unloaded += (s, e) => App.State.Changed -= _onChanged;
        }

        private readonly Action _onChanged;
        private void OnStateChanged() => Dispatcher.BeginInvoke(new Action(Build));

        /// <summary>Two columns of addons when each still fits a name, its version and its tag; one full-width column when not.</summary>
        private void SectionRows_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var list = sender as ItemsControl;
            var panel = FindChild<WrapPanel>(list);
            if (panel == null) return;
            var w = Math.Floor(list.ActualWidth);
            panel.ItemWidth = w >= 500 ? Math.Floor(w / 2) : w;
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var c = VisualTreeHelper.GetChild(parent, i);
                var hit = c as T ?? FindChild<T>(c);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Snapshot hook (addons#id): that addon selected and scrolled into view.</summary>
        public void ShowItem(string id)
        {
            var row = _rows.FirstOrDefault(r => string.Equals(r.Item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (row == null) return;
            Select(row);
            FindRowElement(List, row)?.BringIntoView();
        }

        private static FrameworkElement FindRowElement(DependencyObject parent, AddonRow row)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement element && ReferenceEquals(element.DataContext, row)) return element;
                var hit = FindRowElement(child, row);
                if (hit != null) return hit;
            }
            return null;
        }

        private static readonly string[] SectionOrder = {
            "VanaDreams", "Launcher & setup", "Gear & inventory", "Combat & targeting",
            "Maps & travel", "Interface & camera", "Chat & communication",
            "Crafting", "Screenshots & streaming", "Tools & automation", "Other"
        };

        private void Build()
        {
            var state = App.State;
            var selectedId = _current?.Item.Id;
            _rows.Clear();
            _rows.Add(new AddonRow { Item = SwitcherItem, IsSwitcher = true, Installed = true, Enabled = state.Settings.SwitcherOn });
            foreach (var item in state.Catalog.Items)
            {
                string ver; state.Settings.InstalledVersions.TryGetValue(item.Id, out ver);
                _rows.Add(new AddonRow
                {
                    Item = item,
                    Installed = item.IsInstalled(state.AshitaRoot),
                    InstalledVersion = ver,
                    Enabled = state.Settings.EnabledAddons.Contains(item.Id, StringComparer.OrdinalIgnoreCase),
                });
            }
            UpdateBlocked();
            var sections = _rows.GroupBy(r => r.IsSwitcher ? "Launcher & setup" :
                (string.IsNullOrWhiteSpace(r.Item.Category) ? "Other" : r.Item.Category))
                .Select(g => new AddonSection { Name = g.Key, Rows = g.OrderBy(r => r.Item.Name, StringComparer.OrdinalIgnoreCase).ToList() })
                .OrderBy(s => { var index = Array.IndexOf(SectionOrder, s.Name); return index < 0 ? SectionOrder.Length : index; })
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            List.ItemsSource = null; List.ItemsSource = sections;
            var enabledCount = _rows.Count(r => r.Enabled && r.HasV4 && !r.IsSwitcher);
            Heading.Text = UiText.F("✦ Your addons · {0} of {1} enabled", enabledCount, _rows.Count(r => r.HasV4 && !r.IsSwitcher)) + (state.CatalogFromCache ? UiText.T(" · catalogue from cache") : "");
            var sel = _rows.FirstOrDefault(r => r.Item.Id == selectedId) ?? _rows.FirstOrDefault(r => !r.IsSwitcher) ?? _rows.FirstOrDefault();
            Select(sel);
        }

        private void Select(AddonRow row)
        {
            foreach (var r in _rows) r.Selected = false;
            _current = row;
            if (row == null) { ItemName.Text = UiText.T("No catalogue yet"); ItemMeta.Text = UiText.T("The catalogue could not be loaded. Check Settings for the catalogue address."); return; }
            row.Selected = true;
            var i = row.Item;
            SwitcherButton.Visibility = row.IsSwitcher ? Visibility.Visible : Visibility.Collapsed;
            if (row.IsSwitcher)
            {
                ItemName.Text = i.Name;
                ItemMeta.Text = UiText.T("Built into the launcher");
                ItemDesc.Text = i.Description;
                ItemState.Text = row.Enabled ? UiText.T("✓ On") : UiText.T("Off · tick it and Save to turn it on");
                ItemState.Foreground = (Brush)FindResource(row.Enabled ? "Ok" : "Mist");
                ItemV3.Text = "";
                InstallButton.Visibility = DocsButton.Visibility = ConfigButton.Visibility = Visibility.Collapsed;
                return;
            }
            ItemName.Text = i.Name;
            var kind = i.Kind == "polplugin" ? UiText.T("POL plugin") : i.Kind == "plugin" ? UiText.T("Plugin") : i.Kind == "overlay" ? UiText.T("Game-file pack") : UiText.T("Addon");
            ItemMeta.Text = kind + (string.IsNullOrEmpty(i.Maintainer) && string.IsNullOrEmpty(i.Repo) ? "" : UiText.T(" by ") + (i.Maintainer ?? i.Repo.Split('/')[0])) + (string.IsNullOrEmpty(i.Version) ? "" : " · " + i.Version) + (i.Source == SourceType.GithubRelease ? UiText.T(" · built for interface ") + App.State.Catalog.Interface : "");
            ItemDesc.Text = i.Description ?? "";
            if (!i.HasV4)
            {
                ItemState.Text = UiText.T("No v4 version exists.") + (string.IsNullOrEmpty(i.Replacement) ? "" : UiText.T(" Use ") + i.Replacement + UiText.T(" instead."));
                ItemState.Foreground = (Brush)FindResource("Bad");
            }
            else if (i.Source == SourceType.Bundled)
            {
                ItemState.Text = row.Installed ? UiText.T("✓ Ships with Ashita v4") : UiText.T("Ships with Ashita v4, but the file is missing. Run Repair.");
                ItemState.Foreground = (Brush)FindResource(row.Installed ? "Ok" : "Warn");
            }
            else if (row.Installed)
            {
                var stale = !string.IsNullOrEmpty(i.Version) && !string.Equals(row.InstalledVersion, i.Version, StringComparison.OrdinalIgnoreCase);
                ItemState.Text = stale ? UiText.F("Installed {0} · {1} available", row.InstalledVersion ?? UiText.T("(unknown)"), i.Version) : UiText.F("✓ Installed {0}", row.InstalledVersion ?? i.Version ?? "");
                ItemState.Foreground = (Brush)FindResource(stale ? "Warn" : "Ok");
            }
            else
            {
                ItemState.Text = UiText.T("Not installed");
                ItemState.Foreground = (Brush)FindResource("Mist");
            }
            ItemState.Text += string.IsNullOrEmpty(i.Load) ? "" : UiText.T("\nLoads as ") + (i.LoadKind == LoadKind.PolPlugin ? UiText.T("POL plugin ") + i.LoadName + UiText.T(" (boot ini)") : i.Load);
            if (row.BlockedBy != null)
            {
                ItemState.Text += UiText.T("\nUnticked and greyed out while ") + row.BlockedBy.Name + UiText.T(" is ticked: ") + row.BlockedBy.Name + UiText.T(" does its job, and the two cannot load together.");
                ItemState.Foreground = (Brush)FindResource("Warn");
            }
            if (!string.IsNullOrEmpty(i.HeldBack))
            {
                // shown ticked or not, so the reason is read before the box is
                ItemState.Text += UiText.T("\nNever written to the startup script: ") + i.HeldBack + (string.IsNullOrEmpty(i.Replacement) ? "" : UiText.T(" Tick ") + i.Replacement + UiText.T(" instead."));
                ItemState.Foreground = (Brush)FindResource("Bad");
            }
            ItemV3.Text = i.V3Note != null ? UiText.T("From v3: ") + i.V3Note : i.V3Name != null ? UiText.T("From v3: ") + i.V3Name + (i.V3Carry != null ? ", " + i.V3Carry + UiText.T(" carried over") : "") : "";
            InstallButton.Visibility = i.HasV4 && i.Source != SourceType.Bundled ? Visibility.Visible : Visibility.Collapsed;
            InstallButton.Content = !row.Installed ? UiText.T("Install") : (!string.IsNullOrEmpty(i.Version) && !string.Equals(row.InstalledVersion, i.Version, StringComparison.OrdinalIgnoreCase)) ? UiText.T("Update to ") + i.Version : UiText.T("Reinstall");
            DocsButton.Visibility = string.IsNullOrEmpty(i.Docs) ? Visibility.Collapsed : Visibility.Visible;
            ConfigButton.Visibility = string.IsNullOrEmpty(i.Config) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Row_Click(object sender, MouseButtonEventArgs e)
        {
            var row = ((FrameworkElement)sender).Tag as AddonRow;
            if (row != null) Select(row);
        }

        private void Check_Click(object sender, RoutedEventArgs e)
        {
            var row = ((FrameworkElement)sender).Tag as AddonRow;
            UpdateBlocked();
            if (row != null) Select(row);
        }

        /// <summary>An item that gives way to a ticked one (catalogue "conflicts") is unticked and greyed out;
        /// ticking vanagear unticks LuAshitacast, unticking vanagear lets it be ticked again.</summary>
        private void UpdateBlocked()
        {
            var catalog = App.State.Catalog;
            var ticked = TickedIds();
            foreach (var r in _rows.Where(r => !r.IsSwitcher))
            {
                var by = catalog.BlockedBy(r.Item, ticked.Where(id => !string.Equals(id, r.Item.Id, StringComparison.OrdinalIgnoreCase)));
                r.BlockedBy = by;
                if (by != null && r.Enabled) r.Enabled = false;
            }
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || _busy) return;
            var item = _current.Item;
            var state = App.State;
            if (!state.HasAshita) { ProgressText.Visibility = Visibility.Visible; ProgressText.Text = UiText.T("Set the Ashita folder on Settings first."); return; }
            _busy = true; InstallButton.IsEnabled = false;
            Progress.Visibility = ProgressText.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true; ProgressText.Text = UiText.T("Looking up ") + item.Name + "…";
            string syncNote = null;
            try
            {
                var progress = new Progress<DownloadProgress>(p => { Progress.IsIndeterminate = false; Progress.Value = p.Fraction * 100; ProgressText.Text = UiText.F("{0}: {1:0.0} of {2:0.0} MB", p.Label, p.Done / 1048576.0, p.Total / 1048576.0); });
                if (item.Source == SourceType.GithubRelease)
                {
                    await AddonInstaller.InstallReleaseAsync(state.Downloader, state.Settings, state.AshitaRoot, item, progress, text => ProgressText.Text = text);
                }
                else if (item.Source == SourceType.RepoFolder)
                {
                    // only what is missing or changed comes down; what the repo dropped goes (RepoSync)
                    var plan = await AddonInstaller.InstallRepoFolderAsync(state.Downloader, state.Settings, state.AshitaRoot, item,
                        (name, n, of) => Dispatcher.Invoke(() =>
                        {
                            ProgressText.Text = UiText.F("{0} ({1} of {2})", name, n, of);
                            Progress.IsIndeterminate = false; Progress.Value = 100.0 * n / of;
                        }), state.FfxiFolder);
                    syncNote = UiText.F("{0} downloaded, {1} already here", plan.Download.Count, plan.Keep.Count) + (plan.Delete.Count > 0 ? UiText.F(", {0} removed", plan.Delete.Count) : "");
                }
                // enabled, saved and applied in one motion, so Play right after Install loads it
                _current.Enabled = true;
                if (!state.Settings.EnabledAddons.Contains(item.Id, StringComparer.OrdinalIgnoreCase)) state.Settings.EnabledAddons.Add(item.Id);
                state.Settings.Save();
                state.ApplyEnabledAddons();
                Log.Info("installed " + item.Id);
                var pivot = item.Install == InstallAction.PivotOverlay ? state.Catalog.Find("pivot") : null;
                ProgressText.Text = pivot != null && !pivot.IsInstalled(state.AshitaRoot) ? UiText.T("Installed. It needs XIPivot to show in game: install that too, then Save.") : syncNote != null ? UiText.T("Installed: ") + syncNote + "." : UiText.T("Installed.");
                Build();
            }
            catch (Exception ex)
            {
                Log.Error("install " + item.Id, ex);
                ProgressText.Text = ex.Message;
            }
            finally
            {
                _busy = false; InstallButton.IsEnabled = true; Progress.Visibility = Visibility.Collapsed; Progress.IsIndeterminate = false;
            }
        }

        private void Docs_Click(object sender, RoutedEventArgs e) { if (_current?.Item.Docs != null) MenuPage.OpenUrl(_current.Item.Docs); }

        private void Config_Click(object sender, RoutedEventArgs e)
        {
            if (_current?.Item.Config == null) return;
            var path = CatalogItem.ResolveConfigPath(App.State.AshitaRoot, _current.Item.Config, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            try { Directory.CreateDirectory(dir); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); } catch (Exception ex) { Log.Warn(ex.Message); }
        }

        private List<string> TickedIds() => _rows.Where(r => r.Enabled && r.HasV4 && !r.IsSwitcher).Select(r => r.Item.Id).ToList();

        /// <summary>The window switcher's row: not in the catalogue and never in the startup script, it runs in the launcher.</summary>
        private static readonly CatalogItem SwitcherItem = new CatalogItem
        {
            Id = "vanadreams-window-switcher",
            Name = UiText.T("Window switcher"),
            Kind = "launcher",
            Source = SourceType.Bundled,
            Description = UiText.T("For playing more than one character: a key or a controller press brings the next, the previous or a numbered game window to the front, and saved layouts put the windows back where they go."),
        };

        private void Switcher_Click(object sender, RoutedEventArgs e) => _win.Navigate(new SwitcherPage(_win));

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var state = App.State;
            state.Settings.EnabledAddons = TickedIds();
            var switcher = _rows.FirstOrDefault(r => r.IsSwitcher);
            if (switcher != null) state.Settings.SwitcherOn = switcher.Enabled;
            state.Settings.Save();
            App.Switcher?.Apply();
            try
            {
                state.ApplyEnabledAddons();
                var never = _rows.Where(r => r.Enabled && r.HasV4 && !string.IsNullOrEmpty(r.Item.HeldBack)).Select(r => r.Item).ToList();
                var held = _rows.Where(r => r.Enabled && r.HasV4 && string.IsNullOrEmpty(r.Item.HeldBack)).Select(r => new { r.Item, By = state.Catalog.BlockedBy(r.Item, state.Settings.EnabledAddons) }).Where(x => x.By != null).ToList();
                Footer.Text = UiText.T("Saved: scripts\\vanadreams.txt written for ") + (state.Settings.EnabledAddons.Count - held.Count - never.Count) + UiText.T(" item(s).")
                    + string.Concat(held.Select(x => " " + x.Item.Name + UiText.T(" left out while ") + x.By.Name + UiText.T(" is ticked.")))
                    + string.Concat(never.Select(x => " " + x.Name + UiText.T(" is never loaded here") + (string.IsNullOrEmpty(x.Replacement) ? "." : UiText.T("; tick ") + x.Replacement + UiText.T(" instead."))));
                Footer.Foreground = (Brush)FindResource("Ok");
            }
            catch (Exception ex)
            {
                Log.Error("save script", ex);
                Footer.Text = ex.Message; Footer.Foreground = (Brush)FindResource("Bad");
            }
            state.Notify();
        }

        private void Windower_Click(object sender, RoutedEventArgs e) => _win.Navigate(new WindowerPage(_win));

    }
}
