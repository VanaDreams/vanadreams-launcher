using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Vanadreams.Services
{
    /// <summary>Retail and character capture may never use a launcher-managed copy, including via a Windows path alias.</summary>
    public static class RetailClientIsolation
    {
        public const string PickCopy = "Choose a separate retail FINAL FANTASY XI folder in Profiles. The folder must have its own PlayOnlineViewer beside it. Do not choose the Vanadreams copy.";
        public const string WrongViewer = "This retail profile must start pol.exe from the PlayOnlineViewer beside its selected retail game. Choose that pol.exe in Profiles.";
        public const string SharedCopy = "This retail folder shares files or a location with Vanadreams. Use a separate retail install, not a link to a Vanadreams folder.";
        public const string CannotCheck = "Could not verify that the retail copy is separate. Choose an accessible local retail install that does not share linked files.";
        public const string CloseGames = "Close all copies of Final Fantasy XI, PlayOnline, Ashita and Windower before changing the registered game or starting retail from this launcher.";
        public const string WrongRegistration = "Windows is not registered to this retail copy. The launcher stopped before opening PlayOnline so it cannot update the Vanadreams files.";

        public static readonly string[] GameProcessNames =
            { "pol", "xiloader", "Ashita-cli", "Windower", "polboot", "ffxi", "polcore" };

        // Check process names without opening/elevating existing sessions; inability to check is not idle.
        public static bool GameProcessActive()
        {
            try
            {
                foreach (var name in GameProcessNames)
                {
                    var matches = Process.GetProcessesByName(name);
                    foreach (var process in matches) process.Dispose();
                    if (matches.Length != 0) return true;
                }
                return false;
            }
            catch { return true; }
        }

        public static string[] ProtectedRoots(LauncherSettings settings) =>
            new[] { settings.GameInstallRoot, settings.FfxiFolderOverride, ClientInstall.DefaultRoot,
                GameRegistration.HasGame(settings.AshitaRoot) ? settings.AshitaRoot : null };

        public static bool TryResolve(string selection, string boot, IEnumerable<string> protectedRoots,
            out string root, out string error, bool checkTree = false)
        {
            root = null;
            error = PickCopy;
            try
            {
                var candidate = Absolute(GameRegistration.RootOf(selection));
                if (candidate == null || !GameRegistration.HasGame(candidate)) return false;
                var game = ClientInstall.GameFolder(candidate);
                var viewer = ClientInstall.ViewerFolder(candidate);
                var expectedBoot = Path.Combine(viewer, "pol.exe");
                if (!File.Exists(expectedBoot)) return false;

                error = CannotCheck;
                var physicalRoot = Identity(candidate);
                var physicalGame = Identity(game);
                var physicalViewer = Identity(viewer);
                var physicalBoot = Identity(expectedBoot);
                if (new[] { physicalRoot, physicalGame, physicalViewer, physicalBoot }.Any(p => p == null)) return false;

                error = SharedCopy;
                if (File.Exists(ClientInstall.RecordPath(candidate)) ||
                    !GameRegistration.SameFolder(physicalGame, Path.Combine(physicalRoot, ClientInstall.GameFolderName)) ||
                    !GameRegistration.SameFolder(physicalViewer, Path.Combine(physicalRoot, ClientInstall.ViewerFolderName)) ||
                    !GameRegistration.SameFolder(physicalBoot, Path.Combine(physicalViewer, "pol.exe"))) return false;

                foreach (var path in protectedRoots ?? Enumerable.Empty<string>())
                {
                    var knownRoot = Absolute(GameRegistration.RootOf(path));
                    if (knownRoot == null) continue;
                    if (Overlaps(candidate, knownRoot)) return false;
                    if (!Directory.Exists(knownRoot)) continue;
                    // An ancestor junction, SUBST/short-name alias, or a junction inside the managed root
                    // can spell the same files differently. Compare the actual NT-volume paths.
                    var knownIdentity = Identity(knownRoot);
                    if (knownIdentity == null) { error = CannotCheck; return false; }
                    if (Overlaps(physicalRoot, knownIdentity)) return false;
                    foreach (var known in new[] { ClientInstall.GameFolder(knownRoot), ClientInstall.ViewerFolder(knownRoot) })
                    {
                        if (!Directory.Exists(known)) continue;
                        var physical = Identity(known);
                        if (physical == null) { error = CannotCheck; return false; }
                        if (Overlaps(physical, physicalGame) || Overlaps(physical, physicalViewer)) return false;
                    }
                }

                error = WrongViewer;
                var configuredBoot = Absolute(boot);
                if (configuredBoot == null || !File.Exists(configuredBoot) ||
                    !GameRegistration.SameFolder(Identity(configuredBoot), physicalBoot)) return false;

                error = SharedCopy;
                if (checkTree && (!NoLinkedFiles(game) || !NoLinkedFiles(viewer))) return false;
                root = candidate;
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                                      ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                error = CannotCheck;
                return false;
            }
        }

        public static bool RegistrationMatches(RegisteredGame reg, string root)
        {
            if (reg == null || string.IsNullOrWhiteSpace(root)) return false;
            var game = Identity(ClientInstall.GameFolder(root));
            var viewer = Identity(ClientInstall.ViewerFolder(root));
            if (game == null || viewer == null || !GameRegistration.SameFolder(Identity(reg.GameFolder), game) ||
                !GameRegistration.SameFolder(Identity(reg.ViewerFolder), viewer)) return false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in reg.ComServers)
            {
                var real = Identity(path);
                if (real == null) return false;
                var name = Path.GetFileName(real);
                var expected = Path.Combine(name.StartsWith("polcore", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(viewer, "viewer", "com") : game, name);
                if (!GameRegistration.SameFolder(expected, real)) return false;
                seen.Add(name);
            }
            return seen.Contains("FFXi.dll") && seen.Contains("FFXiMain.dll") &&
                   (seen.Contains("polcore.dll") || seen.Contains("polcoreeu.dll"));
        }

        private static bool Overlaps(string a, string b) =>
            GameRegistration.SameFolder(a, b) || GameRegistration.IsUnder(a, b) || GameRegistration.IsUnder(b, a);

        private static string Absolute(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var p = value.Trim().Replace('/', '\\');
            // Use a normal local drive path. Drive-relative paths, UNC and device paths are not supported.
            if (p.Length < 3 || !char.IsLetter(p[0]) || p[1] != ':' || p[2] != '\\' || p.IndexOf(':', 2) >= 0 ||
                p.Any(c => c == '"' || c == '%' || c < ' ')) return null;
            return Path.GetFullPath(p);
        }

        // The retail updater can change more than patch.cfg. Reject reparse points anywhere below its
        // folders; compare hardlink counts on loose mutable files, not just the root directories.
        private static bool NoLinkedFiles(string root)
        {
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(root));
            while (pending.Count > 0)
            {
                foreach (var entry in pending.Pop().EnumerateFileSystemInfos())
                {
                    var attrs = entry.Attributes;
                    if ((attrs & FileAttributes.ReparsePoint) != 0) return false;
                    if ((attrs & FileAttributes.Directory) != 0) pending.Push((DirectoryInfo)entry);
                    else
                    {
                        using (var handle = Open(entry.FullName))
                        {
                            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var metadata) || metadata.NumberOfLinks != 1)
                                return false;
                        }
                    }
                }
            }
            return true;
        }

        private static string Identity(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var full = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (full.Length > 3) full = full.TrimEnd('\\', '/');
            using (var handle = Open(full))
            {
                if (handle.IsInvalid) return null;
                const uint VolumeNameNt = 2;
                var b = new StringBuilder(512);
                var n = GetFinalPathNameByHandle(handle, b, (uint)b.Capacity, VolumeNameNt);
                if (n >= b.Capacity && n < 32768)
                {
                    b.EnsureCapacity((int)n + 1);
                    n = GetFinalPathNameByHandle(handle, b, (uint)b.Capacity, VolumeNameNt);
                }
                return n > 0 && n < b.Capacity ? b.ToString() : null;
            }
        }

        private static SafeFileHandle Open(string path) => CreateFile(path, 0x80, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint size, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
                VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
        }
    }
}
