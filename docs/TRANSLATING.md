# Translating Shotly

Shotly's interface is available in 13 languages: English, Русский, Українська, Deutsch, Français, Español,
Italiano, Português (Brasil), Polski, Türkçe, 简体中文, 日本語 and 한국어.

By default Shotly follows the Windows display language and falls back to English when there is no translation.
The language can also be picked by hand in **Settings → System → Language**; the change applies right away.

All texts live in one JSON file per language:

```
src/Shotly/Localization/Languages/
  en.json   ← reference: every key, in English
  ru.json
  de.json
  …
```

## Fixing a translation

Edit the value in your language's file and open a pull request. CI runs `tools/check_translations.py`, which
catches missing keys and broken placeholders. You can run it yourself: `python tools/check_translations.py`.

## Adding a language

1. Copy `en.json` to `<code>.json`, where `<code>` is a .NET culture name: `nl`, `cs`, `vi`, `pt-BR`, `zh-Hans`…
2. Translate every value. Set `LanguageName` to the language's own name (`Nederlands`, `Čeština`) — this is what the
   language list in Settings shows, so people find their language even if they can't read the current one.
3. Add the code to `Loc.Languages` in `src/Shotly/Localization/Loc.cs`. If the language needs glyphs that the brand
   fonts (Onest, Unbounded) don't have — Chinese, Japanese, Korean, Thai, Hindi… — also pass a system font for it,
   like `new("ja", "Yu Gothic UI, Meiryo UI", "Yu Gothic UI")`: the first is for WPF windows, the second for the tray menu.
4. Run `python tools/check_translations.py`.

Right-to-left languages (Arabic, Hebrew, Persian) need layout work beyond translation and aren't supported yet.

## Rules for values

* `{0}`, `{1}` are placeholders (a version number, a file path, an error message). Keep all of them; you can move them.
* In `HintSelect` and `HintCloseAgain`, words between asterisks (`*Esc*`) are drawn as keys. Keep the same number of
  them and keep the key name itself (`Esc`) as it's printed on keyboards in your language.
* Windows setting names (for example “Use the Print screen key to open screen capture”) should match how they
  appear in Windows in your language, so people can find them.
* Keep texts short: menu items and buttons have little room. Long settings labels wrap to a second line.
* Keyboard shortcuts in tooltips (`Ctrl+Z`, `Shift`) may use the names printed on keyboards in your language
  (`Strg+Z`, `Umschalt`, `Maj`).

## Adding a new text in code

Add the key to `en.json`, then add a property with the same name to `src/Shotly/Localization/L.cs`
(a method with parameters if the text has placeholders) and use `L.YourKey` in code. CI requires every key in every
language file: translate it where you can and copy the English text into the rest — mention those in the pull
request so native speakers can fix them. If a key is ever missing at runtime, Shotly shows the English text.
