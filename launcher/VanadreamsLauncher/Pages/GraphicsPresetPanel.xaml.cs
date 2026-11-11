using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    /// <summary>The three looks for one profile (Services\Graphics.cs). Picking one saves it to the profile at once.</summary>
    public partial class GraphicsPresetPanel : UserControl
    {
        private Profile _profile;

        /// <summary>A preset was picked: the window panel beside it shows the new frame rate.</summary>
        public event Action Changed;

        private static readonly Tuple<GfxPreset, string, string>[] Looks =
        {
            Tuple.Create(GfxPreset.Potato, UiText.T("Potato"), UiText.T("Lighting and sharpening off, 30 fps. Runs on anything.")),
            Tuple.Create(GfxPreset.Balanced, UiText.T("Balanced"), UiText.T("The modern look pack, 60 fps.")),
            Tuple.Create(GfxPreset.Pretty, UiText.T("Pretty"), UiText.T("The look pack with lighting and sharpening, 60 fps.")),
        };

        public GraphicsPresetPanel()
        {
            InitializeComponent();
        }

        public void Show(Profile p)
        {
            _profile = p;
            Heading.Text = UiText.T("✦ Graphics") + (p == null ? "" : " · " + p.Name);
            IsEnabled = p != null && !p.IsExample;
            Draw();
        }

        private void Draw()
        {
            Choices.Children.Clear();
            var picked = Graphics.Parse(_profile?.GfxPreset);
            foreach (var look in Looks)
            {
                var on = picked == look.Item1;
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = look.Item2, Style = (Style)FindResource("NavText"), FontSize = 18, Foreground = (Brush)FindResource(on ? "GoldSoft" : "Cream") });
                text.Children.Add(new TextBlock { Text = look.Item3, Style = (Style)FindResource("Muted"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                var b = new Button
                {
                    Content = text, Tag = look.Item1, Margin = new Thickness(0, 0, 0, 8), Cursor = System.Windows.Input.Cursors.Hand,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, Template = CardTemplate(on)
                };
                b.Click += Pick_Click;
                Choices.Children.Add(b);
            }
        }

        /// <summary>A card: a dark panel with a hairline, gold-edged while it is the profile's look.</summary>
        private ControlTemplate CardTemplate(bool on)
        {
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(on ? Color.FromArgb(0x40, 0xF1, 0xCD, 0x78) : Color.FromArgb(0x40, 0, 0, 0)));
            bd.SetValue(Border.BorderBrushProperty, on ? (Brush)FindResource("GoldSoft") : (Brush)FindResource("Line"));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(on ? 2 : 1));
            bd.SetValue(Border.PaddingProperty, new Thickness(12, 7, 12, 8));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            bd.AppendChild(cp);
            t.VisualTree = bd;
            return t;
        }

        private void Pick_Click(object sender, RoutedEventArgs e)
        {
            if (_profile == null) return;
            Graphics.Choose(_profile, (GfxPreset)((FrameworkElement)sender).Tag);
            _profile.Save();
            Log.Info("graphics: " + _profile.Id + " is " + _profile.GfxPreset + " at " + _profile.Fps + " fps");
            Draw();
            Changed?.Invoke();
        }
    }
}
