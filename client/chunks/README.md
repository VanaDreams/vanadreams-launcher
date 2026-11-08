# Game-file packs

Packs here ship to every player as part of the client download. Merging one into `main` runs the
**Ship client packs** workflow, which uploads it to the `vanadreams-client` R2 bucket, reads it back, and adds it to
`fairywitch.ca/download/client/<client_ver>/manifest.json` (the old manifest is kept as `manifest.backup-<time>.json`).
The launcher picks it up on the player's next Play or Install/Patch.

- `<client_ver>/<name>.zip` unpacks over the game folder.
- `<client_ver>/<name>.zip.json` (optional) can name `"dest"`, a folder under the install such as
  `{ashita}/polplugins/DATs/<name>`, and `"sha256"` to pin the zip.

Changing a zip replaces its entry in place; unchanged ZIP bytes are not uploaded again. Sidecar-only metadata changes still update the manifest. Dependencies are validated and ordered before any upload. The run needs the `CLOUDFLARE_API_TOKEN` secret.
After a game update moves the client to a new version, move the packs to the new version's folder (rebuild any that
patch the game exe or dll).

Current packs: `30260904_1/vanadreams-hair-01.zip`, pink and platinum hair for the Stylist Moogle (see
`client/hair-pack/README.md`). Keep `xi.stylist.HAIR_COLOURS_OPEN` off on the server until players have it.

## Required character face packs

A pack that replaces shared DLL/table files must declare its exact baseline and installed-file checks in its
`.zip.json` sidecar:

```json
{
  "dest": "FINAL FANTASY XI",
  "sha256": "<archive SHA-256>",
  "required": true,
  "requires": [
    { "name": "vanadreams-hair-01.zip", "sha256": "daec3d8434bab1dd4411e089c03e80434747eed8369834a8218dc6a2d0883993" }
  ],
  "verify": [
    { "path": "FFXiMain.dll", "sha256": "<installed file SHA-256>" }
  ]
}
```

Include **every** face-pack archive member in `verify`, including the primary and ROM9 tables and new face DATs.
Paths are relative to `dest`. The publisher verifies these hashes against the actual ZIP, rejects missing or
changed dependencies and dependency cycles, and orders the final manifest for both new and older launchers.
Only the final pack should verify shared paths: hair's original DLL/tables are intentionally superseded by faces.

The updated launcher checks the actual selected game folder on each Vanadreams Play, before launching. A matching
receipt alone is insufficient for verified files. Missing/corrupt files cause a repair. Reinstalling a prerequisite
also replays its dependents; repairing earlier chunks in the same destination replays verified packs afterward.
Pending receipts are invalidated before extraction and restored only after verification, so interrupted or partial
repairs resume safely. A final verification pass catches conflicts before a patch is reported complete.

`required` also installs this pack and its prerequisite closure on an imported **exact-version** game copy without
redownloading the base client. Those receipts do not turn an imported copy into an incomplete managed installation.
Full installations keep a `@managed -` receipt for safe resumption. Other-server/retail profile launches do not run
Vanadreams's required-pack check. If the manifest cannot be checked, or the selected client version is incompatible,
Vanadreams Play stops with an explanation instead of launching unchecked.

### Release sequence (requires separate deployment approval)

1. Review and test the ZIP, sidecar, launcher changes, and server changes together. Keep server face availability off.
2. Bump and release a tested, signed launcher containing these checks before activating the new server choices.
   Existing launcher 0.2.57 does not understand `requires`, `verify`, or `required`.
3. Merge the approved pack/publisher change to main only when ready to publish: that merge triggers live R2 shipping.
   A draft branch does not. Do not dispatch the workflow as part of review.
4. Verify the public manifest includes the exact ZIP hash, pinned hair baseline, dependency order, and full file-check
   list; verify the downloadable ZIP hash. Test a fresh install, an existing install, imported exact-version client,
   interrupted repair, and deliberately missing DLL/table/DAT using the new launcher.
5. Enable the server asset gate only after player rollout and in-game visibility checks pass. A normal FFXI client
   version stamp is **not** evidence that a player has this pack. Clients bypassing the launcher are not attested by
   these changes; universal enforcement would require server/loader protocol support.

Offline checks (no R2 credentials or writes):

```text
python -m unittest discover -s tools/clientpack -p "test_*.py" -v
dotnet test launcher/VanadreamsLauncher.Tests -c Release --nologo -v q
```

`ship_chunks.py --dry-run` reads the live R2 manifest; it is not the offline test command.
