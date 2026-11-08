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

Existing baseline: `30260904_1/vanadreams-hair-01.zip`, pink and platinum hair for the Stylist Moogle (see
`client/hair-pack/README.md`). Keep `xi.stylist.HAIR_COLOURS_OPEN` off on the server until players have it.

New review candidate: `30260904_1/vanadreams-stylist-faces-01.zip`, 41 race/sex choices including the five Ark Angels.
See [the roster, limitations, validation record and staged release notes](../stylist-face-pack/README.md).
Keep its asset commit separate from the launcher 0.2.58 source release, and keep the server asset gate off until rollout.

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
Vanadreams Play stops with an explanation instead of launching unchecked. After registration, it also verifies that
Windows actually points at the same game/COM root whose files passed checks; the legacy best-effort registration
fallback cannot launch a different copy when required packs are present. Older clients can still use the normal
full-version Patch flow, but the final launch check requires the exact pack-compatible version.

### Release sequence (requires separate deployment approval)

1. Review and test the ZIP, sidecar, launcher changes, and server changes together. Keep server face availability off.
2. Land the reviewed launcher/publisher source and version **0.2.58**, without the new face ZIP/sidecar commit.
   Keep source and binary-pack commits separate for this purpose; split a combined review before merging.
   Existing launcher 0.2.57 does not understand `requires`, `verify`, or `required`.
3. With separate approval, run **Launcher release** on that exact main commit. It builds/tests, obtains Azure OIDC
   signing credentials, signs and verifies the executable, scans it, and publishes `v0.2.58`. Confirm the signed
   release asset/version and the launcher self-update path before shipping the face pack. Do not tag, sign, release,
   or dispatch workflows as part of review.
4. Merge the approved face ZIP/sidecar commit to main only when ready to publish: that merge triggers **Ship client
   packs**, which uploads the new manifest and archive to live R2. A draft branch does not.
5. Verify the public manifest includes the exact ZIP hash, pinned hair baseline, dependency order, and full file-check
   list; verify the downloadable ZIP hash. Test a fresh install, an existing install, imported exact-version client,
   interrupted repair, and deliberately missing DLL/table/DAT using the new launcher.
6. Enable the server asset gate only after player rollout and in-game visibility checks pass. A normal FFXI client
   version stamp is **not** evidence that a player has this pack. Clients bypassing the launcher are not attested by
   these changes; universal enforcement would require server/loader protocol support.

Offline checks (no R2 credentials or writes):

```text
python -m unittest discover -s tools/clientpack -p "test_*.py" -v
dotnet test launcher/VanadreamsLauncher.Tests -c Release --nologo -v q
```

`ship_chunks.py --dry-run` reads the live R2 manifest; it is not the offline test command.

### Local 0.2.58 candidate and signing prerequisites

The local Release build is an **unsigned review candidate**, not a distributable update. The existing release
workflow remains the supported signed distribution route. It requires the repository's Azure client/tenant/
subscription variables, OIDC federation, and the Artifact Signing Certificate Profile Signer permission; those
credentials/permissions have not been exercised during this source-only preparation.

On the review computer, the Windows SDK `signtool.exe` and Azure CLI are installed, and the repository contains
signing metadata. The local `launcher/release.ps1` route is blocked because
`launcher/tools/signing/client/bin/x64/Azure.CodeSigning.Dlib.dll` is absent. No signing package was downloaded and
no Azure login, signing request, tag, release, workflow dispatch, or deployment was performed. Do not bypass the
signature requirements or distribute the unsigned candidate to players.
