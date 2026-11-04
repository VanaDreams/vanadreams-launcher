using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class CaptureImportTests
    {
        [TestMethod]
        public void RouteUrl_swaps_the_last_part_of_the_capture_address()
        {
            var u = new CaptureUploader { Url = "https://vanadreams.org/api/public/vanadreams/capture" };
            Assert.AreEqual("https://vanadreams.org/api/public/vanadreams/import", u.RouteUrl("import"));
            Assert.AreEqual("https://vanadreams.org/api/public/vanadreams/import-status", u.RouteUrl("import-status"));
        }

        [TestMethod]
        public void InterpretImport_reads_the_sites_answers()
        {
            var ok = CaptureUploader.InterpretImport(200, "{\"ok\":true,\"state\":\"requested\",\"message\":\"Asked for.\"}");
            Assert.IsTrue(ok.Ok);
            Assert.AreEqual("Asked for.", ok.Message);
            var none = CaptureUploader.InterpretImport(404, "{\"ok\":false,\"error\":\"No capture of Tyr has been sent from your login yet. Send it first.\"}");
            Assert.IsFalse(none.Ok);
            StringAssert.Contains(none.Message, "Send it first");
            Assert.IsFalse(CaptureUploader.InterpretImport(429, "").Ok);
        }

        [TestMethod]
        public void InterpretStatus_is_ok_only_when_the_import_is_done()
        {
            var done = CaptureUploader.InterpretStatus(200, "{\"ok\":true,\"state\":\"done\",\"message\":\"Tyr is imported. Log in on it.\"}");
            Assert.IsTrue(done.Ok);
            Assert.AreEqual("done", done.State);
            var refused = CaptureUploader.InterpretStatus(200, "{\"ok\":true,\"state\":\"refused\",\"message\":\"Tyr was in the game.\"}");
            Assert.IsFalse(refused.Ok);
            Assert.AreEqual("refused", refused.State);
            Assert.AreEqual("none", CaptureUploader.InterpretStatus(200, "{\"ok\":true,\"state\":\"none\"}").State);
        }
    }
}
