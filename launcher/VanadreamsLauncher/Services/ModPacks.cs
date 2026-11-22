using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Vanadreams.Services
{
    public sealed class ModPack
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Detail { get; set; }
        public bool On { get; set; }
        /// <summary>
        /// False for the packs unpacked into the game's own files (the modern look and the title pack): they replaced the
        /// files they change, so there is nothing to switch off or to order. Only XIPivot overlays can be.
        /// </summary>
        public bool CanSwitch { get; set; }
    }

    /// <summary>
    /// The Install page's mod packs. Two kinds, and they behave differently:
    ///   - The modern look pack and the title pack come with the game download (looks-*.zip and vanadreams-title-*.zip in
    ///     the client manifest) and unpack straight into FINAL FANTASY XI, over the files they change. They are listed so a
    ///     player can see they are there; there is no original left to go back to, so they cannot be switched or ordered.
    ///   - XIPivot overlays: folders under polplugins\DATs, loaded in the order pivot.ini lists them, the first listed
    ///     winning (PivotConfig). These switch on and off and move up and down for real.
    /// Shown top to bottom with the pack that wins at the bottom: "lower packs load on top of higher ones".
    /// </summary>
    public static class ModPacks
    {
        public static List<ModPack> List(string gameRoot, string version, string ashitaRoot)
        {
            var list = new List<ModPack>();
            var done = string.IsNullOrWhiteSpace(gameRoot) || string.IsNullOrEmpty(version) ? new HashSet<string>() : ClientInstall.Done(gameRoot, version);
            var overlays = Overlays(ashitaRoot);
            var moved = !string.IsNullOrWhiteSpace(ashitaRoot) && LookOverlays.Migrated(ashitaRoot, version);
            // still in the game files: shown, locked, until the next Patch moves them into their overlays
            if (!moved && !overlays.Any(o => o.Id == LookOverlays.Looks) && done.Any(d => LookOverlays.OverlayFor(d.Split(' ')[0]) == LookOverlays.Looks))
                list.Add(new ModPack { Id = "looks", Name = NameOf(LookOverlays.Looks), Detail = "Part of the game files", On = true });
            if (!moved && !overlays.Any(o => o.Id == LookOverlays.Title) && done.Any(d => LookOverlays.OverlayFor(d.Split(' ')[0]) == LookOverlays.Title))
                list.Add(new ModPack { Id = "title", Name = NameOf(LookOverlays.Title), Detail = "Part of the game files", On = true });
            list.AddRange(overlays);
            return list;
        }

        /// <summary>XIPivot overlays: those off first, then those on from the one that loses to the one that wins.</summary>
        public static List<ModPack> Overlays(string ashitaRoot)
        {
            var result = new List<ModPack>();
            if (string.IsNullOrWhiteSpace(ashitaRoot)) return result;
            var root = PivotConfig.OverlaysRoot(ashitaRoot);
            var folders = Directory.Exists(root) ? Directory.GetDirectories(root).Select(Path.GetFileName).Where(f => !ChapterOnePatch.IsManagedOverlay(f)).ToList() : new List<string>();
            var listed = PivotConfig.ReadOverlays(ashitaRoot).Where(n => folders.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (var f in folders.Where(f => !listed.Contains(f, StringComparer.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                result.Add(new ModPack { Id = f, Name = NameOf(f), Detail = "Off", On = false, CanSwitch = true });
            for (var i = listed.Count - 1; i >= 0; i--)
                result.Add(new ModPack { Id = listed[i], Name = NameOf(listed[i]), Detail = "On", On = true, CanSwitch = true });
            return result;
        }

        /// <summary>The packs Vanadreams ships go by their names; any other overlay by its folder.</summary>
        public static string NameOf(string id) =>
            string.Equals(id, LookOverlays.Looks, StringComparison.OrdinalIgnoreCase) ? "Modern look pack"
            : string.Equals(id, LookOverlays.Title, StringComparison.OrdinalIgnoreCase) ? "Title pack"
            : string.Equals(id, LookOverlays.Text, StringComparison.OrdinalIgnoreCase) ? "Text fixes" : id;

        /// <summary>True when XIPivot itself is in Ashita's polplugins folder, without which no overlay loads.</summary>
        public static bool PivotInstalled(string ashitaRoot) =>
            !string.IsNullOrWhiteSpace(ashitaRoot) && File.Exists(Path.Combine(ashitaRoot, "polplugins", "pivot.dll"));

        /// <summary>Write pivot.ini from the overlays as shown: the ones on, the bottom one first since it wins.</summary>
        public static void Save(string ashitaRoot, IEnumerable<ModPack> shown)
        {
            var on = shown.Where(p => p.CanSwitch && p.On).Select(p => p.Id).Reverse().ToList();
            PivotConfig.SetOverlays(ashitaRoot, on);
        }
    }
}
