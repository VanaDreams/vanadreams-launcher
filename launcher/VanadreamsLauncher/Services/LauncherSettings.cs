using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Vanadreams.Services
{
    /// <summary>What the launcher remembers between runs. Lives in %LOCALAPPDATA%\Vanadreams\settings.json.</summary>
    public sealed class LauncherSettings
    {
        /// <summary>Test hook (--data &lt;folder&gt;): a separate data folder, so demo screenshots never read or touch the player's own.</summary>
        public static string DataOverride { get; set; }
        public static string DataFolder => DataOverride ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vanadreams");
        public static string DefaultPath => System.IO.Path.Combine(DataFolder, "settings.json");

        public string Path { get; private set; }
        public string AshitaRoot { get; set; } = "";
        public string FfxiFolderOverride { get; set; } = "";

        /// <summary>
        /// Where this launcher put the game. Empty until it installs one.
        ///
        /// Windows holds a single Final Fantasy XI registration for the machine and the game reads its
        /// own location from there, so the two have to agree. Until this was recorded the launcher did
        /// not know where it had installed either: the install folder was a local variable that reset to
        /// the default every time the page opened, which is why a player who installed to a second drive
        /// could not be put right without finding the folder himself.
        /// </summary>
        public string GameInstallRoot { get; set; } = "";
        /// <summary>
        /// The copy of the game Windows pointed at before this launcher took the registration for its own: most
        /// often an older copy kept for another server. A profile for any other server with no game folder of its
        /// own plays this one, so taking the registration for Vanadreams never strands the player's other game.
        /// </summary>
        public string OtherGameRoot { get; set; } = "";
        public string LastProfile { get; set; } = "";
        public string CatalogUrl { get; set; } = "https://raw.githubusercontent.com/VanaDreams/vanadreams-launcher/main/catalog.json";
        public string StatusUrl { get; set; } = ServerStatusClient.DefaultUrl;
        public string CaptureUrl { get; set; } = "https://vanadreams.org/api/public/vanadreams/capture";
        public string ExpectedClientVer { get; set; } = "30260805_0";
        public int VerLock { get; set; } = 2;
        public bool SetupDone { get; set; }
        /// <summary>
        /// Windows holds one Final Fantasy XI location for the whole PC and Play has to point it at our copy, which
        /// takes it from any other launcher that reads it (Windower, PlayOnline: 18 Oct 2026, a player with an older
        /// client on another drive found Windower starting ours). When on, the copy Windows had before is put back
        /// once the loader closes. Play puts ours back again at the next launch.
        /// </summary>
        public bool RestoreOtherGame { get; set; } = true;
        public bool MusicOn { get; set; } = true;
        /// <summary>The menu down the left ("vertical", the default) or as tabs across the top ("horizontal"), the player's pick on Settings.</summary>
        /// <summary>Launcher interface: auto (Windows display language), en, or ja. Applied on next start.</summary>
        public string UiLanguage { get; set; } = "auto";
        public string MenuLayout { get; set; } = "vertical";
        public bool HorizontalMenu => string.Equals(MenuLayout, "horizontal", StringComparison.OrdinalIgnoreCase);
        public string NewsUrl { get; set; } = "https://raw.githubusercontent.com/VanaDreams/vanadreams-launcher/main/news.json";
        /// <summary>Low spec: the modern look's effects (d3d8to9 and ReShade in bootloader\) are renamed to .off (Effects).</summary>
        public bool LowSpec { get; set; }
        public List<string> EnabledAddons { get; set; } = new List<string>();
        public List<string> GivenDefaults { get; set; } = new List<string>();   // on-by-default items already given once; unticking one sticks
        public bool TickAllTidied { get; set; }   // the pre-0.2.24 "every Ashita addon on" list has been checked for, once (AddonInstaller.TidyTickAll)
        public Dictionary<string, string> InstalledVersions { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> LastPlayed { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> GuideDone { get; set; } = new List<string>();

        // The window switcher (WindowSwitcher), off until the player turns it on. Keys and pad presses are kept as text (KeyBinding, PadBinding).
        public bool SwitcherOn { get; set; }
        public string SwitchNext { get; set; } = "Pause";
        public string SwitchPrev { get; set; } = "Scroll";
        public List<string> SwitchSlots { get; set; } = NewSlots();
        public bool SwitchKeysAnywhere { get; set; }
        public string PadNext { get; set; } = "";
        public string PadPrev { get; set; } = "";
        /// <summary>Character names in the slot order the player arranged.</summary>
        public List<string> SwitchOrder { get; set; } = new List<string>();
        /// <summary>Saved window layouts by name; each entry is SwitchRules.LayoutEntry text.</summary>
        public Dictionary<string, List<string>> Layouts { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public string ActiveLayout { get; set; } = "";
        /// <summary>A character who logs in goes to its place in the active layout.</summary>
        public bool AutoLayout { get; set; }
        public const int SlotKeyCount = 6;
        private static List<string> NewSlots() => Enumerable.Repeat("", SlotKeyCount).ToList();

        public static LauncherSettings Load(string path = null)
        {
            var s = new LauncherSettings { Path = path ?? DefaultPath };
            if (!File.Exists(s.Path)) return s;
            try
            {
                var d = Json.ParseObject(File.ReadAllText(s.Path, Encoding.UTF8));
                if (d == null) return s;
                s.AshitaRoot = Json.Str(d, "ashitaRoot", "");
                s.FfxiFolderOverride = Json.Str(d, "ffxiFolderOverride", "");
                s.GameInstallRoot = Json.Str(d, "gameInstallRoot", "");
                s.OtherGameRoot = Json.Str(d, "otherGameRoot", "");
                s.LastProfile = Json.Str(d, "lastProfile", "");
                s.CatalogUrl = Json.Str(d, "catalogUrl", s.CatalogUrl);
                s.StatusUrl = Json.Str(d, "statusUrl", s.StatusUrl);
                s.CaptureUrl = Json.Str(d, "captureUrl", s.CaptureUrl);
                s.ExpectedClientVer = Json.Str(d, "expectedClientVer", s.ExpectedClientVer);
                s.VerLock = Json.Int(d, "verLock", 2);
                s.SetupDone = Json.Bool(d, "setupDone");
                s.MusicOn = Json.Bool(d, "musicOn", true);
                s.RestoreOtherGame = Json.Bool(d, "restoreOtherGame", true);
                s.UiLanguage = global::Vanadreams.Services.UiLanguage.Normalize(Json.Str(d, "uiLanguage", "auto"));
                s.MenuLayout = Json.Str(d, "menuLayout", s.MenuLayout);
                s.NewsUrl = Json.Str(d, "newsUrl", s.NewsUrl);
                s.LowSpec = Json.Bool(d, "lowSpec");
                s.EnabledAddons = Json.Strings(d, "enabledAddons");
                s.GivenDefaults = Json.Strings(d, "givenDefaults");
                s.TickAllTidied = Json.Bool(d, "tickAllTidied");
                s.GuideDone = Json.Strings(d, "guideDone");
                s.SwitcherOn = Json.Bool(d, "switcherOn");
                s.SwitchNext = Json.Str(d, "switchNext", s.SwitchNext);
                s.SwitchPrev = Json.Str(d, "switchPrev", s.SwitchPrev);
                if (d.ContainsKey("switchSlots")) s.SwitchSlots = Json.Strings(d, "switchSlots");
                while (s.SwitchSlots.Count < SlotKeyCount) s.SwitchSlots.Add("");
                s.SwitchKeysAnywhere = Json.Bool(d, "switchKeysAnywhere");
                s.PadNext = Json.Str(d, "padNext", "");
                s.PadPrev = Json.Str(d, "padPrev", "");
                s.SwitchOrder = Json.Strings(d, "switchOrder");
                var layouts = Json.Obj(d.ContainsKey("layouts") ? d["layouts"] : null);
                if (layouts != null)
                    foreach (var kv in layouts)
                    {
                        s.Layouts[kv.Key] = Json.List(kv.Value).Select(x => Convert.ToString(x)).ToList();
                    }
                s.ActiveLayout = Json.Str(d, "activeLayout", "");
                s.AutoLayout = Json.Bool(d, "autoLayout");
                var inst = Json.Obj(d.ContainsKey("installedVersions") ? d["installedVersions"] : null);
                if (inst != null) foreach (var kv in inst) s.InstalledVersions[kv.Key] = Convert.ToString(kv.Value);
                var last = Json.Obj(d.ContainsKey("lastPlayed") ? d["lastPlayed"] : null);
                if (last != null) foreach (var kv in last) s.LastPlayed[kv.Key] = Convert.ToString(kv.Value);
            }
            catch (Exception) { /* unreadable settings start fresh */ }
            return s;
        }

        public void Save()
        {
            var d = new Dictionary<string, object>
            {
                { "ashitaRoot", AshitaRoot ?? "" },
                { "ffxiFolderOverride", FfxiFolderOverride ?? "" },
                { "gameInstallRoot", GameInstallRoot ?? "" },
                { "otherGameRoot", OtherGameRoot ?? "" },
                { "lastProfile", LastProfile ?? "" },
                { "catalogUrl", CatalogUrl },
                { "statusUrl", StatusUrl },
                { "captureUrl", CaptureUrl },
                { "expectedClientVer", ExpectedClientVer },
                { "verLock", VerLock },
                { "setupDone", SetupDone },
                { "musicOn", MusicOn },
                { "restoreOtherGame", RestoreOtherGame },
                { "uiLanguage", global::Vanadreams.Services.UiLanguage.Normalize(UiLanguage) },
                { "menuLayout", MenuLayout ?? "vertical" },
                { "newsUrl", NewsUrl },
                { "lowSpec", LowSpec },
                { "enabledAddons", EnabledAddons.ToList() },
                { "givenDefaults", GivenDefaults.ToList() },
                { "tickAllTidied", TickAllTidied },
                { "guideDone", GuideDone.ToList() },
                { "switcherOn", SwitcherOn },
                { "switchNext", SwitchNext ?? "" },
                { "switchPrev", SwitchPrev ?? "" },
                { "switchSlots", SwitchSlots.ToList() },
                { "switchKeysAnywhere", SwitchKeysAnywhere },
                { "padNext", PadNext ?? "" },
                { "padPrev", PadPrev ?? "" },
                { "switchOrder", SwitchOrder.ToList() },
                { "layouts", Layouts.ToDictionary(k => k.Key, k => (object)k.Value.ToList()) },
                { "activeLayout", ActiveLayout ?? "" },
                { "autoLayout", AutoLayout },
                { "installedVersions", InstalledVersions.ToDictionary(k => k.Key, k => (object)k.Value) },
                { "lastPlayed", LastPlayed.ToDictionary(k => k.Key, k => (object)k.Value) },
            };
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllText(Path, Json.Stringify(d), new UTF8Encoding(false));
        }

        public bool HasAshita => !string.IsNullOrWhiteSpace(AshitaRoot) && File.Exists(System.IO.Path.Combine(AshitaRoot, "Ashita-cli.exe"));
        public string CredentialsPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "credentials.dat");
        public string CatalogCachePath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "catalog.cache.json");
        public string NewsCachePath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "news.cache.json");
        public string StatusCachePath =>System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "status.cache.json");
        public string DownloadsFolder => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "downloads");
        public string LogPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "launcher.log");
    }
}
