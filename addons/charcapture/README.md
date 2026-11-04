# charcapture

Takes a snapshot of your character for porting to Vanadreams: jobs and levels, skills, merits and job points, currencies, spells, abilities, weapon skills, traits, key items, quests and missions, every bag with augments, gear, gil, look, nation, rank and title. Fame is the one thing the game never tells the client, so Vanadreams grants it on import.

It reads only. It sends nothing and changes nothing in the game.

## Install

Copy this folder to `Ashita-v4beta\addons\charcapture\`, then in game:

```
/addon load charcapture
```

## Use

Log in on the character you want to bring with charcapture already loaded, and zone once (any door or exit), since the game sends your quest and mission logs when you zone. Open the **Merit Points** menu and the **Job Points** menu once from the main menu, since the game only sends your spent merits and job point upgrades when those open. Do the same for **Profile > Currencies** and its second tab, since the game only sends your currencies (conquest points, Bayld, Hallmarks and so on) when those tabs open. The game only shows the skills of the job you are on, so switch to each of your jobs once in your Mog House while charcapture is loaded; the snapshot keeps every job's skills. A skill it never sees is filled in at the cap for your level when you are imported. If you play Puppetmaster, switch to it once (main or sub) while charcapture is loaded, since the game only sends your automaton's name, head, frame and attachments then. The exemplar points of your Mastery job come along on their own. If you have your own chocobo, call it with the **Chocobo Whistle** and ride it for a moment, since the game only sends its colour and look while you ride it. Then:

```
/capture
```

It writes `config\charcapture\<YourName>.json` in the Ashita folder and prints a one-line summary. `/capture show` prints the summary again.

Then open the Vanadreams Launcher, go to Capture, pick the snapshot and press **Send to Vanadreams**. The server side imports it onto your Vanadreams account after a look.

**Wardrobes that are not paid for this month:** the game will not let you open them to use them, but opening the wardrobe from the main menu still shows the gear in them. Do that once for each wardrobe before you capture, and the gear comes across. If a bag reads as locked but its items were captured, the capture summary says so.

## What's in the file

Documented in [the design](../../docs/design/2026-09-13-character-capture-design.md). Item `extra` blocks are kept whole, so augments come with the gear.
