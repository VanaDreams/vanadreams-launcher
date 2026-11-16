# VanaDunna client patch (staged, not published)

Owner-requested custom handbell, item **22999**, cloned from **Matre Bell 21460**.
Level 1, all 22 jobs, ranged slot, handbell skill type 45. Name and icon are available
in English and Japanese. Description matches the server: MP+100%, Magic accuracy+20,
Geomancy +15, Fast Cast+10%. Server acquisition is **500 login points**; this client
project does not create the server item or alter Login Moogle prices.

## Build and verify

Uses the existing XI Tools `xi.database.xi_build` database action. `vanadunna.json`
is its reusable action, not a new binary format or new asset-authoring workflow.
`build.py` wraps that tool with guards that refuse occupied English/Japanese target
slots, reject input/output overlap, verify the donor is a handbell, and verify every
other record remains byte-identical. Dunna and Matre Bell are untouched.

```
python client/vanadunna/build.py <effective-current-client-root> <new-staging-output> --xi-tools <xi-tools-src/src>
```

Supply the effective current weapon files including any existing patches, not an
arbitrary pristine client if a prior release changed these files. Builds never write
to the source root. Set `VANADUNNA_SOURCE` and `XI_TOOLS_SRC`, then run:

```
python -m unittest discover -s client/vanadunna -p test_build.py -v
```

2026-11-16: all six focused tests passed. Both original client slots were blank (`.`).
The installed source weapon DATs match the packaged ROM-02/ROM-05 weapon files.
All non-target bytes in both 0x1400-format tables were preserved, and repeated builds
produced the same ZIP hash. No local overlay contained either exact weapon DAT path.

Important: **client all-jobs mask is 8388606; server all-jobs mask is 4194303**.
The original supplied SQL confused these representations. XI Tools encodes the client
mask from `jobs: all`.

## Prepared artifact / existing delivery route

`client/chunks/30260904_1/vanadreams-vanadunna-01.zip` and `.zip.json` contain only:
- `ROM/118/108.DAT` (English weapons)
- `ROM/0/6.DAT` (Japanese weapons)

Destination is the launcher's existing `{ashita}/polplugins/DATs/vanadreams-text`
overlay. No new executable, overlay name, or player manual install is required.
The ZIP SHA-256 is recorded in the adjacent sidecar. `verification.json` records the
input/output DAT hashes and the checks performed.

This branch is **not a release**. Publishing is the normal coordinated client-pack
release after the server migration/shop change is ready. Recheck the latest client
manifest and overlay priority at release time: if another patch has since changed
either weapon DAT, rebuild from those effective files rather than replacing them.
Do not start hosted Actions for testing. No server restart, live database write,
client install write, R2 upload, or manifest update was performed in this work.

The in-game name/icon display and equip/use behavior still need player verification
following coordinated release; offline validation is not an in-game test.
