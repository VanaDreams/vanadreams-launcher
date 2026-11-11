# VanaGear changes

## 0.2.9 — English and Japanese display support

- Auto follows Ashita's addon language; Settings and `/vg language auto|en|ja`
  provide a saved manual choice. `/vg language` reports the active selection.
- Japanese translations cover the main interface, Settings, command help,
  instructions and common messages. Missing entries retain English.
- A bundled M PLUS 1p Japanese font and its SIL Open Font License are included;
  no Windows font installation is needed.
- UI text stays UTF-8; translated game-chat messages use CP932. Legacy messages
  with non-ASCII bytes of unknown origin keep their wording and bytes intact.
- English and Japanese controls use the same canonical ImGui IDs. This upgrade
  may reset ImGui-only window/control state once; language changes then share it.
- Saved set names, profile keys, item IDs, gameplay commands and client resource
  language are unchanged. Switching VanaGear's display language does not change
  the language of the FFXI client itself.
- Language catalogs support future additions with English fallback and format
  validation. Chinese, Hindi, Spanish and French translations are not included.

This is a local release candidate. In-game rendering, layout and reload behavior
have not yet been verified. Japanese wording remains a draft for speaker review.
