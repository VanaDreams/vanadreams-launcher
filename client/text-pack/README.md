# Vanadreams text pack

Corrections to retail dialog lines the game client shows, shipped as the `vanadreams-text` XIPivot overlay
(`vanadreams-text-NN.zip`, dest `{ashita}/polplugins/DATs/vanadreams-text`) in the launcher's client manifest.
The launcher lists it in pivot.ini once, like the look and title packs; the Install page shows it as "Text fixes".

- `fixes.json`: each correction, by dialog file id, line number, the exact old text and its replacement, and why.
- `build.py`: reads an untouched client, changes only those lines, checks every other line is byte for byte the
  same, and writes the zip and its `.chunk.json`. Run it with no arguments for usage. Name the other overlay packs
  after the out folder and it stops if any of them holds one of this pack's files.
- `homepoint.py`: the Rolanberry hangout home point (below). `build.py` runs it; it needs xi-tools.

To find a line: `explain/zone_NNN.txt` in the project's `cutscenes/xi-explain-all-zones.tgz` names the event and the
line number (`print_msg2 ... <speaker> <line> "..."`). English dialog for zones 0-255 is file id 6420 + zone.

The built DATs hold Square Enix text, so they are not committed here; only the script and the fixes are.
Bump `PACK` in `build.py` for every new pack, upload the zip and append its `.chunk.json` entry as the last chunk.

## Fixes

| Zone | Line | Was | Now |
|---|---|---|---|
| Port San d'Oria (232) | 7421, Avandale's new-character intro | "...at the bottom of the staircast in Northern San d'Oria." | "...at the bottom of the staircase leading to Northern San d'Oria." |

Teilsa stands in Port San d'Oria (F-9), at the foot of the stairs up to Northern San d'Oria; the Japanese line says
exactly that (北サンドリア方面の階段下). The English line had a typo and put her in the wrong city.

### Support job lesson (Isacio in Selbina, Vera in Mhaura)

Vanadreams runs `SUBJOB_RATIO = 3`: the support job's level equals the main job's level, not half of it. Both NPCs
that teach the support job (Elder Memories / The Old Lady) said "half". The lines that mentioned half (Selbina file id 6668
lines 6870, 6872, 6876, 6877; Mhaura file id 6669 lines 7064, 7066, 7068, 7070, 7071) now say the support job is held to the main job's level.
Isacio's closing advice (line 6878) also says a beastmaster and a summoner, in either order, keep two companions at once
(`main.ENABLE_DUAL_PETS`, covered by `scripts/tests/systems/dual_pets.lua` in the server repo).

### Item strings (Jubilee Ring becomes Skillz Ring)

`fixes.json` also has an `items` list for item names, log names and descriptions. Item DATs (English: 73 general,
74 usable, 75 weapons, 76 armor, 77 puppet, 91 currency, 55671 general2, 55668 armor2; 55667 is a general2 file of
blank "." records, not the English strings) are bit-rotated records.
`build.py` decodes the file, proves it re-encodes to the original bytes, rewrites only the named strings (each must
fit where the old one sat, before the icon at record offset 0x280) and checks that no other byte changed. Item 27593
(armor2, file id 55668) shows as "Skillz Ring", "Skill-up rate +100%" / "(skill 300 or below)", with no experience
line. The pack then carries the armor2 DAT (about 28 MB unpacked, 5 MB zipped).

Item 9025 (general2, file id 55671, "??? Box +1" on retail) is Clan Centurio's Mark Trophy on the server: a mark's
killer gets one, and Montblanc takes it with the mark's own drop. It shows as "Mark Trophy" / "mark trophies" with a
three-line description. The pack then carries the general2 DAT as well.

### The Rolanberry hangout home point (from vanadreams-text-02)

Every home point crystal (events 8700-8704; 335 actor blocks in 67 zone event DATs) gains home point 122,
"Rolanberry Fields #1": zone 110, fee 500, zone-list position 67 under Derfland (region 11), at the hangout crystal
(327.0, 23.62, 588.5). The loop bounds 122 and 67 become 123 and 68. Rolanberry Fields' event DAT (file id 5930)
gains one home point block (actor 0x0106E290, event 8700) whose messages point at the home point text its dialog DAT
already has (lines 0-21, and 6422 for "You do not have enough gil."). The server side is index 122 in
`scripts/globals/homepoint.lua` and the crystal in `modules/vanadreams/hangout/hangout_decor.lua`; the fee there
(`fee = 1`, 500 gil) must match.

`homepoint.py` finds every table by decoding the script, writes only slots that hold the empty constant, appends
new constants, changes the two bounds in place only after showing every reader is a loop bound or a loop exit, keeps
every other block byte for byte, and re-decodes each patched block with xi-tools' `xi_explain` against the original.
The pack then carries 68 event DATs (about 22.7 MB unpacked).

### Anniversary-family exemplar descriptions (unreleased)

The five anniversary-family rings retain their display and log names. Their descriptions
now say "Exemplar point bonus" instead of "Experience point bonus". Bonus percentage,
720-minute duration and individual caps are unchanged. Decennial (10796), Novennial
(11666) and Anniversary (15793) use English armor file ID 76; Undecennial (28528) and
Duodecennial (28562, displayed as "Duodec. Ring") use armor2 file ID 55668.

Run `python -B -m unittest discover -s client/text-pack -p test_ring_text.py -v` for
catalog checks. Set `TEXT_PACK_TEST_CLIENT` to a client directory to also run read-only
DAT tests: exact old strings, rotation roundtrip, rejection of mismatched/oversized
edits, and byte-identical preservation outside the five descriptions. Tests write no
DATs. The inspected client already contains the Skillz Ring patch; these tests therefore
do not certify it as an untouched input for the complete text-pack builder.

This is source-only and must ship with the matching server exemplar effect changes.
No pack version or launcher manifest is changed here. Before release, obtain a verified
untouched input for the full builder, coordinate the next PACK version and existing
Kupo/home-point overlays, and validate all five tooltips in game. Do not overwrite a
newer text overlay with a partial pack or publish client chunks as part of this change.
