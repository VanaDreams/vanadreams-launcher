using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class RetailClientIsolationTests
    {
        private string _temp;
        private string _retail;
        private string _managed;

        [TestInitialize]
        public void Setup()
        {
            _temp = Path.Combine(Path.GetTempPath(), "vd-retail-tests-" + Guid.NewGuid().ToString("N"));
            _retail = Copy("Retail");
            _managed = Copy("Vanadreams");
        }

        [TestCleanup]
        public void Cleanup() { if (Directory.Exists(_temp)) Directory.Delete(_temp, true); }

        [TestMethod]
        public void ExplicitSeparateRootAndPairedViewerAreRequired()
        {
            var protectedRoots = new[] { _managed };
            Assert.IsFalse(RetailClientIsolation.TryResolve("", Boot(_retail), protectedRoots, out _, out var blank));
            Assert.AreEqual(RetailClientIsolation.PickCopy, blank);
            Assert.IsFalse(RetailClientIsolation.TryResolve(_retail, Boot(_managed), protectedRoots, out _, out var mismatched));
            Assert.AreEqual(RetailClientIsolation.WrongViewer, mismatched);
            Assert.IsFalse(RetailClientIsolation.TryResolve(_managed, Boot(_managed), protectedRoots, out _, out var reused));
            Assert.AreEqual(RetailClientIsolation.SharedCopy, reused);
            Assert.IsTrue(RetailClientIsolation.TryResolve(ClientInstall.GameFolder(_retail), Boot(_retail), protectedRoots,
                out var root, out _, checkTree: true));
            Assert.AreEqual(_retail, root);
        }

        [TestMethod]
        public void LauncherOwnedRecordAndDotDotAliasAreNotRetail()
        {
            File.WriteAllText(ClientInstall.RecordPath(_retail), "launcher-owned fixture");
            Assert.IsFalse(RetailClientIsolation.TryResolve(_retail, Boot(_retail), Array.Empty<string>(), out _, out _));
            var alias = Path.Combine(_retail, "..", "Vanadreams");
            Assert.IsFalse(RetailClientIsolation.TryResolve(alias, Boot(_managed), new[] { _managed }, out _, out _));
        }

        [TestMethod]
        public void LinkedFilesInsideTheRetailTreeAreNotSafeForUpdates()
        {
            var retailDat = Path.Combine(ClientInstall.GameFolder(_retail), "linked.dat");
            var managedDat = Path.Combine(ClientInstall.GameFolder(_managed), "original.dat");
            File.WriteAllText(managedDat, "managed file");
            if (!CreateHardLink(retailDat, managedDat, IntPtr.Zero))
                Assert.Inconclusive("This test filesystem cannot create hardlinks: " + Marshal.GetLastWin32Error());
            Assert.IsFalse(RetailClientIsolation.TryResolve(_retail, Boot(_retail), new[] { _managed },
                out _, out var reason, checkTree: true));
            Assert.AreEqual(RetailClientIsolation.SharedCopy, reason);
            Assert.AreEqual("managed file", File.ReadAllText(managedDat));
        }

        [TestMethod]
        public void RetailRejectsMixedOrIncompleteWindowsRegistration()
        {
            var r = new RegisteredGame
            {
                GameFolder = ClientInstall.GameFolder(_retail) + "\\",
                ViewerFolder = ClientInstall.ViewerFolder(_retail),
            };
            Assert.IsFalse(RetailClientIsolation.RegistrationMatches(r, _retail));
            r.ComServers.Add(Path.Combine(r.GameFolder, "FFXi.dll"));
            r.ComServers.Add(Path.Combine(r.GameFolder, "FFXiMain.dll"));
            r.ComServers.Add(Core(_managed));
            Assert.IsFalse(RetailClientIsolation.RegistrationMatches(r, _retail));
            r.ComServers[2] = Core(_retail);
            Assert.IsTrue(RetailClientIsolation.RegistrationMatches(r, _retail));
            r.GameFolder = ClientInstall.GameFolder(_managed);
            Assert.IsFalse(RetailClientIsolation.RegistrationMatches(r, _retail));
        }

        [TestMethod]
        public void DeferredRegistryRestoreRefusesActiveOrReplacedSessions()
        {
            var script = GameRegistration.WatcherScript(new[] { 123 }, @"C:\Temp\restore.cmd", @"D:\Expected", 2);
            StringAssert.Contains(script, "Get-Process -Id 123");
            StringAssert.Contains(script, "Get-Process -Name 'pol','xiloader','Ashita-cli','Windower'");
            StringAssert.Contains(script, @"SOFTWARE\PlayOnlineEU\InstallFolder");
            StringAssert.Contains(script, @"D:\Expected\FINAL FANTASY XI");
            Assert.IsTrue(script.IndexOf("exit 0", StringComparison.Ordinal) < script.IndexOf("Start-Process", StringComparison.Ordinal));
        }

        private string Copy(string name)
        {
            var root = Path.Combine(_temp, name);
            var game = ClientInstall.GameFolder(root);
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(Path.GetDirectoryName(Core(root)));
            File.WriteAllText(Path.Combine(game, "FFXi.dll"), "");
            File.WriteAllText(Path.Combine(game, "FFXiMain.dll"), "");
            File.WriteAllText(Core(root), "");
            File.WriteAllText(Boot(root), "");
            return root;
        }

        private static string Core(string root) => Path.Combine(ClientInstall.ViewerFolder(root), "viewer", "com", "polcore.dll");
        private static string Boot(string root) => Path.Combine(ClientInstall.ViewerFolder(root), "pol.exe");

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr security);
    }
}
