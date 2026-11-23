using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class DownloadResumeTests
    {
        private string root;
        private const string Url = "https://example.test/game.zip";

        [TestInitialize]
        public void Before() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "vd-resume-" + Guid.NewGuid().ToString("N")));

        [TestCleanup]
        public void After() => Directory.Delete(root, true);

        [TestMethod]
        public async Task InterruptedArchiveResumesWithSavedBytesAndReportsThemImmediately()
        {
            var zip = Path.Combine(root, "game.zip");
            using (var http = new HttpClient(new Handler(request =>
            {
                Assert.IsNull(request.Headers.Range);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2 }) };
            })))
                await Assert.ThrowsExceptionAsync<IOException>(() => new Downloader(http).DownloadFileAsync(Url, zip, expectedSize: 5));

            Assert.IsFalse(File.Exists(zip));
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, File.ReadAllBytes(zip + ".part"));
            var reports = new Reports();
            using (var http = new HttpClient(new Handler(request =>
            {
                Assert.AreEqual("bytes=2-", request.Headers.Range.ToString());
                Assert.AreEqual(2L, reports.Items[0].Done, "The saved offset is shown before the server responds.");
                Assert.AreEqual(2L, reports.Items[0].ResumedFrom);
                return Range(2, new byte[] { 3, 4, 5 });
            })))
                await new Downloader(http).DownloadFileAsync(Url, zip, reports, expectedSize: 5);

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(zip));
            Assert.IsFalse(File.Exists(zip + ".part"));
            Assert.AreEqual(5L, reports.Items[reports.Items.Count - 1].Done);
        }

        [TestMethod]
        public async Task InvalidRangeDoesNotOverwriteSavedBytes()
        {
            var zip = Path.Combine(root, "game.zip");
            File.WriteAllBytes(zip + ".part", new byte[] { 1, 2 });
            using (var http = new HttpClient(new Handler(request => Range(1, new byte[] { 2, 3, 4, 5 }))))
                await Assert.ThrowsExceptionAsync<IOException>(() => new Downloader(http).DownloadFileAsync(Url, zip, expectedSize: 5));

            CollectionAssert.AreEqual(new byte[] { 1, 2 }, File.ReadAllBytes(zip + ".part"));
            Assert.IsFalse(File.Exists(zip));
        }

        private static HttpResponseMessage Range(long offset, byte[] bytes)
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + bytes.Length - 1, 5);
            return response;
        }

        private sealed class Reports : IProgress<DownloadProgress>
        {
            public readonly List<DownloadProgress> Items = new List<DownloadProgress>();
            public void Report(DownloadProgress value) => Items.Add(value);
        }

        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> response;
            public Handler(Func<HttpRequestMessage, HttpResponseMessage> response) { this.response = response; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(response(request));
        }
    }
}
