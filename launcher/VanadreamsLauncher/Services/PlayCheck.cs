using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Vanadreams.Services
{
    /// <summary>How a watched launch ended.</summary>
    public enum LaunchOutcome { GameOpened, ClosedEarly, NeverStarted, StillWaiting }

    /// <summary>
    /// The checks around Play that say why the game did not open.
    ///
    /// From 3 Oct to 12 Oct 2026 seven players reported the same thing on Discord: Ashita connects, then the
    /// window closes and the game never starts. Behind it were a loader the server refused, a second copy of the
    /// game, a game at the top of a drive, an old C++ runtime, a network that could not reach the server (milky,
    /// fixed by Tailscale), and no game where Windows pointed (Mogsy). Antivirus taking the loader is the one
    /// known cause none of them named. All of them looked identical, because xiloader's console closes the
    /// moment it gives up and the launcher never looked at what happened after Play.
    ///
    /// So: before Play, the files antivirus takes are checked and the server's login ports are tried; after
    /// Play, the loader is watched until the game window opens, and if it closes first the launcher says why.
    /// </summary>
    public static class PlayCheck
    {
        /// <summary>The TCP ports a login goes through: auth, the character list, login data, and the profile server xiloader 2.2 uses.</summary>
        public static readonly int[] LoginPorts = { 54231, 54001, 54230, 51220 };

        public const int ProfilePort = 51220;

        /// <summary>The game's own window class, which xiloader's console and Ashita's never have.</summary>
        public const string GameWindowClass = "FFXiClass";

        // ---- files ----

        /// <summary>The files a launch needs that antivirus is known to remove: Ashita's console and core, and the profile's loader.</summary>
        public static List<string> NeededFiles(string ashitaRoot, string bootFile)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(ashitaRoot)) return list;
            list.Add(Path.Combine(ashitaRoot, "Ashita-cli.exe"));
            list.Add(Path.Combine(ashitaRoot, "Ashita.dll"));
            if (!string.IsNullOrWhiteSpace(bootFile)) list.Add(Path.Combine(ashitaRoot, bootFile));
            return list;
        }

        public static List<string> Missing(string ashitaRoot, string bootFile) =>
            NeededFiles(ashitaRoot, bootFile).Where(f => !File.Exists(f)).ToList();

        /// <summary>What to tell the player when files are gone. The antivirus is named when Windows knows it.</summary>
        public static string MissingSentence(IList<string> missing, IList<string> antivirus, string ashitaRoot)
        {
            if (missing == null || missing.Count == 0) return null;
            var names = string.Join(", ", missing.Select(Path.GetFileName));
            var av = OtherAntivirus(antivirus);
            var who = av.Count > 0 ? string.Join(" or ", av) : UiText.T("Your antivirus");
            var sb = new StringBuilder();
            sb.Append(names).Append(missing.Count == 1 ? UiText.T(" is") : UiText.T(" are")).Append(UiText.T(" missing from ")).Append(ashitaRoot).Append(".\n\n");
            sb.Append(who).Append(UiText.T(" most likely removed ")).Append(missing.Count == 1 ? UiText.T("it") : UiText.T("them"))
              .Append(UiText.T(". Ashita and xiloader are not signed, so some antivirus treats them as unknown programs.\n\n"));
            sb.Append(UiText.T("1. Open ")).Append(av.Count > 0 ? av[0] : UiText.T("your antivirus")).Append(UiText.T(", restore the file from quarantine if it is there, and add this folder as an exclusion:\n   "))
              .Append(ashitaRoot).Append("\n");
            sb.Append(UiText.T("2. Open Setup on the menu and press Check and update, which puts back anything missing.\n3. Press Play again."));
            return sb.ToString();
        }

        /// <summary>The antivirus products other than Microsoft's own: Defender exclusions can be added for the player, the rest only by hand.</summary>
        public static List<string> OtherAntivirus(IEnumerable<string> products) =>
            (products ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p) && !IsDefender(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public static bool IsDefender(string product) =>
            product != null && (product.IndexOf("Windows Defender", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                product.IndexOf("Microsoft Defender", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>The antivirus Windows Security Center knows about, by display name. Empty when it cannot be read (Windows Server has no Security Center).</summary>
        public static List<string> Antivirus()
        {
            var list = new List<string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct"))
                    foreach (var o in searcher.Get())
                    {
                        var name = o["displayName"] as string;
                        if (!string.IsNullOrWhiteSpace(name)) list.Add(name.Trim());
                    }
            }
            catch (Exception ex) { Log.Warn("antivirus list unreadable: " + ex.Message); }
            return list;
        }

        /// <summary>
        /// Add Windows Security exclusions for the Ashita and game folders through one administrator prompt.
        /// False when the prompt was refused or the command failed.
        /// </summary>
        public static bool AddDefenderExclusions(IEnumerable<string> folders)
        {
            var paths = (folders ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count == 0) return false;
            var list = string.Join(",", paths.Select(p => "'" + p.Replace("'", "''") + "'"));
            var script = "Add-MpPreference -ExclusionPath " + list;
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            try
            {
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(60000);
                    Log.Info("defender exclusions for " + string.Join("; ", paths) + " exited " + p.ExitCode);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex) { Log.Warn("defender exclusions: " + ex.Message); return false; }
        }

        // ---- network ----

        public sealed class Reach
        {
            public string Host { get; set; }
            /// <summary>The name did not resolve at all.</summary>
            public bool NoName { get; set; }
            public List<int> Open { get; } = new List<int>();
            public List<int> Closed { get; } = new List<int>();
            public bool AllOpen => !NoName && Closed.Count == 0;
        }

        /// <summary>Try each login port once, all at the same time, each with its own timeout.</summary>
        public static async Task<Reach> TryPortsAsync(string host, IEnumerable<int> ports, TimeSpan timeout)
        {
            var r = new Reach { Host = host };
            IPAddress[] addresses;
            try { addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false); }
            catch (Exception) { addresses = null; }
            var v4 = addresses?.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
            if (v4 == null || v4.Length == 0) { r.NoName = true; return r; }
            var list = ports.ToList();
            var results = await Task.WhenAll(list.Select(p => ConnectsAsync(v4[0], p, timeout))).ConfigureAwait(false);
            for (var i = 0; i < list.Count; i++) (results[i] ? r.Open : r.Closed).Add(list[i]);
            return r;
        }

        private static async Task<bool> ConnectsAsync(IPAddress address, int port, TimeSpan timeout)
        {
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                try
                {
                    var connect = client.ConnectAsync(address, port);
                    var done = await Task.WhenAny(connect, Task.Delay(timeout)).ConfigureAwait(false);
                    if (done != connect) return false;
                    await connect.ConfigureAwait(false);
                    return client.Connected;
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>What to tell the player when the server could not be reached, or null when it could.</summary>
        public static string ReachSentence(Reach r, bool overTailscale, ServerState status)
        {
            if (r == null || r.AllOpen) return null;
            if (status == ServerState.Offline || status == ServerState.Maintenance)
                return UiText.T("The Vanadreams server is ") + (status == ServerState.Offline ? UiText.T("offline") : UiText.T("down for maintenance")) + UiText.T(" right now, so the loader would close straight away. Try again once the status says Online.");
            if (r.NoName)
                return UiText.T("Your PC could not look up ") + r.Host + UiText.T(", so the loader would close without logging in.\n\n") +
                       UiText.T("Check that you are online. If other sites work, your network's DNS may be blocking the name: restart your router, or set your network's DNS to 1.1.1.1.");
            if (overTailscale)
                return UiText.T("This profile logs in over Tailscale, and the server did not answer there (ports ") + string.Join(", ", r.Closed) + ").\n\n" +
                       UiText.T("Open Tailscale and make sure it is connected and that the Vanadreams server is shared with you. Or untick Tailscale on the Profiles page to use the public address.");
            if (r.Open.Count > 0 && r.Closed.Count == 1 && r.Closed[0] == ProfilePort)
                return UiText.T("Your network lets the game's login through but blocks port ") + ProfilePort + UiText.T(", which the loader uses right after \"Connected\". The loader would close at that point.\n\n") +
                       UiText.T("Allow outgoing TCP ") + ProfilePort + UiText.T(" in your firewall or router, or ask on Discord for Tailscale access and tick Tailscale on the Profiles page.");
            return UiText.T("Your PC could not reach the Vanadreams server on the game's login ports (") + string.Join(", ", r.Closed) + UiText.T("), so the loader would close without logging in.\n\n") +
                   UiText.T("Some providers, school and work networks, and VPNs block these ports. Turn off any VPN and try again. If it still fails, ask on Discord for Tailscale access and tick Tailscale on the Profiles page; it gets through networks that block the game.");
        }

        // ---- after Play ----

        /// <summary>The process name the profile's loader runs as: xiloader, or pol for the bootloader's copy.</summary>
        public static string LoaderProcessName(string bootFile) =>
            string.IsNullOrWhiteSpace(bootFile) ? null : Path.GetFileNameWithoutExtension(bootFile);

        public static HashSet<int> RunningIds(string processName)
        {
            var set = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(processName)) return set;
            foreach (var p in Process.GetProcessesByName(processName)) { set.Add(p.Id); p.Dispose(); }
            return set;
        }

        /// <summary>
        /// Watch a launch: wait for a new loader process, then until it opens the game's window or closes.
        /// The loader runs as administrator, so this only uses what any process may see of another:
        /// its id by name and its windows' class.
        /// </summary>
        public static async Task<LaunchOutcome> WatchAsync(string processName, HashSet<int> before, TimeSpan toStart, TimeSpan toOpen, Action<HashSet<int>> onTick = null)
        {
            if (string.IsNullOrWhiteSpace(processName)) return LaunchOutcome.StillWaiting;
            var started = DateTime.UtcNow;
            HashSet<int> mine = null;
            while (mine == null)
            {
                var now = RunningIds(processName);
                now.ExceptWith(before ?? new HashSet<int>());
                if (now.Count > 0) mine = now;
                else if (DateTime.UtcNow - started > toStart) return LaunchOutcome.NeverStarted;
                else await Task.Delay(1000).ConfigureAwait(false);
            }
            var seen = DateTime.UtcNow;
            while (DateTime.UtcNow - seen < toOpen)
            {
                if (HasGameWindow(mine)) return LaunchOutcome.GameOpened;
                var alive = RunningIds(processName);
                alive.IntersectWith(mine);
                if (alive.Count == 0) return LaunchOutcome.ClosedEarly;
                // the loader's window is read on each look, so its last words are kept when it closes (it waits 2 s after "Closing...")
                if (onTick != null) { try { onTick(alive); } catch (Exception ex) { Log.Warn("launch watch look: " + ex.Message); } }
                await Task.Delay(1000).ConfigureAwait(false);
            }
            return LaunchOutcome.StillWaiting;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

        private static bool HasGameWindow(HashSet<int> ids)
        {
            var found = false;
            try
            {
                EnumWindows((h, l) =>
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (!ids.Contains((int)pid)) return true;
                    var sb = new StringBuilder(64);
                    GetClassName(h, sb, sb.Capacity);
                    if (string.Equals(sb.ToString(), GameWindowClass, StringComparison.Ordinal)) { found = true; return false; }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception) { }
            return found;
        }

        // ---- crash log ----

        public sealed class Crash
        {
            public string App { get; set; }
            public string Module { get; set; }
            public string ModuleVersion { get; set; }
            public DateTime When { get; set; }
        }

        /// <summary>Windows' own record of a program crashing (Application log, event 1000) for these programs since a moment.</summary>
        public static Crash LastCrash(IEnumerable<string> apps, DateTime sinceUtc)
        {
            var names = new HashSet<string>(apps.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a : a + ".exe"), StringComparer.OrdinalIgnoreCase);
            Crash last = null;
            try
            {
                var ms = (long)Math.Max(1000, (DateTime.UtcNow - sinceUtc).TotalMilliseconds);
                var query = new EventLogQuery("Application", PathType.LogName, "*[System[(EventID=1000) and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]");
                using (var reader = new EventLogReader(query))
                {
                    for (var e = reader.ReadEvent(); e != null; e = reader.ReadEvent())
                        using (e)
                        {
                            var c = ParseCrash(e.Properties.Select(p => p.Value as string).ToList(), e.TimeCreated ?? DateTime.Now);
                            if (c != null && names.Contains(c.App)) last = c;
                        }
                }
            }
            catch (Exception ex) { Log.Warn("crash log unreadable: " + ex.Message); }
            return last;
        }

        /// <summary>Event 1000's fields: 0 app name, 1 app version, 2 app time, 3 module name, 4 module version.</summary>
        public static Crash ParseCrash(IList<string> fields, DateTime when)
        {
            if (fields == null || fields.Count < 5 || string.IsNullOrWhiteSpace(fields[0])) return null;
            return new Crash { App = fields[0], Module = fields[3], ModuleVersion = fields[4], When = when };
        }

        public static bool IsRuntimeModule(string module) =>
            module != null && (module.StartsWith("msvcp", StringComparison.OrdinalIgnoreCase) ||
                               module.StartsWith("vcruntime", StringComparison.OrdinalIgnoreCase) ||
                               module.StartsWith("msvcr", StringComparison.OrdinalIgnoreCase) ||
                               module.StartsWith("ucrtbase", StringComparison.OrdinalIgnoreCase));

        public static string CrashSentence(Crash c)
        {
            if (c == null) return null;
            var where = c.Module + (string.IsNullOrWhiteSpace(c.ModuleVersion) ? "" : " " + c.ModuleVersion);
            if (IsRuntimeModule(c.Module))
                return c.App + UiText.T(" crashed in ") + where + UiText.T(", part of Microsoft's 32-bit C++ runtime, which is too old on this PC.\n\n") +
                       UiText.T("Install https://aka.ms/vc14/vc_redist.x86.exe (the x86 one, even on 64-bit Windows), restart the PC, and press Play again.");
            return c.App + UiText.T(" crashed in ") + where + UiText.T(" before the game opened.\n\n") +
                   UiText.T("Press Ctrl+C on this box to copy it and post it in the Vanadreams Discord bug channel, so it can be looked at.");
        }

        /// <summary>When the loader's window could not be read and nothing else explains it.</summary>
        public const string ClosedNoCause =
            "The loader closed before the game opened, and its window could not be read from here.\n\n" +
            "If its last line was \"Closing...\" with no red error above it, the login worked and the game itself quit as it started: " +
            "close other copies of FFXI, turn on Low spec on the Graphics page, or Repair the game from Install game. " +
            "If a red line was above it, that line is the reason. Send bug report on the Setup page gathers what a helper needs.";

        public const string NoGameSentence =
            "No complete copy of FINAL FANTASY XI was found on this PC: FFXi.dll, FFXiMain.dll or PlayOnline's polcore.dll is missing " +
            "everywhere the launcher looks, including the copy Windows has registered. The loader would log in and then close with no game to start.\n\n" +
            "Open Install game on the menu to install or repair it, or set your game folder in Settings if it is somewhere else.\n\nPlay anyway?";

        public const string NeverStartedNoCause =
            "Ashita closed without starting the loader.\n\n" +
            "If Windows asked for permission, choose Yes. If it did not ask, open Setup on the menu, press Check and update, and press Play again.";
    }
}
