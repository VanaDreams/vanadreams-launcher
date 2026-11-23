using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Vanadreams.Services
{
    /// <summary>What Windows currently says the game is: the folders PlayOnline registered and the COM servers xiloader creates.</summary>
    public sealed class RegisteredGame
    {
        /// <summary>InstallFolder 0001, the FINAL FANTASY XI folder.</summary>
        public string GameFolder { get; set; }
        /// <summary>InstallFolder 1000, the PlayOnlineViewer folder.</summary>
        public string ViewerFolder { get; set; }
        /// <summary>Every file on disk registered as the COM server for FFXi.dll, FFXiMain.dll or polcore.dll.</summary>
        public List<string> ComServers { get; set; } = new List<string>();
    }

    /// <summary>
    /// Which copy of the game a launch will start, and which it should.
    ///
    /// Windows holds one Final Fantasy XI registration for the whole machine. xiloader does not take a game
    /// path: it creates PlayOnline's and the game's COM objects, and whichever FFXi.dll and polcore.dll were
    /// registered last are the ones that run. So a player with an older copy of the game for another server,
    /// registered after ours, starts that copy from a Vanadreams profile, and the newer game this launcher
    /// installed sits unused. Before every launch the registration is compared with the copy the profile
    /// belongs to, folders and COM servers both, and put right when they differ.
    /// </summary>
    public static class GameRegistration
    {
        /// <summary>The COM servers that decide which game xiloader starts.</summary>
        public static readonly string[] DecidingComServers = { "FFXi.dll", "FFXiMain.dll", "polcore.dll", "polcoreeu.dll" };

        /// <summary>
        /// A root holds a game we can point Windows at: the two things xiloader creates are on disk, the game's
        /// FFXi.dll with FFXiMain.dll beside it, and PlayOnline's polcore.dll. An empty PlayOnlineViewer folder
        /// is not a game, and neither is an install that stopped part way: polcore.dll is in the last part.
        /// </summary>
        public static bool HasGame(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return false;
            var game = ClientInstall.GameFolder(root);
            var com = Path.Combine(ClientInstall.ViewerFolder(root), "viewer", "com");
            return File.Exists(Path.Combine(game, "FFXi.dll")) &&
                   File.Exists(Path.Combine(game, "FFXiMain.dll")) &&
                   (File.Exists(Path.Combine(com, "polcore.dll")) || File.Exists(Path.Combine(com, "polcoreeu.dll")));
        }

        /// <summary>
        /// The copy a Vanadreams profile starts, out of every copy known on this PC. Nothing is refused here: the
        /// launcher looks for the right one. The copy Windows already starts comes first, so a game that works is
        /// never taken away from the player; then the others in the order given, most ours first. The first that
        /// fits the server is the one; when none fits, or the server's version is not known, the first that is a
        /// game at all. Null when there is no game anywhere we know to look, and the launch goes ahead with
        /// whatever Windows has. A copy the player picked themselves (pinned) beats all of it while it is a game:
        /// 7 Oct 2026, a player with a newer copy for another server watched Vanadreams start that one, because
        /// it was registered and fitted, and had no way to say otherwise.
        /// </summary>
        public static string Choose(string registeredRoot, IEnumerable<string> roots, Func<string, bool> isGame, Func<string, bool> fits, string pinned = null)
        {
            if (!string.IsNullOrWhiteSpace(pinned) && isGame(pinned)) return pinned;
            var games = new List<string>();
            foreach (var r in new[] { registeredRoot }.Concat(roots ?? Enumerable.Empty<string>()))
                if (!string.IsNullOrWhiteSpace(r) && !games.Any(g => SameFolder(g, r)) && isGame(r)) games.Add(r);
            return games.FirstOrDefault(fits) ?? games.FirstOrDefault();
        }

        /// <summary>
        /// The root for a folder a player picked: the folder above FINAL FANTASY XI when they picked the game
        /// folder itself, otherwise the folder as it is (one that holds FINAL FANTASY XI and PlayOnlineViewer).
        /// </summary>
        public static string RootOf(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return null;
            var f = folder.Trim().TrimEnd('\\', '/');
            var cut = f.LastIndexOfAny(new[] { '\\', '/' });
            if (cut > 0 && string.Equals(f.Substring(cut + 1), ClientInstall.GameFolderName, StringComparison.OrdinalIgnoreCase))
                return WholeDrive(f.Substring(0, cut));
            return WholeDrive(f);
        }

        /// <summary>
        /// "D:" on its own means the current folder on D:, not the top of the drive, so a game installed straight into
        /// D:\ was registered as D:FINAL FANTASY XI and closed four seconds after Play (6 Oct 2026). A drive keeps its backslash.
        /// </summary>
        private static string WholeDrive(string root) => root.Length == 2 && root[1] == ':' ? root + "\\" : root;

        public static bool SameFolder(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
            string.Equals(a.Trim().TrimEnd('\\', '/'), b.Trim().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

        public static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
            var r = root.Trim().TrimEnd('\\', '/');
            var p = path.Trim();
            return p.Length > r.Length && p.StartsWith(r, StringComparison.OrdinalIgnoreCase) && (p[r.Length] == '\\' || p[r.Length] == '/');
        }

        /// <summary>True when everything Windows would start belongs to this root.</summary>
        public static bool PointsAt(RegisteredGame reg, string root)
        {
            if (reg == null || string.IsNullOrWhiteSpace(root)) return false;
            if (!SameFolder(reg.GameFolder, ClientInstall.GameFolder(root))) return false;
            if (!SameFolder(reg.ViewerFolder, ClientInstall.ViewerFolder(root))) return false;
            return reg.ComServers.All(p => IsUnder(p, root));
        }

        /// <summary>The root of the copy Windows starts now: where the game's COM server lives, else the registered folder.</summary>
        public static string CurrentRoot(RegisteredGame reg)
        {
            if (reg == null) return null;
            var ffxi = reg.ComServers.FirstOrDefault(p => p.EndsWith("\\FFXi.dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith("/FFXi.dll", StringComparison.OrdinalIgnoreCase));
            if (ffxi != null) return RootOf(ffxi.Substring(0, ffxi.Length - "FFXi.dll".Length - 1));
            return RootOf(reg.GameFolder);
        }

        /// <summary>
        /// The copy to hand Windows back to when the loader closes: the one it started on, when that is a whole game
        /// and not the copy we just started. Null when there is nothing to restore.
        /// </summary>
        public static string RestoreTarget(string previousRoot, string startedRoot, string rememberedOther)
        {
            if (string.IsNullOrWhiteSpace(startedRoot)) return null;
            foreach (var r in new[] { previousRoot, rememberedOther })
                if (!string.IsNullOrWhiteSpace(r) && !SameFolder(r, startedRoot) && HasGame(r)) return r;
            return null;
        }

        /// <summary>
        /// The PowerShell script that waits for the loader to close, then runs the register script for the other copy
        /// with one administrator prompt. It is run as a file by a hidden PowerShell that outlives the launcher, which
        /// closes after Play. Polls by id: the loader runs elevated, and Get-Process can see an elevated process
        /// that Wait-Process cannot open.
        /// </summary>
        public static string WatcherScript(IEnumerable<int> loaderIds, string registerScript, string startedRoot = null, int lang = 1)
        {
            var ids = string.Join(",", (loaderIds ?? Enumerable.Empty<int>()).Distinct());
            var s = registerScript.Replace("'", "''");
            var names = string.Join(",", RetailClientIsolation.GameProcessNames.Select(n => "'" + n + "'"));
            var guards = $"if (Get-Process -Name {names} -ErrorAction SilentlyContinue) {{ exit 0 }}\r\n";
            if (!string.IsNullOrWhiteSpace(startedRoot))
            {
                var key = ClientInstall.VendorKey(lang);
                var game = ClientInstall.GameFolder(startedRoot).Replace("'", "''");
                var viewer = ClientInstall.ViewerFolder(startedRoot).Replace("'", "''");
                guards += $"$key = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine','Registry32').OpenSubKey('SOFTWARE\\{key}\\InstallFolder')\r\n" +
                          $"if (!$key -or (([string]$key.GetValue('0001')).TrimEnd('\\') -ine '{game}') -or (([string]$key.GetValue('1000')).TrimEnd('\\') -ine '{viewer}')) {{ exit 0 }}\r\n";
            }
            return (ids.Length > 0 ? $"while (Get-Process -Id {ids} -ErrorAction SilentlyContinue) {{ Start-Sleep -Seconds 3 }}\r\n" : "") +
                   "try {\r\n" + guards + $"Start-Process -FilePath cmd.exe -ArgumentList '/c','\"{s}\"' -Verb RunAs -WindowStyle Hidden -Wait\r\n" +
                   $"}} finally {{ Remove-Item -LiteralPath '{s}',$PSCommandPath -Force -ErrorAction SilentlyContinue }}\r\n";
        }

        /// <summary>Read the registration as a 32-bit process sees it, which is how xiloader and the game see it.</summary>
        public static RegisteredGame Read(int lang = 1, bool exactVendor = false)
        {
            var reg = exactVendor ? ReadExactFolders(lang) : new RegisteredGame { GameFolder = ClientVersion.FindFfxiFolder(lang), ViewerFolder = ReadViewerFolder(lang) };
            try
            {
                using (var hkcr = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32))
                using (var clsid = hkcr.OpenSubKey("CLSID"))
                {
                    if (clsid == null) return reg;
                    foreach (var name in clsid.GetSubKeyNames())
                    {
                        string server;
                        try
                        {
                            using (var inproc = clsid.OpenSubKey(name + @"\InprocServer32"))
                                server = inproc?.GetValue(null) as string;
                        }
                        catch (Exception) { continue; }
                        if (string.IsNullOrWhiteSpace(server)) continue;
                        server = Environment.ExpandEnvironmentVariables(server.Trim().Trim('"'));
                        var file = Path.GetFileName(server);
                        // a registration left behind by a deleted copy can never be started, so it never decides anything
                        if (DecidingComServers.Any(d => string.Equals(d, file, StringComparison.OrdinalIgnoreCase)) &&
                            File.Exists(server) &&
                            !reg.ComServers.Contains(server, StringComparer.OrdinalIgnoreCase))
                            reg.ComServers.Add(server);
                    }
                }
            }
            catch (Exception ex) { Log.Warn("COM registration unreadable: " + ex.Message); }
            return reg;
        }

        private static RegisteredGame ReadExactFolders(int lang)
        {
            var result = new RegisteredGame();
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\" + ClientInstall.VendorKey(lang) + @"\InstallFolder"))
                {
                    result.GameFolder = key?.GetValue("0001") as string;
                    result.ViewerFolder = key?.GetValue("1000") as string;
                }
            }
            catch (Exception ex) { Log.Warn("region-specific game registration unreadable: " + ex.Message); }
            return result;
        }

        private static string ReadViewerFolder(int lang)
        {
            foreach (var vendor in ClientInstall.VendorKeys(lang))
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                    using (var key = hklm.OpenSubKey(@"SOFTWARE\" + vendor + @"\InstallFolder"))
                    {
                        var dir = key?.GetValue("1000") as string;
                        if (!string.IsNullOrWhiteSpace(dir)) return dir.TrimEnd('\\');
                    }
                }
                catch (Exception) { /* keep looking */ }
            }
            return null;
        }
    }
}
