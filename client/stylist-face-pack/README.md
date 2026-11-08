# Stylist character face pack 01

Review candidate for client **30260904_1**, requiring launcher **0.2.58** for enforced asset verification.
It adds **41 race/sex mappings representing 34 distinct looks**, including all five Ark Angels.
This is the compatible roster below, not a claim that every unique NPC model is supported.
Server unlocks and asset availability remain gated until the staged rollout and in-game checks pass.

## Package identity

- File: `../chunks/30260904_1/vanadreams-stylist-faces-01.zip`
- SHA-256: `47ece5a82234f3e94aec8e9c8c3755d94a342144988e68179d4163e099dc1e87`
- Compressed: 3,355,243 bytes. Unpacked: 5,865,838 bytes. Files: 46.
- Required baseline: `vanadreams-hair-01.zip`, SHA-256
  `daec3d8434bab1dd4411e089c03e80434747eed8369834a8218dc6a2d0883993`.
- Generated server catalog: `vanadreams_stylist_faces.lua`, SHA-256
  `f14aaeb3c3936a3e53ae86e931065d7e10ae479efe124f449f7ff7f7c42898aa`.
  The catalog embeds this exact client version, ZIP name/hash, race, face byte and Ark Angel flag.

The ZIP replaces `FFXiMain.dll`, `FTABLE.DAT`, `VTABLE.DAT`, `ROM9/FTABLE9.DAT`, and `ROM9/VTABLE9.DAT`,
and adds 41 head DATs under `ROM9/102/`. File IDs are 54701–54741; custom face bytes start at 64 per race.
The sidecar pins the hair prerequisite, marks the pack required, and verifies all 46 installed files.
This is a direct game-folder patch, not an optional XIPivot overlay. No rollback ZIP belongs in `client/chunks/`:
every ZIP there is automatically shipped. Preserve the builder's rollback artifacts separately with the release.

## Roster

Names are in ascending face-byte order within each group, starting at 64. Tarutaru male and female each receive
seven choices; the other races only receive the choices compatible with their listed player skeleton.

- Hume male: Ark Angel HM, Volker, Volker (S), Tenzen, August, Cid, Selh'teus.
- Hume female: Nashmeira, Mildaurion, Lion, Lilisette, Arciela, Iroha.
- Elvaan male: Trion, Luzaf, Pieuje (UC), Excenmille (S).
- Elvaan female: Ark Angel EV, Ulmia, Prishe.
- Tarutaru male and female: Ark Angel TT, Ajido-Marujido, Cherukiki, Kukki-Chebukki, Makki-Chebukki,
  Tosuka-Porika, Zonpa-Zippa.
- Mithra: Ark Angel MR, Perih Vashai, Perih Vashai (S), Jakoh Wahcondalo.
- Galka: Ark Angel GK, Zeid II, Gumbah (S).

Ark Angel selection is intended to require completion of the all-five-Ark-Angels Divine Might fight, enforced by
the matching server change. Installing assets does not itself grant that unlock. All players need the assets to
see another player's selected face, whether or not they have unlocked it themselves.

## Visual limits and exclusions

- Eight retargeted heads (Lion, Lilisette, Arciela, Iroha, Prishe, Cid, Selh'teus, Excenmille (S)) use neutral
  expressions. They follow remapped player head/neck/chest animation but do not preserve NPC blinking, mouth
  expressions, or independent hair animation. Original proportions are retained, including smaller Prishe and
  child-size Excenmille (S) heads; neck seams and long-hair collisions need in-game review.
- Full-helmet coverage/hiding passed offline validation, but this is not an all-headgear or in-game compatibility
  guarantee. August's fused scalp can intersect partial wizard hats; Lion's face-connected bandana remains visible
  under partial hats; Cid's
  short side hair/beard can intersect tight hats; Luzaf retains two small forehead intersections with hat model 18.
  Mildaurion retains a short source neck collar. Selh'teus retains the source low-alpha texture/render state,
  whose live-game appearance remains untested.
- Helmeted Zeid is held because the face mesh includes his spiked helmet; the unhelmeted Zeid II is included.
- Gessho is excluded because the source head/wing-shoulder geometry has no native player neck seam.
- **No in-game rendering or gameplay validation has been completed.** Offline geometry/header/table validation
  and successful installation do not prove every animation, hat, zone transition, or another player's view.

## Verification completed

The final real archive passed the publisher's ZIP/hash/sidecar/dependency checks and 19 offline integration
assertions through launcher 0.2.58's actual installation API. These cover fresh install, every installed-file hash,
no duplicate download, all 256 non-overlapping hair assets unchanged, missing head DAT repair, legacy hair
DLL/table overwrite, interrupted hair→face replay, locked-table partial extraction/recovery, and imported clients.
The source suite passed 182 launcher tests and 9 publisher tests. See `validation.json` for the artifact-bound record.

Remote main `6a8f01a39615423ea7615f59a1ca8ddbd40df083` contained only the original pinned hair ZIP for this version.
An audit of 56 remote refs found only one extra review pack, a separate Vision UI overlay containing
`ROM/119/51.DAT` and a README, with no DLL/table/ROM9 overlap. Public manifest verification returned HTTP 403;
live/manual changes outside these repository refs remain unverified.

## Staged release, not performed by this review

1. Land the launcher/publisher source through `64a8279d22e0d0c4262635e0dc39b39a304a3c73` **without this asset commit**.
2. With release approval, build/test/sign/publish launcher 0.2.58 using the existing Launcher release workflow.
   Verify the signed release and self-update path. Existing 0.2.57 launchers do not enforce the new metadata.
3. Only then merge this separate asset commit. That merge triggers Ship client packs and changes live R2 content.
   Verify the public ZIP hash and all manifest metadata, then test fresh/existing/imported client patching.
4. Apply the matching server catalog/menu changes with their asset gate off. Finish in-game checks of the roster,
   representative headgear/animations, zone changes, and visibility from another updated client. Enable the gate
   only after player coverage is verified. Bypassing the launcher is not attested by this change.

The locally built 0.2.58 executable is unsigned and must not be distributed to players. Windows SDK signtool and
Azure CLI are installed on the review computer, but local signing is blocked by the absent
`launcher/tools/signing/client/bin/x64/Azure.CodeSigning.Dlib.dll`. CI signing requires the configured Azure OIDC
variables/federation and Artifact Signing signer permission; these credentials were not exercised here.
No signing software was installed, no login/signing attempt was made, and no main merge, tag, release, workflow
dispatch or deployment was performed. Full delivery-contract details are in [the chunk guide](../chunks/README.md).
