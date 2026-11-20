using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Vanadreams.Services
{
    /// <summary>Restore missing ReShade presets and shaders, never bootloader DLLs.</summary>
    public static class EffectsRepair
    {
        private const string Package = "looks-effects.zip";
        private const string Destination = "{ashita}/bootloader";
        private const int MaxPackBytes = 32 * 1024 * 1024;
        private static readonly Regex VersionStamp = new Regex(@"\A[0-9]{8}_[0-9]{1,3}\z", RegexOptions.CultureInvariant);
        private static readonly string[] Required =
        {
            "ReShade.ini",
            "Vanadreams-Look.ini", "Vanadreams-Standard.ini", "Vanadreams-Ultra.ini",
            "reshade-shaders/Shaders/AdaptiveSharpen.fx", "reshade-shaders/Shaders/MagicBloom.fx",
            "reshade-shaders/Shaders/MultiLUT.fx", "reshade-shaders/Shaders/ReShade.fxh",
            "reshade-shaders/Shaders/ReShadeUI.fxh", "reshade-shaders/Shaders/Vignette.fx",
            "reshade-shaders/Textures/MagicBloom_Dirt.png", "reshade-shaders/Textures/Vanadreams_LUT_Atlas.png",
        };
        private static readonly HashSet<string> Known = new HashSet<string>(Required, StringComparer.OrdinalIgnoreCase);
        private static int _pending;

        public static bool Needed(Profile profile, LauncherSettings settings, string root)
        {
            return profile != null && profile.IsVanadreams && Graphics.EffectsFor(profile, settings)
                && Effects.Installed(root)
                && Missing(root).Count > 0;
        }

        public static List<string> Missing(string root) =>
            Required.Where(rel => !Present(Effects.Folder(root), rel)).ToList();

        private static bool Present(string boot, string rel) => File.Exists(Under(boot, rel));

        private static string Under(string boot, string rel) => Path.Combine(boot, rel.Replace('/', Path.DirectorySeparatorChar));

        public static async Task<int?> TryAsync(string root, string version, ClientManifest cached, HttpClient http, TimeSpan budget, Func<bool> gameRunning = null)
        {
            if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0) return null;
            try
            {
                gameRunning = gameRunning ?? GameRunning;
                if (!Effects.Installed(root)) return 0;
                var missing = Missing(root);
                if (missing.Count == 0) return 0;
                if (!VersionStamp.IsMatch(version ?? "")) throw new IOException("No published game package version.");
                if (gameRunning()) throw new IOException("A game is already running.");
                var boot = Effects.Folder(root);
                RequireNoReparse(boot);

                // No game-directory writes until the whole response and ZIP are verified.
                // A timed-out fetch can finish its cleanup in the background, but cannot deploy files.
                using (var stop = new CancellationTokenSource())
                {
                    var fetch = FetchAsync(version, cached, http ?? Http.Shared, missing, stop.Token);
                    var deadline = Task.Delay(budget);
                    if (await Task.WhenAny(fetch, deadline).ConfigureAwait(false) != fetch)
                    {
                        stop.Cancel();
                        _ = fetch.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        throw new TimeoutException("Download timed out.");
                    }

                    var staged = await fetch.ConfigureAwait(false);
                    if (gameRunning()) throw new IOException("A game started while the repair was being prepared.");
                    var restored = InstallMissing(boot, staged);
                    if (Missing(root).Count != 0) throw new IOException("The effects pack is still incomplete.");
                    if (restored > 0) Log.Info("ReShade repair: restored " + restored + " missing bundled file(s).");
                    return restored;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("ReShade repair did not finish: " + ex.Message);
                return null; // optional effects never prevent playing
            }
            finally { Volatile.Write(ref _pending, 0); }
        }

        private static bool GameRunning() =>
            PlayCheck.RunningIds("xiloader").Count != 0 || PlayCheck.RunningIds("pol").Count != 0;

        private static async Task<Dictionary<string, byte[]>> FetchAsync(
            string version, ClientManifest cached, HttpClient http, List<string> missing, CancellationToken ct)
        {
            var manifest = cached != null && cached.Version == version
                ? cached
                : ClientManifest.Parse(await new Downloader(http).GetStringAsync(ClientInstall.ManifestUrl(version), ct).ConfigureAwait(false));
            if (manifest.Version != version) throw new IOException("The effects manifest version does not match.");
            var chunks = manifest.Chunks.Where(c => string.Equals(c.Name, Package, StringComparison.OrdinalIgnoreCase)).ToList();
            if (chunks.Count != 1 || !string.Equals(chunks[0].Dest, Destination, StringComparison.OrdinalIgnoreCase)
                || chunks[0].Size <= 0 || chunks[0].Size > MaxPackBytes
                || chunks[0].Sha256 == null || chunks[0].Sha256.Length != 64 || !chunks[0].Sha256.All(Uri.IsHexDigit))
                throw new IOException("The effects package is missing or invalid in the official manifest.");

            var chunk = chunks[0];
            using (var response = await http.GetAsync(ClientInstall.ChunkUrl(version, Package), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var file = new MemoryStream())
                {
                    var buffer = new byte[64 * 1024];
                    int n;
                    while ((n = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        if (file.Length + n > chunk.Size) throw new IOException("The effects package is bigger than expected.");
                        file.Write(buffer, 0, n);
                    }
                    if (file.Length != chunk.Size) throw new IOException("The effects package is incomplete.");
                    file.Position = 0;
                    using (var hasher = SHA256.Create())
                    {
                        var hash = BitConverter.ToString(hasher.ComputeHash(file)).Replace("-", "");
                        if (!string.Equals(hash, chunk.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("The effects package did not match its checksum.");
                    }
                    ct.ThrowIfCancellationRequested();
                    file.Position = 0;
                    return ReadEntries(file, missing, ct);
                }
            }
        }

        public static Dictionary<string, byte[]> ReadEntries(Stream package, List<string> missing, CancellationToken ct)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            using (var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
            {
                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = entry.FullName.Replace('\\', '/');
                    bool directory = rel.EndsWith("/", StringComparison.Ordinal);
                    var trimmed = directory ? rel.Substring(0, rel.Length - 1) : rel;
                    if (trimmed.Split('/').Any(part => part.Length == 0 || part == "." || part == ".."
                        || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal)
                        || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.Contains(":"))
                        || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                        throw new IOException("The effects package contains an unsafe path.");
                    if (!seen.Add(trimmed)) throw new IOException("The effects package contains duplicate paths.");
                    if (directory || !Known.Contains(rel) || !missing.Contains(rel, StringComparer.OrdinalIgnoreCase)) continue;
                    total += entry.Length;
                    if (entry.Length <= 0 || total > MaxPackBytes) throw new IOException("Invalid effects archive length.");
                    using (var src = entry.Open())
                    using (var data = new MemoryStream())
                    {
                        var buffer = new byte[64 * 1024];
                        int n;
                        while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (data.Length + n > entry.Length) throw new IOException("Unexpected effects entry length.");
                            data.Write(buffer, 0, n);
                        }
                        if (data.Length != entry.Length) throw new IOException("Incomplete effects archive entry.");
                        contents.Add(rel, data.ToArray());
                    }
                }
            }
            if (missing.Any(rel => !contents.ContainsKey(rel)))
                throw new IOException("An expected effects file is missing from the package.");
            return contents;
        }

        private static void RequireNoReparse(string path)
        {
            for (var p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
            {
                try
                {
                    if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("ReShade repair will not follow a redirected folder or file.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }

        public static int InstallMissing(string boot, Dictionary<string, byte[]> contents)
        {
            // Validate all destinations first, then re-check immediately before each create.
            foreach (var rel in contents.Keys)
            {
                if (!Known.Contains(rel)) throw new IOException("Unexpected effects output path.");
                RequireNoReparse(Under(boot, rel));
            }

            int restored = 0;
            foreach (var pair in contents)
            {
                var target = Under(boot, pair.Key);
                if (Present(boot, pair.Key)) continue;
                RequireNoReparse(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                RequireNoReparse(target);
                var temp = target + "." + Guid.NewGuid().ToString("N") + ".part";
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        stream.Write(pair.Value, 0, pair.Value.Length);
                    if (Present(boot, pair.Key)) continue;
                    File.Move(temp, target); // never overwrite an existing file
                    restored++;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            return restored;
        }
    }
}
