using System;
using System.IO;
using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class IntegratedClientTests
    {
        private string _scratch;
        [TestInitialize]
        public void Setup()
        {
            _scratch = Path.Combine(Path.GetTempPath(), "VanaDreams-integrated-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scratch);
        }

        [TestCleanup]
        public void Cleanup() => Directory.Delete(_scratch, true);

        [TestMethod]
        public void ManifestMustBeIntegratedForTheCurrentVersion()
        {
            var v = ClientInstall.IntegratedVersion;
            Assert.IsTrue(ClientInstall.ManifestUrl(v).EndsWith("/manifest-integrated-20261124.json"));
            Assert.IsTrue(ClientInstall.ManifestUrl("old").EndsWith("/manifest.json"));
            var old = "{\"client_ver\":\"" + v + "\",\"chunks\":[{\"name\":\"base.zip\"}]}";
            Assert.ThrowsException<InvalidDataException>(() => ClientInstall.ParseManifest(v, old));
            var current = old.Replace("]}", ",{\"name\":\"" + ClientInstall.IntegratedChunk +
                "\",\"size\":300,\"sha256\":\"" + new string('b', 64) + "\"}]}");
            Assert.AreEqual(ClientInstall.IntegratedChunk, ClientInstall.ParseManifest(v, current).Chunks[1].Name);
        }

        [TestMethod]
        public void OctoberDriftGoesToSiblingAndExternalCopyIsUntouched()
        {
            var root = Path.Combine(_scratch, "game");
            var game = ClientInstall.GameFolder(root);
            Directory.CreateDirectory(game);
            var cfg = Path.Combine(game, "patch.cfg");
            File.WriteAllText(cfg, "30261006_0");
            var v = ClientInstall.IntegratedVersion;
            Assert.ThrowsException<InvalidOperationException>(() => ClientInstall.ResolveUpdateDestination(root, v, true));
            File.WriteAllText(ClientInstall.RecordPath(root), v + " base.zip hash");
            Assert.AreEqual(root + "-VanaDreams-" + v, ClientInstall.ResolveUpdateDestination(root, v, true));
            Assert.AreEqual("30261006_0", File.ReadAllText(cfg));
            Assert.ThrowsException<InvalidOperationException>(() => ClientInstall.ResolveUpdateDestination(root, v, false));
        }

        [TestMethod]
        public void RecordDoesNotHideDriftedTablesOrIncompleteUpdate()
        {
            var v = ClientInstall.IntegratedVersion;
            var integrated = new ClientChunk { Name = ClientInstall.IntegratedChunk, Sha256 = "test" };
            var m = new ClientManifest { Version = v };
            m.Chunks.Add(integrated);
            Directory.CreateDirectory(ClientInstall.GameFolder(_scratch));
            File.WriteAllText(Path.Combine(ClientInstall.GameFolder(_scratch), "patch.cfg"), v);
            File.WriteAllText(ClientInstall.RecordPath(_scratch), v + " " + integrated.Name + " test");
            File.WriteAllText(ClientInstall.RecordPath(_scratch) + ".integrated-incomplete", v);
            Assert.IsTrue(ClientInstall.IntegratedInstallIncomplete(_scratch));
            Assert.AreSame(integrated, ClientInstall.Planned(m, _scratch, null)[0]);
        }

        [TestMethod]
        public void ManagedExtractionKeepsExistingUserFiles()
        {
            var target = Path.Combine(_scratch, "game");
            Directory.CreateDirectory(Path.Combine(target, "USER"));
            var saved = Path.Combine(target, "USER", "macros.txt");
            File.WriteAllText(saved, "player settings");
            var file = Path.Combine(_scratch, "download.zip");
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("USER/macros.txt").Open())) writer.Write("default");
                using (var writer = new StreamWriter(zip.CreateEntry("FTABLE.DAT").Open())) writer.Write("updated");
            }
            Downloader.ExtractZipOverwrite(file, target, preserveUser: true);
            Assert.AreEqual("player settings", File.ReadAllText(saved));
            Assert.AreEqual("updated", File.ReadAllText(Path.Combine(target, "FTABLE.DAT")));
        }

        [TestMethod]
        public void IntegratedPrivatePivotRemovesOnlyChapterOverlaysWithoutEditingSharedConfig()
        {
            var source = PivotConfig.IniPath(_scratch);
            Directory.CreateDirectory(Path.GetDirectoryName(source));
            var original = "[settings]\ndebug_log = true\n[overlays]\n0 = custom\n1 = " +
                ChapterOnePatch.PublicOverlay + "\n2 = vanadreams-looks\n";
            File.WriteAllText(source, original);
            var boot = IniFile.FromText("[ashita.polplugins]\npivot = 1\n");
            Assert.IsTrue(ChapterOnePatch.ConfigureIntegratedLaunch(_scratch, boot));
            var profile = boot.Get("ashita.polplugins.args", "pivot");
            var filtered = File.ReadAllText(Path.Combine(Path.GetDirectoryName(source), profile + ".ini"));
            StringAssert.Contains(filtered, "0 = custom");
            StringAssert.Contains(filtered, "1 = vanadreams-looks");
            Assert.IsFalse(filtered.Contains(ChapterOnePatch.PublicOverlay));
            Assert.AreEqual(original, File.ReadAllText(source));
        }
    }
}
