using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Vanadreams.Services
{
    /// <summary>Chapter I uses the player's exact base DATs and the standard client's hash-verified download.
    /// Only the named XIPivot overlay receives outputs; no game originals or DLLs are written.</summary>
    public static class ChapterOnePatch
    {
        public const string Overlay = "vanadreams-chapter-i";
        public const string HairOverlay = "vanadreams-chapter-i-hair";
        public const string PublicOverlay = "vanadreams-chapter-i-official";
        public const string ManifestSlot = "chapter_i";
        public const string Package = "vanadreams-chapter-i-01.zip";
        public static bool IsManagedOverlay(string id) => new[] { Overlay, HairOverlay, PublicOverlay }.Contains(id, StringComparer.OrdinalIgnoreCase);
        private const int MaxZip = 4 * 1024 * 1024;
        private const int MaxDat = 2 * 1024 * 1024;
        private static readonly HashSet<string> Added = new HashSet<string>(Enumerable.Range(55, 8).Select(i => "ROM/490/" + i + ".DAT"), StringComparer.Ordinal);
        private static readonly HashSet<string> Replaced = new HashSet<string>(new[] {
            "FTABLE.DAT", "VTABLE.DAT", "ROM/118/114.DAT", "ROM2/13/13.DAT", "ROM2/13/58.DAT",
            "ROM2/13/103.DAT", "ROM2/17/63.DAT"
        }.Concat(new[] { 28, 29, 30, 32, 34, 36, 37, 38, 39, 42, 44, 45, 46, 47, 50 }.Select(i => "ROM4/2/" + i + ".DAT")), StringComparer.Ordinal);

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        internal static bool IsDigest(string s) => s != null && s.Length == 64 && s.All(c => Uri.IsHexDigit(c));

        public static ClientChunk ParseChunk(object value)
        {
            var d = Json.Obj(value);
            var size = Json.Long(d, "size");
            var unpacked = Json.Long(d, "unpacked");
            var hash = Json.Str(d, "sha256");
            var dest = LookOverlays.OverlayDest(Overlay);
            if (Json.Str(d, "name") != Package || Json.Str(d, "dest") != dest || size <= 0 || size > MaxZip ||
                unpacked <= 0 || unpacked > MaxZip || !IsDigest(hash))
                throw new InvalidDataException("Chapter I manifest entry is invalid.");
            return new ClientChunk { Name = Package, Size = size, Unpacked = Math.Max(unpacked, 3L * MaxZip), Sha256 = hash, Dest = dest, ChapterOne = true };
        }

        private static byte[] ReadEntry(ZipArchiveEntry e, int limit)
        {
            if (e == null || e.Length <= 0 || e.Length > limit)
                throw new InvalidDataException("Missing or oversized Chapter I package entry.");
            using (var stream = e.Open())
            using (var reader = new BinaryReader(stream))
            {
                var bytes = reader.ReadBytes((int)e.Length);
                if (bytes.Length != e.Length || stream.ReadByte() >= 0) throw new InvalidDataException("Invalid Chapter I ZIP entry length.");
                return bytes;
            }
        }

        private static string Under(string root, string relative)
        {
            var path = root;
            var segments = relative.Split('/');
            foreach (var seg in segments)
            {
                if (string.IsNullOrEmpty(seg) || seg == "." || seg == "..") throw new InvalidDataException("Invalid Chapter I path.");
                path = Path.Combine(path, seg);
                if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A Chapter I path uses a link; the pack was not installed.");
            }
            return path;
        }

        public static byte[] ApplyDelta(byte[] basis, byte[] patch, int expected)
        {
            if (expected <= 0 || expected > MaxDat) throw new InvalidDataException("Invalid Chapter I DAT size.");
            using (var r = new BinaryReader(new MemoryStream(patch, writable: false)))
            {
                if (r.ReadUInt32() != 0x31504456) throw new InvalidDataException("Unrecognized Chapter I delta.");
                var result = new byte[expected];
                var pos = 0;
                while (true)
                {
                    var op = r.ReadByte();
                    if (op == 255)
                    {
                        if (pos != expected || r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("Incomplete Chapter I delta.");
                        return result;
                    }
                    var from = op == 0 ? r.ReadInt32() : 0;
                    var count = r.ReadInt32();
                    if (count <= 0 || count > expected - pos) throw new InvalidDataException("Chapter I delta output is out of bounds.");
                    if (op == 0)
                    {
                        if (from < 0 || from > basis.Length || count > basis.Length - from)
                            throw new InvalidDataException("Chapter I delta base range is out of bounds.");
                        Buffer.BlockCopy(basis, from, result, pos, count);
                    }
                    else if (op == 1)
                    {
                        var literal = r.ReadBytes(count);
                        if (literal.Length != count) throw new InvalidDataException("Truncated Chapter I delta.");
                        Buffer.BlockCopy(literal, 0, result, pos, count);
                    }
                    else throw new InvalidDataException("Unknown Chapter I delta operation.");
                    pos += count;
                }
            }
        }

        /// <summary>Validate all outputs before an atomic directory move. A prior human-made overlay is not overwritten.</summary>
        public static void Install(string zipPath, string gameFolder, string ashitaRoot, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(ashitaRoot) || !Directory.Exists(gameFolder))
                throw new DirectoryNotFoundException("Chapter I needs the normal Ashita and game folders.");
            var contents = new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal) {
                [Overlay] = new Dictionary<string, byte[]>(StringComparer.Ordinal),
                [HairOverlay] = new Dictionary<string, byte[]>(StringComparer.Ordinal),
                [PublicOverlay] = new Dictionary<string, byte[]>(StringComparer.Ordinal),
            };
            using (var z = ZipFile.OpenRead(zipPath))
            {
                var entries = z.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
                if (entries.Count > 80 || !entries.ContainsKey("chapter-i.json"))
                    throw new InvalidDataException("Unexpected Chapter I ZIP contents.");
                var json = System.Text.Encoding.UTF8.GetString(ReadEntry(entries["chapter-i.json"], 24000));
                var manifest = Json.ParseObject(json);
                if (manifest == null || !manifest.TryGetValue("dat", out var rows))
                    throw new InvalidDataException("Chapter I DAT index is absent.");
                var i = 0;
                string fileTable = null, volumeTable = null;
                foreach (var o in Json.List(rows))
                {
                    ct.ThrowIfCancellationRequested();
                    var row = Json.Obj(o);
                    if (row == null) throw new InvalidDataException("Invalid Chapter I DAT row.");
                    var rel = Json.Str(row, "path");
                    var replacement = Replaced.Contains(rel);
                    if ((!replacement && !Added.Contains(rel)) || contents[Overlay].ContainsKey(rel)
                        || !row.TryGetValue("inputs", out var rawInputs) || !row.TryGetValue("outputs", out var rawOutputs))
                        throw new InvalidDataException("Invalid Chapter I DAT index.");
                    var inputs = Json.Obj(rawInputs);
                    var outputs = Json.Obj(rawOutputs);
                    if (inputs == null || outputs == null || !inputs.ContainsKey("retail"))
                        throw new InvalidDataException("Invalid Chapter I checksums.");
                    var basis = Array.Empty<byte>();
                    var variant = "retail";
                    if (replacement)
                    {
                        var path = Under(gameFolder, rel);
                        if (!File.Exists(path) || new FileInfo(path).Length > MaxDat)
                            throw new IOException("The expected game file for Chapter I is missing or unexpected: " + rel);
                        basis = File.ReadAllBytes(path);
                        var actual = Hash(basis);
                        variant = null;
                        foreach (var allowed in new[] { "retail", "hair", "public" })
                            if (string.Equals(actual, Json.Str(inputs, allowed), StringComparison.OrdinalIgnoreCase))
                            {
                                variant = allowed;
                                break;
                            }
                        if (variant == null)
                            throw new IOException("The game has a different version of " + rel + ". Chapter I left the game and overlays unchanged.");
                    }
                    else if (inputs.Count != 1 || inputs["retail"] != null)
                        throw new InvalidDataException("Unexpected base for a new Chapter I DAT.");

                    if (rel == "FTABLE.DAT") fileTable = variant;
                    if (rel == "VTABLE.DAT") volumeTable = variant;
                    var table = rel == "FTABLE.DAT" || rel == "VTABLE.DAT";
                    var shared = table ? null : ReadOutput(entries, outputs, i + "-" + variant, "shared", basis);
                    foreach (var target in new[] { (Overlay, "retail"), (HairOverlay, "hair"), (PublicOverlay, "public") })
                        contents[target.Item1].Add(rel, shared ?? ReadOutput(entries, outputs, i + "-" + variant, target.Item2, basis));
                    i++;
                }
                if (i != Added.Count + Replaced.Count) throw new InvalidDataException("Incomplete Chapter I DAT set.");
                if (fileTable == null || volumeTable != (fileTable == "retail" ? "retail" : "hair"))
                    throw new IOException("Chapter I needs a matching pair of file tables.");
            }

            ct.ThrowIfCancellationRequested();
            var parent = Under(ashitaRoot, "polplugins/DATs");
            var ready = contents.ToDictionary(x => x.Key, x => SameOrMissing(Under(ashitaRoot, "polplugins/DATs/" + x.Key), x.Value));
            foreach (var item in contents)
                if (!ready[item.Key]) Stage(parent, Under(ashitaRoot, "polplugins/DATs/" + item.Key), item.Value, ct);
        }

        private static byte[] ReadOutput(Dictionary<string, ZipArchiveEntry> entries, Dictionary<string, object> outputs,
            string prefix, string target, byte[] basis)
        {
            if (!outputs.TryGetValue(target, out var value)) throw new InvalidDataException("Unexpected Chapter I output variant.");
            var row = Json.Obj(value);
            var len = Json.Long(row, "size");
            var hash = Json.Str(row, "sha256");
            if (!IsDigest(hash) || len <= 0 || len > MaxDat) throw new InvalidDataException("Invalid Chapter I output hash or size.");
            return Decode(entries, prefix + "-" + target + ".vdp", basis, (int)len, hash);
        }


        // The launcher loads these only in a transient VanaDreams boot profile, not global pivot.ini.
        // The active DATs are already in the game. Do not let saved Chapter I overlays
        // shadow them, and never alter a shared Pivot config used by retail/other profiles.
        public static bool ConfigureIntegratedLaunch(string ashitaRoot, IniFile boot)
        {
            var legacy = boot.Get("ashita.polplugins", XIPivotBundle.ChapterPlugin) == "1";
            var stock = boot.Get("ashita.polplugins", "pivot") == "1";
            if (!stock && !legacy) return false;
            var plugin = stock ? "pivot" : XIPivotBundle.ChapterPlugin;
            var settingsName = boot.Get("ashita.polplugins.args", plugin, "pivot").Trim();
            if (settingsName.Length == 0 || settingsName.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new IOException("The current Ashita profile has an invalid XIPivot config name.");
            var configDir = Under(ashitaRoot, "config/pivot");
            var sourcePath = Under(configDir, settingsName + ".ini");
            var original = File.Exists(sourcePath) ? IniFile.Load(sourcePath) : IniFile.FromText("[settings]");
            var lines = new List<string>();
            var overlays = new List<string>();
            var inOverlays = false;
            var chapter = false;
            var removed = false;
            foreach (var line in original.Lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inOverlays = trimmed.Equals("[overlays]", StringComparison.OrdinalIgnoreCase);
                    chapter = trimmed.Equals("[chapter_i]", StringComparison.OrdinalIgnoreCase);
                    removed |= chapter;
                    if (inOverlays || chapter) continue;
                }
                if (chapter) continue;
                if (!inOverlays) { lines.Add(line); continue; }
                var eq = trimmed.IndexOf('=');
                if (eq < 0 || !int.TryParse(trimmed.Substring(0, eq).Trim(), out var _)) continue;
                var name = trimmed.Substring(eq + 1).Trim();
                if (IsManagedOverlay(name)) removed = true;
                else if (name != "") overlays.Add(name);
            }
            if (!legacy && !removed) return false;
            lines.Add("[overlays]");
            for (var i = 0; i < overlays.Count; i++)
                lines.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture) + " = " + overlays[i]);
            var text = string.Join("\r\n", lines) + "\r\n";
            var privateName = WritePrivatePivotConfig(configDir, text, "vd-integrated-");
            boot.Set("ashita.polplugins", XIPivotBundle.ChapterPlugin, "0");
            boot.Set("ashita.polplugins", "pivot", "1");
            boot.Set("ashita.polplugins.args", "pivot", privateName);
            return true;
        }

        public static bool ConfigureLaunch(string ashitaRoot, string gameFolder, IniFile boot)
        {
            if (string.IsNullOrWhiteSpace(gameFolder)) return false;
            var retail = Under(ashitaRoot, "polplugins/DATs/" + Overlay);
            var hair = Under(ashitaRoot, "polplugins/DATs/" + HairOverlay);
            var official = Under(ashitaRoot, "polplugins/DATs/" + PublicOverlay);
            if (!Directory.Exists(retail) || !Directory.Exists(hair) || !Directory.Exists(official)) return false;
            var fp = Under(gameFolder, "FTABLE.DAT");
            var vp = Under(gameFolder, "VTABLE.DAT");
            if (!File.Exists(fp) || !File.Exists(vp) || new FileInfo(fp).Length != 219402 || new FileInfo(vp).Length != 109701)
                return false;
            var pair = ClientInstall.Sha256Of(fp) + ClientInstall.Sha256Of(vp);
            string overlay;
            if (pair == "81d657637e8c2492bfce8d73e263e5ba41c09a49ae91968d4c10acbdb7771f2eb671edc1084c1a726072b78cdaff4394994153d5f13f1360de21965b50ac2e5d")
                overlay = Overlay;
            else if (pair == "e45c2ca79ea63de2cbfd3cb90474442e015159ce68a4e52dcb7e830079c623eecb5a6cc150420ec70a098e40d8d0d1f9c6f3ae2cbd7b484d16f130b04f36efe7")
                overlay = HairOverlay;
            else if (pair == "cccb9cbf6aecb456f447ea74634b41fbfc9eed3a7cdab9e5da89b9824449bc53cb5a6cc150420ec70a098e40d8d0d1f9c6f3ae2cbd7b484d16f130b04f36efe7")
                overlay = PublicOverlay;
            else return false;
            var folder = Under(ashitaRoot, "polplugins/DATs/" + overlay);
            if (Replaced.Concat(Added).Any(p => !File.Exists(Under(folder, p)))) return false;

            var settingsName = boot.Get("ashita.polplugins.args", "pivot", "pivot").Trim();
            if (settingsName.Length == 0 || settingsName.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new IOException("The current Ashita profile's XIPivot config name is not supported for Chapter I.");
            var configDir = Under(ashitaRoot, "config/pivot");
            var sourcePath = Under(configDir, settingsName + ".ini");
            var profilePivot = File.Exists(sourcePath) ? IniFile.Load(sourcePath) : IniFile.FromText("[settings]\ndebug_log = false");
            var rootPath = profilePivot.Get("settings", "root_path");
            if (!string.IsNullOrWhiteSpace(rootPath) &&
                !string.Equals(Path.GetFullPath(rootPath).TrimEnd('\\', '/'), Path.GetFullPath(Path.GetDirectoryName(folder)).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new IOException("The current Ashita profile uses a custom XIPivot DAT root; it was left unchanged.");

            var dataRoot = Path.GetFullPath(Path.GetDirectoryName(folder));
            var resolvedGameRoot = Path.GetFullPath(gameFolder);
            profilePivot.Set("settings", "root_path", dataRoot);
            XIPivotBundle.EnsureChapter(Under(ashitaRoot, "polplugins/" + XIPivotBundle.ChapterPlugin + ".dll"));

            // Preserve enabled overlays and their relative order. If Pivot was off, avoid enabling any prior packs.
            var enabled = boot.Get("ashita.polplugins", "pivot") == "1";
            var overlays = new List<string>();
            if (enabled)
                for (var i = 0; ; i++)
                {
                    var name = profilePivot.Get("overlays", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (name == null) break;
                    if (!IsManagedOverlay(name))
                        overlays.Add(name);
                }
            overlays.Add(overlay);
            var lines = new List<string>();
            var dropSection = false;
            foreach (var line in profilePivot.Lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    dropSection = trimmed.Equals("[overlays]", StringComparison.OrdinalIgnoreCase) ||
                                  trimmed.Equals("[chapter_i]", StringComparison.OrdinalIgnoreCase);
                if (!dropSection) lines.Add(line);
            }
            lines.Add("[chapter_i]");
            lines.Add("game_root = " + resolvedGameRoot);
            lines.Add("overlay = " + overlay);
            lines.Add("[overlays]");
            for (var i = 0; i < overlays.Count; i++) lines.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture) + " = " + overlays[i]);
            var text = string.Join("\r\n", lines) + "\r\n";
            var privateName = WritePrivatePivotConfig(configDir, text, "vd-chapter-i-");
            boot.Set("ashita.polplugins", "pivot", "0");
            boot.Set("ashita.polplugins", XIPivotBundle.ChapterPlugin, "1");
            boot.Set("ashita.polplugins.args", XIPivotBundle.ChapterPlugin, privateName);
            return true;
        }

        private static string WritePrivatePivotConfig(string configDir, string text, string prefix)
        {
            var utf8 = new System.Text.UTF8Encoding(false);
            var privateName = prefix + Hash(utf8.GetBytes(text)).Substring(0, 24);
            var privatePath = Path.Combine(configDir, privateName + ".ini");
            Directory.CreateDirectory(configDir);
            // XIPivot may normalize or edit its private config. Never overwrite even that file while another launch uses it.
            if (File.Exists(privatePath) && File.ReadAllText(privatePath) != text)
            {
                privateName += "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                privatePath = Path.Combine(configDir, privateName + ".ini");
            }
            if (!File.Exists(privatePath))
            {
                var staging = privatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(staging, text, utf8);
                    try { File.Move(staging, privatePath); }
                    catch (IOException) { if (!File.Exists(privatePath)) throw; }
                }
                finally { if (File.Exists(staging)) File.Delete(staging); }
            }
            // No unrelated configuration file is overwritten or repointed.
            if (File.ReadAllText(privatePath) != text) throw new IOException("A generated XIPivot config differs; it was left unchanged.");
            return privateName;
        }

        private static byte[] Decode(Dictionary<string, ZipArchiveEntry> entries, string name, byte[] basis, int length, string expected)
        {
            if (!entries.TryGetValue(name, out var entry)) throw new InvalidDataException("Missing Chapter I delta.");
            var output = ApplyDelta(basis, ReadEntry(entry, MaxDat + 64 * 1024), length);
            if (!string.Equals(Hash(output), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Chapter I output checksum did not match.");
            return output;
        }

        private static bool SameOrMissing(string dest, Dictionary<string, byte[]> contents)
        {
            if (Directory.Exists(dest))
            {
                if (contents.All(x => File.Exists(Under(dest, x.Key)) &&
                    new FileInfo(Under(dest, x.Key)).Length == x.Value.Length &&
                    string.Equals(ClientInstall.Sha256Of(Under(dest, x.Key)), Hash(x.Value), StringComparison.OrdinalIgnoreCase)) &&
                    Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).Count() == contents.Count)
                    return true;
                throw new IOException("The existing Chapter I overlay differs. It was not overwritten.");
            }
            return false;
        }

        private static void Stage(string parent, string dest, Dictionary<string, byte[]> contents, CancellationToken ct)
        {
            Directory.CreateDirectory(parent);
            var staging = Path.Combine(parent, "." + Overlay + "-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var item in contents)
                {
                    ct.ThrowIfCancellationRequested();
                    var output = Under(staging, item.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    File.WriteAllBytes(output, item.Value);
                }
                ct.ThrowIfCancellationRequested();
                Directory.Move(staging, dest);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        }
    }
}

