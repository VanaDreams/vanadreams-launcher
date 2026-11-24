using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Vanadreams.Services
{
    public sealed class DownloadProgress
    {
        public long Done { get; set; }
        public long Total { get; set; }
        public string Label { get; set; }
        public double Fraction => Total > 0 ? Math.Min(1.0, (double)Done / Total) : 0;
    }

    public sealed class ReleaseAsset
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
        public string Tag { get; set; }
    }

    /// <summary>HTTP downloads with progress, GitHub release and folder lookups, and unzip with overwrite.</summary>
    public sealed class Downloader
    {
        private readonly HttpClient _http;
        public Downloader(HttpClient http = null) { _http = http ?? Http.Shared; }

        public async Task<string> GetStringAsync(string url, CancellationToken ct = default(CancellationToken))
        {
            using (var resp = await _http.GetAsync(url, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Download to a file, resuming a partial file if the server allows it.</summary>
        public async Task DownloadFileAsync(string url, string destination, IProgress<DownloadProgress> progress = null, string label = null, long expectedSize = 0, CancellationToken ct = default(CancellationToken))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var partial = destination + ".part";
            long already = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (already > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(already, null);
                using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (already > 0 && resp.StatusCode != System.Net.HttpStatusCode.PartialContent) { already = 0; File.Delete(partial); }
                    resp.EnsureSuccessStatusCode();
                    var total = (resp.Content.Headers.ContentLength ?? 0) + already;
                    if (total == 0) total = expectedSize;
                    using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = new FileStream(partial, already > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    {
                        var buf = new byte[1 << 16];
                        long done = already;
                        int n;
                        var last = DateTime.UtcNow;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                            done += n;
                            if (progress != null && (DateTime.UtcNow - last).TotalMilliseconds > 100)
                            {
                                progress.Report(new DownloadProgress { Done = done, Total = total, Label = label });
                                last = DateTime.UtcNow;
                            }
                        }
                        progress?.Report(new DownloadProgress { Done = done, Total = total, Label = label });
                    }
                }
            }
            if (expectedSize > 0 && new FileInfo(partial).Length != expectedSize)
            {
                File.Delete(partial);
                throw new IOException($"{label ?? Path.GetFileName(destination)} came down the wrong size; press the button again to retry.");
            }
            if (File.Exists(destination)) File.Delete(destination);
            File.Move(partial, destination);
        }

        /// <summary>
        /// The asset glob that means the release's own source zip, the one GitHub makes from the tagged code. For
        /// authors whose attached archive the launcher cannot open (Fancychat's is a RAR with a .zip name).
        /// </summary>
        public const string SourceZip = "source";

        /// <summary>The newest release's asset whose name matches the glob, e.g. "Bellhop.*Interface.4.30.zip".</summary>
        public async Task<ReleaseAsset> LatestReleaseAssetAsync(string repo, string assetGlob, CancellationToken ct = default(CancellationToken))
        {
            var text = await GetStringAsync($"https://api.github.com/repos/{repo}/releases?per_page=10", ct).ConfigureAwait(false);
            return LatestReleaseAsset(text, assetGlob);
        }

        /// <summary>The asset LatestReleaseAssetAsync picks from GitHub's release list. A source zip has no size listed.</summary>
        public static ReleaseAsset LatestReleaseAsset(string releasesJson, string assetGlob)
        {
            var source = string.Equals(assetGlob, SourceZip, StringComparison.OrdinalIgnoreCase);
            var rx = GlobToRegex(assetGlob);
            foreach (var r in Json.List(Json.Parse(releasesJson)))
            {
                var rel = Json.Obj(r);
                if (rel == null || Json.Bool(rel, "draft")) continue;
                if (source)
                {
                    var url = Json.Str(rel, "zipball_url");
                    if (string.IsNullOrEmpty(url)) continue;
                    var tag = Json.Str(rel, "tag_name");
                    return new ReleaseAsset { Name = "source.zip", Url = url, Size = 0, Tag = tag };
                }
                foreach (var a in Json.List(rel.ContainsKey("assets") ? rel["assets"] : null))
                {
                    var asset = Json.Obj(a);
                    var name = Json.Str(asset, "name", "");
                    if (rx.IsMatch(name))
                        return new ReleaseAsset { Name = name, Url = Json.Str(asset, "browser_download_url"), Size = Json.Long(asset, "size"), Tag = Json.Str(rel, "tag_name") };
                }
            }
            return null;
        }

        /// <summary>One release's asset by tag, e.g. ("LandSandBoat/xiloader", "v2.1.2", "xiloader.exe"). Null when the tag or the asset is not there.</summary>
        public async Task<ReleaseAsset> ReleaseAssetAsync(string repo, string tag, string assetGlob, CancellationToken ct = default(CancellationToken))
        {
            string text;
            try { text = await GetStringAsync($"https://api.github.com/repos/{repo}/releases/tags/{tag}", ct).ConfigureAwait(false); }
            catch (HttpRequestException) { return null; }
            var rel = Json.Obj(Json.Parse(text));
            if (rel == null || Json.Bool(rel, "draft")) return null;
            var rx = GlobToRegex(assetGlob);
            foreach (var a in Json.List(rel.ContainsKey("assets") ? rel["assets"] : null))
            {
                var asset = Json.Obj(a);
                var name = Json.Str(asset, "name", "");
                if (rx.IsMatch(name))
                    return new ReleaseAsset { Name = name, Url = Json.Str(asset, "browser_download_url"), Size = Json.Long(asset, "size"), Tag = Json.Str(rel, "tag_name") };
            }
            return null;
        }

        /// <summary>Every file under a folder of a GitHub repo, with the content hash and size GitHub lists for it.</summary>
        public async Task<List<RepoFile>> ListRepoFolderAsync(string repo, string path, string branch, CancellationToken ct = default(CancellationToken))
        {
            var result = new List<RepoFile>();
            await WalkAsync(repo, (path ?? "").Trim('/'), branch, "", result, ct).ConfigureAwait(false);
            return result;
        }

        private async Task WalkAsync(string repo, string path, string branch, string rel, List<RepoFile> into, CancellationToken ct)
        {
            var text = await GetStringAsync($"https://api.github.com/repos/{repo}/contents/{path}?ref={branch}", ct).ConfigureAwait(false);
            into.AddRange(ParseFolderListing(text, rel, out var folders));
            foreach (var name in folders)
                await WalkAsync(repo, path + "/" + name, branch, string.IsNullOrEmpty(rel) ? name : rel + "/" + name, into, ct).ConfigureAwait(false);
        }

        /// <summary>One page of GitHub's contents listing: the files in it, and the names of the folders to go into.</summary>
        public static List<RepoFile> ParseFolderListing(string json, string rel, out List<string> folders)
        {
            var files = new List<RepoFile>();
            folders = new List<string>();
            foreach (var e in Json.List(Json.Parse(json)))
            {
                var d = Json.Obj(e);
                if (d == null) continue;
                var name = Json.Str(d, "name", "");
                var type = Json.Str(d, "type", "");
                if (type == "dir") folders.Add(name);
                else if (type == "file")
                    files.Add(new RepoFile
                    {
                        Rel = string.IsNullOrEmpty(rel) ? name : rel + "/" + name,
                        Url = Json.Str(d, "download_url"),
                        Sha = Json.Str(d, "sha"),
                        Size = Json.Long(d, "size"),
                    });
            }
            return files;
        }

        public static Regex GlobToRegex(string glob)
        {
            var pattern = "^" + Regex.Escape(glob ?? "*").Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return new Regex(pattern, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Unzip over an existing tree, replacing files that are already there. stripTopFolder drops the one folder
        /// a GitHub source zip wraps everything in (Owner-Repo-1a2b3c4/), so its contents land in destinationRoot.
        /// </summary>
        public static List<string> ExtractZipOverwrite(string zipPath, string destinationRoot, bool stripTopFolder = false, bool preserveUser = false)
        {
            var written = new List<string>();
            var root = Path.GetFullPath(destinationRoot).TrimEnd('\\') + "\\";
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // a folder entry
                    var name = entry.FullName;
                    if (stripTopFolder)
                    {
                        var slash = name.IndexOfAny(new[] { '/', '\\' });
                        if (slash < 0) continue; // a file beside the top folder: GitHub never makes one
                        name = name.Substring(slash + 1);
                    }
                    if (preserveUser && name.Split(new[] { '/', '\\' })[0].Equals("USER", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var target = Path.GetFullPath(Path.Combine(root, name.Replace('/', '\\')));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // never write outside the folder
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                    written.Add(target.Substring(root.Length));
                }
            }
            return written;
        }
    }
}
