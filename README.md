# Basic Software Installer

Ein Windows-Desktop-Tool (.NET Framework 4.8, WinForms) für AS Computertechnik, das nach einer
Windows-Neuinstallation die gängige Grundausstattung an Software (Browser, PDF-Reader, Archiv-Tool,
Virenschutz, ...) per Klick silent installiert – ähnlich dem Grundprinzip von „Patch My PC", aber als
eigenes, schlankes Tool mit frei editierbarem Katalog statt eines gepflegten Fremd-Produkts.

## Features

- **Ein Katalog, viele Programme** – `catalog/apps.json` beschreibt deklarativ, was installierbar ist:
  Download-URL, Silent-Switches, optionaler Pfad-Override, optionale Sprachvarianten, optionale
  Installationsoptionen (Checkboxen) und optionale Lizenzierung. Neue Programme ergänzen heißt: JSON
  bearbeiten, kein Code ändern (siehe [„Katalog erweitern"](#katalog-erweitern) unten).
- **Versionserkennung** – vor jeder Aktion wird geprüft, was laut Windows-Uninstall-Registry bereits
  installiert ist. Per Default werden bereits installierte Programme beim Klick auf „Installieren"
  übersprungen (Häkchen „Bereits installierte Programme überspringen" abwählen, um trotzdem zu
  aktualisieren/neu zu installieren – die Hersteller-Silent-Installer sind dafür ausgelegt, über eine
  bestehende Installation zu laufen).
- **Profile** – die aktuelle Auswahl (welche Apps, welcher Pfad, welche Sprache, welche Optionen) lässt
  sich unter einem Namen speichern und später mit einem Klick wieder laden (z. B. „Kundenstandard" vs.
  „Technikerstandard"). Liegt unter `%APPDATA%\BasicInstaller\profiles.json`.
- **Nur herunterladen** – lädt die gewählten Installer in den lokalen Cache, ohne zu installieren. Für
  Technikereinsätze mit unsicherem Internet beim Kunden: vorher auf schnellem Netz vorladen, Installation
  läuft dann auch offline.
- **Auto-Update der App selbst** – prüft gegen die GitHub-Releases-API dieses Repos auf eine neuere
  Version und installiert sie bei Bestätigung selbst (Details unten).
- **Persistentes Protokoll** – jeder Lauf schreibt zusätzlich zur Textbox im Fenster eine Datei unter
  `%APPDATA%\BasicInstaller\logs\` (Audit-Spur: was wurde wann auf diesem Rechner installiert).
- **Admin-Pflicht** – die exe fordert per Manifest automatisch Administratorrechte an (UAC-Prompt beim
  Start), weil Installationen in `%ProgramFiles%` das ohnehin erfordern.

## Build

Voraussetzungen: .NET SDK mit installiertem **.NET Framework 4.8 Developer/Targeting Pack** (auf
GitHub-Actions-Runnern `windows-latest` bereits vorhanden). Optional für einen klassischen Setup.exe:
[Inno Setup 6](https://jrsoftware.org/isinfo.php).

```bat
build.bat
```

Baut die Release-exe (`src\BasicInstaller\bin\Release\net48\BasicInstaller.exe`) und, falls Inno Setup
gefunden wird, zusätzlich `installer\Output\BasicInstaller-Setup.exe`. Ohne Inno Setup wird dieser
zweite Schritt übersprungen (Hinweis statt Fehler) – für reine Entwicklung nicht nötig.

Reine Entwicklung/Debuggen direkt über Visual Studio (`BasicInstaller.sln`) oder:

```bat
dotnet build src\BasicInstaller\BasicInstaller.csproj -c Debug
```

**Keine NuGet-Pakete.** Alles verwendete (WinForms, `JavaScriptSerializer`, `HttpClient`,
`System.IO.Compression`, Windows-Registry-Zugriff) ist Teil von .NET Framework 4.8 selbst – ein
`dotnet build` braucht deshalb nie einen Package-Restore-Schritt oder Netzzugriff.

## Release & Auto-Update

Ein Push eines Tags `vX.Y.Z` löst `.github/workflows/release.yml` aus: baut die Release-exe, baut (falls
Inno Setup auf dem Runner vorhanden ist – bei `windows-latest` der Fall) das Setup, und veröffentlicht
beides als GitHub Release.

`Services/AutoUpdateService.cs` prüft beim Programmstart und per Button gegen
`api.github.com/repos/<owner>/<repo>/releases/latest`, vergleicht die Version und bietet bei einer
neueren Version an, das ZIP-Asset herunterzuladen und selbst einzuspielen (Batch-Skript ersetzt die
Dateien, nachdem sich die laufende exe beendet hat – eine exe kann sich nicht selbst überschreiben,
solange sie läuft).

**Das setzt ein öffentliches Repo voraus** – die unauthentifizierte Releases-API liefert bei privaten
Repos `404`. Es wird bewusst kein Zugriffstoken im Client eingebettet; ein privates Repo heißt also
schlicht: kein Auto-Update, keine halbe Lösung mit eingebettetem Secret.

## Katalog erweitern

`catalog/apps.json` ist eine Liste von Objekten mit diesen Feldern:

| Feld | Bedeutung |
|---|---|
| `id` | eindeutiger Schlüssel, u. a. für Cache-Dateinamen und Registry-Erkennung |
| `name`, `category`, `homepage` | Anzeige in der Tabelle |
| `installer_url` | Download-Link; `null` wenn keine stabile URL existiert (siehe `notes`) |
| `silent_args` | Array von Kommandozeilen-Switches für den Silent-Install |
| `default_install_path`, `path_arg_template` | Pfad-Override; `{path}` wird ersetzt. `null` = vom Installer nicht unterstützt |
| `license_type` | `"cli_arg"` (Schlüssel als zusätzlicher Switch) \| `"key_file"` (Lizenzdatei wird nach der Installation ins Zielverzeichnis geschrieben) \| `null` |
| `license_key_arg_template` | nur bei `cli_arg`, z. B. `"--license-key={key}"` |
| `license_file_name` | nur bei `key_file`, z. B. `"rarreg.key"` |
| `options` | Array `{key, label, default_value}` – Checkboxen für Installationsoptionen, optional |
| `options_ini_template`, `options_ini_filename` | wenn gesetzt: INI-Inhalt (mit `{key}`-Platzhaltern, werden durch `1`/`0` ersetzt) wird vor dem Install neben den Installer gelegt |
| `detect_display_name_contains` | Teilstring, der gegen `DisplayName` in der Windows-Uninstall-Registry gematcht wird, um die installierte Version zu finden |
| `default_language`, `languages` | optionale Sprachvarianten: `languages` ist ein Dictionary `{sprachcode: {installer_url, extra_args}}`, das `installer_url`/`silent_args` der App überschreibt bzw. ergänzt |
| `notes` | **Pflichtfeld für Unsicherheiten.** Wenn eine URL/ein Switch/ein INI-Format nicht zu 100 % sicher ist: hier ehrlich vermerken, was vor Produktiveinsatz zu prüfen ist, statt einen selbstbewusst klingenden, aber möglicherweise falschen Wert einzutragen. Siehe bestehende Einträge (z. B. IrfanView, Acrobat Reader, WinRAR) als Vorbild. |

Ein optionaler Benutzerkatalog unter `%APPDATA%\BasicInstaller\user_apps.json` (gleiches Format) wird
beim Start zusätzlich geladen und über `id` mit dem mitgelieferten Katalog gemischt (gleiche `id`
überschreibt, neue `id` wird ergänzt) – eigene Einträge überleben so ein Update der App.

## Sicherheit / Secrets

Lizenzschlüssel und Lizenzdateiinhalte werden **nie** in `config.json`, `profiles.json` oder sonst einer
Datei gespeichert, die versehentlich geteilt/committet werden könnte. Optional lassen sie sich über den
Windows Credential Manager merken (`Services/WindowsCredentialStore.cs`, reines P/Invoke ohne NuGet) –
pro Rechner, nie im Repo. Das Repo ist öffentlich (für Auto-Update nötig) – es landen dort ausschließlich
Code und Katalog-Metadaten, niemals Zugangsdaten.

## UX-Prinzip

Die Haupttabelle zeigt nur, was zur Auswahl nötig ist (Checkbox, erkannte Version, Sprache, Pfad,
Lizenzfeld). Alles Zusätzliche – Installationsoptionen, Kontaktdaten/Impressum – sitzt hinter kleinen
Buttons/Popups (⚙-Spalte, „ℹ Über / Kontakt"-Link), damit das Hauptfenster aufgeräumt bleibt.

## Lizenz

Quellcode öffentlich einsehbar auf GitHub (für Auto-Update erforderlich), aber nicht unter einer
Open-Source-Lizenz freigegeben – alle Rechte bei AS Computertechnik, siehe `LICENSE`.
