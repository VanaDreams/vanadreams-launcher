using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace Vanadreams.Services
{
    /// <summary>
    /// XIPivot 4.3.001 for Ashita v4 (HealsCodes, BSD 3-Clause; LICENSE.txt ships beside it) is carried inside the launcher,
    /// so the look and title overlays load on every install without a trip to the Addons page. Put in place when missing,
    /// with the release's own layout; the catalogue item "pivot" is ticked once, which turns it on in each boot profile.
    /// </summary>
    public static class XIPivotBundle
    {
        public const string Version = "4.3.001";
        internal const string ChapterPlugin = "pivot_chapter_i";

        /// <summary>A side-by-side plugin used only by a generated VanaDreams Chapter I boot profile.</summary>
        internal static void EnsureChapter(string target)
        {
            var res = Application.GetResourceStream(new Uri("pack://application:,,,/assets/xipivot/" + ChapterPlugin + ".dll"));
            if (res == null) throw new IOException("The launcher's Chapter I compatibility plugin is missing.");
            byte[] expected;
            using (var stream = res.Stream)
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                expected = ms.ToArray();
            }
            if (expected.Length == 0 || expected.Length > 4 * 1024 * 1024)
                throw new IOException("The bundled Chapter I compatibility plugin is invalid.");
            if (!File.Exists(target))
            {
                var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temp, expected);
                    try { File.Move(temp, target); }
                    catch (IOException) { if (!File.Exists(target)) throw; }
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            // Preserve a preexisting unknown plugin and do not execute it.
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(target).Length != expected.Length || !File.ReadAllBytes(target).SequenceEqual(expected))
                throw new IOException("The existing Chapter I compatibility plugin differs from this launcher. It was not changed or loaded.");
        }

        private static readonly Tuple<string, string>[] Files =
        {
            Tuple.Create("pivot.dll", @"polplugins\pivot.dll"),
            Tuple.Create("pivot.sample.ini", @"config\pivot\pivot.sample.ini"),
            Tuple.Create("README.md", @"docs\pivot\README.md"),
            Tuple.Create("LICENSE.txt", @"docs\pivot\LICENSE.txt"),
            Tuple.Create("DATs-README.txt", @"polplugins\DATs\README.txt"),
        };

        /// <summary>Write the bundled files that are missing. True when pivot.dll was put in place.</summary>
        public static bool Ensure(string ashitaRoot)
        {
            if (string.IsNullOrWhiteSpace(ashitaRoot) || !Directory.Exists(ashitaRoot)) return false;
            var placed = false;
            foreach (var f in Files)
            {
                var target = Path.Combine(ashitaRoot, f.Item2);
                if (File.Exists(target)) continue;
                try
                {
                    var res = Application.GetResourceStream(new Uri("pack://application:,,,/assets/xipivot/" + f.Item1));
                    if (res == null) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var s = res.Stream)
                    using (var o = File.Create(target)) s.CopyTo(o);
                    placed |= f.Item1 == "pivot.dll";
                }
                catch (Exception ex)
                {
                    // A missing or unwritable bundled file must never stop an install; the overlays just stay off.
                    Log.Info("XIPivot file " + f.Item1 + " not put in place: " + ex.Message);
                }
            }
            if (placed) Log.Info("XIPivot " + Version + " put in place from the launcher");
            return placed;
        }
    }
}
