# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Basic Software Installer is a Windows desktop application (.NET Framework 4.8, WinForms) for
AS Computertechnik that silently installs a selectable set of common software (browser, PDF reader,
archiver, antivirus, ...) after a fresh Windows install — one click, catalog-driven, similar in spirit
to "Patch My PC" but a small in-house tool with a freely editable catalog instead of a maintained
third-party product. See `README.md` for the user-facing feature list and build instructions; this file
covers things a future session needs that aren't obvious from the code alone.

## Build

```bat
build.bat
```

Runs `dotnet build` (Release) and, if Inno Setup's `ISCC.exe` is found (PATH or the default install
path), also compiles `installer\setup.iss` into a classic `setup.exe`. No Inno Setup installed? The
second step is skipped with a message, not a failure — not needed for development.

No NuGet packages anywhere in this project — deliberately. Everything used (WinForms,
`System.Web.Script.Serialization.JavaScriptSerializer` for all JSON, `HttpClient`,
`System.IO.Compression`, the Windows Credential Manager via raw P/Invoke) ships inside .NET Framework
4.8 itself. Keep it that way unless there's a strong reason not to — it means `dotnet build` never needs
package restore or network access, on a dev machine or in CI.

No test suite or linter is configured.

## Architecture

**Catalog-driven, not hardcoded.** `catalog/apps.json` is the single source of truth for what can be
installed — URL, silent switches, path override, language variants, install options, licensing. Adding
a program means editing JSON (see README "Katalog erweitern"), not touching C#. An optional
`%APPDATA%\BasicInstaller\user_apps.json` (same shape) is merged in by `id` at load time
(`Services/CatalogLoader.cs`) — same `id` overrides the bundled entry, new `id` is appended. This is why
a Windows reinstall of the app itself never destroys a technician's custom catalog entries.

**Models vs. Services split:**
- `Models/AppEntry.cs` — 1:1 deserialization target of a catalog JSON object (`AppOption` for the
  checkbox-option sub-objects, `LanguageVariant` for the `languages` map values).
- `Models/AppRow.cs` — the GUI-facing wrapper around one `AppEntry`: holds the user's current
  selections (selected?, path, language, license, per-option booleans) plus the detected installed
  version. Computes its own sane defaults in the constructor (default language, default option values,
  initial registry-detected version) so `MainForm` doesn't have to.
- `Services/*` — stateless helpers: `CatalogLoader`, `ConfigStore` (persisted selection/paths/
  options — **never** secrets), `ProfileStore` (named `ConfigStore`-shaped snapshots), `Downloader`,
  `SilentInstaller`, `LanguageResolution`, `InstalledAppDetector` (registry scan), `AutoUpdateService`,
  `WindowsCredentialStore` (P/Invoke secrets store), `InstallLog` (persistent log file), `Utils`.

**GUI composition uses an explicit `TableLayoutPanel` with fixed row indices, not stacked
`Dock=Top` controls.** `MainForm.BuildUi()` has a comment explaining why: with several controls sharing
`Dock=Top`, the actual visual stacking order depends on `Controls.Add` order in a way that's easy to get
backwards and that nobody here could visually verify (this was built in a Linux sandbox with no Windows
GUI to render it). An explicit row number removes the ambiguity. If you add a new top-level section to
the main window, add a table row for it — don't reach for another `Dock=Top` control at the root level.

**`DataGridViewButtonColumn` + `UseColumnTextForButtonValue=true` is column-wide and ignores a cell's
empty `Value`.** The "⚙" options button needs to be hidden for apps without `options`/license fields.
Setting `cell.Value = ""` alone does **nothing** visually when the column has
`UseColumnTextForButtonValue=true` — you must also cast to `DataGridViewButtonCell` and set that same
property to `false` **on the cell** to actually override the column default. See
`Grid_DataBindingComplete` for the fix; this was a real bug caught during review, not a hypothetical.

**Two independent licensing mechanisms, not one generic "license key" field** (see `AppEntry` and
`SilentInstaller.Run`):
- `license_type: "cli_arg"` — `license_key_arg_template` (with a `{key}` placeholder) is appended as an
  extra silent switch, e.g. ESET's `--license-key=...`. Empty input = installer runs without the
  switch, product stays unactivated, can be activated later — this is intentional, not a missing
  feature; ESET volume licenses are often entered after the fact.
- `license_type: "key_file"` — after a *successful* install, `license_file_name` (e.g. WinRAR's
  `rarreg.key`) is written with the user-supplied content into the install directory. This models how
  WinRAR volume licensing actually works (a license file dropped into the install folder), which is
  nothing like a CLI switch — don't try to force it through `license_key_arg_template`.
- If the install succeeds but the license file write fails, `SilentInstaller.Run` still reports
  `Success` but with a message explaining the license-file failure — the install itself genuinely
  succeeded and must not be reported as failed, but the license problem must not be silently swallowed
  either (same "fail loud, don't conflate two different outcomes" principle as the sibling IMAP-Mover
  project's append-status rule).

**Per-app install options are generated at install time, never shipped as static files.** An app can
declare `options` (`AppOption[]`) + `options_ini_template` + `options_ini_filename`. Two option kinds:
`type` omitted/`"bool"` is a checkbox (`AppRow.Options`, substituted as `"1"`/`"0"`); `type: "choice"` is
a fixed set of named string values (`AppRow.ChoiceOptions`, substituted literally — e.g. Office 365's
`arch` option becomes `OfficeClientEdition="32"` or `"64"` inside its `configuration.xml`). Both are
merged into one `Dictionary<string,string>` by `AppRow.BuildOptionSubstitutions()` before
`SilentInstaller.WriteOptionsIni` does pure `{key}` → value text substitution and writes the result next
to the downloaded installer, deleting it again in a `finally` after the run. This means the *entire*
generated-file structure (WinRAR's `WinRAR.ini` sections, Office's ODT `configuration.xml`) lives in
`catalog/apps.json`, editable without touching code — important because a vendor can change their config
format and you don't want to rebuild/redeploy the app just to fix that. Known limitation (see the
`office365` entry's `notes`): a `choice`/`bool` value can only replace text *inside* a line — it cannot
make a whole XML element conditionally appear or disappear, so per-component install toggles (e.g.
"exclude Access") aren't expressible yet; that would need a real template-engine upgrade, not attempted
here. The WinRAR catalog entry's `options_ini_template` section/key names are explicitly flagged in its
`notes` as an unverified placeholder after a real search found no authoritative source for them — the
*mechanism* (drop a file next to the installer, then run `/S`) is confirmed correct, the exact keys are
not. Check before relying on it in production, and update the `notes` once verified either way.

**`InstalledAppDetector` scans both `HKLM` (native + `WOW6432Node`) and `HKCU`.** Several of these
vendor installers (Firefox, Brave, sometimes VLC) can install either per-machine or per-user depending
on how they're invoked — scanning only `HKLM` would silently miss a per-user install and report "nicht
installiert" even though it's there. Matching is by `DisplayName` substring (`detect_display_name_contains`
in the catalog), not exact match, because real `DisplayName` values carry version numbers / locale
suffixes.

**Secrets never touch `config.json` or `profiles.json`.** `AppUserSettings` (the type both files
serialize) has no field for license keys or license-file content — not an oversight, a deliberate
omission so there's no field to accidentally populate. The optional persistence path for a license is
`WindowsCredentialStore` (raw `advapi32.dll` `Cred*` P/Invoke, keyed `WindowsCredentialStore.KeyForApp(id)`
= `"BasicInstaller_license_" + id`), which is a per-machine OS secret store, never a file that could end
up in this (public) repo. This mirrors the sibling IMAP-Mover project's rule of keeping passwords in the
Windows Credential Manager and out of the INI config — same reasoning, different API (P/Invoke here
instead of the Python `keyring` package, since this project has a zero-NuGet constraint and .NET
Framework has no in-box high-level wrapper for it).

**Auto-update requires the repo to be public, and that's a conscious trade-off, not an oversight.**
`AutoUpdateService.CheckForUpdateAsync` hits the unauthenticated GitHub Releases API
(`api.github.com/repos/<owner>/<repo>/releases/latest`), which 404s on a private repo. No token is
embedded in the client on purpose — embedding one would mean shipping a credential inside a distributed
exe. If this repo is ever made private again, auto-update silently stops finding updates (returns
`null`, logged nowhere alarming) rather than erroring — that's intentional graceful degradation, not a
bug to "fix" by adding a token.

**Update mechanism: stage-then-relaunch via a generated `.bat`, not an in-place file swap.** A running
.NET exe holds its own file locked and cannot overwrite itself. `AutoUpdateService.
StageUpdateAndLaunchInstallerAsync` downloads the release ZIP, extracts it to a temp staging dir, writes
a batch script that `timeout`s briefly (so the calling process has actually exited by the time
`robocopy` runs), copies the staged files over the install directory, relaunches the exe, and
self-deletes. The caller (`MainForm`) **must** call `Application.Exit()` immediately after awaiting this
— the method itself doesn't exit the process, by design (keeps `Services/*` free of a `System.Windows.
Forms` dependency).

**Version comparison avoids `System.Version` on purpose.** `AutoUpdateService.IsNewer` does manual
3-part integer comparison instead of `Version.Parse`/`CompareTo`, because `System.Version`'s `Revision`
field distinguishes "not specified" (`-1`) from "explicitly 0" — comparing a parsed tag like `"1.0.0"`
(`Revision=-1`) against an assembly version like `"1.0.0.0"` (`Revision=0`) through `CompareTo` produces
inconsistent results at that boundary. See the method's comment if tempted to "simplify" this back to
`System.Version`.

**`IMAP_TIMEOUT`-equivalent for this project: `SilentInstaller.InstallTimeout` (10 min) and
`Downloader.DownloadTimeout` (15 min).** A hung installer (unexpected reboot prompt that `/S` never
shows, a "already installed" dialog nobody can click) or a dead download connection must not block the
whole queue forever — same failure class as the IMAP-Mover sibling project's mandatory IMAP timeout, same
fix (always use a timeout, kill and report rather than hang).

**`IsRunningElevated()` is defense-in-depth, not the primary admin mechanism.** The primary mechanism is
`app.manifest`'s `requestedExecutionLevel=requireAdministrator`, which triggers the UAC prompt before
`Main()` even runs. `Utils.IsRunningElevated()` + the red banner in `MainForm` exist for the scenario
where the manifest is later removed/bypassed — fail loud (visible warning) instead of silently failing
installs into a directory the process can't write to.

## Real bugs found on first live Windows test (don't reintroduce these)

Everything up to this point had only been reviewed by reading the code — this project was built in a
Linux sandbox with no Windows GUI to run it on. The first real test on actual Windows surfaced several
concrete bugs, all now fixed:

- **Six of the thirteen catalog entries (`brave`, `firefox`, `vlc`, `irfanview`, `acrobat_reader`,
  `eset_nod32`) had no `detect_display_name_contains` at all** — they were written before the
  installed-version-detection feature existed, and nobody went back to add it. `InstalledAppDetector.
  FindInstalledVersion(null)` returns `null` immediately, so these always showed "nicht installiert"
  regardless of reality. This was most of what "viele installierte Programme werden gar nicht erkannt"
  turned out to be. **When adding a new catalog entry, `detect_display_name_contains` is not optional
  polish — forgetting it silently breaks detection for that app with no error anywhere.**
- **The per-row language `DataGridViewComboBoxColumn` didn't work in practice** (dropdown appeared but
  selecting a value didn't stick) — binding a combo column via `DataPropertyName` while also manually
  populating each cell's `Items` in `DataBindingComplete` is a known-fragile WinForms combination. Fixed
  by removing the per-row language column entirely — language is now a single global `ComboBox`
  (`_cmbGlobalLanguage`, "Deutsch"/"Englisch") that calls `ApplyGlobalLanguage()` to push the best-
  matching language onto every `LanguageSelectable` row's `AppRow.SelectedLanguage`. This was also an
  explicit user request independent of the bug ("Sprache würde ich global einstellen, nicht pro App") —
  don't resurrect a per-row language combo column even to "fix" the binding properly; the global control
  is the wanted design now, not a workaround.
- **`AutoUpdateService.CheckForUpdateAsync` returned bare `null` on every possible failure** (no
  network, GitHub rate limit at 60 unauthenticated requests/hour, malformed JSON, missing `.zip` asset —
  all indistinguishable). In practice this showed as "GitHub nicht erreichbar" even when the real cause
  was rate-limiting from repeated manual testing. Fixed via `AutoUpdateService.LastCheckError` (set at
  every failure branch with the specific reason, including detecting `X-RateLimit-Remaining: 0`), which
  `MainForm` now includes in the message box. If you touch this method again, keep setting
  `LastCheckError` at every early-return — a bare `null` here is exactly the kind of unhelpful catch-all
  this project's "fail loud, be specific" principle (see `IMAP_TIMEOUT`/append-status comments) argues
  against.
- **The self-update button's label ("Nach App-Updates suchen") was read as "check for updates to the
  catalog apps," not "check for a new Basic Installer version."** Renamed to "Basic Installer selbst
  aktualisieren" — keep the wording unambiguous if you touch it again; this project has two completely
  different "update" concepts (self-update via GitHub Releases vs. re-running a catalog app's installer)
  and they look confusingly similar in a short button label.
- **The `LicenseKey` grid column and the gear ("⚙") button's visibility were a UX mismatch the user
  rejected on sight**: a dedicated grid column for license key/file, with the *full* editing experience
  (including the file-load button and the "remember in vault" checkbox) actually living in
  `OptionsDialog`. Removed the grid column entirely — license key/file is now **only** reachable through
  the ⚙ popup, consistent with how installation options already worked. This also exposed a real,
  separate bug: the ⚙ button's visibility was gated on `row.HasOptions` alone, so a license-only app like
  ESET (which has no `options` array, only `license_type`) never showed a ⚙ at all and its license was
  unreachable. The gate is now `row.HasOptions || row.LicenseKeyEditable` everywhere it's checked
  (`Grid_DataBindingComplete`'s hide-the-button block, and `Grid_CellClick`'s guard) — if you add a third
  per-app "needs its own popup" mechanism later, remember both places check this combined condition, not
  just `HasOptions`.
- **"Standard" profile auto-load** was a feature request that arrived alongside the bug reports, not a
  bug itself: if a profile named `"Standard"` (case-insensitive) exists in `profiles.json`, it's applied
  automatically at startup, *after* the normal `config.json` restore and before the window is shown — see
  the end of `LoadCatalogAndConfig`. A profile's own `global_language` (if set) wins over whatever
  `config.json` had.

**Takeaway for future sessions:** this project's build pipeline (GitHub Actions) only proves the code
*compiles* — it has never proven the GUI actually works, because nobody here can render a WinForms
window. Treat any claim about dropdown/data-binding/layout behavior as unverified until a human
confirms it on real Windows, and prefer the simplest, most well-established WinForms pattern over a
clever one when you can't test it yourself.

## Round two of real bugs (same lesson, different control)

- **The ⚙ column was invisible** because its `AutoSizeMode` was `ColumnHeader` with an empty
  `HeaderText` — that mode sizes the column to fit the header text, so an empty header collapsed it to
  near-zero width and silently overrode the `Width = 40` set right next to it. `AutoSizeMode.None` is
  what actually respects an explicit `Width`; `ColumnHeader`/`Fill`/etc. all override it. If a column
  ever looks explicitly sized but doesn't show up right, check `AutoSizeMode` before suspecting anything
  else about that column.
- **The "skip already-installed" checkbox was removed entirely, not just unchecked by default** — user
  feedback was that toggling it made no real sense without also knowing whether the installed version is
  *outdated* (which this project doesn't detect — see `InstalledAppDetector`, it only reports presence +
  version string, never compares against a "latest" value). Skipping an already-detected install is now
  unconditional in `BtnInstall_Click`. Don't resurrect this checkbox without also building real
  version-comparison first — otherwise it's back to being a toggle with no good reason to ever use it.
- **`Theme.cs`** holds the one accent color (`#FF914D`, extracted from `assets/as-ct-logo.png`'s only
  non-white pixels) used sparingly: the Installieren button, the DataGridView column headers (needs
  `EnableHeadersVisualStyles = false` or the OS header style silently wins over
  `ColumnHeadersDefaultCellStyle`), row selection tint, and link colors. Deliberately not applied to
  every control — a small UI like this reads as "designed" with one accent used consistently, not
  "painted" with color everywhere.
- **The self-update version status moved from a one-shot `MessageBox` to a persistent label**
  (`_lblVersionStatus`, in the row that used to hold the skip checkbox) because showing "installiert vs.
  verfügbar" only inside a dialog that appears once (and only if you click the button, or at startup if
  an update happens to exist) meant the two version numbers were invisible the rest of the time. It now
  always shows at least the installed version, and both + a warning glyph when an update is sitting
  there unacted-on.

## Configuration / Data Files

All under `%APPDATA%\BasicInstaller\`:
- `config.json` — last-used selection/paths/languages/options (auto-saved on close, no secrets).
- `profiles.json` — named snapshots of the same shape, saved explicitly via "💾 Speichern als…".
- `user_apps.json` — optional user-authored catalog additions/overrides, same shape as
  `catalog/apps.json`.
- `logs\install_<timestamp>.log` — one file per app run, persistent copy of the on-screen log.

Download cache: `%LOCALAPPDATA%\BasicInstaller\cache\` (`Downloader.CacheDirectory`).

Secrets: Windows Credential Manager, target name `BasicInstaller_license_<app id>` — never a file.

## Code signing

`.github/workflows/release.yml` has two `azure/artifact-signing-action@v1` steps (exe before
zipping/Inno Setup, `Setup.exe` after Inno Setup builds it), both gated on
`if: ${{ secrets.AZURE_TENANT_ID != '' }}` so the pipeline keeps working unsigned until those secrets
exist — don't remove the gate "to simplify," that's what keeps an unconfigured fork/clone building. Full
setup steps (Azure side + which six GitHub secrets) are in README.md "Code-Signing"; don't duplicate
them here, keep this pointer short. One thing worth knowing even without touching Azure: this project
deliberately did **not** reach for a classic exportable `.pfx` — as of June 2023 the CA/Browser Forum
requires hardware-token/HSM-backed private keys for *all* newly issued code-signing certs (OV included,
not just EV), so a droppable `.pfx` + password GitHub secret isn't realistically obtainable from a
reputable CA anymore. Azure Artifact Signing (no physical token, ~$10/month, scriptable from CI) was
picked specifically because it fits that constraint, not as a workaround for a cheaper option that
would otherwise be preferred.

## Branding

Icon (`assets/app.ico`, multi-resolution) and the "ℹ Über / Kontakt" popup's contact details were pulled
from `https://as-ct.de` and its Impressum page. The AI-summarized fetch of that page initially rendered
the email as a literal "[email&nbsp;protected]"-style placeholder — a JS-cloaking artifact, not a real
value — and it was deliberately **not** guessed/fabricated at that point. The real address
(`info@as-ct.de`) was later recovered legitimately by downloading the Impressum page's raw HTML and
decoding its Cloudflare `data-cfemail` hex attribute — a standard, intentionally reversible
anti-scraping scheme (XOR each byte pair against the first byte as key), not a security measure to work
around. If this page's contact details ever need re-pulling, prefer fetching raw HTML and checking for
`data-cfemail` over trusting an AI-summarized fetch's rendering of the email field.
