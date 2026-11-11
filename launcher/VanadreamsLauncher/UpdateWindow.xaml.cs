using System;
using System.Threading.Tasks;
using System.Windows;
using Vanadreams.Services;

namespace Vanadreams
{
    /// <summary>
    /// The update, put in front of the player when the launcher starts: a release newer than this
    /// build was found and is downloading behind this window. "Update and restart" lights once the new
    /// exe is signed-checked and swapped in; "Later" (or Esc, or the X) carries on with this build.
    /// The swap happens either way, so a player who says Later still starts the new one next time.
    /// </summary>
    public partial class UpdateWindow : Window
    {
        private readonly Task<string> _ready;

        public UpdateWindow(string tag, Version current, Task<string> ready, Progress<DownloadProgress> progress)
        {
            InitializeComponent();
            _ready = ready;
            Wording(tag, current);
            progress.ProgressChanged += (s, p) =>
            {
                Bar.Value = p.Fraction;
                BarLine.Text = UiText.T("Moogling in progress… ") +(p.Done / 1048576.0).ToString("0.0") + UiText.T(" of ") + (p.Total / 1048576.0).ToString("0.0") + " MB";
            };
            Loaded += async (s, e) => await WaitForDownloadAsync(tag);
        }

        /// <summary>The words the player reads: Heading is the big line, Body the sentence under it.</summary>
        private void Wording(string tag, Version current)
        {
            Heading.Text = UiText.T("A new launcher is here, kupo!");
            Body.Text = UiText.T("Version ") + tag.TrimStart('v') + UiText.T(" is on its way (you have ") + current + "). "
                      + UiText.T("A moogle is fetching it now. Restart when it's ready to stay in step with the server.");
        }

        private async Task WaitForDownloadAsync(string tag)
        {
            string exe = null;
            try { exe = await _ready; }
            catch (Exception ex) { Log.Warn("update window: " + ex.Message); }
            if (exe == null)
            {
                Bar.Visibility = Visibility.Collapsed;
                BarLine.Text = UiText.T("Launcher ") + tag + UiText.T(" couldn't be downloaded. It is on vanadreams.org.");
                UpdateButton.Visibility = Visibility.Collapsed;
                LaterButton.Content = UiText.T("Close");
                return;
            }
            Bar.Value = 1;
            BarLine.Text = UiText.T("Delivered, kupo! Ready when you are.");
            UpdateButton.IsEnabled = true;
            UpdateButton.Focus();
        }

        private void Update_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
