using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Vanadreams.Services;
using KeyBinding = Vanadreams.Services.KeyBinding;

namespace Vanadreams.Pages
{
    public sealed class SlotRow
    {
        public int Index { get; set; }
        public string Number => (Index + 1).ToString();
        public GameWindow Window { get; set; }
        public string Label { get; set; }
        public string Status { get; set; }
        public string StatusTip { get; set; }
        public Brush StatusBrush { get; set; }
        public string KeyText { get; set; }
        public Visibility KeyVisibility { get; set; }
        public bool HasWindow => Window != null;
        public bool CanUp { get; set; }
        public bool CanDown { get; set; }
        public double Opacity => Window == null ? 0.45 : 1.0;
    }

    /// <summary>The window switcher's page: the game windows in slot order, their keys, the layouts, and the switcher's own keys.</summary>
    public partial class SwitcherPage : UserControl
    {
        private readonly MainWindow _win;
        private readonly DispatcherTimer _look = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly Action _onChanged;
        private string _capturing;   // "next", "prev" or "slot<n>" while waiting for a key
        private bool _loading;

        private static LauncherSettings S => App.State.Settings;
        private static WindowSwitcher Switcher => App.Switcher;

        public SwitcherPage(MainWindow win)
        {
            InitializeComponent();
            _win = win;
            _loading = true;
            OnBox.IsChecked = S.SwitcherOn;
            AnywhereBox.IsChecked = S.SwitchKeysAnywhere;
            AutoLayoutBox.IsChecked = S.AutoLayout;
            foreach (var box in new[] { PadNextBox, PadPrevBox })
                foreach (var c in PadBinding.Choices) box.Items.Add(new ComboBoxItem { Content = PadBinding.Words(c), Tag = c });
            SelectPad(PadNextBox, S.PadNext);
            SelectPad(PadPrevBox, S.PadPrev);
            LoadLayouts();
            _loading = false;
            ShowKeys();
            Refresh();
            PreviewKeyDown += OnPreviewKeyDown;
            _onChanged = () => Dispatcher.BeginInvoke(new Action(Refresh));
            // while the switcher is off it does not look for windows by itself, so the page asks it to
            _look.Tick += (s, e) => { if (Switcher != null && !Switcher.Running) Switcher.Scan(); };
            Loaded += (s, e) => { if (Switcher != null) Switcher.Changed += _onChanged; _look.Start(); };
            Unloaded += (s, e) => { if (Switcher != null) { Switcher.Changed -= _onChanged; Switcher.Paused = false; } _look.Stop(); };
        }

        private static void SelectPad(ComboBox box, string value)
        {
            var i = Array.FindIndex(PadBinding.Choices, c => string.Equals(c, value ?? "", StringComparison.OrdinalIgnoreCase));
            box.SelectedIndex = i < 0 ? 0 : i;
        }

        private void Refresh()
        {
            var windows = Switcher?.Windows ?? new List<GameWindow>();
            var rows = new List<SlotRow>();
            var count = Math.Max(LauncherSettings.SlotKeyCount, windows.Count);
            for (var i = 0; i < count; i++)
            {
                var w = i < windows.Count ? windows[i] : null;
                var row = new SlotRow
                {
                    Index = i,
                    Window = w,
                    Label = w == null ? "empty" : w.Label(i),
                    CanUp = w != null && i > 0,
                    CanDown = w != null && i < windows.Count - 1,
                    KeyVisibility = i < LauncherSettings.SlotKeyCount ? Visibility.Visible : Visibility.Hidden,
                    KeyText = i < LauncherSettings.SlotKeyCount ? (_capturing == "slot" + i ? UiText.T("press a key…") : UiText.T("Key: ") + KeyBinding.Words(S.SwitchSlots[i])) : "",
                };
                if (w != null && w.Elevated)
                {
                    row.Status = "administrator";
                    row.StatusTip = UiText.T("This game was started as administrator, so the launcher cannot bring it forward. Start it with Play here, or untick Run as administrator on its profile.");
                    row.StatusBrush = (Brush)FindResource("Warn");
                }
                rows.Add(row);
            }
            Slots.ItemsSource = rows;
            Heading.Text = windows.Count == 0 ? UiText.T("✦ Game windows · none open") : UiText.T("✦ Game windows · ") + windows.Count + UiText.T(" open");
            ShowNote();
        }

        private void ShowNote()
        {
            if (Switcher?.LastProblem != null) { Note.Text = Switcher.LastProblem; Note.Foreground = (Brush)FindResource("Warn"); return; }
            Note.ClearValue(TextBlock.ForegroundProperty);
            if (!S.SwitcherOn) { Note.Text = UiText.T("Tick On to use the keys. Windows show here as soon as each game opens; names appear once the character is in."); return; }
            Note.Text = UiText.T("In game, ") + KeyBinding.Words(S.SwitchNext) + UiText.T(" brings the next character forward and ") + KeyBinding.Words(S.SwitchPrev) +
                        UiText.T(" the one before; the keys never reach the game. The game also sees controller presses, so pick ones your pad layout leaves free.") +
                        UiText.T(" Closing the launcher keeps it in the tray while a game is open.");
        }

        private void ShowKeys()
        {
            NextKey.Content = _capturing == "next" ? UiText.T("press a key…") : KeyBinding.Words(S.SwitchNext);
            PrevKey.Content = _capturing == "prev" ? UiText.T("press a key…") : KeyBinding.Words(S.SwitchPrev);
        }

        // ---- keys ----

        private void Key_Click(object sender, RoutedEventArgs e) => StartCapture((string)((Button)sender).Tag);

        private void SlotKey_Click(object sender, RoutedEventArgs e)
        {
            var row = ((FrameworkElement)sender).Tag as SlotRow;
            if (row != null) StartCapture("slot" + row.Index);
        }

        private void StartCapture(string what)
        {
            _capturing = what;
            if (Switcher != null) Switcher.Paused = true;   // a key already bound must reach this page, not switch windows
            ShowKeys();
            Refresh();
            Focus();
            Keyboard.Focus(this);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_capturing == null) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return;   // wait for the key itself
            e.Handled = true;
            string text;
            if (key == Key.Escape) text = "";
            else
            {
                var b = new KeyBinding
                {
                    Key = (System.Windows.Forms.Keys)KeyInterop.VirtualKeyFromKey(key),
                    Ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0,
                    Alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0,
                    Shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0,
                };
                text = b.ToString();
            }
            if (_capturing == "next") S.SwitchNext = text;
            else if (_capturing == "prev") S.SwitchPrev = text;
            else if (_capturing.StartsWith("slot")) S.SwitchSlots[int.Parse(_capturing.Substring(4))] = text;
            // one key does one thing: the same key taken off anything else it was on
            if (text.Length > 0)
            {
                if (_capturing != "next" && S.SwitchNext == text) S.SwitchNext = "";
                if (_capturing != "prev" && S.SwitchPrev == text) S.SwitchPrev = "";
                for (var i = 0; i < S.SwitchSlots.Count; i++) if (_capturing != "slot" + i && S.SwitchSlots[i] == text) S.SwitchSlots[i] = "";
            }
            _capturing = null;
            if (Switcher != null) Switcher.Paused = false;
            S.Save();
            Switcher?.Apply();
            ShowKeys();
            Refresh();
        }

        private void Pad_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            S.PadNext = (string)((ComboBoxItem)PadNextBox.SelectedItem)?.Tag ?? "";
            S.PadPrev = (string)((ComboBoxItem)PadPrevBox.SelectedItem)?.Tag ?? "";
            if (S.PadNext.Length > 0 && S.PadNext == S.PadPrev) { S.PadPrev = ""; _loading = true; PadPrevBox.SelectedIndex = 0; _loading = false; }
            S.Save();
            Switcher?.Apply();
        }

        private void On_Click(object sender, RoutedEventArgs e)
        {
            S.SwitcherOn = OnBox.IsChecked == true;
            S.Save();
            Switcher?.Apply();
            Log.Info("window switcher " + (S.SwitcherOn ? "on" : "off"));
            Refresh();
        }

        private void Anywhere_Click(object sender, RoutedEventArgs e)
        {
            S.SwitchKeysAnywhere = AnywhereBox.IsChecked == true;
            S.Save();
        }

        // ---- slots ----

        private void Up_Click(object sender, RoutedEventArgs e) { var r = ((FrameworkElement)sender).Tag as SlotRow; if (r != null) Switcher?.Move(r.Index, -1); }
        private void Down_Click(object sender, RoutedEventArgs e) { var r = ((FrameworkElement)sender).Tag as SlotRow; if (r != null) Switcher?.Move(r.Index, 1); }
        private void Show_Click(object sender, RoutedEventArgs e) { var r = ((FrameworkElement)sender).Tag as SlotRow; if (r != null) Switcher?.FocusSlot(r.Index); }

        // ---- layouts ----

        private void LoadLayouts()
        {
            LayoutBox.Items.Clear();
            foreach (var name in S.Layouts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)) LayoutBox.Items.Add(name);
            LayoutBox.SelectedItem = S.Layouts.ContainsKey(S.ActiveLayout ?? "") ? S.Layouts.Keys.First(k => string.Equals(k, S.ActiveLayout, StringComparison.OrdinalIgnoreCase)) : null;
            if (LayoutBox.SelectedItem == null && LayoutBox.Items.Count > 0) LayoutBox.SelectedIndex = 0;
            LayoutName.Text = LayoutBox.SelectedItem as string ?? UiText.T("Full party");
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = LayoutName.Text.Trim();
            if (name.Length == 0) { LayoutNote.Text = UiText.T("Give the layout a name first."); return; }
            Switcher?.Scan();
            if (Switcher == null || Switcher.Windows.Count == 0) { LayoutNote.Text = UiText.T("No game window is open to save."); return; }
            var n = Switcher.SaveLayout(name);
            LoadLayouts();
            LayoutNote.Text = UiText.T("Saved ") + name + ": " + n + UiText.T(" window") + (n == 1 || UiText.Language == "ja" ? "" : "s") + ".";
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            var name = LayoutBox.SelectedItem as string;
            if (name == null || Switcher == null) { LayoutNote.Text = UiText.T("No layout saved yet."); return; }
            var n = Switcher.RestoreLayout(name);
            LayoutNote.Text = n == 0 ? UiText.T("None of the open windows is in ") + name + "." : UiText.T("Put ") + n + UiText.T(" window") + (n == 1 || UiText.Language == "ja" ? "" : "s") + UiText.T(" back as in ") + name + ".";
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var name = LayoutBox.SelectedItem as string;
            if (name == null) return;
            S.Layouts.Remove(name);
            if (string.Equals(S.ActiveLayout, name, StringComparison.OrdinalIgnoreCase)) S.ActiveLayout = "";
            S.Save();
            LoadLayouts();
            LayoutNote.Text = UiText.T("Deleted ") + name + ".";
        }

        private void AutoLayout_Click(object sender, RoutedEventArgs e)
        {
            S.AutoLayout = AutoLayoutBox.IsChecked == true;
            if (S.AutoLayout && LayoutBox.SelectedItem is string name) S.ActiveLayout = name;
            S.Save();
            LayoutNote.Text = S.AutoLayout ? (string.IsNullOrEmpty(S.ActiveLayout) ? UiText.T("Save a layout first; its places are used as characters log in.") : UiText.T("Characters go to their places in ") + S.ActiveLayout + UiText.T(" as they log in.")) : "";
        }

        // opened from its row on the Addons page, so Back goes there
        private void Back_Click(object sender, RoutedEventArgs e) => _win.Navigate(new AddonsPage(_win));
    }
}
