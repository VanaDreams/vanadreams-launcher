using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Vanadreams.Services
{
    internal static class InstallCleanup
    {
        internal const string StagePrefix = ".vanadreams-stage-";
        private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
        private static readonly Regex LegacyVersion = new Regex(@"\A3[0-9]{7}_[0-9]+\z");

        internal sealed class Entry
        {
            public string Path { get; }
            public bool Download { get; }
            public long Bytes { get; }
            public int Files { get; }
            public DateTime LastWrite { get; }

            public Entry(string path, bool download, long bytes, int files, DateTime lastWrite)
            {
                Path = path;
                Download = download;
                Bytes = bytes;
                Files = files;
                LastWrite = lastWrite;
            }

            public bool Matches(Entry other) =>
                Download == other.Download && Paths.Equals(Path, other.Path) &&
                Bytes == other.Bytes && Files == other.Files && LastWrite == other.LastWrite;
        }

        internal sealed class Result
        {
            public long Bytes { get; set; }
            public int Removed { get; set; }
            public int Skipped { get; set; }
        }

        // The file stays in the cache. Closing its handle releases the lock, including after a crash.
        internal static FileStream Lock(string downloads)
        {
            var file = ManagedClient.SafePath(downloads, ".client-install.lock");
            Directory.CreateDirectory(downloads);
            try { return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) == 32)
            {
                throw new IOException(UiText.T("Another launcher is installing the game or cleaning up. Try again when it is finished."), ex);
            }
        }

        internal static List<Entry> Find(IEnumerable<string> installTargets, IEnumerable<string> protectedPaths, string downloads)
        {
            var protectedItems = protectedPaths.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath).ToArray();
            var results = new List<Entry>();
            foreach (var parent in installTargets.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => Path.GetDirectoryName(ManagedClient.InstallTarget(p))).Distinct(Paths))
            {
                foreach (var folder in ChildDirectories(parent, StagePrefix + "*"))
                {
                    var name = Path.GetFileName(folder);
                    if (!name.StartsWith(StagePrefix, StringComparison.OrdinalIgnoreCase) ||
                        !Guid.TryParseExact(name.Substring(StagePrefix.Length), "N", out _) ||
                        IsProtected(folder, protectedItems) || !TryDirectory(folder, out var stage))
                        continue;
                    var entry = InspectStage(stage);
                    if (entry != null) results.Add(entry);
                }
            }

            foreach (var cache in new[] { "managed-client", "client" })
            foreach (var folder in ChildDirectories(Path.Combine(downloads, cache), "*"))
            {
                var name = Path.GetFileName(folder);
                if (!(cache == "managed-client" ? ChapterOnePatch.IsDigest(name) : LegacyVersion.IsMatch(name)) ||
                    !TryDirectory(folder, out var directory))
                    continue;
                results.AddRange(DownloadEntries(directory).Where(entry => !IsProtected(entry.Path, protectedItems)));
            }
            return results.OrderBy(x => x.Path, Paths).ToList();
        }

        internal static Result Remove(IReadOnlyList<Entry> approved, IEnumerable<string> installTargets,
            IEnumerable<string> protectedPaths, string downloads)
        {
            using (Lock(downloads))
            {
                var allowed = Find(installTargets, protectedPaths, downloads).ToDictionary(x => x.Path, Paths);
                var result = new Result();
                foreach (var entry in approved)
                {
                    if (!allowed.TryGetValue(entry.Path, out var current) || !entry.Matches(current))
                    {
                        result.Skipped++;
                        continue;
                    }
                    try
                    {
                        ManagedClient.SafePath(Path.GetDirectoryName(entry.Path), Path.GetFileName(entry.Path));
                        if (entry.Download) File.Delete(entry.Path);
                        else Directory.Delete(entry.Path, recursive: true);
                        result.Removed++;
                        result.Bytes += entry.Bytes;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Log.Warn("install cleanup: " + entry.Path + " · " + ex.Message);
                        result.Skipped++;
                    }
                }
                return result;
            }
        }

        private static bool IsProtected(string path, IEnumerable<string> protectedPaths) =>
            protectedPaths.Any(p => GameRegistration.SameFolder(p, path) ||
                GameRegistration.IsUnder(p, path) || (!ClientInstall.IsDriveTop(p) && GameRegistration.IsUnder(path, p)));

        private static IEnumerable<string> ChildDirectories(string path, string pattern)
        {
            if (!TryDirectory(path, out var directory)) return Array.Empty<string>();
            try { return Directory.GetDirectories(directory.FullName, pattern); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("install cleanup scan: " + path + " · " + ex.Message);
                return Array.Empty<string>();
            }
        }

        private static bool TryDirectory(string path, out DirectoryInfo directory)
        {
            directory = null;
            if (!Directory.Exists(path)) return false;
            try
            {
                var candidate = new DirectoryInfo(path);
                ManagedClient.SafePath(candidate.FullName, "cleanup-check");
                directory = candidate;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
        }

        private static Entry InspectStage(DirectoryInfo stage)
        {
            try
            {
                var pending = new Stack<DirectoryInfo>();
                pending.Push(stage);
                long bytes = 0;
                int files = 0;
                var lastWrite = stage.LastWriteTimeUtc;
                while (pending.Count > 0)
                {
                    var folder = pending.Pop();
                    foreach (var item in folder.EnumerateFileSystemInfos())
                    {
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                        if (item.LastWriteTimeUtc > lastWrite) lastWrite = item.LastWriteTimeUtc;
                        if (item is DirectoryInfo child)
                        {
                            if (Paths.Equals(child.Name, "USER") && child.EnumerateFileSystemInfos().Any()) return null;
                            pending.Push(child);
                        }
                        else if (item is FileInfo file)
                        {
                            bytes += file.Length;
                            files++;
                        }
                    }
                }
                return new Entry(stage.FullName, false, bytes, files, lastWrite);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("install cleanup scan: " + stage.FullName + " · " + ex.Message);
                return null;
            }
        }

        private static IEnumerable<Entry> DownloadEntries(DirectoryInfo directory)
        {
            try
            {
                return directory.GetFiles().Where(f =>
                    (f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                     f.Name.EndsWith(".zip.part", StringComparison.OrdinalIgnoreCase)) &&
                    (f.Attributes & FileAttributes.ReparsePoint) == 0)
                    .Select(f => new Entry(f.FullName, true, f.Length, 1, f.LastWriteTimeUtc)).ToArray();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("install cleanup scan: " + directory.FullName + " · " + ex.Message);
                return Array.Empty<Entry>();
            }
        }
    }
}
