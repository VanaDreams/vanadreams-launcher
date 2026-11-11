using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    /// <summary>
    /// The profile's game window, saved as it is picked: the window mode and size are the profile's own [ffxi.registry]
    /// values (the ones the Profiles page used to edit), the display and frame rate are kept with it (Services\Graphics.cs).
    /// </summary>
    public partial class WindowPanel : UserControl
    {
        private Profile _profile;
        private bool _filling;

        private static readonly Tuple<WindowMode, string>[] ModeChoices =
            { Tuple.Create(WindowMode.Windowed, UiText.FromKey("window-mode")), Tuple.Create(WindowMode.Borderless, UiText.T("Borderless")), Tuple.Create(WindowMode.Fullscreen, UiText.T("Fullscreen")) };
        private static readonly int[][] SizeChoices = { new[] { 1280, 720 }, new[] { 1600, 900 }, new[] { 1920, 1080 }, new[] { 2560, 1440 } };

        public WindowPanel()
        {
            InitializeComponent();
        }

        public void Show(Profile p)
        {
            _profile = p;
            IsEnabled = p != null && !p.IsExample;
            Draw();
        }

        public void Draw()
        {
            var p = _profile;
            Modes.Children.Clear();
            foreach (var m in ModeChoices) Modes.Children.Add(Chip(m.Item2, p != null && p.Mode == m.Item1, (s, e) => Set(x => x.Mode = m.Item1)));

            Sizes.Children.Clear();
            // the chosen display's own size first: the right one for borderless and fullscreen
            var monitors = Graphics.Monitors();
            var screen = monitors.FirstOrDefault(m => p != null && string.Equals(m.Device, p.Monitor, StringComparison.OrdinalIgnoreCase)) ?? monitors.FirstOrDefault(m => m.Primary);
            // then every other attached display's size, so the right one is there whichever screen the game goes on
            var sizes = SizeChoices.ToList();
            // inserted at the front one by one, the chosen display last so it ends up first
            foreach (var d in monitors.Where(m => m != screen).Concat(screen == null ? new MonitorInfo[0] : new[] { screen }))
            {
                sizes.RemoveAll(s => s[0] == d.Width && s[1] == d.Height);
                sizes.Insert(0, new[] { d.Width, d.Height });
            }
            // a size the player typed on an older launcher stays offered, so picking nothing never changes it
            if (p != null && p.Width > 0 && p.Height > 0 && !sizes.Any(s => s[0] == p.Width && s[1] == p.Height)) sizes.Add(new[] { p.Width, p.Height });
            foreach (var s in sizes)
            {
                var w = s[0]; var h = s[1];
                Sizes.Children.Add(Chip(w + "×" + h, p != null && p.Width == w && p.Height == h, (o, e) => Set(x => Resize(x, w, h))));
            }

            _filling = true;
            MonitorBox.Items.Clear();
            foreach (var m in Graphics.Monitors()) MonitorBox.Items.Add(m);
            var at = MonitorBox.Items.Cast<MonitorInfo>().ToList().FindIndex(m => p != null && string.Equals(m.Device, p.Monitor, StringComparison.OrdinalIgnoreCase));
            MonitorBox.SelectedIndex = at >= 0 ? at : (MonitorBox.Items.Count > 0 ? 0 : -1);
            _filling = false;

            Rates.Children.Clear();
            foreach (var f in Graphics.FpsChoices) Rates.Children.Add(Chip(f.ToString(), p != null && (p.Fps == f || (p.Fps <= 0 && f == 30)), (o, e) => Set(x => x.Fps = f)));
        }

        /// <summary>
        /// The menu keeps its scale (Profiles page, Menu scale) as the window changes size, so a bigger window never
        /// shrinks the menus. A menu never set gets the default scale for the new window.
        /// </summary>
        private static void Resize(Profile p, int w, int h)
        {
            var scale = p.MenuWidth > 0 && p.Width > 0 ? p.MenuScale : Profile.DefaultMenuScale(h);
            p.Width = w; p.Height = h;
            p.SetMenuScale(scale);
        }

        private Button Chip(string text, bool on, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Style = (Style)FindResource(on ? "ChipOn" : "Chip") };
            b.Click += click;
            return b;
        }

        private void Set(Action<Profile> change)
        {
            if (_profile == null || _profile.IsExample) return;
            change(_profile);
            _profile.Save();
            Draw();
        }

        private void Monitor_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_filling || _profile == null) return;
            var m = MonitorBox.SelectedItem as MonitorInfo;
            // the main display is stored as nothing, so a profile moved to another PC opens on that PC's main one
            Set(x =>
            {
                x.Monitor = m == null || m.Primary ? "" : m.Device;
                // borderless and fullscreen fill the display they are moved to
                if (m != null && (x.Mode == WindowMode.Borderless || x.Mode == WindowMode.Fullscreen)) Resize(x, m.Width, m.Height);
            });
        }
    }
}
