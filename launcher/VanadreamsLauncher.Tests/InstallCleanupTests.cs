using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class InstallCleanupTests
    {
        private string root;
        private string downloads;
        private string[] targets;

        [TestInitialize]
        public void Before()
        {
            root = Path.Combine(Path.GetTempPath(), "vd-cleanup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            downloads = Path.Combine(root, "downloads");
            targets = new[] { Path.Combine(root, "game") };
        }

        [TestCleanup]
        public void After() => Directory.Delete(root, true);

        [TestMethod]
        public void FindsOnlyKnownScratchDirectoriesAndGameDownloads()
        {
            var abandoned = Stage();
            var registered = Stage();
            var withPlayerData = Stage();
            var similar = Path.Combine(root, InstallCleanup.StagePrefix + "personal-copy");
            var working = Path.Combine(root, "game.vd-30260904_1-0123456789ab-01234567");
            var pathsToKeep = new[] { registered, withPlayerData, similar, working };
            foreach (var directory in pathsToKeep) Write(Path.Combine(directory, "game.DAT"));
            Write(Path.Combine(withPlayerData, ClientInstall.GameFolderName, "USER", "macro.dat"));
            var protectedPaths = new[] { ClientInstall.GameFolder(registered) };
            var cache = Path.Combine(downloads, "managed-client", new string('a', 64));
            var partial = Write(Path.Combine(cache, "base.zip.part"));
            var archive = Write(Path.Combine(downloads, "client", "30260904_1", "base.zip"));
            var unrelated = Write(Path.Combine(cache, "readme.txt"));
            var addon = Write(Path.Combine(downloads, "addons", "addon.zip"));

            var entries = InstallCleanup.Find(targets, protectedPaths, downloads);
            CollectionAssert.AreEquivalent(new[] { abandoned, partial, archive }, entries.Select(x => x.Path).ToArray());
            var result = InstallCleanup.Remove(entries.Where(x => !x.Download).ToArray(), targets, protectedPaths, downloads);
            Assert.AreEqual(1, result.Removed);
            Assert.IsFalse(Directory.Exists(abandoned));
            Assert.IsTrue(pathsToKeep.All(Directory.Exists));
            Assert.IsTrue(new[] { partial, archive, unrelated, addon }.All(File.Exists));

            result = InstallCleanup.Remove(entries.Where(x => x.Download).ToArray(), targets, protectedPaths, downloads);
            Assert.AreEqual(2, result.Removed);
            Assert.IsFalse(File.Exists(partial));
            Assert.IsFalse(File.Exists(archive));
            Assert.IsTrue(File.Exists(unrelated));
            Assert.IsTrue(File.Exists(addon));
        }

        [TestMethod]
        public void LeavesFilesThatChangedAfterThePreview()
        {
            var stage = Stage();
            var protectedPaths = Array.Empty<string>();
            var preview = InstallCleanup.Find(targets, protectedPaths, downloads);
            File.AppendAllText(Path.Combine(stage, "remaining.DAT"), "new content");

            var result = InstallCleanup.Remove(preview, targets, protectedPaths, downloads);
            Assert.AreEqual(0, result.Removed);
            Assert.AreEqual(1, result.Skipped);
            Assert.IsTrue(Directory.Exists(stage));
        }

        [TestMethod]
        public void ActiveInstallLockPreventsCleanup()
        {
            var stage = Stage();
            var protectedPaths = Array.Empty<string>();
            var preview = InstallCleanup.Find(targets, protectedPaths, downloads);
            using (InstallCleanup.Lock(downloads))
                Assert.ThrowsException<IOException>(() => InstallCleanup.Remove(preview, targets, protectedPaths, downloads));

            Assert.IsTrue(Directory.Exists(stage));
        }

        private string Stage()
        {
            var folder = Path.Combine(root, InstallCleanup.StagePrefix + Guid.NewGuid().ToString("N"));
            Write(Path.Combine(folder, "remaining.DAT"));
            return folder;
        }

        private static string Write(string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, "fixture");
            return file;
        }
    }
}
