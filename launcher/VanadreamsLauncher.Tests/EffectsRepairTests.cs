using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class EffectsRepairTests
    {
        private string root;
        private string Boot => Effects.Folder(root);
        private const string Version = "30260904_1";

        [TestInitialize]
        public void Make()
        {
            root = Path.Combine(Path.GetTempPath(), "vdl-effects-repair-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Boot);
            foreach (var rel in EffectsRepair.Missing(root))
            {
                var path = Path.Combine(Boot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "original " + rel);
            }
        }

        [TestCleanup]
        public void Clean() => Directory.Delete(root, true);

        private static MemoryStream Zip(params string[] names)
        {
            var file = new MemoryStream();
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var name in names)
                    using (var w = new StreamWriter(zip.CreateEntry(name).Open()))
                        w.Write("published " + name);
            file.Position = 0;
            return file;
        }

        private static ClientManifest Manifest(byte[] archive)
        {
            using (var h = SHA256.Create())
                return new ClientManifest
                {
                    Version = Version,
                    Chunks = { new ClientChunk
                    {
                        Name = "looks-effects.zip", Dest = "{ashita}/bootloader", Size = archive.Length,
                        Sha256 = BitConverter.ToString(h.ComputeHash(archive)).Replace("-", ""),
                    } },
                };
        }

        private sealed class Handler : HttpMessageHandler
        {
            public int Count;
            public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Send;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Count++;
                return Send(request, ct);
            }
        }

        [TestMethod]
        public async Task HealthyOrOffCopies_DoesNotUseNetwork()
        {
            Effects.Apply(root, true);
            using (var client = new HttpClient(new Handler { Send = (r, c) => throw new Exception("Unexpected request.") }))
            {
                var result = await EffectsRepair.TryAsync(root, null, null, client, TimeSpan.FromSeconds(1), () => false);
                Assert.AreEqual((int?)0, result);
                Assert.AreEqual(0, EffectsRepair.Missing(root).Count);
                Assert.IsTrue(File.Exists(Path.Combine(Boot, "d3d9.dll.off")));
            }
        }

        [TestMethod]
        public async Task ExactPackage_RestoresOnlyMissingFiles()
        {
            File.Delete(Path.Combine(Boot, "d3d9.dll"));
            var shader = "reshade-shaders/Shaders/MultiLUT.fx";
            File.Delete(Path.Combine(Boot, shader));
            File.Move(Path.Combine(Boot, "d3d8.dll"), Path.Combine(Boot, "d3d8.dll.off"));
            using (var pack = Zip("d3d9.dll", shader, "ReShade.ini", "d3d8.dll", "not-a-bundled-file"))
            {
                var bytes = pack.ToArray();
                var handler = new Handler { Send = (r, c) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }) };
                using (var client = new HttpClient(handler))
                {
                    var result = await EffectsRepair.TryAsync(root, Version, Manifest(bytes), client, TimeSpan.FromSeconds(2), () => false);
                    Assert.AreEqual((int?)2, result);
                    Assert.AreEqual(1, handler.Count);
                }
            }
            Assert.AreEqual("published d3d9.dll", File.ReadAllText(Path.Combine(Boot, "d3d9.dll")));
            Assert.AreEqual("published " + shader, File.ReadAllText(Path.Combine(Boot, shader)));
            Assert.AreEqual("original ReShade.ini", File.ReadAllText(Path.Combine(Boot, "ReShade.ini")));
            Assert.AreEqual("original d3d8.dll", File.ReadAllText(Path.Combine(Boot, "d3d8.dll.off")));
            Assert.IsFalse(File.Exists(Path.Combine(Boot, "d3d8.dll")));
            Assert.IsFalse(File.Exists(Path.Combine(Boot, "not-a-bundled-file")));
        }

        [TestMethod]
        public async Task HashMismatch_WritesNothing()
        {
            var dll = Path.Combine(Boot, "d3d9.dll");
            File.Delete(dll);
            using (var pack = Zip("d3d9.dll"))
            {
                var bytes = pack.ToArray();
                var manifest = Manifest(bytes);
                manifest.Chunks[0].Sha256 = new string('0', 64);
                var handler = new Handler { Send = (r, c) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }) };
                using (var client = new HttpClient(handler))
                {
                    Assert.IsNull(await EffectsRepair.TryAsync(root, Version, manifest, client, TimeSpan.FromSeconds(2), () => false));
                }
            }
            Assert.IsFalse(File.Exists(dll));
        }

        [TestMethod]
        public void InvalidArchiveOrPath_WritesNothing()
        {
            var required = new List<string> { "d3d9.dll" };
            foreach (var names in new[]
            {
                new[] { "d3d9.dll", "../outside.dll" },
                new[] { "d3d9.dll", "D3D9.DLL" },
                new[] { "d3d9.dll", "C:/outside.dll" },
                new[] { "d3d8.dll" },
            })
                using (var pack = Zip(names))
                    Assert.ThrowsException<IOException>(() => EffectsRepair.ReadEntries(pack, required, CancellationToken.None));
            Assert.AreEqual("original d3d9.dll", File.ReadAllText(Path.Combine(Boot, "d3d9.dll")));
        }

        [TestMethod]
        public async Task OfflineTimeout_DoesNotInstallEvenIfResponseArrivesLater()
        {
            var dll = Path.Combine(Boot, "d3d9.dll");
            File.Delete(dll);
            using (var pack = Zip("d3d9.dll"))
            {
                var bytes = pack.ToArray();
                var gate = new TaskCompletionSource<HttpResponseMessage>();
                var handler = new Handler { Send = (r, c) => gate.Task };
                using (var client = new HttpClient(handler))
                {
                    var result = await EffectsRepair.TryAsync(root, Version, Manifest(bytes), client, TimeSpan.FromMilliseconds(30), () => false);
                    Assert.IsNull(result);
                    Assert.AreEqual(1, handler.Count);
                    gate.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
                    await Task.Delay(50);
                }
            }
            Assert.IsFalse(File.Exists(dll));
        }

        [TestMethod]
        public void PotatoBalancedAndOtherServers_DoNotTriggerRepair()
        {
            File.Delete(Path.Combine(Boot, "d3d9.dll"));
            var settings = new LauncherSettings { LowSpec = false };
            var profile = new Profile { Command = new LoaderCommand { Server = LoaderCommand.VanadreamsServer }, GfxPreset = "potato" };
            Assert.IsFalse(EffectsRepair.Needed(profile, settings, root));
            profile.GfxPreset = "balanced";
            Assert.IsFalse(EffectsRepair.Needed(profile, settings, root));
            profile.GfxPreset = "pretty";
            settings.LowSpec = true;
            Assert.IsTrue(EffectsRepair.Needed(profile, settings, root));
            profile.Command = new LoaderCommand { Server = "example.org" };
            Assert.IsFalse(EffectsRepair.Needed(profile, settings, root));
        }
    }
}
