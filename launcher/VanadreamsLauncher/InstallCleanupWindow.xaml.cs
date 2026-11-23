using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Vanadreams.Services;

namespace Vanadreams
{
    public partial class InstallCleanupWindow : Window
    {
        private readonly IReadOnlyList<InstallCleanup.Entry> entries;
        internal IReadOnlyList<InstallCleanup.Entry> Selected { get; private set; }

        internal InstallCleanupWindow(IReadOnlyList<InstallCleanup.Entry> entries)
        {
            this.entries = entries;
            InitializeComponent();
            Title = Heading.Text = UiText.T("Clean up failed installs");
            Explanation.Text = UiText.T("Review the files to delete permanently. Your installed games and player data are kept.");
            var downloads = entries.Where(entry => entry.Download).Sum(entry => entry.Bytes);
            IncludeDownloads.IsEnabled = entries.Any(entry => entry.Download);
            IncludeDownloads.Content = UiText.F("Include saved game downloads ({0})", FormatSize(downloads));
            DownloadWarning.Text = UiText.T("Removing saved downloads means Patch may need to download those parts again.");
            CancelButton.Content = UiText.T("Cancel");
            RemoveButton.Content = UiText.T("Delete selected files");
            Refresh();
        }

        internal static string FormatSize(long bytes) =>
            bytes >= 1073741824 ? UiText.F("{0:0.00} GB", bytes / 1073741824.0)
            : bytes >= 1048576 ? UiText.F("{0:0.0} MB", bytes / 1048576.0)
            : bytes >= 1024 ? UiText.F("{0:0.0} KB", bytes / 1024.0)
            : UiText.F("{0} bytes", bytes);

        private void SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) Refresh();
        }

        private void Refresh()
        {
            Selected = entries.Where(entry => !entry.Download || IncludeDownloads.IsChecked == true).ToArray();
            var stages = Selected.Where(entry => !entry.Download)
                .Select(entry => UiText.F("{0} · unfinished install ({1})", entry.Path, FormatSize(entry.Bytes)));
            var downloads = Selected.Where(entry => entry.Download)
                .GroupBy(entry => Path.GetDirectoryName(entry.Path), StringComparer.OrdinalIgnoreCase)
                .Select(folder => UiText.F("{0} · saved game downloads ({1})", folder.Key, FormatSize(folder.Sum(entry => entry.Bytes))));
            Preview.Text = Selected.Count == 0 ? UiText.T("No unfinished install folders were found. You can still choose to clear saved game downloads.")
                : string.Join(Environment.NewLine + Environment.NewLine, stages.Concat(downloads));
            Total.Text = UiText.F("Selected size on disk: {0}", FormatSize(Selected.Sum(entry => entry.Bytes)));
            RemoveButton.IsEnabled = Selected.Count != 0;
        }

        private void Remove_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
