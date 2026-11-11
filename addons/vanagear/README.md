# vanagear

Gear sets built from what you carry, for the job and level you are on, and a list of commands
for that job. Type `/vg` in game.

vanagear is built on [LuAshitacast](https://github.com/ThornyFFXI/LuAshitacast) by ThornyFFXI
(MIT licence, see `LICENSE-LuAshitacast`), which still does the swapping. It replaces the
separate luashitacast addon: tick one or the other, and the launcher keeps luashitacast out
while vanagear is ticked.

## Display language

The Settings tab offers **Auto / 自動**, **English**, and **日本語 / Japanese**.
You can also type `/vg language auto`, `/vg language en`, or `/vg language ja`
(`/vg lang` is an alias). `/vg language` shows the saved choice and effective language.
The choice is saved in VanaGear's existing `config/addons/vanagear/prefs.lua`.

Auto follows **Ashita's addon/resource language**: `ashita.language` → `ashita = 1`
means Japanese; English and unavailable/unsupported values fall back to English.
This is separate from xiloader's `--lang` numbering. A manual choice changes VanaGear's
display, not your client installation, loader arguments, or Ashita settings.

Set names, profile keys, item IDs, command words and the lines in your own commands stay
as written. Item/spell/ability resources continue to follow the client/Ashita resources;
choosing Japanese labels does not change an English game into a Japanese game. Built-in
examples keep their original command lines; use names accepted by your own game client.
Untranslated diagnostics fall back to English. Legacy messages echoing non-ASCII player text
of unknown encoding retain their original wording and bytes, so a name is not double-encoded.

Japanese window text is UTF-8. VanaGear includes the M PLUS 1p Japanese font under the
SIL Open Font License (`fonts/OFL.txt`); it loads the font for its own windows without
installing a Windows font. Translated game-chat messages use the existing CP932 converter.
See [LOCALIZATION.md](LOCALIZATION.md) for adding languages and verification requirements.

## What it does

- Reads your inventory and wardrobes, and scores every piece your job, level and race can wear
  against that job's weights (`jobs\<JOB>.lua`, built on `vg\roles.lua`). Caps count: once gear
  haste is at 25% or damage taken at -50%, a slot goes to something else.
- The weights come from the player-written job guides on FFXIclopedia and BG-wiki, by level
  band (1-30, 31-60, 61-75, 76-99), so a level 60 is scored by what the guides say a 60 wants,
  not by what a 99 wants. Each job file names its guides. The support jobs (WHM, BLM, RDM,
  SCH, GEO, SMN, BRD) get no fighting set unless their guide gives one at that level; NIN, PLD
  and RUN are built as tanks.
- Each weaponskill's set starts from the server's own script for it (its stat modifiers, hits
  and kind) and adds what the wiki weaponskill pages say about gear (`vg\wsguide.lua`): crit
  rate on the ones that crit with TP, attack on Fudo, Kaiten, Iron Tempest and Sturmwind,
  accuracy on the ones whose accuracy drops with TP, and so on.
- Builds idle, resting, damage taken, fighting (default, hybrid, accuracy), one set per
  weaponskill your weapons can use, precast, interim and midcast by skill, one set per spell you
  have learned and can cast now (main or support job: Utsusemi, elemental and debuff ninjutsu,
  cures, nukes...; listed under "Your spells" in /vg), shooting, and pet sets for pet jobs.
- Rebuilds by itself, without a word in chat, on job change, level up, a weapon change, and
  every zone (gear you picked up or moved since is counted then). `/vg build` rebuilds now and
  says so.
- Generated player sets leave Main, Sub, Range and Ammo alone. A jug, pet food, animator, ranged weapon
  or ammunition you equip stays until you change it or an explicit profile/action set changes it.
  There is no automatic slot lock; ordinary armour still follows your profile.
- Multi-slot armour is kept compatible: a Tunic cannot be worn with a head piece. Generated
  sets account for those exclusions, and old or combined sets are checked before swapping.
  Your explicit set choices beat conflicting generated pieces. If two explicit choices conflict,
  item Priority wins, then the later equip call, then slot order. Existing disabled/encumbered
  slots stay protected; simply wearing a multi-slot piece does not lock the other slots.
- The fighting set is listed as **Engaged** (a set of yours named Engaged, TP or Melee is the
  same set).
- Waits after every zone: nothing is equipped, loaded or sent to the game until you have been
  standing in the zone for 10 seconds (`/vg wait <seconds>` changes it).

## Your own profiles

Profiles in `config\addons\luashitacast\` keep working, `/lac` included. A profile with sets of
its own runs exactly as LuAshitacast runs it: its weapons swap as written, nothing vanagear built
fills a slot it leaves out, and a piece you put on from the menu is swapped off as usual. The one
difference is the wait after every zone (below), which protects the server. vanagear's sets go
under your profile only if you turn that on with `/vg underlay on`.

Don't load the luashitacast addon as well: vanagear is LuAshitacast, and two of them swap every slot
against each other. If it is loaded, vanagear notices, stops swapping and says so in chat; type
`/addon unload luashitacast` and your profile and `/lac` commands carry on in vanagear.
Profiles the launcher made from GearSwap files, and profiles whose sets are all empty, get the
built sets underneath automatically, so anything they leave out is filled.

To move a profile over to vanagear, press **Copy its sets into vanagear and stop using it** in
the window, or type `/vg takeover`: its sets become your own vanagear sets for that job (Engaged,
precast and the rest under the names vanagear wears them by), and vanagear runs the job from
then on. The profile file is left as it was; `/vg profile on` uses it again.

## Commands

`/vg help` lists the ones that mean something for your job. `/gs c <word>` does the same as
`/vg <word>`, and Motenten's mode commands and F9-F12 keys (`/vg keys mote`) work for players
coming from GearSwap.

## Your support job's gear

Vanadreams lets your main job wear the weapons and armour your support job can wear, through your
own sets. The game's equipment menu only uses your main job's gear; the sets vanagear builds for
you and your trusts, and `/vg upgrades`, count the support job's pieces too. In the `/vg`
window, every slot's list of your own set also offers your support job's pieces; or use
`/vg set <name> <slot> <item>` (for example `/vg set Axe Main Great Axe` on BLM/WAR), then
`/vg wear <name>`. Weapon skills you get this way cap at 80% of the support job's cap.

## Your trusts

Every trust you have called gets its own tab in the `/vg` window, next to yours. The top of the
tab says who the trust is: its name, its job and support job, its level, and the spells,
abilities and weaponskills it uses (the server tells vanagear; those lines stay out of your chat).

Like your own, a trust has several sets, built from your inventory and wardrobes for its job and
level:

- **Idle** while it stands around, and **Engaged** while it fights. A Paladin or Rune Fencer
  trust's Engaged set is a tank's: damage taken down, HP, defence and enmity.
- **Weaponskill**, and a set for each weaponskill it uses that vanagear knows (Savage Blade...).
- A set for each kind of spell it casts: **Cure**, **Nuke**, **Enfeebling**, **Enhancing**,
  **Dark**, **Divine**, **Song**, **Ninjutsu**, **Blue Magic**, **Geomancy**.
- **Ability** (enmity) for a tank.

Every slot of every set is a list you can change. Under the set list, **Add a set for** offers a
set for anything the trust uses that has none yet (Flash, Sentinel, Cure IV), and **New set for**
takes any name. An action's set goes on over Idle or Engaged while the action goes off: first a
set with the action's own name, else one for its kind, else **Magic** for any spell. Slots it
leaves empty keep the Idle or Engaged piece.

The trust never wears anything and nothing leaves your bags: the server gives the trust the
stats of the pieces, and a weapon's damage when it beats the trust's own, evened out to the
trust's swing speed. A piece you drop, sell or move to storage stops counting; the trust's job
and level must fit the piece, the same as yours (its support job's gear counts too). The server
keeps every set, so a set is only sent again when it changes; **Send all again** sends them all.
`/vg trusts off` stops vanagear sending anything. Without vanagear, `!trustgear <trust>` lists a
trust's sets, `!trustgear <trust> <set>` shows one, `!trustgear <trust> <set> clear` deletes it
and `!trustgear <trust> clear` deletes them all. Your picks are kept in
`config\addons\vanagear\trusts.lua`.

**Choosing what a trust uses.** Each spell, ability and weaponskill in a trust's "Uses" list has a
checkbox. Untick one and that trust never casts or uses it: the server skips it for as long as the
trust is out and remembers the choice for the next time you call it. "other spells" and "other
abilities" switch off the ones a trust picks in the fight (bursts, storms, Indi spells...). Without
vanagear, `!trustuse <trust>` lists what is switched off, `!trustuse <trust> off <action>` and
`on <action>` change it (a name like `Cure IV` or `Flash`, or a kind: `cure`, `nuke`, `enfeebling`,
`enhancing`, `dark`, `divine`, `song`, `ninjutsu`, `ability`, `weaponskill`) and
`!trustuse <trust> reset` puts everything back. The server answers `!trustuse <trust> info` with a
`[VG off]` line that vanagear reads and hides, as it does the `[VG uses]` line.

## Weapons

Generated player sets never choose weapons or ammunition, including when an old saved setting
has `/vg weapons on`. Old generated weapon/ammo entries are ignored immediately.

With `/vg weapons off` (the default), Main/Sub keep their existing protection. The Main/Sub
lists in `/vg` (or `/vg weapon main|sub <item>`) choose a weapon for every set. A standalone
LuAshitacast profile still swaps its weapons as written; a vanagear profile can allow a Main/Sub
item through with `gVana.WeaponAllow`. `/vg weapons on` now only allows Main/Sub choices
written in your own profile; it does not let the builder choose weapons.

Range and Ammo are never generated, even with `/vg weapons on`. Their drop-downs are normal
per-set choices, saved in your profile. Plain equipment-menu and macro changes are not undone by
generated sets. Explicit profile choices still swap normally, including instruments, ranged
action ammunition, BST jugs/Reward food and PUP Repair/Maintenance oil. No permanent slot lock
is added. If an item runs out or an action fails, generated sets do not substitute another
Range/Ammo item; choose a replacement yourself. Existing helper action timeouts are unchanged.

Old generated Range/Ammo entries are ignored immediately, before the next rebuild. A legacy
job-wide Range preference stays in prefs for reference but is no longer forced on every set.
`/vg weapon range <item>` now equips once; use `/vg set <set> Range <item>` to save a choice.
A profile's explicit Range/Ammo choices can still replace a manual choice when that set runs;
remove that slot from the set (choose Built in its drop-down) if you want it left alone.

## Geomancer handbells

A handbell you equip manually is left alone by generated sets. You can also choose it explicitly
in Geomancy (Geo and Indi spells), Indi (Indi spells only), or an individual spell set such as
IndiFury. An individual spell choice takes priority over the shared Indi choice. This also works
with existing GEO template profiles and the underlay off, without rewriting your profile.
Clearing a spell-specific pick returns to its shared Indi choice; clear that slot in Indi too
if you want the currently equipped item left alone.

Only your explicit selections are used; vanagear does not choose a bell or lock Range. An explicit
incompatible Ammo choice can still remove a bell under the server's equipment rules, so leave
Ammo unspecified in those sets unless you intentionally want that swap.

## Your own sets

- `/vg dress` stops the swapping. Put on what you like from the equipment menu, then
  `/vg save <name>` saves it under that name and swapping comes back on. Main/Sub are left out
  unless `/vg weapons on`; Range/Ammo are saved as explicit choices.
- Every slot in the `/vg` window is a list: pick a piece and it is yours in that set (marked *).
  **New set** saves what you wear under the typed name. A slot you pick in Idle stays yours
  while you walk and when your HP or MP is low: the Movement, Regen and Refresh pieces vanagear
  would lay over Idle by themselves skip it. `/vg idle speed`, `/vg idle regen`, `/vg idle refresh`
  and `/vg kite` are you asking for them, so they still go on.
- A set of yours named like a built one (`Idle`, `Tp_Default`, `Reward`, `CallBeast`, a
  weaponskill or spell) replaces the slots it names wherever that set goes on. Any other name is a
  set of its own: `/vg wear <name>` (until `/vg wear off`), `/vg with <name> <command>`,
  `/gs equip <name>`, `/vg look <name>` (lockstyle).
- **Lock my look to this set**, the box above a set's slots in the window (or `/vg look <name>`,
  built sets included), locks your appearance to that set: the swaps for actions and overlays
  still happen, they stop showing. It is kept per job and put back after every rebuild and zone.
  Untick, or `/vg look off`, to unlock.
- `/vg set <name> <slot> <item>` changes one slot from chat; `/vg sets` lists yours.
- Kept in your job's profile, `config\addons\vanagear\profiles\<Name>_<id>\<JOB>.lua`, in its sets table.
- **Put this set on me** in the window puts a set on once (your picks, built filling the rest); **Put the built set on me** puts the built set on alone; **Save what I'm wearing to this set** writes what you wear into the set; **Undo my last change** takes back your last change to it. Your profile's own swaps carry on from there.
  `/vg wear <name>` is the one that keeps it on.

## Settings

The **Settings** tab of the window has the gear modes as buttons and boxes: damage taken (off,
all, physical, magic), the fighting set, standing, nukes, your pet, magic burst, kite, Treasure
Hunter, staged casting, allowing your own Main/Sub swaps, filling empty slots from built sets, trust gear,
Motenten's keys, the zone wait, the slots you lock, and your job template's own settings. Each
one does exactly what its `/vg` command does, so macros keep working.

Every `/vg` command is listed in the **Your sets** tab, under **Commands**: click one to see what
it does. Your own commands are there too, as soon as you make them.

## Your own commands

- `/vg add <word> <command>; <command>` makes `/<word>` (and `/vg <word>`) run those lines in
  order; `wait 2` pauses. `/vg add all <word> ...` makes it for every job.
- **Chains.** A line `then <command>` goes once the action on the line before it has been seen
  to start and finish, plus the 3.3 s gap the server wants between links; `burst <command>` goes
  the moment it finishes. The lines it can wait on are `/ws`, `/ja`, `/ma` and `/pet`. A step
  that never starts, or a target that dies or changes, stops the command and says why; `/vg stop`
  stops one by hand. It does not check TP, Ready charges or finishing moves first.
  `/vg add chain /pet "Brain Crush" <t>; then /pet "Tail Blow" <t>; then /ws "Cloudsplitter" <t>; burst /pet "Fireball" <t>`
- The **How to** tab of the window explains the lines, chains, your own sets in a command, and
  what a LuAshitacast profile in Lua can do on top (the hooks, `gFunc`, `gData`).
- `/vg with <set> <command>` puts a set on, sends the command and keeps the set on until the
  action is done. Example: `/vg dress`, equip the jug, `/vg save lizard`, then
  `/vg add lizard /vg with lizard /ja "Bestial Loyalty" <me>` and type `/lizard`.
- The **Your commands** tab of the window walks you through making one (a word, its lines, which
  jobs), has examples to start from, and runs, changes and deletes the ones you have. They are
  written into your job's profile (its commands table; `ALL.lua` beside it for every job); **Open
  the folder** opens your profiles to write them by hand (`/vg reload` after).

## Beastmaster

- `/vg call <jug>` puts the jug in the ammo slot and uses Call Beast; `/vg loyal <jug>` does the
  same with Bestial Loyalty. A part of the name is enough (`/vg call fizzy`); with no jug it
  calls the last one. `/vg jugs` lists the ones you carry.
- `/vg reward [food]` puts the pet food in and uses Reward: `alpha` ... `theta`, `roborant`,
  `poultice`, or `debuff` (Pet Roborant, else Pet Poultice). With no food it uses the best
  biscuit for your level. `/vg foods` lists them.

## Data

`data\` is generated from the Vanadreams server's tables by `tools\gearstats\build.py`; rebuild
it when the server's gear changes. `tests\run.py` builds sets off-game from a stand-in bag.
