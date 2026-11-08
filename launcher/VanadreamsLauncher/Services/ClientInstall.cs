using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Vanadreams.Services
{
    public sealed class ChunkRequirement
    {
        public string Name { get; set; }
        public string Sha256 { get; set; }
    }

    public sealed class ChunkFile
    {
        public string Path { get; set; }
        public string Sha256 { get; set; }
    }

    public sealed class ClientChunk
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
        /// <summary>Folder the zip unpacks into: under the install root ("FINAL FANTASY XI", "PlayOnlineViewer"), or under
        /// the Ashita folder when it starts with {ashita} ("{ashita}/bootloader", where the effects DLLs go beside xiloader).</summary>
        public string Dest { get; set; }
        public bool ToAshita => ClientInstall.IsAshitaDest(Dest);
        public long Unpacked { get; set; }
        public bool Required { get; set; }
        public List<ChunkRequirement> Requires { get; set; } = new List<ChunkRequirement>();
        public List<ChunkFile> Verify { get; set; } = new List<ChunkFile>();
    }

    public sealed class ClientManifest
    {
        public string Version { get; set; }
        public List<string> CreateDirs { get; set; } = new List<string>();
        public List<string> ViewerCreateDirs { get; set; } = new List<string>();
        public List<ClientChunk> Chunks { get; set; } = new List<ClientChunk>();
        public long TotalSize => Chunks.Sum(c => c.Size);
        public long TotalUnpacked => Chunks.Sum(c => c.Unpacked);

        public static ClientManifest Parse(string json)
        {
            var d = Json.ParseObject(json) ?? throw new InvalidDataException("The game's download list could not be read.");
            var m = new ClientManifest { Version = Json.Str(d, "client_ver"), CreateDirs = Json.Strings(d, "create_dirs") };
            m.ViewerCreateDirs = Json.Strings(d, "viewer_create_dirs");
            object chunks;
            d.TryGetValue("chunks", out chunks);
            foreach (var o in Json.List(chunks))
            {
                var c = Json.Obj(o);
                m.Chunks.Add(new ClientChunk
                {
                    Name = Json.Str(c, "name"),
                    Size = Json.Long(c, "size"),
                    Sha256 = Json.Str(c, "sha256"),
                    Dest = Json.Str(c, "dest", ClientInstall.GameFolderName),
                    Unpacked = Json.Long(c, "unpacked"),
                    Required = Json.Bool(c, "required"),
                    Requires = Json.List(c.ContainsKey("requires") ? c["requires"] : null).Select(x => new ChunkRequirement
                    { Name = Json.Str(Json.Obj(x), "name"), Sha256 = Json.Str(Json.Obj(x), "sha256") }).ToList(),
                    Verify = Json.List(c.ContainsKey("verify") ? c["verify"] : null).Select(x => new ChunkFile
                    { Path = Json.Str(Json.Obj(x), "path"), Sha256 = Json.Str(Json.Obj(x), "sha256") }).ToList(),
                });
            }
            if (string.IsNullOrEmpty(m.Version) || m.Chunks.Count == 0) throw new InvalidDataException("The game's download list is empty.");
            m.Chunks = ClientInstall.Ordered(m);
            return m;
        }
    }

    public sealed class InstallProgress
    {
        public int Part { get; set; }
        public int Parts { get; set; }
        public long Done { get; set; }
        public long Total { get; set; }
        public string Stage { get; set; }
    }

    /// <summary>
    /// Installs the game from the Vanadreams package: each zip is downloaded (resuming), checked against the
    /// manifest's sha256, unpacked and deleted; a record in the install folder lets a stopped install carry on.
    /// xiloader needs PlayOnline's polcore.dll and the game's FFXi.dll registered as COM objects and both folders
    /// in the PlayOnlineUS InstallFolder key, so the last step is one elevated command script.
    /// </summary>
    public static class ClientInstall
    {
        public const string BaseUrl = "https://vanadreams.org/download/client/";
        public const string GameFolderName = "FINAL FANTASY XI";
        public const string ViewerFolderName = "PlayOnlineViewer";
        public const string RecordName = ".vanadreams-client.txt";
        /// <summary>Dest prefix for a chunk that unpacks under the Ashita folder rather than the game's install root.</summary>
        public const string AshitaDest = "{ashita}";

        public static string DefaultRoot => @"C:\Games\Vanadreams";
        public static string ManifestUrl(string version) => BaseUrl + version + "/manifest.json";
        public static string ChunkUrl(string version, string name) => BaseUrl + version + "/" + name;
        public static string GameFolder(string root) => Path.Combine(root, GameFolderName);
        public static string ViewerFolder(string root) => Path.Combine(root, ViewerFolderName);

        public static bool IsAshitaDest(string dest) => dest != null && dest.StartsWith(AshitaDest, StringComparison.OrdinalIgnoreCase);

        /// <summary>The folder a chunk unpacks into; null for an Ashita chunk while no Ashita folder is set.</summary>
        public static string DestFolder(ClientChunk c, string root, string ashitaRoot)
        {
            var parts = (c.Dest ?? GameFolderName).Substring(c.ToAshita ? AshitaDest.Length : 0)
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(x => x == "..")) throw new InvalidDataException("The game's download list names a folder outside the install: " + c.Dest);
            var start = c.ToAshita ? ashitaRoot : root;
            if (string.IsNullOrWhiteSpace(start)) return null;
            return parts.Aggregate(start, Path.Combine);
        }

        /// <summary>Where a chunk's line in the record goes: an Ashita chunk is recorded in the Ashita folder, so a fresh
        /// Ashita folder gets it again even though the game's own record says it was done.</summary>
        public static string RecordFolder(ClientChunk c, string root, string ashitaRoot) => c.ToAshita ? ashitaRoot : root;

        /// <summary>
        /// The record file for a folder: beside the game, except when the folder is the top of a drive. Windows lets a
        /// normal user make folders in C:\ but not files, so a game installed straight into C:\ unpacked and then
        /// stopped on "Access to the path 'C:\.vanadreams-client.txt' is denied" (30 Oct 2026). That record lives in
        /// the launcher's own data folder instead, one per drive.
        /// </summary>
        public static string RecordPath(string folder) => IsDriveTop(folder)
            ? Path.Combine(LauncherSettings.DataFolder, "client-record-" + char.ToUpperInvariant(folder.Trim()[0]) + ".txt")
            : Path.Combine(folder, RecordName);

        /// <summary>"C:\", "C:" or "C:/": the top of a drive, where files cannot be written without admin rights.</summary>
        public static bool IsDriveTop(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return false;
            var f = folder.Trim().TrimEnd('\\', '/');
            return f.Length == 2 && f[1] == ':' && char.IsLetter(f[0]);
        }

        /// <summary>Zips already unpacked for this version, from the record in the game's root and, for the chunks that
        /// go under Ashita, the one in the Ashita folder.</summary>
        public static HashSet<string> Done(string root, string version, string ashitaRoot = null)
        {
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in new[] { root, ashitaRoot })
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                var path = RecordPath(folder);
                if (!File.Exists(path)) continue;
                foreach (var line in File.ReadAllLines(path))
                {
                    var f = line.Split(' ');
                    if (f.Length >= 3 && f[0] == version) done.Add(f[1] + " " + f[2]);
                }
            }
            return done;
        }

        private static bool HashIsValid(string hash) => hash != null && hash.Length == 64 && hash.All(Uri.IsHexDigit);

        private static bool RelativePath(string path) => !string.IsNullOrWhiteSpace(path) &&
            !path.StartsWith("/") && !path.StartsWith("\\") && !path.Contains(":") &&
            !path.Split('/', '\\').Any(p => p == ".." || p == "." || p.Length == 0);

        /// <summary>Stable dependency order. A missing or changed prerequisite must never silently patch a DLL
        /// built against a different baseline. Existing manifests without dependencies keep their order.</summary>
        public static List<ClientChunk> Ordered(ClientManifest m)
        {
            var byName = new Dictionary<string, ClientChunk>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in m.Chunks)
            {
                if (!RelativePath(c.Name) || c.Name.Contains("/") || c.Name.Contains("\\") || byName.ContainsKey(c.Name))
                    throw new InvalidDataException("The game's download list contains an invalid or duplicate pack name.");
                byName.Add(c.Name, c);
                if (c.Required && c.Verify.Count == 0)
                    throw new InvalidDataException(c.Name + " is required but has no installed-file checks.");
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in c.Verify)
                    if (!RelativePath(f.Path) || !HashIsValid(f.Sha256) || !paths.Add(f.Path.Replace('\\', '/')))
                        throw new InvalidDataException("Invalid file check in " + c.Name + ".");
            }
            var ordered = new List<ClientChunk>();
            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<ClientChunk> visit = null;
            visit = c =>
            {
                if (seen.Contains(c.Name)) return;
                if (!active.Add(c.Name)) throw new InvalidDataException("The game's packs have a dependency cycle.");
                foreach (var need in c.Requires)
                {
                    ClientChunk prerequisite;
                    if (need.Name == null || !HashIsValid(need.Sha256) || !byName.TryGetValue(need.Name, out prerequisite) ||
                        !string.Equals(need.Sha256, prerequisite.Sha256, StringComparison.OrdinalIgnoreCase) || c.ToAshita != prerequisite.ToAshita)
                        throw new InvalidDataException(c.Name + " needs a different or missing prerequisite pack: " + need.Name);
                    visit(prerequisite);
                }
                active.Remove(c.Name);
                seen.Add(c.Name);
                ordered.Add(c);
            };
            foreach (var c in m.Chunks) visit(c);
            return ordered;
        }

        /// <summary>Check the installed bytes, not just the download receipt. Critical packs opt in through verify.</summary>
        public static bool FilesMatch(ClientChunk c, string root, string ashitaRoot = null)
        {
            var dest = DestFolder(c, root, ashitaRoot);
            if (dest == null) return c.Verify.Count == 0;
            foreach (var f in c.Verify)
            {
                if (!RelativePath(f.Path) || !HashIsValid(f.Sha256)) throw new InvalidDataException("Invalid file check in " + c.Name);
                var path = Path.Combine(dest, f.Path.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    if (!File.Exists(path) || !string.Equals(Sha256Of(path), f.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
                }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }
            return true;
        }

        /// <summary>Reinstall dependents after any prerequisite is replayed, even if their receipts still match.</summary>
        public static List<ClientChunk> Remaining(ClientManifest m, HashSet<string> done, string root = null, string ashitaRoot = null)
        {
            var todo = new List<ClientChunk>();
            var replay = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Ordered(m))
                if (!done.Contains(c.Name + " " + c.Sha256) || c.Requires.Any(r => replay.Contains(r.Name)) ||
                    (c.Verify.Count > 0 && touched.Contains(c.Dest ?? GameFolderName)) ||
                    (root != null && !FilesMatch(c, root, ashitaRoot)))
                { todo.Add(c); replay.Add(c.Name); touched.Add(c.Dest ?? GameFolderName); }
            return todo;
        }

        private static HashSet<string> RequiredNames(ClientManifest m)
        {
            var required = new HashSet<string>(m.Chunks.Where(c => c.Required).Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var c in Ordered(m).AsEnumerable().Reverse())
                if (required.Contains(c.Name)) foreach (var r in c.Requires) required.Add(r.Name);
            return required;
        }

        /// <summary>Managed installs get all pending packs. An imported, exact-version copy gets only required
        /// packs and their prerequisites; recording those packs must not make it look like a partial full install.</summary>
        public static List<ClientChunk> Updates(ClientManifest m, string root, string ashitaRoot)
        {
            var mine = Done(root, m.Version);
            var required = RequiredNames(m);
            var managed = mine.Contains("@managed -") || mine.Any(line => !required.Contains(line.Split(' ')[0]));
            // Preserve support for changed hashes in pre-metadata manifests.
            if (required.Count == 0) managed = mine.Count > 0;
            if (!managed && (required.Count == 0 || ClientVersion.ReadInstalled(GameFolder(root)) != m.Version))
                return new List<ClientChunk>();
            var hasAshita = !string.IsNullOrWhiteSpace(ashitaRoot) && Directory.Exists(ashitaRoot);
            var eligible = managed ? m : new ClientManifest { Version = m.Version,
                Chunks = m.Chunks.Where(c => required.Contains(c.Name)).ToList() };
            return Remaining(eligible, Done(root, m.Version, ashitaRoot), root, ashitaRoot)
                .Where(c => !c.ToAshita || hasAshita).ToList();
        }

        /// <summary>Invalidate the complete pending plan before overwriting any file. If extraction or the process
        /// stops halfway, receipts for previously installed dependent packs cannot hide the unfinished repair.</summary>
        private static void Invalidate(ClientManifest m, IEnumerable<ClientChunk> todo, string root, string ashitaRoot)
        {
            foreach (var group in todo.GroupBy(c => RecordPath(RecordFolder(c, root, ashitaRoot)), StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(group.Key)) continue;
                var names = new HashSet<string>(group.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
                var lines = File.ReadAllLines(group.Key).Where(line =>
                {
                    var fields = line.Split(' ');
                    return fields.Length < 3 || fields[0] != m.Version || !names.Contains(fields[1]);
                }).ToArray();
                File.WriteAllLines(group.Key, lines);
            }
        }

        /// <summary>Required packs were verified in this root; Windows must actually start that same copy.
        /// Never accept the legacy best-effort registration fallback for a required binary pack.</summary>
        public static void RequireRegisteredRoot(RegisteredGame registration, string root)
        {
            if (!GameRegistration.PointsAt(registration, root) ||
                !GameRegistration.SameFolder(GameRegistration.CurrentRoot(registration), root))
                throw new IOException("Windows still points at a different game copy. The required character files were checked in " +
                    root + ". Select that copy and complete its registration before playing.");
        }

        public static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Unpacks every zip still to do. Ashita chunks are left for later when no Ashita folder is set; the
        /// start-up check offers them once there is one. Low spec is put back on the effects after they unpack.</summary>
        public static async Task InstallFilesAsync(Downloader downloader, ClientManifest m, string root, string downloads,
            IProgress<InstallProgress> progress, CancellationToken ct, string ashitaRoot = null, bool lowSpec = false, bool updatesOnly = false)
        {
            Directory.CreateDirectory(root);
            var hasAshita = !string.IsNullOrWhiteSpace(ashitaRoot) && Directory.Exists(ashitaRoot);
            var todo = updatesOnly ? Updates(m, root, ashitaRoot) :
                Remaining(m, Done(root, m.Version, ashitaRoot), root, ashitaRoot).Where(c => !c.ToAshita || hasAshita).ToList();
            // Keep the managed/full-install distinction even when interruption removes every pending receipt.
            var required = RequiredNames(m);
            var wasManaged = !updatesOnly || Done(root, m.Version).Any(line => !required.Contains(line.Split(' ')[0]));
            if (wasManaged)
            {
                var record = RecordPath(root);
                Directory.CreateDirectory(Path.GetDirectoryName(record));
                if (!Done(root, m.Version).Contains("@managed -")) File.AppendAllText(record, m.Version + " @managed -" + Environment.NewLine);
            }
            Invalidate(m, todo, root, ashitaRoot);
            var touchedAshita = false;
            var total = m.TotalSize;
            var before = total - todo.Sum(c => c.Size);
            var staging = Path.Combine(downloads, "client", m.Version);
            for (var i = 0; i < todo.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var c = todo[i];
                var zip = Path.Combine(staging, c.Name);
                var part = m.Chunks.IndexOf(c) + 1;
                var dl = new Progress<DownloadProgress>(p => progress?.Report(new InstallProgress { Part = part, Parts = m.Chunks.Count, Done = before + p.Done, Total = total, Stage = "Downloading" }));
                if (!File.Exists(zip) || new FileInfo(zip).Length != c.Size)
                    await downloader.DownloadFileAsync(ChunkUrl(m.Version, c.Name), zip, dl, c.Name, c.Size, ct).ConfigureAwait(false);
                progress?.Report(new InstallProgress { Part = part, Parts = m.Chunks.Count, Done = before + c.Size, Total = total, Stage = "Checking and unpacking" });
                if (!string.Equals(Sha256Of(zip), c.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(zip);
                    throw new IOException(c.Name + " came down damaged and was thrown away. Press Install game again to fetch it.");
                }
                var dest = DestFolder(c, root, ashitaRoot);
                await Task.Run(() => Downloader.ExtractZipOverwrite(zip, dest), ct).ConfigureAwait(false);
                if (!FilesMatch(c, root, ashitaRoot))
                    throw new IOException(c.Name + " unpacked, but its required files could not be verified. Press Patch again.");
                var record = RecordPath(RecordFolder(c, root, ashitaRoot));
                Directory.CreateDirectory(Path.GetDirectoryName(record));
                File.AppendAllText(record, $"{m.Version} {c.Name} {c.Sha256}{Environment.NewLine}");
                File.Delete(zip);
                before += c.Size;
                touchedAshita |= c.ToAshita;
            }
            foreach (var c in m.Chunks.Where(c => c.Verify.Count > 0 && (!c.ToAshita || hasAshita) &&
                (!updatesOnly || wasManaged || required.Contains(c.Name))))
                if (!FilesMatch(c, root, ashitaRoot))
                    throw new IOException(c.Name + " was changed by another pack. Press Patch again before playing.");
            if (touchedAshita) Effects.Apply(ashitaRoot, lowSpec);
            foreach (var d in m.CreateDirs) Directory.CreateDirectory(Path.Combine(GameFolder(root), d));
            foreach (var d in m.ViewerCreateDirs) Directory.CreateDirectory(Path.Combine(ViewerFolder(root), d.Replace('/', '\\')));
        }

        public static int RegisterSteps => 7 + RetailDefaults.Length + ComServers.Length;

        /// <summary>The PlayOnline registry key xiloader reads for --lang: Japanese PlayOnline, English PlayOnlineUS, European PlayOnlineEU.</summary>
        public static string VendorKey(int lang) => lang == 0 ? "PlayOnline" : lang == 2 ? "PlayOnlineEU" : "PlayOnlineUS";

        /// <summary>Every key to look in, the one for this language first.</summary>
        public static string[] VendorKeys(int lang) =>
            new[] { VendorKey(lang), "PlayOnlineUS", "PlayOnline", "PlayOnlineEU" }.Distinct().ToArray();

        /// <summary>Steps in the script for a Japanese or European copy: both folders, Japanese only the language, then the COM servers.</summary>
        public static int RegisterStepsFor(int lang) => lang == 1 ? RegisterSteps : 2 + (lang == 0 ? 1 : 0) + ComServers.Length;

        /// <summary>
        /// Register a copy for a Japanese (0) or European (2) loader. The player's own PlayOnline already holds the
        /// language and controller settings, so only the two folders xiloader reads are written (Japanese keeps its
        /// settings under "Square", and gets its language only where missing), then the COM servers.
        /// </summary>
        private static string RegisterScriptOther(string root, int lang)
        {
            var game = GameFolder(root) + "\\";
            var viewer = ViewerFolder(root);
            var folders = @"HKLM\SOFTWARE\" + VendorKey(lang) + @"\InstallFolder";
            const string regsvr = @"%SystemRoot%\SysWOW64\regsvr32.exe";
            var lines = new List<string>
            {
                "@echo off",
                $"reg add \"{folders}\" /v 0001 /t REG_SZ /d \"{game}\\\" /f /reg:32 || exit /b 1",
                $"reg add \"{folders}\" /v 1000 /t REG_SZ /d \"{viewer}\" /f /reg:32 || exit /b 2",
            };
            var step = 3;
            if (lang == 0)
            {
                var settings = @"HKLM\SOFTWARE\PlayOnline\Square\PlayOnlineViewer\Settings";
                lines.Add($"reg query \"{settings}\" /v Language /reg:32 >nul 2>&1 || reg add \"{settings}\" /v Language /t REG_DWORD /d 0 /f /reg:32 || exit /b {step++}");
            }
            lines.AddRange(ComServers.Select(rel => $"\"{regsvr}\" /s \"{Path.Combine(root, rel)}\" || exit /b {step++}"));
            lines.Add("exit /b 0");
            return string.Join("\r\n", lines) + "\r\n";
        }

        /// <summary>The elevated step, 32-bit registry view as PlayOnline writes it: both folders into InstallFolder,
        /// the PlayOnline language xiloader reads (English), a controller mapping so a gamepad works from the first
        /// launch (Lee's, 25 Sept 2026; only written where the player has none), and both COM servers registered with
        /// the 32-bit regsvr32. Other game settings are left to the game's own defaults.
        /// Exit code is the number of the first step that failed.</summary>
        public static string RegisterScript(string root, int lang = 1)
        {
            if (lang != 1) return RegisterScriptOther(root, lang);
            var game = GameFolder(root) + "\\";
            var viewer = ViewerFolder(root);
            const string folders = @"HKLM\SOFTWARE\PlayOnlineUS\InstallFolder";
            const string settings = @"HKLM\SOFTWARE\PlayOnlineUS\SquareEnix\PlayOnlineViewer\Settings";
            const string ffxi = @"HKLM\SOFTWARE\PlayOnlineUS\SquareEnix\FinalFantasyXI";
            const string regsvr = @"%SystemRoot%\SysWOW64\regsvr32.exe";
            // a controller already set up on this PC is kept: the value is only added when it is missing
            Func<string, string, int, string> padIfMissing = (name, value, step) =>
                $"reg query \"{ffxi}\" /v {name} /reg:32 >nul 2>&1 || reg add \"{ffxi}\" /v {name} /t REG_SZ /d \"{value}\" /f /reg:32 || exit /b {step}";
            return string.Join("\r\n", new[]
            {
                "@echo off",
                $"reg add \"{folders}\" /v 0001 /t REG_SZ /d \"{game}\\\" /f /reg:32 || exit /b 1",
                $"reg add \"{folders}\" /v 1000 /t REG_SZ /d \"{viewer}\" /f /reg:32 || exit /b 2",
                $"reg add \"{settings}\" /v Language /t REG_DWORD /d 1 /f /reg:32 || exit /b 3",
                $"reg add \"{settings}\" /v SupportLanguage /t REG_DWORD /d 1 /f /reg:32 || exit /b 4",
                padIfMissing("padguid000", "{00000000-0000-0000-0000-000000000000}", 5),
                padIfMissing("padmode000", "1,1,0,0,0,1", 6),
                padIfMissing("padsin000", "8,9,13,12,10,0,1,3,2,15,-1,-1,14,-33,-33,32,32,-36,-36,35,35,6,7,5,4,11,-1", 7),
            }.Concat(RetailDefaults.Select((v, i) => IfMissing(v, 8 + i)))
             .Concat(ComServers.Select((rel, i) => $"\"{regsvr}\" /s \"{Path.Combine(root, rel)}\" || exit /b {8 + RetailDefaults.Length + i}"))
             .Concat(new[] { "exit /b 0" })) + "\r\n";
        }

        /// <summary>The rest of what Square Enix's installer writes under PlayOnlineUS (read from Ferrin's retail
        /// install, 12 Oct 2026), so the Retail profile can start the package's own pol.exe on a PC that never had
        /// FFXI. Each is only added where missing, so a player's own PlayOnline settings are kept. Key, name, type, data
        /// (null data writes an empty value). Sign-in data is never written.</summary>
        public static readonly string[][] RetailDefaults =
        {
            new[] { @"HKLM\SOFTWARE\PlayOnlineUS", "CommonFilesFolder", "REG_SZ", @"C:\Program Files\Common Files\" },
            new[] { @"HKLM\SOFTWARE\PlayOnlineUS\Interface", "0001", "REG_SZ", "0" },
            new[] { @"HKLM\SOFTWARE\PlayOnlineUS\Interface", "1000", "REG_SZ", null },   // filled with a fresh install id
            new[] { @"HKLM\SOFTWARE\PlayOnlineUS\Product", "0001", "REG_BINARY", null },
            new[] { @"HKLM\SOFTWARE\PlayOnlineUS\SquareEnix\FinalFantasyXI", "bFirst", "REG_BINARY", "00" },
            new[] { @"HKLM\SOFTWARE\PlayOnlineUS\SquareEnix\PlayOnlineViewer", "FirstBootPlayMovie", "REG_DWORD", "0" },
            new[] { Pol + "Settings", "FullScreen", "REG_DWORD", "0" },
            new[] { Pol + "Settings", "PlayAudio", "REG_DWORD", "1" },
            new[] { Pol + "Settings", "PlayOpeningMovie", "REG_DWORD", "1" },
            new[] { Pol + "Settings", "ResetSettings", "REG_DWORD", "0" },
            new[] { Pol + "Settings", "UseGameController", "REG_DWORD", "0" },
            new[] { Pol + @"Settings\Controler", "AnchorDown", "REG_DWORD", "0x34" },   // Square's spelling
            new[] { Pol + @"Settings\Controler", "AnchorLeft", "REG_DWORD", "0x36" },
            new[] { Pol + @"Settings\Controler", "AnchorRight", "REG_DWORD", "0x32" },
            new[] { Pol + @"Settings\Controler", "AnchorUp", "REG_DWORD", "0x30" },
            new[] { Pol + @"Settings\Controler", "Cancel", "REG_DWORD", "2" },
            new[] { Pol + @"Settings\Controler", "ChrCsrNext", "REG_DWORD", "7" },
            new[] { Pol + @"Settings\Controler", "ChrCsrPrev", "REG_DWORD", "6" },
            new[] { Pol + @"Settings\Controler", "ID", "REG_DWORD", "0" },
            new[] { Pol + @"Settings\Controler", "Menu", "REG_DWORD", "0" },
            new[] { Pol + @"Settings\Controler", "Navi", "REG_DWORD", "3" },
            new[] { Pol + @"Settings\Controler", "Ok", "REG_DWORD", "1" },
            new[] { Pol + @"Settings\Controler", "PageNext", "REG_DWORD", "5" },
            new[] { Pol + @"Settings\Controler", "PagePrev", "REG_DWORD", "4" },
        };

        private const string Pol = @"HKLM\SOFTWARE\PlayOnlineUS\SquareEnix\PlayOnlineViewer\";

        private static string IfMissing(string[] v, int step)
        {
            var data = v[1] == "1000" && v[0].EndsWith("Interface") ? Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant() : v[3];
            // reg.exe reads \" as an escaped quote, so a trailing backslash is doubled
            var d = data == null ? "" : " /d \"" + (data.EndsWith("\\") ? data + "\\" : data) + "\"";
            return $"reg query \"{v[0]}\" /v {v[1]} /reg:32 >nul 2>&1 || reg add \"{v[0]}\" /v {v[1]} /t {v[2]}{d} /f /reg:32 || exit /b {step}";
        }

        /// <summary>Every COM server Square Enix's installer registers (read from a retail PC, 25 Sept 2026).
        /// xiloader needs polcore and FFXi; PlayOnline itself, for the Retail profile, uses the rest.</summary>
        public static readonly string[] ComServers =
        {
            @"PlayOnlineViewer\viewer\com\polcore.dll",
            @"PlayOnlineViewer\viewer\com\app.dll",
            @"PlayOnlineViewer\viewer\ax\polmvf.dll",
            @"PlayOnlineViewer\viewer\ax\polmvfINT.dll",
            @"PlayOnlineViewer\viewer\contents\PolContents.dll",
            @"PlayOnlineViewer\viewer\contents\polcontentsINT.dll",
            @"FINAL FANTASY XI\FFXi.dll",
            @"FINAL FANTASY XI\FFXiMain.dll",
            @"FINAL FANTASY XI\FFXiVersions.dll",
        };

        /// <summary>Runs the register script with one administrator prompt. False if the prompt was refused.</summary>
        public static bool RunRegister(string root, string downloads, int lang = 1)
        {
            Directory.CreateDirectory(downloads);
            var script = Path.Combine(downloads, "register-game.cmd");
            File.WriteAllText(script, RegisterScript(root, lang));
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }))
                {
                    p.WaitForExit();
                    if (p.ExitCode != 0) throw new InvalidOperationException("Registering the game failed at step " + p.ExitCode + " of " + RegisterStepsFor(lang) + ".");
                }
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return false; }   // the prompt was cancelled
        }

        // xiloader starts the game through these two COM classes, so the DLLs registered under them
        // decide which copy runs, whatever InstallFolder says.
        public const string FfxiEntryClsid = "{989D790D-6236-11D4-80E9-00105A81E890}";
        public const string PolcoreUsClsid = "{3501F5DD-7894-42DF-866A-A2B6527D8049}";

        /// <summary>The four paths Windows holds for the game, 32-bit view; null where one is missing.</summary>
        public static Registration ReadRegistration()
        {
            var r = new Registration();
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\PlayOnlineUS\InstallFolder"))
                {
                    r.GameFolder = key?.GetValue("0001") as string;
                    r.ViewerFolder = key?.GetValue("1000") as string;
                }
                using (var hkcr = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32))
                {
                    using (var key = hkcr.OpenSubKey(@"CLSID\" + FfxiEntryClsid + @"\InprocServer32")) r.FfxiDll = key?.GetValue(null) as string;
                    using (var key = hkcr.OpenSubKey(@"CLSID\" + PolcoreUsClsid + @"\InprocServer32")) r.PolcoreDll = key?.GetValue(null) as string;
                }
            }
            catch (Exception ex) { Log.Warn("reading the game's registration: " + ex.Message); }
            return r;
        }

        /// <summary>The first of the four paths that is not inside this root, in words for the log; null when all are.</summary>
        public static string FirstStray(Registration r, string root)
        {
            Func<string, string> norm = p => (p ?? "").Trim().Trim('"').TrimEnd('\\');
            Func<string, string, bool> same = (a, b) => string.Equals(norm(a), norm(b), StringComparison.OrdinalIgnoreCase);
            Func<string, string, bool> under = (file, folder) => norm(file).StartsWith(norm(folder) + "\\", StringComparison.OrdinalIgnoreCase);
            if (!same(r.GameFolder, GameFolder(root))) return "game folder is " + (r.GameFolder ?? "not set");
            if (!same(r.ViewerFolder, ViewerFolder(root))) return "PlayOnline folder is " + (r.ViewerFolder ?? "not set");
            if (!under(r.FfxiDll, GameFolder(root))) return "FFXi.dll is " + (r.FfxiDll ?? "not registered");
            if (!under(r.PolcoreDll, ViewerFolder(root))) return "polcore.dll is " + (r.PolcoreDll ?? "not registered");
            return null;
        }

        public static bool PointsAt(Registration r, string root) => FirstStray(r, root) == null;

        /// <summary>True when this root is the copy Windows will run: both folders and both DLLs.</summary>
        public static bool IsRegistered(string root) => PointsAt(ReadRegistration(), root);
    }

    public sealed class Registration
    {
        public string GameFolder { get; set; }
        public string ViewerFolder { get; set; }
        public string FfxiDll { get; set; }
        public string PolcoreDll { get; set; }
    }
}
