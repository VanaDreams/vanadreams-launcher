using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Vanadreams.Services
{
    /// <summary>
    /// The Setup page's "Send bug report": one zip on the Desktop holding what a helper needs to see why the game will
    /// not start, which the player drops into #bugs-we-all-found themselves. Nothing is uploaded from here, and the
    /// launcher holds no webhook or token.
    ///   - the launcher's log and settings.json (paths and ticks, no logins);
    ///   - Ashita's newest logs and crash dumps, and Windows' crash dumps for the game, loader and Ashita;
    ///   - every boot profile with any password taken out. The launch copies that carry a login and the credential
    ///     store are never read.
    /// Kept under Discord's 10 MB upload: logs are cut to their last part and only the newest small dumps go in.
    /// </summary>
    public static class BugReport
    {
        public const long MaxLog = 2L * 1024 * 1024;
        public const long MaxDump = 4L * 1024 * 1024;
        public const int MaxDumps = 2;
        public const int MaxLogs = 8;

        private static readonly Regex PassArg = new Regex(@"(--pass(?:word)?\s+)(""[^""]*""|\S+)", RegexOptions.IgnoreCase);
        private static readonly Regex PassKey = new Regex(@"^(\s*[^=;\r\n]*pass(?:word)?[^=\r\n]*=\s*).*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        /// <summary>The text with every password value replaced, whether on a loader command line or as an ini key.</summary>
        public static string StripPasswords(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            text = PassArg.Replace(text, "$1********");
            return PassKey.Replace(text, "$1********");
        }

        public static string DefaultPath(DateTime when) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Vanadreams-bugreport-" + when.ToString("yyyy-MM-dd") + ".zip");

        /// <summary>Writes the zip and returns the names of the files it holds.</summary>
        public static List<string> Build(string zipPath, string dataFolder, string ashitaRoot, string windowsDumps, string launcherVersion, DateTime when, string checks = null)
        {
            var names = new List<string>();
            var notes = new StringBuilder();
            notes.AppendLine("Vanadreams bug report");
            notes.AppendLine("Made " + when.ToString("yyyy-MM-dd HH:mm") + " by launcher " + launcherVersion + " on " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? ", 64-bit" : ""));
            notes.AppendLine("Ashita folder: " + (string.IsNullOrWhiteSpace(ashitaRoot) ? "not set" : ashitaRoot));
            notes.AppendLine();

            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                // the launcher
                AddTail(zip, Path.Combine(dataFolder ?? "", "launcher.log"), "launcher/launcher.log", names, notes);
                AddText(zip, Path.Combine(dataFolder ?? "", "settings.json"), "launcher/settings.json", names, notes, strip: true);
                // what the loader's window said on the last launch that closed early, and what the PC showed then
                AddText(zip, LaunchDiagnosis.LastLaunchPath(dataFolder), "launcher/last-launch.txt", names, notes, strip: true);
                // the same checks made now: which game Windows starts, its files, wrappers, firewall, antivirus, Windows' error events
                if (!string.IsNullOrWhiteSpace(checks))
                {
                    var c = zip.CreateEntry("checks.txt");
                    using (var w = new StreamWriter(c.Open(), new UTF8Encoding(false))) w.Write(StripPasswords(checks));
                    names.Add("checks.txt");
                }

                if (!string.IsNullOrWhiteSpace(ashitaRoot) && Directory.Exists(ashitaRoot))
                {
                    // the profiles, without passwords; the per-launch copies hold a login and are never read
                    var boot = Path.Combine(ashitaRoot, "config", "boot");
                    if (Directory.Exists(boot))
                        foreach (var ini in Directory.GetFiles(boot, "*.ini").Where(f => !Path.GetFileName(f).StartsWith(".launch-", StringComparison.OrdinalIgnoreCase)))
                            AddText(zip, ini, "ashita/config/boot/" + Path.GetFileName(ini), names, notes, strip: true);
                    AddText(zip, Path.Combine(ashitaRoot, "scripts", "vanadreams.txt"), "ashita/scripts/vanadreams.txt", names, notes, strip: true);
                    AddText(zip, Path.Combine(ashitaRoot, "config", "pivot", "pivot.ini"), "ashita/config/pivot/pivot.ini", names, notes, strip: true);

                    // Ashita's logs, newest first
                    var logs = Path.Combine(ashitaRoot, "logs");
                    if (Directory.Exists(logs))
                        foreach (var f in Newest(Directory.GetFiles(logs, "*.*", SearchOption.TopDirectoryOnly)).Take(MaxLogs))
                            AddTail(zip, f, "ashita/logs/" + Path.GetFileName(f), names, notes);

                    // Ashita's crash dumps
                    foreach (var dir in Directory.GetDirectories(ashitaRoot).Where(d => Path.GetFileName(d).IndexOf("dump", StringComparison.OrdinalIgnoreCase) >= 0))
                        AddDumps(zip, Directory.GetFiles(dir, "*.dmp", SearchOption.AllDirectories), "ashita/" + Path.GetFileName(dir) + "/", names, notes);
                }

                // Windows' own crash dumps for the game, the loader and Ashita
                if (!string.IsNullOrWhiteSpace(windowsDumps) && Directory.Exists(windowsDumps))
                {
                    var ours = new[] { "pol", "ffxi", "xiloader", "ashita" };
                    AddDumps(zip, Directory.GetFiles(windowsDumps, "*.dmp").Where(f => ours.Any(o => Path.GetFileName(f).StartsWith(o, StringComparison.OrdinalIgnoreCase))),
                        "windows-crashdumps/", names, notes);
                }

                var about = zip.CreateEntry("about.txt");
                using (var w = new StreamWriter(about.Open(), new UTF8Encoding(false))) w.Write(notes.ToString());
                names.Add("about.txt");
            }
            return names;
        }

        private static IEnumerable<string> Newest(IEnumerable<string> files) => files.OrderByDescending(File.GetLastWriteTimeUtc);

        private static void AddText(ZipArchive zip, string path, string name, List<string> names, StringBuilder notes, bool strip)
        {
            if (!File.Exists(path)) return;
            try
            {
                var text = File.ReadAllText(path);
                if (strip) text = StripPasswords(text);
                var e = zip.CreateEntry(name);
                using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(text);
                names.Add(name);
            }
            catch (Exception ex) { notes.AppendLine("Could not read " + path + ": " + ex.Message); }
        }

        /// <summary>A log, or its last MaxLog bytes when it is longer.</summary>
        private static void AddTail(ZipArchive zip, string path, string name, List<string> names, StringBuilder notes)
        {
            if (!File.Exists(path)) return;
            try
            {
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (f.Length > MaxLog) { f.Seek(-MaxLog, SeekOrigin.End); notes.AppendLine(name + ": the last " + MaxLog / 1024 + " KB of " + f.Length / 1024 + " KB"); }
                    var bytes = new byte[Math.Min(f.Length, MaxLog)];
                    var read = 0;
                    while (read < bytes.Length) { var n = f.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                    var text = StripPasswords(Encoding.UTF8.GetString(bytes, 0, read));
                    var e = zip.CreateEntry(name);
                    using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(text);
                    names.Add(name);
                }
            }
            catch (Exception ex) { notes.AppendLine("Could not read " + path + ": " + ex.Message); }
        }

        private static void AddDumps(ZipArchive zip, IEnumerable<string> dumps, string prefix, List<string> names, StringBuilder notes)
        {
            var taken = 0;
            foreach (var d in Newest(dumps))
            {
                var size = new FileInfo(d).Length;
                if (taken >= MaxDumps || size > MaxDump) { notes.AppendLine("Left out (too big for Discord, or older): " + d + ", " + size / 1024 + " KB"); continue; }
                try
                {
                    zip.CreateEntryFromFile(d, prefix + Path.GetFileName(d), CompressionLevel.Optimal);
                    names.Add(prefix + Path.GetFileName(d));
                    taken++;
                }
                catch (Exception ex) { notes.AppendLine("Could not read " + d + ": " + ex.Message); }
            }
        }
    }
}
