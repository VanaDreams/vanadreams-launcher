using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;

namespace Vanadreams.Services
{
    internal sealed class ClientReuseIndex
    {
        private const string Resource = "Vanadreams.ClientReuseIndex";
        private static readonly Lazy<ClientReuseIndex> Bundled = new Lazy<ClientReuseIndex>(() =>
        {
            using (var resource = typeof(ClientReuseIndex).Assembly.GetManifestResourceStream(Resource))
            using (var reader = new StreamReader(new GZipStream(resource, CompressionMode.Decompress)))
                return new ClientReuseIndex(reader);
        });
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
        private readonly Dictionary<string, Archive> archives = new Dictionary<string, Archive>(Names);

        private sealed class Archive
        {
            public string Hash;
            public string Destination;
            public readonly List<Entry> Files = new List<Entry>();
        }

        private sealed class Entry
        {
            public string Name;
            public long Size;
            public string Hash;
        }

        internal static ClientReuseIndex Published => Bundled.Value;

        internal ClientReuseIndex(TextReader reader)
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var header = line.Split('\t');
                var archive = new Archive { Hash = header[1], Destination = header[2] };
                while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                {
                    var fields = line.Split('\t');
                    archive.Files.Add(new Entry
                    {
                        Name = fields[0],
                        Size = long.Parse(fields[1], CultureInfo.InvariantCulture),
                        Hash = fields[2],
                    });
                }
                archives.Add(header[0], archive);
            }
        }

        internal bool TryCopy(ClientChunk chunk, string source, string stage,
            IDictionary<string, string> expected, CancellationToken ct)
        {
            if (chunk.ChapterOne || !archives.TryGetValue(chunk.Name, out var archive) ||
                !Names.Equals(archive.Hash, chunk.Sha256) || archive.Destination != ManagedClient.Destination(chunk))
                return false;

            // On a miss, normal extraction overwrites every file from this archive before verification.
            // The caller records these hashes only when the entire archive came from matching local bytes.
            foreach (var entry in archive.Files)
            {
                ct.ThrowIfCancellationRequested();
                var relative = archive.Destination + "/" + entry.Name;
                try
                {
                    var inputPath = ManagedClient.SafePath(source, relative);
                    using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        64 * 1024, FileOptions.SequentialScan))
                    {
                        if (input.Length != entry.Size) return false;
                        var outputPath = ManagedClient.SafePath(stage, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                        using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var sha = SHA256.Create())
                        using (var checkedInput = new CryptoStream(input, sha, CryptoStreamMode.Read))
                        {
                            checkedInput.CopyTo(output);
                            if (!Names.Equals(ManagedClient.Hex(sha.Hash), entry.Hash)) return false;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    return false;
                }
            }
            foreach (var entry in archive.Files)
                expected[archive.Destination + "/" + entry.Name] = entry.Hash;
            return true;
        }
    }
}
