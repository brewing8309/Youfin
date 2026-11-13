# Translating Youfin

The web interface and the plugin settings page are translated through plain JSON files. English is the source and the fallback
(a missing key shows the English text). Currently available: English (`en`) and German (`de`).

## Add a language
1. Copy `src/Jellyfin.Plugin.Youfin/Web/app/i18n/en.json` to `<code>.json` (for example `fr.json`, `pt-BR.json`) in the same folder and translate the **values**.
   - Keep the keys and the `{placeholders}` (for example `{n}`, `{name}`) unchanged.
   - Plural texts end in a [CLDR plural category](https://cldr.unicode.org/index/cldr-spec/plural-rules): `_one`, `_other` (and `_zero`, `_two`, `_few`, `_many` where your language needs them). `_other` is required.
2. Add the language to `src/Jellyfin.Plugin.Youfin/Web/app/languages.json`: `{ "code": "fr", "name": "Français" }`. The name is shown in the language selector in the native language.
3. Run `dotnet test`. The tests check that every key used by the interface exists, that your file is complete and that the placeholders match English.
4. Open a pull request.

## How the language is chosen
Saved choice (language selector in the header) → browser language (exact code such as `pt-br`, then the base `pt`) → English.

## Changing or adding texts in the code
Use `tr('section.key')` in `app.js` or `data-i18n="section.key"` in the HTML (`data-i18n-placeholder`, `data-i18n-aria-label` and `data-i18n-title` for attributes),
add the key to `en.json` and to every other language file. The tests fail if a key is missing, unused, or differs between languages.

Not translated (shown as delivered by Jellyfin or the video's metadata): titles, descriptions and channel names, and the name of the scheduled task in the Jellyfin dashboard.
