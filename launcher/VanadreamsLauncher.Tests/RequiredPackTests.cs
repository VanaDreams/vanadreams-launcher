using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class RequiredPackTests
    {
        private string _root;
        private string _downloads;
        private ClientManifest _manifest;
        private ClientChunk _hair;
        private ClientChunk _faces;
        private Dictionary<string, byte[]> _archives;

        [TestInitialize]
        public void Make()
        {
            _root = Path.Combine(Path.GetTempPath(), "vdl-faces-" + Guid.NewGuid().ToString("N"));
            _downloads = Path.Combine(_root, "downloads");
            Directory.CreateDirectory(ClientInstall.GameFolder(_root));
            File.WriteAllText(Path.Combine(ClientInstall.GameFolder(_root), "patch.cfg"), "30260904_1\n");
            _archives = new Dictionary<string, byte[]>();
            _hair = Pack("hair.zip", "old DLL", "old table");
            _faces = Pack("faces.zip", "new DLL", "new table");
            _faces.Required = true;
            _faces.Requires.Add(new ChunkRequirement { Name = _hair.Name, Sha256 = _hair.Sha256 });
            _manifest = new ClientManifest { Version = "30260904_1", Chunks = new List<ClientChunk> { _faces, _hair } };
        }

        [TestCleanup]
        public void Clean() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        private ClientChunk Pack(string name, string dll, string table)
        {
            var archive = Path.Combine(_root, name);
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                foreach (var pair in new[] { new[] { "FFXiMain.dll", dll }, new[] { "ROM9/FTABLE9.DAT", table } })
                    using (var writer = new StreamWriter(zip.CreateEntry(pair[0]).Open())) writer.Write(pair[1]);
            var chunk = new ClientChunk { Name = name, Sha256 = ClientInstall.Sha256Of(archive), Size = new FileInfo(archive).Length, Unpacked = 100 };
            if (name == "faces.zip")
                using (var zip = ZipFile.OpenRead(archive))
                    foreach (var entry in zip.Entries)
                    {
                        var path = Path.Combine(_root, "check.dat");
                        entry.ExtractToFile(path, true);
                        chunk.Verify.Add(new ChunkFile { Path = entry.FullName, Sha256 = ClientInstall.Sha256Of(path) });
                    }
            _archives[name] = File.ReadAllBytes(archive);
            return chunk;
        }

        private void Record(params ClientChunk[] chunks) => File.WriteAllLines(ClientInstall.RecordPath(_root),
            chunks.Select(c => _manifest.Version + " " + c.Name + " " + c.Sha256));

        private sealed class Archives : HttpMessageHandler
        {
            internal Dictionary<string, byte[]> Files;
            internal string Fail;
            internal readonly List<string> Requested = new List<string>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var name = Path.GetFileName(request.RequestUri.LocalPath);
                Requested.Add(name);
                return Task.FromResult(name == Fail
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Files[name]) });
            }
        }

        private async Task<Archives> Install(string fail = null, bool updatesOnly = false)
        {
            var handler = new Archives { Files = _archives, Fail = fail };
            using (var http = new HttpClient(handler))
                await ClientInstall.InstallFilesAsync(new Downloader(http), _manifest, _root, _downloads, null,
                    CancellationToken.None, updatesOnly: updatesOnly);
            return handler;
        }

        [TestMethod]
        public void Metadata_parses_and_orders_even_when_faces_arrive_first()
        {
            var json = Json.Stringify(new { client_ver = _manifest.Version, chunks = new object[] {
                new { name = _faces.Name, sha256 = _faces.Sha256, required = true,
                    requires = new[] { new { name = _hair.Name, sha256 = _hair.Sha256 } },
                    verify = _faces.Verify.Select(f => new { path = f.Path, sha256 = f.Sha256 }).ToArray() },
                new { name = _hair.Name, sha256 = _hair.Sha256 } } });
            var parsed = ClientManifest.Parse(json);
            CollectionAssert.AreEqual(new[] { "hair.zip", "faces.zip" }, parsed.Chunks.Select(c => c.Name).ToList());
            Assert.IsTrue(parsed.Chunks[1].Required);
            Assert.AreEqual(2, parsed.Chunks[1].Verify.Count);
        }

        [TestMethod]
        public async Task Fresh_install_writes_latest_files_receipts_and_skips_completed_downloads()
        {
            var first = await Install();
            CollectionAssert.AreEqual(new[] { "hair.zip", "faces.zip" }, first.Requested);
            Assert.IsTrue(ClientInstall.FilesMatch(_faces, _root));
            Assert.IsTrue(ClientInstall.Done(_root, _manifest.Version).Contains(_faces.Name + " " + _faces.Sha256));
            Assert.AreEqual(0, (await Install()).Requested.Count);
        }

        [TestMethod]
        public async Task Missing_or_changed_installed_bytes_repair_despite_matching_receipt()
        {
            await Install();
            File.WriteAllText(Path.Combine(ClientInstall.GameFolder(_root), "FFXiMain.dll"), "overwritten");
            CollectionAssert.AreEqual(new[] { "faces.zip" }, ClientInstall.Updates(_manifest, _root, null).Select(c => c.Name).ToList());
            await Install();
            File.Delete(Path.Combine(ClientInstall.GameFolder(_root), "ROM9", "FTABLE9.DAT"));
            Assert.AreEqual(1, ClientInstall.Updates(_manifest, _root, null).Count);
            Assert.IsTrue((await Install()).Requested.Contains("faces.zip"));
            Assert.IsTrue(ClientInstall.FilesMatch(_faces, _root));
        }

        [TestMethod]
        public async Task Replaying_hair_also_replays_faces_even_when_faces_initially_verify()
        {
            await Install();
            Record(_faces);
            var request = await Install();
            CollectionAssert.AreEqual(new[] { "hair.zip", "faces.zip" }, request.Requested);
            Assert.IsTrue(ClientInstall.FilesMatch(_faces, _root));
        }

        [TestMethod]
        public async Task Interrupted_replay_cannot_keep_old_success_receipt_for_faces()
        {
            await Install();
            Record(_faces);
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => Install("faces.zip"));
            Assert.IsFalse(ClientInstall.FilesMatch(_faces, _root));
            Assert.IsFalse(ClientInstall.Done(_root, _manifest.Version).Contains(_faces.Name + " " + _faces.Sha256));
            CollectionAssert.AreEqual(new[] { "faces.zip" }, (await Install()).Requested);
            Assert.IsTrue(ClientInstall.FilesMatch(_faces, _root));
        }

        [TestMethod]
        public async Task Failed_post_extract_verification_never_records_success()
        {
            _faces.Verify[0].Sha256 = new string('f', 64);
            await Assert.ThrowsExceptionAsync<IOException>(() => Install());
            Assert.IsFalse(ClientInstall.Done(_root, _manifest.Version).Contains(_faces.Name + " " + _faces.Sha256));
            Assert.AreEqual("faces.zip", ClientInstall.Updates(_manifest, _root, null).Single().Name);
        }

        [TestMethod]
        public async Task Imported_exact_version_gets_only_required_closure_and_stays_imported()
        {
            var unrelated = Pack("base.zip", "base DLL", "base table");
            _manifest.Chunks.Insert(0, unrelated);
            CollectionAssert.AreEqual(new[] { "hair.zip", "faces.zip" }, ClientInstall.Updates(_manifest, _root, null).Select(c => c.Name).ToList());
            CollectionAssert.AreEqual(new[] { "hair.zip", "faces.zip" }, (await Install(updatesOnly: true)).Requested);
            Assert.IsFalse(ClientInstall.Done(_root, _manifest.Version).Contains("@managed -"));
            Assert.AreEqual(0, ClientInstall.Updates(_manifest, _root, null).Count);
        }

        [TestMethod]
        public async Task Base_repair_replays_the_verified_face_pack_after_overwriting_shared_files()
        {
            var baseline = Pack("base.zip", "base DLL", "base table");
            _manifest.Chunks.Insert(0, baseline);
            await Install();
            Record(_hair, _faces);
            CollectionAssert.AreEqual(new[] { "base.zip", "faces.zip" }, (await Install()).Requested);
            Assert.IsTrue(ClientInstall.FilesMatch(_faces, _root));
        }

        [TestMethod]
        public async Task Partially_extracted_face_pack_has_no_success_receipt_and_resumes()
        {
            await Install();
            File.WriteAllText(Path.Combine(ClientInstall.GameFolder(_root), "FFXiMain.dll"), "damaged");
            var table = Path.Combine(ClientInstall.GameFolder(_root), "ROM9", "FTABLE9.DAT");
            using (var locked = new FileStream(table, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                await Assert.ThrowsExceptionAsync<IOException>(() => Install());
            Assert.IsFalse(ClientInstall.Done(_root, _manifest.Version).Contains(_faces.Name + " " + _faces.Sha256));
            await Install();
            Assert.IsTrue(ClientInstall.FilesMatch(_faces, _root));
        }

        [TestMethod]
        public async Task Interrupted_managed_repair_retains_full_install_identity()
        {
            var baseline = Pack("base.zip", "base DLL", "base table");
            _manifest.Chunks.Insert(0, baseline);
            Record(baseline, _hair, _faces);
            File.WriteAllText(ClientInstall.RecordPath(_root), _manifest.Version + " base.zip old-hash\n");
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => Install("base.zip", updatesOnly: true));
            Assert.IsTrue(ClientInstall.Done(_root, _manifest.Version).Contains("@managed -"));
            CollectionAssert.AreEqual(new[] { "base.zip", "hair.zip", "faces.zip" },
                ClientInstall.Updates(_manifest, _root, null).Select(c => c.Name).ToList());
        }

        [TestMethod]
        public void Imported_other_version_is_not_patched_with_binary_pack()
        {
            File.WriteAllText(Path.Combine(ClientInstall.GameFolder(_root), "patch.cfg"), "30261001_1\n");
            Assert.AreEqual(0, ClientInstall.Updates(_manifest, _root, null).Count);
        }

        [TestMethod]
        public void Missing_changed_or_cyclic_dependencies_fail_closed()
        {
            _faces.Requires[0].Sha256 = new string('0', 64);
            Assert.ThrowsException<InvalidDataException>(() => ClientInstall.Ordered(_manifest));
            _faces.Requires[0].Sha256 = _hair.Sha256;
            _hair.Requires.Add(new ChunkRequirement { Name = _faces.Name, Sha256 = _faces.Sha256 });
            Assert.ThrowsException<InvalidDataException>(() => ClientInstall.Ordered(_manifest));
            _hair.Requires.Clear();
            _manifest.Chunks.Remove(_hair);
            Assert.ThrowsException<InvalidDataException>(() => ClientInstall.Ordered(_manifest));
        }

        [TestMethod]
        public void Unsafe_verification_paths_and_duplicate_chunks_are_rejected()
        {
            foreach (var path in new[] { "../FFXiMain.dll", "C:/file", "/file", "ROM9/../../file", "ROM9\\..\\file" })
            {
                _faces.Verify[0].Path = path;
                Assert.ThrowsException<InvalidDataException>(() => ClientInstall.Ordered(_manifest));
            }
            _faces.Verify[0].Path = "FFXiMain.dll";
            _manifest.Chunks.Add(_faces);
            Assert.ThrowsException<InvalidDataException>(() => ClientInstall.Ordered(_manifest));
        }
    }
}
