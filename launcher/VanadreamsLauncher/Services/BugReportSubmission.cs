using System;

namespace Vanadreams.Services
{
    /// <summary>Only public launcher version metadata is passed to the authenticated report form.</summary>
    public static class BugReportSubmission
    {
        public const string ReportForm = "https://vanadreams.org/report/";

        public static string FormUrl(Version version)
        {
            return ReportForm + "?source=launcher" + (version == null ? "" : "&version=" + Uri.EscapeDataString(version.ToString()));
        }
    }
}
