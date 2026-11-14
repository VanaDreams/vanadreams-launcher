using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class BugReportSubmissionTests
    {
        [TestMethod]
        public void Report_form_has_fixed_https_destination_and_public_version_only()
        {
            var uri = new Uri(BugReportSubmission.FormUrl(new Version(0, 2, 59)));
            Assert.AreEqual("https", uri.Scheme);
            Assert.AreEqual("vanadreams.org", uri.Host);
            Assert.AreEqual("/report/", uri.AbsolutePath);
            Assert.AreEqual("?source=launcher&version=0.2.59", uri.Query);
            Assert.AreEqual("", uri.UserInfo);
            Assert.AreEqual("", uri.Fragment);
        }

        [TestMethod]
        public void Missing_version_does_not_stop_reporting()
        {
            Assert.AreEqual("https://vanadreams.org/report/?source=launcher", BugReportSubmission.FormUrl(null));
        }
    }
}
