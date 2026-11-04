using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Vanadreams.Services
{
    /// <summary>
    /// Reads the text in the loader's own window while it runs.
    ///
    /// xiloader writes nothing to disk, and when it gives up its window closes two seconds later, so the one line that
    /// said why was gone before a player could read it, and no log or bug report ever held it (4 Nov 2026: "99 times out
    /// of 100 the loader just says Closing... with no information"). The launcher has no console of its own, so it can
    /// attach to the loader's for a moment, copy the screen text and let go again. That works for a loader started as
    /// the player, which is how GameLauncher starts it; a loader running as administrator refuses, and nothing is read.
    /// </summary>
    public static class LoaderConsole
    {
        private const int MaxRows = 400;
        private static readonly object Gate = new object();

        [StructLayout(LayoutKind.Sequential)] private struct Coord { public short X; public short Y; }
        [StructLayout(LayoutKind.Sequential)] private struct SmallRect { public short Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ScreenBufferInfo { public Coord Size; public Coord Cursor; public ushort Attributes; public SmallRect Window; public Coord MaxWindow; }

        private delegate bool CtrlHandler(uint ctrlType);

        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint processId);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeConsole();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleCtrlHandler(CtrlHandler handler, bool add);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetConsoleScreenBufferInfo(IntPtr output, out ScreenBufferInfo info);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ReadConsoleOutputCharacter(IntPtr output, [Out] StringBuilder text, uint length, Coord start, out uint read);

        private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareRead = 1, ShareWrite = 2, OpenExisting = 3;
        private static readonly IntPtr Invalid = new IntPtr(-1);

        // While attached, the launcher shares the loader's console: Ctrl+C or closing that window must never close the launcher
        private static readonly CtrlHandler KeepRunning = _ => true;

        /// <summary>The loader window's text, top to bottom, trailing blanks trimmed; null when it could not be read.</summary>
        public static string Read(int processId, out string error)
        {
            error = null;
            lock (Gate)
            {
                var attached = false;
                var output = Invalid;
                try
                {
                    SetConsoleCtrlHandler(KeepRunning, true);
                    if (!AttachConsole((uint)processId)) { error = "attach refused (" + Marshal.GetLastWin32Error() + ")"; return null; }
                    attached = true;
                    output = CreateFile("CONOUT$", GenericRead | GenericWrite, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                    if (output == Invalid) { error = "no console output (" + Marshal.GetLastWin32Error() + ")"; return null; }
                    if (!GetConsoleScreenBufferInfo(output, out var info)) { error = "console size unreadable (" + Marshal.GetLastWin32Error() + ")"; return null; }
                    var width = Math.Max(1, (int)info.Size.X);
                    var last = Math.Min(info.Size.Y - 1, Math.Max(info.Cursor.Y, info.Window.Bottom));
                    var first = Math.Max(0, last - MaxRows + 1);
                    var rows = new List<string>();
                    var sb = new StringBuilder(width);
                    for (var y = first; y <= last; y++)
                    {
                        sb.Clear();
                        sb.EnsureCapacity(width);
                        if (!ReadConsoleOutputCharacter(output, sb, (uint)width, new Coord { X = 0, Y = (short)y }, out var read)) break;
                        rows.Add(sb.ToString(0, (int)Math.Min(read, (uint)sb.Length)).TrimEnd());
                    }
                    return Tidy(rows);
                }
                catch (Exception ex) { error = ex.Message; return null; }
                finally
                {
                    if (output != Invalid) CloseHandle(output);
                    if (attached) FreeConsole();
                    SetConsoleCtrlHandler(KeepRunning, false);
                }
            }
        }

        /// <summary>The rows as text, without the blank rows at the end.</summary>
        public static string Tidy(IEnumerable<string> rows)
        {
            var list = (rows ?? Enumerable.Empty<string>()).Select(r => (r ?? "").TrimEnd()).ToList();
            while (list.Count > 0 && list[list.Count - 1].Length == 0) list.RemoveAt(list.Count - 1);
            return string.Join("\n", list);
        }
    }

    /// <summary>
    /// Follows a launch's loader window: the newest text read from any of the loader processes is kept, so the
    /// last thing it said is still there after it closes.
    /// </summary>
    public sealed class LoaderWatch
    {
        private readonly HashSet<int> _refused = new HashSet<int>();
        public string LastText { get; private set; }
        public string Problem { get; private set; }

        public void Look(IEnumerable<int> processIds)
        {
            foreach (var id in processIds ?? Enumerable.Empty<int>())
            {
                if (_refused.Contains(id)) continue;
                var text = LoaderConsole.Read(id, out var error);
                if (text != null) { if (text.Length > 0) LastText = BugReport.StripPasswords(text); continue; }
                // a loader running as administrator refuses every time; ask it once
                if (error != null && error.StartsWith("attach refused (5)")) _refused.Add(id);
                if (Problem == null) { Problem = error; Log.Warn("loader window unreadable: " + error); }
            }
        }
    }
}
