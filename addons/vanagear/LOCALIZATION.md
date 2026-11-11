# VanaGear language catalogs

This local implementation supplies English and Japanese. It has not been released or
installed into a player's Ashita folder. In-game rendering and native-speaker review are
required before release; offline tests cannot establish layout or translation quality.

## Boundaries

`vg/i18n.lua` owns language detection, the saved manual choice, fallback, formatting and
local display adapters. `vg/locales/en.lua` defines English; `vg/locales/ja.lua` combines
the Japanese UI, help and message catalogs. The English source phrase is the stable key.
`vg/locale_font.lua` owns the font lifecycle. The gear engine still uses its original keys,
IDs, resource names and commands. Do not translate the data in saved profiles, macros,
network protocols or server commands.

The local ImGui/style/chat adapters are not global hooks. They leave the shared APIs and
other addons untouched. Translated controls retain the original English widget ID using
ImGui's `###` suffix in both English and Japanese. The first upgrade may reset ImGui-only
window/control state once as the IDs become canonical; subsequent language switches share
the same IDs. Editable values are not translated. Whole English phrases are matched
first; formatted messages can use the same printf template as their translation key.
Keep placeholders in the same order and with the same conversion types. A bounded cache
avoids scanning the templates on every frame and cannot grow indefinitely with player text.

Auto reads the configured Ashita addon/resource language, not xiloader arguments, party
search flags, Windows location or nationality. Japanese is Ashita `1`; other values currently
select English. A valid explicit preference wins. Missing/unknown saved choices use Auto.

## Adding a language

1. Create `vg/locales/es.lua`, `fr.lua`, `zh_hans.lua`, `zh_hant.lua` or `hi.lua` as appropriate:

   ```lua
   return { name = 'Español', strings = {
       ['Settings'] = 'Ajustes',
       ['Built %d sets.'] = 'Se crearon %d conjuntos.',
   } };
   ```

2. Register it alongside the existing registrations in `vg/i18n.lua`:

   ```lua
   locale.Register('es', require('vg.locales.es'));
   ```

   Use case-insensitive language tags (`en`, `ja`, `es`, `fr`, `zh-hans`, `zh-hant`, `hi`).
   Registered catalogs automatically appear in Settings and work with `/vg language <tag>`.
   Empty/missing entries retain the English source phrase. Invalid format placeholders are
   rejected when a catalog is registered. Keep the English recovery choice visible.

3. Translate the complete user-facing phrases and help, including dynamic templates.
   Keep command syntax, file paths, profile fields, job abbreviations and placeholder order.
   Add a focused test for the locale and review wording with a speaker of that language.
   Verify any added automatic language mapping against Ashita's actual API before using it.

4. Verify fonts, rendering, text input and layout for that script before marking it supported.
   Spanish and French require their accented characters. Simplified and Traditional Chinese
   are separate translation targets and need suitable glyph coverage. Hindi (`hi`) uses
   Devanagari: glyph coverage alone does not establish correct shaping, combining marks or
   input. Those need renderer validation and potentially a different text-rendering route.
   Extend the font loader with an appropriately licensed font when required; the current
   Japanese font is not a declaration of support for every script.

5. Keep transport encoding separate. ImGui UI text is UTF-8; FFXI chat is CP932. Currently
   Japanese translations cross the existing UTF-8→CP932 helper. Legacy messages containing
   non-ASCII values of unknown provenance retain their original wording and bytes: an
   already-composed message may contain CP932 game input or UTF-8 resource names, and
   guessing can corrupt it. For a producer with explicitly UTF-8 arguments, use
   `locale.ChatFormat('No set called %s.', utf8Name)`; its output is ready for chat.
   Use `locale.Format` for window text, not as input to the legacy chat adapter.
   All other UI locales use
   English chat until their characters have a verified transport and font path. Never feed
   arbitrary UTF-8 Chinese/Hindi into the game's Shift-JIS chat buffer, and never change
   gameplay/action language solely because the UI language changed.

## Font provenance

`fonts/MPLUS1p-Regular.ttf` is the unmodified M PLUS 1p Regular distributed by Google Fonts:
https://github.com/google/fonts/tree/main/ofl/mplus1p

Copyright 2016 The M+ Project Authors. SIL Open Font License 1.1 is included as `fonts/OFL.txt`.
SHA-256: `2f294ad496432b1608f070d310e3aa2adcf1de4af429f4901df97ec4bd361ed1`.
The loader tries this bundled font first, then existing Windows Japanese fonts. It loads
once in the addon load callback and brackets only VanaGear's windows with PushFont/PopFont.
No font is installed into Windows. Unload/reload and older Ashita font APIs still require
an in-game check before shipping. No shared theme source was modified.

## Local verification

Tests require Python, `lupa` (LuaJIT 2.1), and `fonttools` for the font character-map check.
Keep temporary test output on the approved project drive:

```powershell
$env:TEMP = 'D:\Workshop\Projects\VanaGear\test-evidence\tmp'
$env:TMP = $env:TEMP
$env:VANAGEAR_SERVER_ROOT = 'D:\Workshop\Git\vanadreams'
python addons/vanagear/tests/localization.py
python addons/vanagear/tests/localization_font.py
python addons/vanagear/tests/commands_settings.py
python addons/vanagear/tests/stat_names.py
python addons/vanagear/tests/syntax.py
```

`VANAGEAR_SERVER_ROOT` is read-only input for the stat-name test. No test launches FFXI,
connects to the live server, deploys an addon, or publishes a launcher/catalog change.

Before release, check a Japanese game and an English game with Auto and both manual choices;
check every tab and tooltip, font appearance, command execution, profile/set preservation,
preference reload, long messages and addon reload. Have a Japanese speaker review the draft
translations. Retail dialogue/DAT changes and server localization are separate work.
