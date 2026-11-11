# Launcher interface languages

The saved `uiLanguage` choice is `auto`, `en`, or `ja`. Auto resolves from Windows `CurrentUICulture`: Japanese uses Japanese; other display languages use English. The choice applies on the next launcher start and never changes thread numeric culture, xiloader `--lang`, registry selection, game files, profiles or addon settings.

`launcher.json` is embedded into the executable. Each entry keeps the original English `en` text and Japanese `ja` text. XAML uses `lang:Text` with an entry key. Runtime launcher-owned text uses `UiText.T` or `UiText.F` with the unchanged English source. Unknown strings retain their English text. Context-specific entries use `context: true` and `UiText.FromKey`, so an ambiguous English label does not change the common lookup. Missing keys and malformed resources are release defects; verify keys before publication.

Translate complete formatted messages where word order or counting differs. Keep format placeholders, file names, commands, URLs, parsing markers and control suffixes intact. Player text and downloaded descriptions/news are data and are not automatically translated. Raw loader output, Windows exception details and technical finding records stay verbatim for support.

Additional languages can reuse the explicit entry keys and English fallback; add their locale code, resolver/selector option and catalog lookup together. Do not use a global UI-tree rewrite or change CurrentCulture to translate presentation.
