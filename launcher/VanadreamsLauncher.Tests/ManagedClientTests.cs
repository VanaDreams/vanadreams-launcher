using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class ManagedClientTests
    {
        private string root;
        [TestInitialize] public void Before() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "vd-managed-" + Guid.NewGuid().ToString("N")));
        [TestCleanup] public void After() => Directory.Delete(root, true);

        [TestMethod]
        public void ManagedDestinationsCannotTargetExternalFolder()
        {
            Assert.AreEqual(ManagedClient.Payload + "/polplugins/DATs/text",
                ManagedClient.Destination(new ClientChunk { Dest = "{ashita}/polplugins/DATs/text" }));
            Assert.ThrowsException<InvalidDataException>(() => ManagedClient.Destination(new ClientChunk { Dest = @"D:\Retail" }));
            Assert.ThrowsException<InvalidDataException>(() => ManagedClient.SafePath(root, "x/../retail"));
        }

        [TestMethod]
        public void LastVerifiedLayerWinsAndUserOnlyTransfer()
        {
            var expected = new Dictionary<string, string>();
            foreach (var contents in new[] { "old", "final" })
            {
                var zip = Path.Combine(root, contents + ".zip");
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                using (var w = new StreamWriter(archive.CreateEntry("ROM/1/1.DAT").Open())) w.Write(contents);
                ManagedClient.ExtractVerified(zip, root, ClientInstall.GameFolderName, expected, CancellationToken.None);
            }
            var rel = ClientInstall.GameFolderName + "/ROM/1/1.DAT";
            var dat = ManagedClient.SafePath(root, rel);
            Assert.AreEqual("final", File.ReadAllText(dat));
            Assert.AreEqual(ClientInstall.Sha256Of(dat), expected[rel]);

            var user = ManagedClient.SafePath(root, ClientInstall.GameFolderName + "/USER");
            Directory.CreateDirectory(user);
            File.WriteAllText(Path.Combine(user, "mcr.dat"), "macros");
            var next = Path.Combine(root, "other-generation");
            ManagedClient.TransferUser(root, next);
            Assert.AreEqual("macros", File.ReadAllText(ManagedClient.SafePath(next, ClientInstall.GameFolderName + "/USER/mcr.dat")));
            Assert.IsFalse(File.Exists(ManagedClient.SafePath(next, rel)));
        }

        [TestMethod]
        public void CanonicalManifestRequiresChapterAndSeptember()
        {
            var m = new ClientManifest { Version = ManagedClient.Version };
            m.Chunks.Add(new ClientChunk { Name = "base.zip", Dest = ClientInstall.GameFolderName, Sha256 = new string('1', 64), Size = 10 });
            Assert.ThrowsException<InvalidDataException>(() => ManagedClient.Key(m));
            m.Chunks.Add(new ClientChunk { Name = ChapterOnePatch.Package, Dest = "{ashita}/polplugins/DATs/vanadreams-chapter-i", Sha256 = new string('a', 64), Size = 10, ChapterOne = true });
            var key = ManagedClient.Key(m);
            m.Chunks[0].Sha256 = new string('2', 64);
            Assert.AreNotEqual(key, ManagedClient.Key(m));
            m.Version = "30261001_1";
            Assert.ThrowsException<InvalidDataException>(() => ManagedClient.Key(m));
        }

        [TestMethod, TestCategory("PublishedFixture")]
        public async Task PublishedManifestFreshAndDriftedGenerations()
        {
            var fixture = Environment.GetEnvironmentVariable("VD_MANAGED_TEST_FIXTURE");
            if (string.IsNullOrWhiteSpace(fixture)) { Assert.Inconclusive("Set an isolated published client archive fixture to exercise this test."); return; }
            var manifest = ClientManifest.Parse(File.ReadAllText(Path.Combine(fixture, "manifest.json")));
            var key = ManagedClient.Key(manifest);
            var cache = Path.Combine(root, "downloads", "managed-client", key);
            Directory.CreateDirectory(cache);
            foreach (var c in manifest.Chunks)
            {
                // The runner pre-populates this cache with hard links/safe copies from a separate read-only fixture.
                var link = Path.Combine(fixture, "downloads", "managed-client", key, c.Name);
                Assert.IsTrue(File.Exists(link), c.Name);
            }

            var foreign = Path.Combine(root, "retail");
            Directory.CreateDirectory(foreign);
            File.WriteAllText(Path.Combine(foreign, "retail-sentinel.dat"), "preserve");
            var downloads = Path.Combine(fixture, "downloads");
            var first = await ManagedClient.InstallAsync(null, manifest, foreign, null, downloads, null, CancellationToken.None);
            Assert.AreNotEqual(foreign, first);
            Assert.IsTrue(File.Exists(Path.Combine(foreign, "retail-sentinel.dat")));
            ManagedClient.Verify(first, key, CancellationToken.None);
            var managedDat = Path.Combine(ClientInstall.GameFolder(first), "FTABLE.DAT");
            var firstHash = ClientInstall.Sha256Of(managedDat);
            var user = Path.Combine(ClientInstall.GameFolder(first), "USER");
            Directory.CreateDirectory(user);
            File.WriteAllText(Path.Combine(user, "test-macro.dat"), "preserve");
            Assert.AreEqual(first, await ManagedClient.InstallAsync(null, manifest, first, first, downloads, null, CancellationToken.None));
            File.AppendAllText(managedDat, "drift");
            Assert.ThrowsException<IOException>(() => ManagedClient.Verify(first, key, CancellationToken.None));
            var second = await ManagedClient.InstallAsync(null, manifest, first, first, downloads, null, CancellationToken.None);
            Assert.AreNotEqual(first, second);
            Assert.IsTrue(Directory.Exists(first));
            Assert.AreEqual(firstHash, ClientInstall.Sha256Of(Path.Combine(ClientInstall.GameFolder(second), "FTABLE.DAT")));
            Assert.AreEqual("preserve", File.ReadAllText(Path.Combine(ClientInstall.GameFolder(second), "USER", "test-macro.dat")));
        }
    }
}

