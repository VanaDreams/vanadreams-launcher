using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Vanadreams.Services
{
    /// <summary>
    /// Starts Ashita for a profile. When a login is remembered it travels in a temporary copy of the
    /// profile, named per launch, which is removed as soon as Ashita has exited or, failing that, on a
    /// ceiling timer, on launcher exit, and by the sweep at the next start. Nothing with a password is
    /// ever left behind on purpose.
    /// </summary>
    public static class GameLauncher
    {
        private const string TempPrefix = ".launch-";

        public static Process Launch(string ashitaRoot, Profile profile, Credential credential, bool asAdmin, string gameRoot = null)
        {
            var cli = Path.Combine(ashitaRoot, "Ashita-cli.exe");
            if (!File.Exists(cli)) throw new FileNotFoundException("Ashita-cli.exe is missing from " + ashitaRoot + "; run Repair.", cli);
            // Ashita-cli closes in a blink, with nothing on screen, when the profile's boot file is not there
            var boot = string.IsNullOrWhiteSpace(profile.BootFile) ? "" : Path.Combine(ashitaRoot, profile.BootFile);
            if (!File.Exists(boot))
                throw new FileNotFoundException("This profile starts " + (boot.Length > 0 ? boot : "no loader at all") +
                    ", and that file is not there. Edit the profile and browse to the loader, or run Repair.", boot);

            var iniName = profile.FileName;
            string temp = null;
            var savedLogin = credential != null && (!string.IsNullOrEmpty(credential.User) || !string.IsNullOrEmpty(credential.Password));
            var mayHaveChapter = profile.IsVanadreams && !string.IsNullOrWhiteSpace(gameRoot);
            var ini = savedLogin || mayHaveChapter ? IniFile.Load(profile.Path) : null;
            var chapter = false;
            if (mayHaveChapter)
            {
                if (ClientInstall.IntegratedInstallIncomplete(gameRoot))
                    throw new InvalidOperationException("A VanaDreams file update was interrupted. Finish Update Files before starting this copy.");
                var game = ClientInstall.GameFolder(gameRoot);
                if (ClientInstall.HasIntegratedTables(game))
                    chapter = ChapterOnePatch.ConfigureIntegratedLaunch(ashitaRoot, ini);
                else
                    chapter = ChapterOnePatch.ConfigureLaunch(ashitaRoot, game, ini);
            }
            if (savedLogin || chapter)
            {
                var launchCmd = new LoaderCommand
                {
                    Server = profile.Command.Server, Hairpin = profile.Command.Hairpin, Extra = profile.Command.Extra,
                    User = credential?.User, Password = credential?.Password
                };
                temp = Path.Combine(Path.GetDirectoryName(profile.Path), TempPrefix + profile.Id + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ini");
                if (savedLogin) ini.Set("ashita.boot", "command", launchCmd.ToLaunchCommand());
                ini.Save(temp);
                iniName = Path.GetFileName(temp);
            }

            Process process;
            try { process = asAdmin ? StartElevated(cli, iniName, ashitaRoot) : StartAsPlayer(cli, iniName, ashitaRoot); }
            catch (Exception ex)
            {
                Remove(temp);
                var w32 = ex as System.ComponentModel.Win32Exception;
                if (w32 != null && w32.NativeErrorCode == 1223)
                    throw new InvalidOperationException("Windows asked to run Ashita as administrator and the prompt was cancelled. Press Play again and choose Yes.");
                throw;
            }
            Log.Info("Launched " + profile.Name + " via " + iniName + ", booting " + boot + (asAdmin ? ", as administrator" : ", as the player"));

            if (temp != null)
            {
                var toDelete = temp;
                // Ashita reads the ini in its first moments; gone the instant it exits, or after a minute regardless
                if (process != null)
                {
                    try { process.EnableRaisingEvents = true; process.Exited += (s, e) => Remove(toDelete); }
                    catch (Exception) { /* no handle to the elevated process: the ceiling below covers it */ }
                }
                Task.Delay(TimeSpan.FromSeconds(60)).ContinueWith(_ => Remove(toDelete));
            }
            return process;
        }

        /// <summary>
        /// Ashita-cli.exe asks for administrator in its manifest, but Ashita v4 needs none of it: it reads the registry
        /// and hands the game its [ffxi.registry] values through a hook, writing nothing outside its own folder.
        /// Windows' RunAsInvoker layer starts it as the player, so no permission prompt appears.
        /// 28 Sept 2026: a player's second client closed at that prompt every time; only switching UAC off let him dual-box.
        /// The prompt switches to Windows' secure desktop, which can take the display from a game already running.
        /// The game and xiloader inherit the layer, so nothing they start prompts either.
        /// </summary>
        private static Process StartAsPlayer(string cli, string iniName, string ashitaRoot)
        {
            var psi = new ProcessStartInfo(cli, "\"" + iniName + "\"") { WorkingDirectory = ashitaRoot, UseShellExecute = false };
            psi.EnvironmentVariables["__COMPAT_LAYER"] = "RunAsInvoker";
            try { return Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 740)
            {
                // ERROR_ELEVATION_REQUIRED: this Windows would not apply the layer, so ask the way it used to
                Log.Warn("Ashita would not start as the player (" + ex.Message + "); asking for administrator");
                return StartElevated(cli, iniName, ashitaRoot);
            }
        }

        /// <summary>Through the shell, so Windows raises the UAC prompt; a direct start fails with "requires elevation" instead.</summary>
        private static Process StartElevated(string cli, string iniName, string ashitaRoot) =>
            Process.Start(new ProcessStartInfo(cli, "\"" + iniName + "\"") { WorkingDirectory = ashitaRoot, UseShellExecute = true });

        private static void Remove(string path)
        {
            if (path == null) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Log.Warn("temp profile not removed: " + ex.Message); }
        }

        /// <summary>Any launch copies still on disk are removed: at startup, and again when the launcher closes.</summary>
        public static void Sweep(string ashitaRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(ashitaRoot)) return;
                var boot = Path.Combine(ashitaRoot, "config", "boot");
                if (!Directory.Exists(boot)) return;
                foreach (var f in Directory.GetFiles(boot, TempPrefix + "*.ini")) Remove(f);
            }
            catch (Exception) { }
        }
    }
}
