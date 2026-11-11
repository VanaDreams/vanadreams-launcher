using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;

namespace Vanadreams.Services
{
    /// <summary>Where in its run the loader stopped, read from its own window.</summary>
    public enum LoaderStop
    {
        /// <summary>Nothing was read from the loader's window.</summary>
        Unknown,
        /// <summary>The loader printed an error line of its own before closing.</summary>
        LoaderError,
        /// <summary>Logged in and loaded the profile, then the game quit as it started; xiloader prints nothing for that.</summary>
        GameQuit,
        /// <summary>The loader closed without an error and before the game was started.</summary>
        Other,
    }

    /// <summary>
    /// Why a launch closed before the game opened, as far as this PC can tell, and the facts behind it.
    ///
    /// xiloader 2.2 (LandSandBoat main.cpp) prints a red line for every step of its own that fails. When the window
    /// shows "Profile loaded." and then only "Closing...", every one of those steps passed: the login, the ports, the
    /// profile server. It then hands over to the game's FFXiMain.dll (IFFXiEntry::GameStart), and when that returns at
    /// once, without a crash, the loader prints "Closing..." and exits with success. No crash record, no dump, no log.
    /// So the cause is in what the game finds as it starts, and that is what is looked at here: which copy Windows
    /// starts, whether its files are there, the graphics wrappers loaded beside xiloader, and another game running.
    /// </summary>
    public static class LaunchDiagnosis
    {
        public const string ProfileLoaded = "Profile loaded.";
        public const string Closing = "Closing...";

        /// <summary>Where the loader stopped, and its own error lines when it printed any.</summary>
        public static LoaderStop Classify(string loaderText, out List<string> errors)
        {
            errors = new List<string>();
            if (string.IsNullOrWhiteSpace(loaderText)) return LoaderStop.Unknown;
            var lines = loaderText.Split('\n').Select(l => StripPrefix(l.Trim())).Where(l => l.Length > 0).ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                if (!IsErrorLine(lines[i])) continue;
                // "Error from remote:" and its kind put the server's own words on the next line
                var l = lines[i];
                if (l.EndsWith(":") && i + 1 < lines.Count && !IsErrorLine(lines[i + 1]) && !lines[i + 1].StartsWith(Closing, StringComparison.OrdinalIgnoreCase)) l += " " + lines[i + 1];
                errors.Add(l);
            }
            if (errors.Count > 0) return LoaderStop.LoaderError;
            var loaded = lines.FindLastIndex(l => l.IndexOf(ProfileLoaded, StringComparison.OrdinalIgnoreCase) >= 0);
            return loaded >= 0 ? LoaderStop.GameQuit : LoaderStop.Other;
        }

        /// <summary>xiloader's lines start with a time stamp in brackets; the message is what follows.</summary>
        public static string StripPrefix(string line)
        {
            var l = line ?? "";
            while (l.StartsWith("[")) { var close = l.IndexOf(']'); if (close < 0) break; l = l.Substring(close + 1).TrimStart(); }
            return l;
        }

        private static readonly string[] ErrorStarts =
        {
            "Failed", "Error from remote", "Remote failed", "Bad json", "Unsupported polcore", "Passwords did not match",
            "Trust token rejected", "The login server sent an unknown result", "The profile server relay", "PlayOnline install folder not found",
            "Hairpin fix failed", "mbedtls_", "xi_connect", "Your xiloader", "--json was specified",
        };

        public static bool IsErrorLine(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            var m = message.Trim();
            if (m.StartsWith(Closing, StringComparison.OrdinalIgnoreCase)) return false;
            return ErrorStarts.Any(s => m.StartsWith(s, StringComparison.OrdinalIgnoreCase)) ||
                   m.IndexOf("too old", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   m.IndexOf("Invalid username or password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   m.IndexOf("already logged in", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- facts ----

        /// <summary>One thing found on this PC; Problem marks what can stop the game starting.</summary>
        public sealed class Finding
        {
            public string Text { get; set; }
            public bool Problem { get; set; }
            public override string ToString() => (Problem ? "PROBLEM: " : "ok: ") + Text;
        }

        /// <summary>The files the game needs in its folder; a name ending in a backslash is a folder (the game data).</summary>
        public static readonly string[] GameFiles = { "FFXi.dll", "FFXiMain.dll", "FFXiVersions.dll", "VTABLE.DAT", "FTABLE.DAT", @"ROM\" };

        /// <summary>DLLs that load into the game from beside xiloader. Ours are Effects.Wrappers; any other one was put there by hand or by another setup.</summary>
        public static readonly string[] HookDlls = { "d3d8.dll", "d3d9.dll", "dxgi.dll", "ddraw.dll", "dinput8.dll", "dsound.dll", "version.dll", "winmm.dll", "opengl32.dll" };

        /// <summary>The files the copy is missing, by name relative to the game folder; empty when the folder is not given.</summary>
        public static List<string> MissingGameFiles(string gameFolder) =>
            string.IsNullOrWhiteSpace(gameFolder) ? new List<string>() :
            GameFiles.Where(f => f.EndsWith("\\") ? !Directory.Exists(Path.Combine(gameFolder, f)) : !File.Exists(Path.Combine(gameFolder, f)))
                     .Select(f => f.TrimEnd('\\')).ToList();

        /// <summary>DLLs beside xiloader that the game process would load, with ours marked.</summary>
        public static List<string> LoadedBesideLoader(string bootloaderFolder)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(bootloaderFolder) || !Directory.Exists(bootloaderFolder)) return list;
            foreach (var d in HookDlls)
                if (File.Exists(Path.Combine(bootloaderFolder, d))) list.Add(d);
            return list;
        }

        /// <summary>
        /// The facts for a launch: registration against the copy the profile starts, the copy's files, the wrappers, the
        /// DirectX 9 helper d3d8to9 needs, and other copies of the game running. Never throws.
        /// </summary>
        public static List<Finding> Gather(string root, string ashitaRoot, int lang, string loaderName)
        {
            var found = new List<Finding>();
            Action<string, bool> add = (t, p) => found.Add(new Finding { Text = t, Problem = p });
            try
            {
                var reg = ClientInstall.ReadRegistration();
                var registered = GameRegistration.Read(lang);
                add("Windows' game folder (InstallFolder 0001): " + (registered.GameFolder ?? "not set"), string.IsNullOrWhiteSpace(registered.GameFolder));
                add("Windows' PlayOnline folder (InstallFolder 1000): " + (registered.ViewerFolder ?? "not set"), string.IsNullOrWhiteSpace(registered.ViewerFolder));
                add("The game xiloader starts (FFXiEntry COM server): " + (reg.FfxiDll ?? "not registered"),
                    string.IsNullOrWhiteSpace(reg.FfxiDll) || !File.Exists(Environment.ExpandEnvironmentVariables(reg.FfxiDll.Trim('"'))));
                add("PlayOnline's core xiloader starts (polcore COM server): " + (reg.PolcoreDll ?? "not registered"),
                    string.IsNullOrWhiteSpace(reg.PolcoreDll) || !File.Exists(Environment.ExpandEnvironmentVariables(reg.PolcoreDll.Trim('"'))));
                if (registered.ComServers.Count > 0) add("Every registered FFXI/PlayOnline COM file: " + string.Join("; ", registered.ComServers), false);
                if (!string.IsNullOrWhiteSpace(root))
                {
                    var stray = ClientInstall.FirstStray(reg, root);
                    add("The profile's copy is " + root + (stray == null ? ", and Windows points at it" : ", but Windows' " + stray), stray != null);
                }
                else add("The launcher knows no complete copy of the game for this profile (Game Folder blank)", true);

                // the folder the game really loads from: the registered COM server's folder, else the registered folder
                var gameFolder = !string.IsNullOrWhiteSpace(reg.FfxiDll) ? Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(reg.FfxiDll.Trim('"'))) : registered.GameFolder;
                if (!string.IsNullOrWhiteSpace(gameFolder))
                {
                    var missing = MissingGameFiles(gameFolder);
                    add(missing.Count == 0 ? "The started copy has its main files (" + gameFolder + ")" : "The started copy, " + gameFolder + ", is missing " + string.Join(", ", missing), missing.Count > 0);
                    foreach (var f in new[] { "FFXi.dll", "FFXiMain.dll" })
                    {
                        var p = Path.Combine(gameFolder, f);
                        if (File.Exists(p)) add(f + ": " + Describe(p), false);
                    }
                    var stamp = ClientVersion.ReadInstalled(gameFolder);
                    add("Game version (patch.cfg): " + (stamp ?? "unknown"), false);
                }

                if (!string.IsNullOrWhiteSpace(ashitaRoot))
                {
                    var boot = Effects.Folder(ashitaRoot);
                    var beside = LoadedBesideLoader(boot);
                    var foreign = beside.Where(d => !Effects.Wrappers.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
                    if (beside.Count > 0)
                        add("Graphics DLLs that load into the game from " + boot + ": " + string.Join(", ", beside) + " (Low spec on the Graphics page turns ours off)", false);
                    if (foreign.Count > 0) add("DLLs beside xiloader that the launcher did not put there: " + string.Join(", ", foreign), true);
                    if (beside.Contains("d3d8.dll", StringComparer.OrdinalIgnoreCase) && !HasD3dx9())
                        add("The modern look's d3d8to9 is on, but DirectX 9's d3dx9_43.dll is not in Windows (SysWOW64). The game cannot start its graphics without it.", true);
                }

                var others = OtherGames(loaderName);
                add(others.Count == 0 ? "No other copy of the game was running" : "Also running: " + string.Join(", ", others), others.Count > 0);

                foreach (var e in RecentAppEvents(new[] { loaderName, "pol", "Ashita-cli", "ffxi-boot" }, TimeSpan.FromMinutes(10), 4))
                    add("Windows event: " + e, true);
            }
            catch (Exception ex) { add("checks stopped early: " + ex.Message, false); }
            return found;
        }

        private static string Describe(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                var v = FileVersionInfo.GetVersionInfo(path);
                return fi.Length + " bytes, " + fi.LastWriteTime.ToString("yyyy-MM-dd") + (string.IsNullOrWhiteSpace(v.FileVersion) ? "" : ", version " + v.FileVersion);
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static bool HasD3dx9()
        {
            try
            {
                var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var wow = Path.Combine(win, Environment.Is64BitOperatingSystem ? "SysWOW64" : "System32");
                return File.Exists(Path.Combine(wow, "d3dx9_43.dll"));
            }
            catch (Exception) { return true; }
        }

        /// <summary>Other game processes running: retail PlayOnline, another loader, Windower, or the game itself.</summary>
        public static List<string> OtherGames(string loaderName)
        {
            var names = new[] { "pol", "xiloader", "ffxi-boot", "Windower", "FFXiMain", "ffxi" };
            var list = new List<string>();
            foreach (var n in names.Concat(string.IsNullOrWhiteSpace(loaderName) ? new string[0] : new[] { loaderName }).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(n)) { list.Add(n + ".exe (" + p.Id + ")"); p.Dispose(); }
                }
                catch (Exception) { }
            }
            return list;
        }

        /// <summary>Windows' Application log entries (crash 1000, hang/report 1001/1002, .NET 1026) naming these programs, newest last, message included.</summary>
        public static List<string> RecentAppEvents(IEnumerable<string> apps, TimeSpan within, int max)
        {
            var names = apps.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a : a + ".exe").ToList();
            var list = new List<string>();
            try
            {
                var ms = (long)Math.Max(1000, within.TotalMilliseconds);
                var query = new EventLogQuery("Application", PathType.LogName,
                    "*[System[(EventID=1000 or EventID=1001 or EventID=1002 or EventID=1026) and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]");
                using (var reader = new EventLogReader(query))
                {
                    for (var e = reader.ReadEvent(); e != null; e = reader.ReadEvent())
                        using (e)
                        {
                            string text;
                            try { text = e.FormatDescription(); } catch (Exception) { text = null; }
                            if (string.IsNullOrWhiteSpace(text)) text = string.Join(" | ", e.Properties.Select(p => Convert.ToString(p.Value)));
                            if (!names.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                            list.Add((e.TimeCreated ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss") + " event " + e.Id + ": " + OneLine(text, 600));
                        }
                }
            }
            catch (Exception ex) { list.Add("Application log unreadable: " + ex.Message); }
            return list.Skip(Math.Max(0, list.Count - max)).ToList();
        }

        private static string OneLine(string s, int max)
        {
            var t = string.Join(" ", (s ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            return t.Length > max ? t.Substring(0, max) + "…" : t;
        }

        // ---- what the player is told ----

        /// <summary>The message after a launch closed early, from where the loader stopped and what was found.</summary>
        public static string Sentence(LoaderStop stop, IList<string> loaderErrors, IList<Finding> findings, string loaderText)
        {
            var sb = new StringBuilder();
            var problems = (findings ?? new List<Finding>()).Where(f => f.Problem).ToList();
            switch (stop)
            {
                case LoaderStop.LoaderError:
                    sb.Append(UiText.T("The loader stopped with this error:\n\n"));
                    foreach (var e in loaderErrors.Take(4)) sb.Append("  ").Append(e).Append('\n');
                    sb.Append('\n').Append(AdviceFor(loaderErrors));
                    break;
                case LoaderStop.GameQuit:
                    sb.Append(UiText.T("You logged in and your profile loaded, so the server, your password and your firewall are fine. Then the game itself quit as it started. ") +
                              UiText.T("The loader prints nothing for that, which is why its window only says \"Closing...\".\n\n"));
                    if (problems.Count > 0)
                    {
                        sb.Append(UiText.T("What the launcher found:\n"));
                        foreach (var p in problems.Take(5)) sb.Append("  • ").Append(p.Text).Append('\n');
                        sb.Append('\n');
                    }
                    sb.Append(UiText.T("Try these in order, pressing Play after each:\n") +
                              UiText.T("1. Close every other copy of FFXI, PlayOnline and Windower.\n") +
                              UiText.T("2. Turn on Low spec on the Graphics page (it turns the modern look's graphics DLLs off). If that works, install DirectX 9 (https://www.microsoft.com/en-us/download/details.aspx?id=35) and turn Low spec off again.\n") +
                              UiText.T("3. Open Install game on the menu and press Repair, which checks every game file.\n\n") +
                              UiText.T("If it still closes, use Send bug report on the Setup page: it now carries the loader's own text and these checks."));
                    break;
                default:
                    sb.Append(UiText.T("The loader closed before the game opened.\n\n"));
                    if (!string.IsNullOrWhiteSpace(loaderText))
                    {
                        sb.Append(UiText.T("Its last lines were:\n"));
                        foreach (var l in LastLines(loaderText, 6)) sb.Append("  ").Append(l).Append('\n');
                        sb.Append('\n');
                    }
                    if (problems.Count > 0)
                    {
                        sb.Append(UiText.T("What the launcher found:\n"));
                        foreach (var p in problems.Take(5)) sb.Append("  • ").Append(p.Text).Append('\n');
                        sb.Append('\n');
                    }
                    sb.Append(UiText.T("Use Send bug report on the Setup page and post it in the Vanadreams Discord bug channel: it carries the loader's own text and these checks."));
                    break;
            }
            return sb.ToString().TrimEnd();
        }

        public static List<string> LastLines(string text, int count)
        {
            var lines = (text ?? "").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
            return lines.Skip(Math.Max(0, lines.Count - count)).ToList();
        }

        private static string AdviceFor(IList<string> errors)
        {
            var all = string.Join("\n", errors ?? new List<string>());
            Func<string, bool> has = s => all.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
            if (has("Invalid username or password")) return UiText.T("Check the username and password on the Profiles page. Passwords are case sensitive.");
            if (has("already logged in")) return UiText.T("That account is still logged in, often from a game that closed badly. Wait a minute and press Play again.");
            if (has("version mismatch") || has("too old")) return UiText.T("Open Setup and press Check and update, which puts in the loader the server takes.");
            if (has("polcore") || has("PlayOnline install folder") || has("FFxi")) return UiText.T("Windows has no working copy of the game registered. Open Install game on the menu and press Repair.");
            if (has("profile server")) return UiText.T("Your network blocks the profile server's port ") + PlayCheck.ProfilePort + UiText.T(". Allow outgoing TCP ") + PlayCheck.ProfilePort + UiText.T(", or ask on Discord for Tailscale access.");
            if (has("Security software")) return UiText.T("Your antivirus blocked the loader. Add the Ashita folder to its exclusions and press Play again.");
            if (has("connect") || has("resolve")) return UiText.T("Your PC could not reach the Vanadreams server. Turn off any VPN and try again, or ask on Discord for Tailscale access.");
            return UiText.T("Post a screenshot of this box in the Vanadreams Discord bug channel.");
        }

        /// <summary>The text written to the log and kept as last-launch.txt for the bug report.</summary>
        public static string Record(DateTime when, string profile, LaunchOutcome outcome, LoaderStop stop, string loaderText, string watchProblem, IEnumerable<Finding> findings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Launch " + when.ToString("yyyy-MM-dd HH:mm:ss") + " of " + profile + ": " + outcome + ", loader stopped at: " + stop);
            sb.AppendLine();
            sb.AppendLine("-- the loader's window --");
            sb.AppendLine(string.IsNullOrWhiteSpace(loaderText) ? "(nothing read" + (watchProblem == null ? "" : ": " + watchProblem) + ")" : loaderText);
            sb.AppendLine();
            sb.AppendLine("-- checks --");
            foreach (var f in findings ?? Enumerable.Empty<Finding>()) sb.AppendLine(f.ToString());
            return BugReport.StripPasswords(sb.ToString());
        }

        public static string LastLaunchPath(string dataFolder) => Path.Combine(dataFolder ?? "", "last-launch.txt");
    }
}
