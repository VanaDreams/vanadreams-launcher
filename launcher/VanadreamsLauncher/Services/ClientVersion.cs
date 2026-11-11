using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Vanadreams.Services
{
    public enum VersionLock { Off = 0, Exact = 1, MatchingOrNewer = 2 }
    public enum VersionVerdict { Ready, ClientTooOld, ClientNewerThanServerAllows, Unknown }

    public sealed class VersionCheck
    {
        public string Installed { get; set; }
        public string Expected { get; set; }
        public VersionLock Lock { get; set; }
        public VersionVerdict Verdict { get; set; }
        /// <summary>True only when the expected version came from the server's own status, not a launcher default.</summary>
        public bool ExpectedIsPublished { get; set; }
        public bool BlocksPlay => ExpectedIsPublished && (Verdict == VersionVerdict.ClientTooOld || Verdict == VersionVerdict.ClientNewerThanServerAllows);

        public string Sentence
        {
            get
            {
                if (!ExpectedIsPublished)
                    return string.IsNullOrEmpty(Installed)
                        ? UiText.T("Client version unknown. Run the PlayOnline updater once, then check again.")
                        : UiText.F("Your client is {0}. The server hasn't published what it expects yet.", Installed);
                switch (Verdict)
                {
                    case VersionVerdict.Ready:
                        return UiText.F("Your client is {0}, the server expects {1}. Ready to play.", Installed, Expected);
                    case VersionVerdict.ClientTooOld:
                        return UiText.F("Your client is {0}, the server expects {1}. Update the client first.", Installed, Expected);
                    case VersionVerdict.ClientNewerThanServerAllows:
                        return UiText.F("Your client is {0}, the server only accepts {1}. Wait for the server to update.", Installed, Expected);
                    default:
                        return UiText.T("Client version unknown. Run the PlayOnline updater once, then check again.");
                }
            }
        }
    }

    /// <summary>
    /// Finds the FFXI client and reads the version stamp the server will see. The server reads
    /// the stamp the client sends from its own patch history and compares only the first six
    /// characters, year and month, so this does the same.
    /// </summary>
    public static class ClientVersion
    {
        private static readonly Regex Stamp = new Regex(@"^(3\d{7}_\d+)\b", RegexOptions.Multiline);

        /// <summary>PlayOnline Viewer's pol.exe, the retail bootloader: from the installer's registry entry, else beside the FFXI folder, else null.</summary>
        public static string FindPlayOnlineViewer()
        {
            foreach (var vendor in new[] { "PlayOnlineUS", "PlayOnline", "PlayOnlineEU" })
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                    using (var key = hklm.OpenSubKey(@"SOFTWARE\" + vendor + @"\InstallFolder"))
                    {
                        var dir = key?.GetValue("1000") as string;   // 1000 is the viewer, 0001 the game
                        if (!string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "pol.exe"))) return Path.Combine(dir.TrimEnd('\\'), "pol.exe");
                    }
                }
                catch (Exception) { /* keep looking */ }
            }
            var ffxi = FindFfxiFolder();
            if (ffxi != null)
            {
                var beside = Path.Combine(Path.GetDirectoryName(ffxi.TrimEnd('\\')) ?? "", "PlayOnlineViewer", "pol.exe");
                if (File.Exists(beside)) return beside;
            }
            return null;
        }

        /// <summary>The FFXI folder the PlayOnline installer registered, or null.</summary>
        public static string FindFfxiFolder(int lang = 1)
        {
            foreach (var vendor in ClientInstall.VendorKeys(lang))
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                    using (var key = hklm.OpenSubKey(@"SOFTWARE\" + vendor + @"\InstallFolder"))
                    {
                        var dir = key?.GetValue("0001") as string;
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) return dir.TrimEnd('\\');
                    }
                }
                catch (Exception) { /* no registry access or hive missing: keep looking */ }
            }
            return null;
        }

        /// <summary>Newest stamp in patch.cfg, or null when the file is missing or carries none.</summary>
        public static string ReadInstalled(string ffxiFolder)
        {
            if (string.IsNullOrEmpty(ffxiFolder)) return null;
            var cfg = Path.Combine(ffxiFolder, "patch.cfg");
            if (!File.Exists(cfg)) return null;
            try { return NewestStamp(File.ReadAllText(cfg, Encoding.GetEncoding(28591))); }
            catch (Exception ex)
            {
                // PlayOnline holds the file while it updates, or the folder is not readable: treat as unknown
                Log.Warn("patch.cfg unreadable: " + ex.Message);
                return null;
            }
        }

        public static string NewestStamp(string patchCfgText)
        {
            string best = null;
            foreach (Match m in Stamp.Matches(patchCfgText ?? ""))
            {
                var s = m.Groups[1].Value;
                if (best == null || string.CompareOrdinal(s, best) > 0) best = s;
            }
            return best;
        }

        public static VersionCheck Compare(string installed, string expected, VersionLock lockMode)
        {
            var check = new VersionCheck { Installed = installed, Expected = expected, Lock = lockMode };
            if (string.IsNullOrEmpty(installed) || string.IsNullOrEmpty(expected) || installed.Length < 6 || expected.Length < 6)
            {
                check.Verdict = VersionVerdict.Unknown;
                return check;
            }
            var have = installed.Substring(0, 6);
            var want = expected.Substring(0, 6);
            var cmp = string.CompareOrdinal(have, want);
            switch (lockMode)
            {
                case VersionLock.Exact:
                    check.Verdict = cmp == 0 ? VersionVerdict.Ready : cmp < 0 ? VersionVerdict.ClientTooOld : VersionVerdict.ClientNewerThanServerAllows;
                    break;
                case VersionLock.MatchingOrNewer:
                    check.Verdict = cmp < 0 ? VersionVerdict.ClientTooOld : VersionVerdict.Ready;
                    break;
                default:
                    check.Verdict = VersionVerdict.Ready;
                    break;
            }
            return check;
        }

        /// <summary>
        /// True when a copy with this stamp is one the server takes. With the lock off the server takes anything,
        /// but an older game is still the wrong one to pick when a current one is on the PC, so off counts as
        /// matching-or-newer here. An unreadable stamp, or a server version nobody has published, fits nothing.
        /// </summary>
        public static bool Fits(string installed, string expected, VersionLock lockMode) =>
            Compare(installed, expected, lockMode == VersionLock.Off ? VersionLock.MatchingOrNewer : lockMode).Verdict == VersionVerdict.Ready;

        public static bool IsUnderProgramFiles(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            foreach (var env in new[] { "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432" })
            {
                var pf = Environment.GetEnvironmentVariable(env);
                if (!string.IsNullOrEmpty(pf) && folder.StartsWith(pf, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
