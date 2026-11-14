using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    /// <summary>Opens authenticated ticket submission; diagnostics export is a separate, local-only action.</summary>
    public partial class BugReportPanel : UserControl
    {
        public BugReportPanel()
        {
            InitializeComponent();
        }

        private void Join_Click(object sender, RoutedEventArgs e) => MenuPage.OpenUrl(MenuPage.DiscordInvite);

        /// <summary>The checks.txt of the report: what this PC shows now. Never throws; a check that fails says so.</summary>
        private static async Task<string> Checks(AppState state)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var ashita = state.HasAshita ? state.AshitaRoot : null;
                var root = state.Settings.GameInstallRoot;
                sb.AppendLine("Launcher game folder setting: " + (string.IsNullOrWhiteSpace(root) ? "blank" : root));
                sb.AppendLine("Ashita folder: " + (ashita ?? "not set"));
                sb.AppendLine("Loader: " + (ashita == null ? "-" : Loader.Describe(Loader.Installed(ashita))) + " (the server takes " + Loader.RequiredMajor + "." + Loader.RequiredMinor + ")");
                sb.AppendLine();
                sb.AppendLine("-- game --");
                var findings = await Task.Run(() => LaunchDiagnosis.Gather(GameRegistration.HasGame(root) ? root : null, ashita, 1, "xiloader"));
                foreach (var f in findings) sb.AppendLine(f.ToString());
                sb.AppendLine();
                sb.AppendLine("-- antivirus --");
                var av = await Task.Run(() => PlayCheck.Antivirus());
                sb.AppendLine(av.Count == 0 ? "none reported by Windows Security Center" : string.Join(", ", av));
                if (ashita != null)
                {
                    sb.AppendLine("Files antivirus takes, missing now: " + string.Join(", ", PlayCheck.Missing(ashita, @"bootloader\xiloader.exe").Select(Path.GetFileName).DefaultIfEmpty("none")));
                    sb.AppendLine();
                    sb.AppendLine("-- firewall (inbound allow rules) --");
                    var exes = new[] { Path.Combine(ashita, "Ashita-cli.exe"), Loader.PathIn(ashita) }.Where(File.Exists).ToList();
                    var fw = await Task.Run(() => Firewall.Check(exes));
                    foreach (var kv in fw) sb.AppendLine(kv.Key + ": " + (kv.Value ? "allowed" : "no allow rule (outgoing logins still work; only matters if the firewall blocks outgoing)"));
                }
                sb.AppendLine();
                sb.AppendLine("-- the server's login ports from this PC --");
                var reach = await PlayCheck.TryPortsAsync(LoaderCommand.VanadreamsServer, PlayCheck.LoginPorts, TimeSpan.FromSeconds(4));
                sb.AppendLine(reach.NoName ? LoaderCommand.VanadreamsServer + " did not resolve" : "open " + string.Join(",", reach.Open) + "; closed " + (reach.Closed.Count == 0 ? "none" : string.Join(",", reach.Closed)));
                sb.AppendLine();
                sb.AppendLine("-- Windows Application log, last 7 days (crashes, hangs, .NET errors) --");
                var events = await Task.Run(() => LaunchDiagnosis.RecentAppEvents(new[] { "xiloader", "pol", "Ashita-cli", "ffxi-boot", "VanadreamsLauncher" }, TimeSpan.FromDays(7), 20));
                sb.AppendLine(events.Count == 0 ? "none" : string.Join(Environment.NewLine, events));
            }
            catch (Exception ex) { sb.AppendLine("checks stopped early: " + ex.Message); }
            return sb.ToString();
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = BugReportSubmission.FormUrl(Updater.Normalise(Updater.Current));
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                Result.Foreground = (Brush)FindResource("Mist");
                Result.Text = UiText.T("Complete the report in your browser and press Submit. The form will show your ticket link after it is created. No logs have been uploaded.");
                JoinLine.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log.Warn("open report form: " + ex.Message);
                Result.Foreground = (Brush)FindResource("Bad");
                Result.Text = UiText.T("Could not open the report form. Visit https://vanadreams.org/report/ in your browser.");
            }
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var state = App.State;
            ExportButton.IsEnabled = false;
            Result.Foreground = (Brush)FindResource("Mist");
            Result.Text = UiText.T("Gathering logs…");
            try
            {
                var now = DateTime.Now;
                var zip = BugReport.DefaultPath(now);
                var dumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
                var version = Updater.Normalise(Updater.Current).ToString();
                Result.Text = UiText.T("Checking the game, firewall and antivirus…");
                var checks = await Checks(state);
                var names = await Task.Run(() => BugReport.Build(zip, LauncherSettings.DataFolder, state.HasAshita ? state.AshitaRoot : null, dumps, version, now, checks));
                Log.Info("bug report: " + zip + " with " + names.Count + " file(s)");
                Result.Foreground = (Brush)FindResource("Ok");
                Result.Text = UiText.F("Saved {0} on your Desktop. Nothing was uploaded. Review it before sharing with staff; crash dumps may contain private data.", Path.GetFileName(zip));
                try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + zip + "\"") { UseShellExecute = true }); } catch (Exception ex) { Log.Warn("show zip: " + ex.Message); }
                JoinLine.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log.Error("bug report", ex);
                Result.Foreground = (Brush)FindResource("Bad");
                Result.Text = ex.Message;
            }
            finally { ExportButton.IsEnabled = true; }
        }
    }
}
