using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vanadreams.Services;

namespace Vanadreams
{
    public sealed class ClientUpdate
    {
        public string Root { get; set; }
        public ClientManifest Manifest { get; set; }
        public System.Collections.Generic.List<ClientChunk> Parts { get; set; }
        public long Size => Parts.Sum(c => c.Size);
        public string Megabytes => Math.Max(1, Size / 1048576) + " MB";
        /// <summary>"3 new parts, 412 MB"</summary>
        public string Words => Parts.Count + (Parts.Count == 1 ? " new part, " : " new parts, ") + Megabytes;
    }

    /// <summary>Everything the pages share: settings, stores, the last status and version check.</summary>
    public sealed class AppState
    {
        public LauncherSettings Settings { get; }
        public CredentialStore Credentials { get; }
        public ServerStatusClient StatusClient { get; }
        public Downloader Downloader { get; } = new Downloader();
        public Catalog Catalog { get; private set; } = new Catalog();
        public bool CatalogFromCache { get; private set; }
        public StatusInfo Status { get; private set; } = new StatusInfo();
        public VersionCheck Version { get; private set; } = new VersionCheck { Verdict = VersionVerdict.Unknown };

        public event Action Changed;

        public AppState()
        {
            Settings = LauncherSettings.Load();
            Log.Path = Settings.LogPath;
            Credentials = new CredentialStore(Settings.CredentialsPath);
            StatusClient = new ServerStatusClient(Settings.StatusCachePath) { Url = Settings.StatusUrl };
            Status = StatusClient.ReadCache();
            Status.FromCache = true;
            LoadCatalogFromCache();
            if (Catalog.Items.Count == 0) LoadBundledCatalog();
            UseCatalogServers();
        }

        /// <summary>The catalogue's server addresses become the ones profiles are pointed at (LoaderCommand.UseServers).</summary>
        private void UseCatalogServers() => LoaderCommand.UseServers(Catalog.TailscaleServer, Catalog.MovedServers);

        /// <summary>
        /// Every profile still pointing at an address the server moved away from is pointed at the new one, so a
        /// player never has to edit a boot ini after a move. Quiet: a file that cannot be written is logged and left.
        /// </summary>
        public int RepointMovedProfiles()
        {
            if (!HasAshita) return 0;
            try
            {
                var n = Profiles.RepointMoved().Count;
                if (n > 0) Notify();
                return n;
            }
            catch (Exception ex) { Log.Warn("profiles not repointed: " + ex.Message); return 0; }
        }

        public ProfileStore Profiles => new ProfileStore(Settings.AshitaRoot);

        /// <summary>The profile Play starts: the last one played or picked, else the first that is not an example.</summary>
        public Profile CurrentProfile()
        {
            var all = Profiles.LoadAll();
            return all.FirstOrDefault(p => string.Equals(p.Id, Settings.LastProfile, StringComparison.OrdinalIgnoreCase))
                   ?? all.FirstOrDefault(p => !p.IsExample) ?? all.FirstOrDefault();
        }
        public string AshitaRoot => Settings.AshitaRoot;
        public bool HasAshita => Settings.HasAshita;
        /// <summary>
        /// The Vanadreams game folder, the one the version check and the guide read: the copy Play would start
        /// (VanadreamsGameRoot), else the folder set in Settings, else whatever Windows has registered.
        /// </summary>
        public string FfxiFolder
        {
            get
            {
                var root = VanadreamsGameRoot();
                return root != null ? ClientInstall.GameFolder(root)
                    : !string.IsNullOrWhiteSpace(Settings.FfxiFolderOverride) ? Settings.FfxiFolderOverride
                    : ClientVersion.FindFfxiFolder();
            }
        }

        /// <summary>
        /// The copy of the game a Vanadreams profile starts, looked for across every copy this PC is known to hold:
        /// the one Windows starts now, the one this launcher installed, one beside Ashita or in the default folder,
        /// the folder set in Settings, and the copy the registration was once taken from. GameRegistration.Choose
        /// picks the one that fits the server, unless the player picked a Vanadreams game folder (Profiles or
        /// Settings): that one is always started while it holds a game. Play passes the registered root read from the COM servers; the
        /// pages that only show a version use the registered folder, which is quicker to read.
        /// 4 Oct 2026: 0.2.27 always took the launcher's copy when one was there, whatever state it was in, and
        /// a player whose game had been starting from another copy logged in and watched the console close.
        /// </summary>
        public string VanadreamsGameRoot(string registeredRoot = null)
        {
            if (registeredRoot == null) registeredRoot = GameRegistration.RootOf(ClientVersion.FindFfxiFolder());
            var expected = ExpectedClientVer;
            var lockMode = VerLock;
            return GameRegistration.Choose(registeredRoot,
                new[] { Settings.GameInstallRoot, Settings.AshitaRoot, ClientInstall.DefaultRoot, GameRegistration.RootOf(Settings.FfxiFolderOverride), Settings.OtherGameRoot },
                GameRegistration.HasGame,
                root => ClientVersion.Fits(InstalledVersion(root), expected, lockMode),
                GameRegistration.RootOf(Settings.FfxiFolderOverride));
        }

        private string ExpectedClientVer => !string.IsNullOrEmpty(Status.ClientVer) ? Status.ClientVer : Settings.ExpectedClientVer;
        private VersionLock VerLock => Status.Lock ?? (Enum.IsDefined(typeof(VersionLock), Settings.VerLock) ? (VersionLock)Settings.VerLock : VersionLock.MatchingOrNewer);

        // patch.cfg runs to megabytes and several copies are read on every look; a file that has not changed is read once
        private readonly System.Collections.Generic.Dictionary<string, Tuple<DateTime, long, string>> _stamps =
            new System.Collections.Generic.Dictionary<string, Tuple<DateTime, long, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The version stamp of the copy in this root, or null when it has none that can be read.</summary>
        public string InstalledVersion(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return null;
            var folder = ClientInstall.GameFolder(root);
            try
            {
                var cfg = new FileInfo(Path.Combine(folder, "patch.cfg"));
                if (!cfg.Exists) return null;
                Tuple<DateTime, long, string> seen;
                lock (_stamps)
                    if (_stamps.TryGetValue(cfg.FullName, out seen) && seen.Item1 == cfg.LastWriteTimeUtc && seen.Item2 == cfg.Length) return seen.Item3;
                var stamp = ClientVersion.ReadInstalled(folder);
                lock (_stamps) _stamps[cfg.FullName] = Tuple.Create(cfg.LastWriteTimeUtc, cfg.Length, stamp);
                return stamp;
            }
            catch (Exception) { return ClientVersion.ReadInstalled(folder); }
        }

        /// <summary>
        /// The copy of the game a profile for another server plays: its own game folder, else the copy Windows had
        /// before this launcher took the registration. Null when neither holds a game; the registration is then left as it is.
        /// </summary>
        public string OtherGameRootFor(Profile p)
        {
            var own = GameRegistration.RootOf(p?.GameFolder);
            if (GameRegistration.HasGame(own)) return own;
            return GameRegistration.HasGame(Settings.OtherGameRoot) ? Settings.OtherGameRoot : null;
        }

        public void Notify() => Changed?.Invoke();

        /// <summary>New or changed parts of the game the launcher installed, found at start; null when there are none.</summary>
        public ClientUpdate PendingClientUpdate { get; private set; }

        /// <summary>
        /// Compares the server's download list with the record in the installed copy and keeps what is new or changed,
        /// so the strip and the menu can say an update is ready. Only a copy this launcher installed from the version the
        /// server runs is checked; anything else is the version check's business.
        /// </summary>
        /// <summary>Snapshot hook (--snapshot file.png menu-update): a made-up 8-part, 569 MB update, the size seen on 11 Oct 2026.</summary>
        public void ShowSampleClientUpdate()
        {
            var parts = Enumerable.Range(1, 8).Select(i => new ClientChunk { Name = "looks-0" + i + ".zip", Size = 569L * 1048576 / 8 }).ToList();
            PendingClientUpdate = new ClientUpdate { Root = Settings.GameInstallRoot, Parts = parts };
        }

        /// <summary>
        /// The Play button reads Patch while this is true: an update found at start, or a game older than the version the
        /// server runs. Unknown (no game found) is not a patch; Play explains that one and Install game handles it.
        /// </summary>
        public bool PatchNeeded => PendingClientUpdate != null || OverlayMigrationDue ||
                                   (Version.ExpectedIsPublished && Version.Verdict == VersionVerdict.ClientTooOld);

        /// <summary>List the shipped overlays on disk in pivot.ini, once each (LookOverlays.GiveOnce). True when it listed any.</summary>
        public bool GiveOverlays()
        {
            if (!HasAshita) return false;
            try
            {
                var listed = LookOverlays.GiveOnce(AshitaRoot, Settings.GivenDefaults);
                if (listed.Count == 0) return false;
                Settings.Save();
                Log.Info("overlays listed in pivot.ini: " + string.Join(", ", listed));
                return true;
            }
            catch (Exception ex) { Log.Warn("overlays: " + ex.Message); return false; }
        }

        /// <summary>The server ships the packs as XIPivot overlays and this copy still has them in the game folder (LookOverlays).</summary>
        public bool OverlayMigrationDue { get; private set; }

        /// <summary>Where the launcher installs and patches the game: the folder it installed to, else the default.</summary>
        public string GameInstallTarget => string.IsNullOrWhiteSpace(Settings.GameInstallRoot) ? ClientInstall.DefaultRoot : Settings.GameInstallRoot;

        /// <summary>
        /// The copy whose look and title packs are moved and switched: the one Vanadreams plays, when its record shows the
        /// packs, else the recorded install. 16 Oct 2026: on Ferrin's PC the recorded install folder held no game and Play
        /// started D:\, so a migration that only looked at the recorded folder never offered itself.
        /// </summary>
        public string PacksRoot(string version)
        {
            var candidates = new[] { VanadreamsGameRoot(), Settings.GameInstallRoot, GameInstallTarget };
            foreach (var r in candidates)
            {
                if (string.IsNullOrWhiteSpace(r) || string.IsNullOrEmpty(version)) continue;
                if (ClientInstall.Done(r, version).Any(d => LookOverlays.OverlayFor(d.Split(' ')[0]) != null)) return r;
            }
            return GameInstallTarget;
        }

        /// <summary>
        /// The Patch button: the same update Install game runs. Fetches what is new or missing for the server's version,
        /// unpacks it, and registers the copy with Windows when it is not registered yet. Null when it went through, or a
        /// sentence for the player when the files are in but the registration prompt was refused.
        /// </summary>
        public async Task<string> PatchClientAsync(IProgress<InstallProgress> progress, System.Threading.CancellationToken ct)
        {
            var root = PendingClientUpdate?.Root ?? GameInstallTarget;
            var patchesOnly = PendingClientUpdate != null;
            var version = Version.ExpectedIsPublished ? Version.Expected : Status.ClientVer;
            var m = PendingClientUpdate?.Manifest;
            if (m == null || (!string.IsNullOrEmpty(version) && m.Version != version))
            {
                if (string.IsNullOrEmpty(version)) throw new InvalidOperationException("The server hasn't published its version yet, so there is nothing to patch to.");
                m = ClientManifest.Parse(await Downloader.GetStringAsync(ClientInstall.ManifestUrl(version)));
            }
            var ashita = HasAshita ? AshitaRoot : null;
            // decided before the move: a Patch pressed only for the packs must never become an install
            var updateWanted = PendingClientUpdate != null || (Version.ExpectedIsPublished && Version.Verdict == VersionVerdict.ClientTooOld);
            // the packs out of the game folder into their overlays first, so nothing below sees them as missing
            var packs = PacksRoot(m.Version);
            if (LookOverlays.Due(m, packs, ashita))
            {
                XIPivotBundle.Ensure(ashita);
                await LookOverlays.MigrateAsync(Http.Shared, m, packs, ashita, progress, ct);
                ApplyEnabledAddons();
                GiveOverlays();
                if (!updateWanted)
                {
                    await CheckClientUpdateAsync();
                    CheckVersion();
                    Notify();
                    return null;
                }
            }
            var todo = patchesOnly ? ClientInstall.Updates(m, root, ashita) : ClientInstall.Remaining(m, ClientInstall.Done(root, m.Version, ashita), root, ashita);
            var updateOnly = todo.Count < m.Chunks.Count && ClientInstall.IsRegistered(root);
            var needed = todo.Sum(c => c.Size + c.Unpacked);
            var drive = new DriveInfo(Path.GetPathRoot(root));
            if (drive.AvailableFreeSpace < needed)
                throw new IOException(drive.Name + " has " + (drive.AvailableFreeSpace / 1073741824.0).ToString("0.0") + " GB free and the patch needs " +
                                      (needed / 1073741824.0).ToString("0.0") + " GB. Free some space, or pick another folder on Install game.");
            await ClientInstall.InstallFilesAsync(Downloader, m, root, Settings.DownloadsFolder, progress, ct, ashita, Settings.LowSpec, patchesOnly);
            Settings.GameInstallRoot = root;
            Settings.Save();
            XIPivotBundle.Ensure(ashita);
            GiveOverlays();
            string said = null;
            if (!updateOnly && !await Task.Run(() => ClientInstall.RunRegister(root, Settings.DownloadsFolder)))
                said = "The files are in, but the permission prompt was refused, so Windows doesn't know where the game is yet. Press Patch again to finish; nothing downloads twice.";
            await CheckClientUpdateAsync();
            CheckVersion();
            Notify();
            return said;
        }

        public async Task CheckClientUpdateAsync(bool requireSuccess = false, string selectedRoot = null)
        {
            var version = Status.ClientVer;
            var root = selectedRoot ?? VanadreamsGameRoot();
            // A removed FFXiMain.dll makes HasGame false, but that is precisely a pack repair's job.
            // Fall back to a known exact-version folder instead of silently skipping its missing files.
            if (string.IsNullOrWhiteSpace(root))
                root = new[] { GameRegistration.RootOf(Settings.FfxiFolderOverride), Settings.GameInstallRoot,
                    GameRegistration.RootOf(ClientVersion.FindFfxiFolder()), AshitaRoot, ClientInstall.DefaultRoot }
                    .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r) && version != null &&
                        ClientVersion.ReadInstalled(ClientInstall.GameFolder(r)) == version);
            if (string.IsNullOrEmpty(version))
            {
                if (requireSuccess) throw new IOException("The server client version could not be checked. Please try Play again when the server is available.");
                PendingClientUpdate = null; Notify(); return;
            }
            try
            {
                var m = ClientManifest.Parse(await Downloader.GetStringAsync(ClientInstall.ManifestUrl(version)));
                if (requireSuccess && m.Chunks.Any(c => c.Required) &&
                    (root == null || ClientVersion.ReadInstalled(ClientInstall.GameFolder(root)) != m.Version))
                    throw new IOException("The required character face pack needs client " + m.Version + ". Select that game copy or use Install game before playing.");
                // the packs belong to the copy Vanadreams plays, which is not always the recorded install
                var packs = PacksRoot(m.Version);
                OverlayMigrationDue = LookOverlays.Due(m, packs, HasAshita ? AshitaRoot : null);
                if (OverlayMigrationDue) Log.Info("the look and title packs in " + packs + " are to move into their overlays");
                if (!GameRegistration.HasGame(root) && !(m.Chunks.Any(c => c.Required) && root != null &&
                    ClientVersion.ReadInstalled(ClientInstall.GameFolder(root)) == m.Version))
                { PendingClientUpdate = null; Notify(); return; }
                var todo = ClientInstall.Updates(m, root, HasAshita ? AshitaRoot : null);
                PendingClientUpdate = todo.Count == 0 ? null : new ClientUpdate { Root = root, Manifest = m, Parts = todo };
                if (PendingClientUpdate != null) Log.Info("game update ready: " + string.Join(", ", todo.Select(c => c.Name)));
            }
            catch (Exception ex) { Log.Warn("game update check: " + ex.Message); if (requireSuccess) throw; }
            Notify();
        }

        public VersionCheck CheckVersion()
        {
            var installed = ClientVersion.ReadInstalled(FfxiFolder);
            var published = !string.IsNullOrEmpty(Status.ClientVer);
            Version = ClientVersion.Compare(installed, ExpectedClientVer, VerLock);
            Version.ExpectedIsPublished = published;
            return Version;
        }

        public async Task RefreshStatusAsync()
        {
            StatusClient.Url = Settings.StatusUrl;
            Status = await StatusClient.FetchAsync();
            if (!string.IsNullOrEmpty(Status.ClientVer)) { Settings.ExpectedClientVer = Status.ClientVer; if (Status.Lock.HasValue) Settings.VerLock = (int)Status.Lock.Value; Settings.Save(); }
            CheckVersion();
            Notify();
        }

        private void LoadCatalogFromCache()
        {
            try
            {
                if (File.Exists(Settings.CatalogCachePath)) { Catalog = Catalog.Parse(File.ReadAllText(Settings.CatalogCachePath, Encoding.UTF8)); CatalogFromCache = true; }
            }
            catch (Exception ex) { Log.Warn("catalogue cache unreadable: " + ex.Message); }
        }

        public async Task RefreshCatalogAsync()
        {
            try
            {
                var text = await Downloader.GetStringAsync(Settings.CatalogUrl);
                var cat = Catalog.Parse(text);
                Catalog = cat;
                CatalogFromCache = false;
                Directory.CreateDirectory(Path.GetDirectoryName(Settings.CatalogCachePath));
                File.WriteAllText(Settings.CatalogCachePath, text, new UTF8Encoding(false));
                UseCatalogServers();
                RepointMovedProfiles();
            }
            catch (Exception ex)
            {
                Log.Warn("catalogue fetch failed, using cache: " + ex.Message);
                CatalogFromCache = true;
                if (Catalog.Items.Count == 0) { LoadBundledCatalog(); UseCatalogServers(); }
                RepointMovedProfiles();
            }
            Notify();
        }

        /// <summary>The catalogue shipped inside the exe, used before the first successful fetch.</summary>
        private void LoadBundledCatalog()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/assets/catalog.json");
                var info = System.Windows.Application.GetResourceStream(uri);
                if (info == null) return;
                using (var reader = new StreamReader(info.Stream, Encoding.UTF8)) Catalog = Catalog.Parse(reader.ReadToEnd());
            }
            catch (Exception ex) { Log.Warn("bundled catalogue unreadable: " + ex.Message); }
        }

        /// <summary>
        /// Installs and ticks the catalogue's on-by-default items, once each. After that the box is the player's:
        /// an item they untick is never ticked for them again. One that fails to install is tried at the next start.
        /// </summary>
        public async Task GiveDefaultsAsync()
        {
            if (!HasAshita) return;
            var changed = false;
            // XIPivot comes with the launcher: the look and title overlays need it (LookOverlays). Ticked once, then the player's.
            try
            {
                if (XIPivotBundle.Ensure(AshitaRoot) && !Settings.InstalledVersions.ContainsKey("pivot")) { Settings.InstalledVersions["pivot"] = XIPivotBundle.Version; changed = true; }
                if (!Settings.GivenDefaults.Contains("pivot", StringComparer.OrdinalIgnoreCase))
                {
                    if (!Settings.EnabledAddons.Contains("pivot", StringComparer.OrdinalIgnoreCase)) Settings.EnabledAddons.Add("pivot");
                    Settings.GivenDefaults.Add("pivot");
                    changed = true;
                }
            }
            catch (Exception ex) { Log.Warn("XIPivot: " + ex.Message); }
            if (GiveOverlays()) changed = true;
            // once per player, and only with a real catalogue to tell Ashita's own addons from ours
            if (!Settings.TickAllTidied && Catalog.Items.Count > 0)
            {
                var tidied = AddonInstaller.TidyTickAll(Catalog, Settings.EnabledAddons);
                if (tidied != null)
                {
                    Log.Info("old every-addon list switched off: " + (Settings.EnabledAddons.Count - tidied.Count) + " of Ashita's addons unticked, " + tidied.Count + " kept");
                    Settings.EnabledAddons = tidied;
                }
                Settings.TickAllTidied = true;
                changed = true;
            }
            foreach (var item in AddonInstaller.DefaultsToGive(Catalog, Settings.GivenDefaults))
            {
                try
                {
                    if (item.Source == SourceType.RepoFolder)
                        await AddonInstaller.InstallRepoFolderAsync(Downloader, Settings, AshitaRoot, item, null, FfxiFolder);
                    else if (item.Source != SourceType.Bundled) continue;   // release archives are installed from the Addons page
                    if (!Settings.EnabledAddons.Contains(item.Id, StringComparer.OrdinalIgnoreCase)) Settings.EnabledAddons.Add(item.Id);
                    Settings.GivenDefaults.Add(item.Id);
                    changed = true;
                    Log.Info("given by default: " + item.Id);
                }
                catch (Exception ex) { Log.Warn("default " + item.Id + " not installed, will try again: " + ex.Message); }
            }
            if (await OverruleOldVisionAsync("at start")) changed = true;
            if (await UpdateInstalledAddonsAsync("at start")) changed = true;
            if (!changed) { ApplyFileEdits(); return; }   // ApplyEnabledAddons runs them otherwise
            Settings.Save();
            ApplyEnabledAddons();
            Notify();
        }

        /// <summary>
        /// An old XI-Vision the player put in by hand, in the game folder or in an overlay of their own (XIPivot's
        /// polplugins\DATs or the old Dats plugin's plugins\DATs), carries a 2018 ROM/119/51.DAT that crashes the game
        /// on Quests. Every such copy is overruled: the game folder's is swapped for a clean current one, an overlay's is
        /// set aside (both kept as 51.DAT.old-mod), and our own XI-Vision is installed and ticked so the player keeps
        /// Vision's look, built safely. Run at start and on every Play. Returns whether settings changed.
        /// </summary>
        public async Task<bool> OverruleOldVisionAsync(string when)
        {
            if (!HasAshita) return false;
            var vision = Catalog.Find("xi-vision");
            const string rel = @"ROM\119\51.DAT";
            var stale = new List<string>();
            try
            {
                var copies = new List<string>();
                if (!string.IsNullOrWhiteSpace(FfxiFolder)) copies.Add(Path.Combine(FfxiFolder, rel));
                foreach (var root in new[] { PivotConfig.OverlaysRoot(AshitaRoot), Path.Combine(AshitaRoot, "plugins", "DATs") })
                    if (Directory.Exists(root))
                        copies.AddRange(Directory.GetDirectories(root)
                            .Where(d => !string.Equals(Path.GetFileName(d), "xi-vision", StringComparison.OrdinalIgnoreCase))
                            .Select(d => Path.Combine(d, rel)));
                stale = copies.Where(f => File.Exists(f) && DatPatch.IsOldInterface(File.ReadAllBytes(f))).ToList();
            }
            catch (Exception ex) { Log.Warn("old Vision check " + when + ": " + ex.Message); return false; }
            if (stale.Count == 0) return false;

            var gameCopy = string.IsNullOrWhiteSpace(FfxiFolder) ? null : Path.Combine(FfxiFolder, rel);
            foreach (var f in stale)
            {
                try
                {
                    if (string.Equals(f, gameCopy, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(vision?.PatchBase)) { Log.Warn("old interface file in the game folder, but no clean base in the catalogue"); continue; }
                        var clean = Path.Combine(Path.GetTempPath(), "vdl-clean-" + Guid.NewGuid().ToString("N"));
                        try
                        {
                            await Downloader.DownloadFileAsync(vision.PatchBase, clean);
                            string said = null;
                            await Task.Run(() => DatPatch.RepairOwnFile(f, clean, out said));
                            Log.Info("old Vision overruled " + when + ": " + said);
                        }
                        finally { try { File.Delete(clean); } catch (Exception) { /* harmless */ } }
                    }
                    else
                    {
                        var keep = f + ".old-mod";
                        if (File.Exists(keep)) File.Delete(keep);
                        File.Move(f, keep);
                        Log.Info("old Vision overruled " + when + ": " + f + " set aside as 51.DAT.old-mod");
                    }
                }
                catch (Exception ex) { Log.Warn("old Vision at " + f + " not overruled: " + ex.Message); }
            }

            if (vision == null || Settings.InstalledVersions.ContainsKey(vision.Id)) return false;
            try
            {
                await AddonInstaller.InstallRepoFolderAsync(Downloader, Settings, AshitaRoot, vision, null, FfxiFolder);
                foreach (var id in new[] { "pivot", vision.Id })
                    if (!Settings.EnabledAddons.Contains(id, StringComparer.OrdinalIgnoreCase)) Settings.EnabledAddons.Add(id);
                Log.Info("our XI-Vision installed in place of the old one " + when);
                return true;
            }
            catch (Exception ex) { Log.Warn("XI-Vision not installed " + when + ", will try again: " + ex.Message); return false; }
        }

        /// <summary>
        /// Any installed repo-folder addon the catalogue has moved past: its files are brought up to date now, so
        /// a fix reaches every player without the Update button. Their tick is left alone. Run at start and again
        /// on every Play, because the launcher sits in the tray for days and a start alone missed a whole day's
        /// addon update (16 Oct 2026: the shared window look). Returns whether anything was updated.
        /// </summary>
        public async Task<bool> UpdateInstalledAddonsAsync(string when)
        {
            if (!HasAshita) return false;
            var changed = false;
            foreach (var item in AddonInstaller.InstalledToUpdate(Catalog, Settings.InstalledVersions))
            {
                try
                {
                    var plan = await AddonInstaller.InstallRepoFolderAsync(Downloader, Settings, AshitaRoot, item, null, FfxiFolder);
                    changed = true;
                    Log.Info(item.Id + " updated to " + item.Version + " " + when + ": " + plan.Download.Count + " file(s) downloaded");
                }
                catch (Exception ex) { Log.Warn(item.Id + " not updated " + when + ", will try again: " + ex.Message); }
            }
            return changed;
        }

        /// <summary>
        /// The catalogue's file edits for every enabled item (Services\FileEdits.cs), such as Fancychat's welcome popup
        /// marked as already seen. Run at every start too, so a settings file the addon wrote since is caught.
        /// </summary>
        public void ApplyFileEdits()
        {
            if (!HasAshita) return;
            foreach (var item in Catalog.ScriptEntries(Settings.EnabledAddons).Select(e => Catalog.Find(e.Id)).Where(i => i != null && i.FileEdits.Count > 0))
            {
                try { foreach (var f in FileEdits.Apply(AshitaRoot, item)) Log.Info("file edit " + item.Id + ": " + f); }
                catch (Exception ex) { Log.Warn("file edits " + item.Id + ": " + ex.Message); }
            }
        }

        public int EnabledCount => Settings.EnabledAddons.Count(id => Catalog.Find(id) != null);
        public int UpdateCount => Catalog.Items.Count(i => i.Source == SourceType.GithubRelease && Settings.InstalledVersions.ContainsKey(i.Id) && !string.IsNullOrEmpty(i.Version) && !string.Equals(Settings.InstalledVersions[i.Id], i.Version, StringComparison.OrdinalIgnoreCase));

        public string LoaderVersion()
        {
            // xiloader's FileVersion string is empty; the fixed parts carry the number (Loader.Installed).
            var v = Loader.Installed(AshitaRoot);
            if (v == null) return "missing";
            return Loader.IsSupported(v) ? v.ToString() : v + " (server takes " + Loader.RequiredMajor + "." + Loader.RequiredMinor + ")";
        }

        public string AshitaUpdated()
        {
            try { var dll = Path.Combine(AshitaRoot, "Ashita.dll"); return File.Exists(dll) ? File.GetLastWriteTime(dll).ToString("yyyy-MM-dd") : "not installed"; }
            catch (Exception) { return ""; }
        }

        /// <summary>Write the startup script and the POL plugin flags from the enabled set.</summary>
        public void ApplyEnabledAddons(Profile profile = null)
        {
            if (!HasAshita) { Log.Warn("apply addons: no Ashita folder set, nothing written"); return; }
            PivotConfig.RemoveRootPath(AshitaRoot);   // a value an earlier launcher wrote that stops overlays loading
            try
            {
                if (RetiredOverlays.Present(AshitaRoot).Count > 0)
                    foreach (var id in RetiredOverlays.Sweep(AshitaRoot, FfxiFolder))
                    {
                        Settings.EnabledAddons.RemoveAll(e => string.Equals(e, id, StringComparison.OrdinalIgnoreCase));
                        Settings.InstalledVersions.Remove(id);
                        Settings.Save();
                        Log.Info("retired overlay removed, the game's own files carry it now: " + id);
                    }
            }
            catch (Exception ex) { Log.Warn("retired overlays: " + ex.Message); }
            var entries = Catalog.ScriptEntries(Settings.EnabledAddons);
            var scriptPath = Path.Combine(AshitaRoot, "scripts", "vanadreams.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));
            ScriptWriter.Write(scriptPath, entries);
            ApplyFileEdits();
            Graphics.EnsureScript(AshitaRoot);   // the startup script runs it
            // XIPivot overlays: listed in pivot.ini while enabled, dropped from it when not
            foreach (var overlay in Catalog.Items.Where(i => i.Install == InstallAction.PivotOverlay))
            {
                try
                {
                    if (Settings.EnabledAddons.Contains(overlay.Id, StringComparer.OrdinalIgnoreCase) && overlay.IsInstalled(AshitaRoot)) PivotConfig.AddOverlay(AshitaRoot, overlay.Id);
                    else PivotConfig.RemoveOverlay(AshitaRoot, overlay.Id);
                }
                catch (Exception ex) { Log.Warn("pivot.ini " + overlay.Id + ": " + ex.Message); }
            }
            var pol = entries.Where(e => e.Kind == LoadKind.PolPlugin).Select(e => e.LoadName).ToList();
            foreach (var p in profile != null ? new[] { profile } : Profiles.LoadAll().Where(x => !x.IsExample).ToArray())
            {
                var changed = false;
                foreach (var name in pol) if (!p.PolPlugins.ContainsKey(name) || !p.PolPlugins[name]) { p.PolPlugins[name] = true; changed = true; }
                foreach (var key in p.PolPlugins.Keys.ToList())
                    if (p.PolPlugins[key] && !pol.Contains(key, StringComparer.OrdinalIgnoreCase) && Catalog.Items.Any(i => i.LoadKind == LoadKind.PolPlugin && string.Equals(i.LoadName, key, StringComparison.OrdinalIgnoreCase)))
                    { p.PolPlugins[key] = false; changed = true; }
                if (!string.Equals(p.Script, "vanadreams.txt", StringComparison.OrdinalIgnoreCase)) { p.Script = "vanadreams.txt"; changed = true; }
                if (changed) p.Save();
            }
        }
    }
}
