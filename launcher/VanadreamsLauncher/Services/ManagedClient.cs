using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("VanadreamsLauncher.Tests")]

namespace Vanadreams.Services
{
    /// <summary>A managed game is an immutable generation of the published distribution, not a registered retail folder.</summary>
    public static class ManagedClient
    {
        public const string Version = "30260904_1";
        internal const string Inventory = ".vanadreams-managed.json";
        internal const string Payload = ".managed-ashita";
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly string[] DataExtensions = { ".DAT", ".DLL", ".EXE" };
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

        internal static string Key(ClientManifest m)
        {
            if (!string.Equals(m.Version, Version, StringComparison.Ordinal) ||
                m.Chunks.Count(x => x.ChapterOne) != 1 || m.Chunks.Count(x => !x.ChapterOne) == 0 ||
                m.Chunks.Select(x => x.Name).Distinct(Names).Count() != m.Chunks.Count ||
                m.Chunks.Any(x => !ChapterOnePatch.IsDigest(x.Sha256) || x.Size <= 0 ||
                    string.IsNullOrWhiteSpace(x.Name) || x.Name != Path.GetFileName(x.Name) || !x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The VanaDreams download list does not contain the complete September client and Chapter I.");
            var entries = m.Chunks.Select(c => c.Name + "|" + c.Dest + "|" + c.Size + "|" + c.Sha256.ToLowerInvariant());
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(m.Version + "\n" + string.Join("\n", entries))));
        }

        internal static string Hex(byte[] v) => BitConverter.ToString(v).Replace("-", "").ToLowerInvariant();
        public static string PayloadRoot(string root) => Path.Combine(root, Payload);

        internal static string SafePath(string root, string relative)
        {
            if (Path.IsPathRooted(relative)) throw new InvalidDataException("Absolute managed-client path.");
            var path = Path.GetFullPath(root);
            for (var d = new DirectoryInfo(path); d != null; d = d.Parent)
                if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A managed-client path uses a link.");
            foreach (var part in relative.Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") ||
                    part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.Contains(":"))
                    throw new InvalidDataException("Unsafe managed-client path: " + relative);
                path = Path.Combine(path, part);
                if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A managed-client path uses a link.");
            }
            return path;
        }

        public static bool IsGeneration(string root)
        {
            try { return !string.IsNullOrWhiteSpace(root) && GameRegistration.HasGame(root) && File.Exists(SafePath(root, Inventory)); }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException) { return false; }
        }

        public static bool HasManifest(string root, ClientManifest manifest)
        {
            try
            {
                return IsGeneration(root) && string.Equals(File.ReadLines(SafePath(root, Inventory)).FirstOrDefault(), Key(manifest), StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException) { return false; }
        }

        // Expected hashes come from verified archives (last published layer wins).
        // Validate all bytes again before activation and managed Play.
        internal static void Verify(string root, string key, CancellationToken ct)
        {
            if (!GameRegistration.HasGame(root)) throw new IOException("No complete managed VanaDreams game. Use Install game or Patch.");
            var rows = File.ReadLines(SafePath(root, Inventory)).GetEnumerator();
            using (rows)
            {
                if (!rows.MoveNext() || !string.Equals(rows.Current, key, StringComparison.Ordinal))
                    throw new IOException("The managed VanaDreams version needs Patch.");
                var expected = new Dictionary<string, string>(Names);
                while (rows.MoveNext())
                {
                    ct.ThrowIfCancellationRequested();
                    var row = rows.Current.Split(new[] { '\t' }, 2);
                    if (row.Length != 2 || !ChapterOnePatch.IsDigest(row[0]) || expected.ContainsKey(row[1]))
                        throw new InvalidDataException("Invalid managed-client file inventory.");
                    expected.Add(row[1], row[0]);
                }
                if (!expected.ContainsKey(ClientInstall.GameFolderName + "/FTABLE.DAT") ||
                    !expected.ContainsKey(ClientInstall.GameFolderName + "/VTABLE.DAT") ||
                    !expected.ContainsKey(Payload + "/polplugins/DATs/" + ChapterOnePatch.Overlay + "/FTABLE.DAT"))
                    throw new InvalidDataException("Incomplete managed-client file inventory.");

                // Enumerate and validate a component at a time, rather than restatting every
                // ancestor for every one of the tens of thousands of published DATs.
                foreach (var area in new[] { ClientInstall.GameFolderName, ClientInstall.ViewerFolderName, Payload })
                    VerifyTree(SafePath(root, area), area, expected, ct, false);
                if (expected.Count != 0)
                    throw new IOException("A managed VanaDreams file is missing (" + expected.Keys.First() + "). Press Patch.");
            }
        }

        private static void VerifyTree(string path, string rel, IDictionary<string, string> expected, CancellationToken ct, bool user)
        {
            user |= Names.Equals(rel, ClientInstall.GameFolderName + "/USER") ||
                Names.Equals(rel, ClientInstall.ViewerFolderName + "/USER");
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
            {
                ct.ThrowIfCancellationRequested();
                var item = rel + "/" + Path.GetFileName(child);
                var attrs = File.GetAttributes(child);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    if (user) continue; // USER links are never transferred or followed.
                    throw new IOException("A managed-client path uses a link.");
                }
                if ((attrs & FileAttributes.Directory) != 0)
                    VerifyTree(child, item, expected, ct, user);
                else if (expected.TryGetValue(item, out var hash))
                {
                    using (var sha = SHA256.Create())
                    using (var file = new FileStream(child, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan))
                        if (!Names.Equals(Hex(sha.ComputeHash(file)), hash))
                            throw new IOException("The managed VanaDreams files changed (" + item + "). Press Patch to create a clean copy.");
                    expected.Remove(item);
                }
                else if (!user && DataExtensions.Contains(Path.GetExtension(child), Names))
                    throw new IOException("An extra game file was found. Press Patch for a clean VanaDreams copy.");
            }
        }

        public static async Task VerifyForPlayAsync(string root, Downloader downloader, CancellationToken ct)
        {
            if (!IsGeneration(root)) throw new IOException("Vanadreams Play needs its own managed copy. Use Install game or Patch.");
            var manifest = ClientManifest.Parse(await downloader.GetStringAsync(ClientInstall.ManifestUrl(Version)).ConfigureAwait(false));
            await Task.Run(() => Verify(root, Key(manifest), ct), ct).ConfigureAwait(false);
        }

        internal static void ExtractVerified(string zip, string root, string dest,
            IDictionary<string, string> expected, CancellationToken ct)
        {
            using (var a = ZipFile.OpenRead(zip))
            foreach (var e in a.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var name = e.FullName.Replace('\\', '/');
                if (name.EndsWith("/", StringComparison.Ordinal)) { SafePath(root, dest + "/" + name.TrimEnd('/')); continue; }
                var rel = dest + "/" + name;
                var output = SafePath(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                using (var input = e.Open())
                using (var sha = SHA256.Create())
                using (var checkedInput = new CryptoStream(input, sha, CryptoStreamMode.Read))
                using (var file = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    checkedInput.CopyTo(file);
                    expected[rel] = Hex(sha.Hash);
                }
            }
        }

        internal static void TransferUser(string prior, string next)
        {
            if (string.IsNullOrWhiteSpace(prior)) return;
            foreach (var area in new[] { ClientInstall.GameFolderName, ClientInstall.ViewerFolderName })
            {
                var rel = area + "/USER";
                var from = SafePath(prior, rel);
                if (Directory.Exists(from)) Transfer(from, SafePath(next, rel));
            }
        }

        private static void Transfer(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.EnumerateFileSystemEntries(from))
            {
                var attr = File.GetAttributes(f);
                if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                var target = Path.Combine(to, Path.GetFileName(f));
                if ((attr & FileAttributes.Directory) != 0) Transfer(f, target);
                else File.Copy(f, target, true);
            }
        }

        internal static string Destination(ClientChunk c)
        {
            // The published pack only writes game/viewer trees and player-independent Ashita assets.
            var raw = c.Dest.Replace('\\', '/');
            if (raw == ClientInstall.GameFolderName || raw == ClientInstall.ViewerFolderName) return raw;
            var prefix = ClientInstall.AshitaDest + "/";
            if (raw.StartsWith(prefix, StringComparison.Ordinal)) return Payload + "/" + raw.Substring(prefix.Length);
            throw new InvalidDataException("Unsupported directory in the managed-client download list: " + raw);
        }

        public static async Task<string> InstallAsync(Downloader downloader, ClientManifest manifest, string target, string prior,
            string downloads, IProgress<InstallProgress> progress, CancellationToken ct, string reuseRoot = null)
        {
            var key = Key(manifest);
            var total = manifest.TotalSize;
            var parts = manifest.Chunks.Count;
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (GameRegistration.SameFolder(target, prior) && HasManifest(prior, manifest))
                {
                    try { await Task.Run(() => Verify(prior, key, ct), ct).ConfigureAwait(false); return prior; }
                    catch (IOException) { /* A drifted generation is replaced, never patched. */ }
                }
                target = Path.GetFullPath(target).TrimEnd('\\', '/');
                if (ClientInstall.IsDriveTop(target))
                    target = Path.Combine(target + Path.DirectorySeparatorChar, Path.GetFileName(ClientInstall.DefaultRoot));
                if (string.IsNullOrEmpty(Path.GetDirectoryName(target)))
                    throw new IOException("Choose a dedicated folder, not the top of a drive.");
                var parent = Path.GetDirectoryName(target);
                var stem = Path.GetFileName(target);
                var suffix = stem.IndexOf(".vd-" + Version + "-", StringComparison.OrdinalIgnoreCase);
                if (suffix > 0) stem = stem.Substring(0, suffix);
                var prefix = stem + ".vd-" + Version + "-" + key.Substring(0, 12) + "-";
                // Do not let a folder choice place a managed installation inside a retail/game tree.
                for (var a = new DirectoryInfo(parent); a != null; a = a.Parent)
                {
                    // Older launchers allowed the two game folders to live directly at the drive root.
                    // A separate directory on that drive is still a valid install location.
                    var insideGameRoot = a.Parent != null ||
                        new[] { ClientInstall.GameFolder(a.FullName), ClientInstall.ViewerFolder(a.FullName) }
                            .Any(folder => GameRegistration.SameFolder(parent, folder) || GameRegistration.IsUnder(parent, folder));
                    if ((insideGameRoot && GameRegistration.HasGame(a.FullName)) ||
                         File.Exists(Path.Combine(a.FullName, "FFXiMain.dll")) ||
                         File.Exists(Path.Combine(a.FullName, "polcore.dll")))
                        throw new IOException("Pick a folder outside every existing FFXI installation.");
                }
                Directory.CreateDirectory(parent);

                // A commit of settings can be interrupted after the verified rename. Reuse that full generation,
                // but never a work directory or loose current retail installation.
                foreach (var ready in Directory.GetDirectories(parent, prefix + "*"))
                {
                    if (GameRegistration.SameFolder(prior, ready) || !HasManifest(ready, manifest)) continue;
                    try
                    {
                        await Task.Run(() => Verify(ready, key, ct), ct).ConfigureAwait(false);
                        TransferUser(prior, ready);
                        return ready;
                    }
                    catch (IOException) { }
                }

                var free = new DriveInfo(Path.GetPathRoot(target)).AvailableFreeSpace;
                if (free < manifest.TotalUnpacked) throw new IOException("Not enough space for a separate VanaDreams copy. Choose another install location.");
                var stage = SafePath(parent, ".vanadreams-stage-" + Guid.NewGuid().ToString("N"));
                var generation = SafePath(parent, prefix + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(stage);
                try
                {
                    var expected = new Dictionary<string, string>(Names);
                    var source = reuseRoot ?? prior;
                    var reuse = string.IsNullOrWhiteSpace(source) ? null : ClientReuseIndex.Published;
                    var cache = Path.Combine(downloads, "managed-client", key);
                    Directory.CreateDirectory(cache);
                    long done = 0;
                    var index = 0;
                    foreach (var c in manifest.Chunks.OrderBy(c => c.ChapterOne))
                    {
                        ct.ThrowIfCancellationRequested();
                        ++index;
                        var zip = SafePath(cache, c.Name);
                        var partial = SafePath(cache, c.Name + ".part");
                        if (!File.Exists(zip) && !File.Exists(partial))
                            AdoptLegacyDownload(downloads, manifest.Version, c, zip, partial);
                        if (reuse != null && !File.Exists(zip) && !File.Exists(partial))
                        {
                            progress?.Report(new InstallProgress
                            { Part = index, Parts = parts, Done = done, Total = total, Stage = "Checking existing files" });
                            if (await Task.Run(() => reuse.TryCopy(c, source, stage, expected, ct), ct).ConfigureAwait(false))
                            {
                                done += c.Size;
                                continue;
                            }
                        }
                        var cached = File.Exists(zip) && new FileInfo(zip).Length == c.Size &&
                            await Task.Run(() => Names.Equals(ClientInstall.Sha256Of(zip), c.Sha256), ct).ConfigureAwait(false);
                        if (!cached)
                        {
                            var dl = new ArchiveProgress(progress, index, parts, done, total);
                            await downloader.DownloadFileAsync(ClientInstall.ChunkUrl(manifest.Version, c.Name), zip, dl, c.Name, c.Size, ct).ConfigureAwait(false);
                            if (!await Task.Run(() => Names.Equals(ClientInstall.Sha256Of(zip), c.Sha256), ct).ConfigureAwait(false))
                            {
                                File.Delete(zip);
                                throw new IOException("Damaged VanaDreams download: " + c.Name + ". Press Patch to retry.");
                            }
                        }
                        progress?.Report(new InstallProgress
                        { Part = index, Parts = parts, Done = done + c.Size, Total = total, Stage = "Checking and unpacking" });
                        if (c.ChapterOne)
                        {
                            var payload = PayloadRoot(stage);
                            Directory.CreateDirectory(payload);
                            await Task.Run(() => ChapterOnePatch.Install(zip, ClientInstall.GameFolder(stage), payload, ct), ct).ConfigureAwait(false);
                            foreach (var overlay in new[] { ChapterOnePatch.Overlay, ChapterOnePatch.HairOverlay, ChapterOnePatch.PublicOverlay })
                            {
                                var dir = "polplugins/DATs/" + overlay;
                                var root = SafePath(payload, dir);
                                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                                {
                                    var rel = Payload + "/" + dir + "/" + file.Substring(root.Length + 1).Replace('\\', '/');
                                    expected[rel] = ClientInstall.Sha256Of(file);
                                }
                            }
                        }
                        else await Task.Run(() => ExtractVerified(zip, stage, Destination(c), expected, ct), ct).ConfigureAwait(false);
                        done += c.Size;
                    }
                    foreach (var d in manifest.CreateDirs) Directory.CreateDirectory(SafePath(stage, ClientInstall.GameFolderName + "/" + d.TrimEnd('/', '\\')));
                    foreach (var d in manifest.ViewerCreateDirs) Directory.CreateDirectory(SafePath(stage, ClientInstall.ViewerFolderName + "/" + d.TrimEnd('/', '\\')));
                    // Carry over player-owned data from the previous VanaDreams install.
                    TransferUser(prior, stage);
                    var lines = new[] { key }.Concat(expected.OrderBy(x => x.Key, Names).Select(x => x.Value + "\t" + x.Key));
                    File.WriteAllLines(SafePath(stage, Inventory), lines, new UTF8Encoding(false));
                    progress?.Report(new InstallProgress
                    { Part = parts, Parts = parts, Done = total, Total = total, Stage = "Verifying game files" });
                    await Task.Run(() => Verify(stage, key, ct), ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    Directory.Move(stage, generation);
                    return generation;
                }
                finally
                {
                    // Only this invocation's dedicated scratch directory; never retire an old game directory.
                    if (Directory.Exists(stage)) try { Directory.Delete(stage, true); } catch (IOException) { }
                }
            }
            finally { Gate.Release(); }
        }

        private static void AdoptLegacyDownload(string downloads, string version, ClientChunk chunk, string zip, string partial)
        {
            var oldCache = Path.Combine(downloads, "client", version);
            var oldZip = SafePath(oldCache, chunk.Name);
            var oldPart = SafePath(oldCache, chunk.Name + ".part");
            if (File.Exists(oldZip) && new FileInfo(oldZip).Length == chunk.Size) File.Move(oldZip, zip);
            else if (File.Exists(oldPart) && new FileInfo(oldPart).Length <= chunk.Size) File.Move(oldPart, partial);
        }

        private sealed class ArchiveProgress : IProgress<DownloadProgress>
        {
            private readonly IProgress<InstallProgress> progress;
            private readonly int part, parts;
            private readonly long done, total;

            public ArchiveProgress(IProgress<InstallProgress> progress, int part, int parts, long done, long total)
            {
                this.progress = progress;
                this.part = part;
                this.parts = parts;
                this.done = done;
                this.total = total;
            }

            public void Report(DownloadProgress report) => progress?.Report(new InstallProgress
            {
                Part = part, Parts = parts, Done = done + report.Done, Total = total,
                Stage = report.ResumedFrom > 0 ? "Resuming download" : "Downloading",
                ArchiveDone = report.Done, ArchiveSize = report.Total,
            });
        }
    }
}
