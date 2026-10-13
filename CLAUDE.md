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

**An `AppOption` can also carry `cli_arg_template`, independent of the file-based mechanism above.**
Same `{key}` substitution, same merged `Dictionary<string,string>` from `BuildOptionSubstitutions()`, but
`SilentInstaller.BuildArguments` appends the result directly as a command-line switch instead of writing
it into a generated file — e.g. ESET's PUA-detection checkbox becomes `--enable-pua={pua}` → `--enable-pua=1`.
Use this when the vendor's installer takes the setting as a plain CLI flag (verified, documented switch);
use `options_ini_template`/`options_ini_filename` when it needs a companion config file instead (WinRAR,
Office 365). Both can be set on the same `options` array if an app genuinely needs both. **Don't add an
option with neither set "just to show a checkbox"** — ESET's LiveGrid toggle was deliberately left out of
`options` entirely (not added with an empty/guessed `cli_arg_template`) because no verified mechanism for
it exists yet; a checkbox with no real effect is worse than no checkbox, see its `notes` for what's still
open there.

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
  *outdated*. At the time `InstalledAppDetector` only reported presence + version string, nothing to
  compare against — skip was unconditional. **This has since changed**: `LatestVersionChecker` +
  `AppRow.HasKnownUpdate` (true only when both `InstalledVersion` and `LatestVersion` are known *and*
  differ) now exist, so `BtnInstall_Click`'s skip condition is `InstalledVersion != "–" &&
  !HasKnownUpdate` — skip only when genuinely up to date or never checked, proceed (reinstall/update)
  when an update is positively known. Still no manual checkbox to re-add — the behavior is now correct
  by itself rather than needing a user toggle. Rows with `HasKnownUpdate` also auto-select themselves
  after a version check (`RefreshAllInstalledVersionsAsync`, both the startup one and manual "Status
  aktualisieren") — only ever ticks the checkbox on, never unticks a row the user selected for another
  reason, so a deliberate manual selection (e.g. preparing a "Nur herunterladen" batch) is never
  silently discarded.
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

## Round three: more real bugs, and one near-miss worth remembering

- **`OptionsDialog`/`AboutDialog`/`PromptDialog` opened as tiny, "distorted" unreadable windows.**
  Root cause: `Form.AutoSize = true` is unreliable specifically when the Form has **multiple
  independently `Dock`-ed top-level children** (here: a content panel `Dock=Top` plus a button row
  `Dock=Bottom`, added as two separate items to `Controls`) — it doesn't reliably account for both when
  computing its own preferred size. **First fix attempt was wrong and got corrected by the user**: just
  replacing `AutoSize` with a fixed `ClientSize` "solved" the distortion but reintroduced a worse,
  genuinely-reported-before problem — fixed pixel sizes break for anyone running Windows at a larger
  text-scaling/font-size setting (common accessibility setting, not an edge case). **The actual correct
  fix**: keep `AutoSize = true` / `AutoSizeMode.GrowAndShrink` on the Form, but wrap the content panel
  and the button row together in **one** outer `TableLayoutPanel` (two `AutoSize` rows) and add *that
  single panel* as the Form's only direct child. With one child to measure, `Form.AutoSize` works
  correctly. Keep `MinimumSize`/`MaximumSize` + `AutoScroll = true` + `FormBorderStyle.Sizable` as a
  safety net for extreme cases (huge font scale + lots of dynamic content), not as the primary sizing
  mechanism. **If you touch any dialog in this project again: don't reach for a fixed `ClientSize` as
  the fix for a sizing bug** — the one-outer-container pattern is the one to use instead.
- **`InstalledAppDetector` used `Registry.LocalMachine`/`Registry.CurrentUser`, which only see the
  registry view matching the *calling process's own bitness*.** A 32-bit process reading
  `HKLM\SOFTWARE\...\Uninstall` gets transparently (and silently — no error) redirected by Windows to
  `HKLM\SOFTWARE\WOW6432Node\...\Uninstall` and can never see the native 64-bit view, and vice versa for
  a 64-bit process reading the WOW6432Node path. This meant a genuinely-installed 64-bit app (e.g.
  64-bit 7-Zip, the normal case today) could be invisible depending on which bitness the exe happened to
  build/run as — a real, reported bug ("7-Zip hat er nicht erkannt, obwohl installiert"), not a
  configuration gap like the earlier missing-`detect_display_name_contains` bug. Fixed by using
  `RegistryKey.OpenBaseKey(hive, view)` with both `RegistryView.Registry64` and `RegistryView.Registry32`
  explicitly for both `LocalMachine` and `CurrentUser` — four scans instead of relying on ambient
  process bitness. Don't go back to the static `Registry.*` properties here.
- **Two catalog `installer_url`s (`vlc`, `brave`) were simply dead** — both vendors had changed their
  stable-link scheme since those entries were written, confirmed live via `curl` (VLC: 404 "not
  available on our mirrors", the generic non-versioned filename is gone, only versioned filenames like
  `vlc-3.0.24-win64.exe` exist now, same situation as IrfanView/WinRAR/7-Zip/Notepad++, `installer_url`
  set back to `null` with notes; Brave: S3 404 "NoSuchKey", replaced with the still-live
  `brave-browser-downloads.s3.brave.com/latest/brave_installer-x64.exe`, which is a different installer
  variant than before and needed explicit `--silent --install --system-level` switches since it's not
  the dedicated "StandaloneSilentSetup" build anymore). **Lesson: a "verified live" URL note in `notes`
  has a shelf life — vendors change these without notice. If an install starts failing for an app whose
  `notes` say a URL was checked on some past date, re-check it live before assuming the code is at
  fault.**
- **The embedded assembly version never actually matched the git tag.** `BasicInstaller.csproj`'s
  `<Version>` stayed hardcoded at `0.1.0` through the `v0.1.1`/`v0.1.2`/`v0.1.3` tags — only the git tag
  was bumped each time, not the csproj. This made the footer *and* `AutoUpdateService.IsNewer`'s
  comparison silently wrong (comparing the real GitHub tag against an embedded version that was always
  "0.1.0", which could make the self-update check think an update was always available, even right after
  installing the latest one). Fixed at the CI level, not by remembering to hand-edit the csproj every
  time: `release.yml` now extracts the version from the git tag (`refs/tags/vX.Y.Z`) and passes
  `-p:Version=`/`-p:AssemblyVersion=`/`-p:FileVersion=` to `dotnet build`, so the embedded version is
  always derived from the tag that triggered the build. Same pattern for `-p:Copyright`, built from the
  runner's current year (`date +%Y`) instead of a hardcoded year in the csproj — the in-window footer
  already used `DateTime.Now.Year` and was never affected, this was specifically about the exe's static
  file-properties metadata. **Don't add a manual "remember to bump the version in the csproj" step back
  in — keep deriving it from the tag in CI.**

## "portable" apps: destination folder reuses the normal path column, not a new field

`installer_type: "portable"` apps (O&O ShutUp10++, TeamViewer QuickSupport) don't install anything —
`SilentInstaller.RunPortable` just copies the downloaded file to a destination folder and optionally
(`AppEntry.shortcut_name` set) creates a desktop `.lnk` pointing at it. The destination folder
deliberately reuses `AppRow.InstallPath`/`AppEntry.default_install_path` — the same field and the same
"Installationspfad" grid column every other app uses for its install directory — rather than inventing a
separate field, specifically so a technician can change where a portable app lands directly in the main
table, no separate settings UI needed. `AppRow.PathEditable` is `true` for any `portable` app even
without `path_arg_template` (which is an install-time-CLI-arg concept that doesn't apply here) — don't
gate that column on `path_arg_template` alone or portable apps lose their only way to redirect the
target. Resolution order in `RunPortable`: explicit `InstallPath` override → `default_install_path` →
`KnownFolders.GetDownloadsFolder()` as the final fallback (this is why ShutUp10, which sets neither, still
lands in Downloads — same behavior as before this mechanism generalized). `KnownFolders.cs` wraps
`SHGetKnownFolderPath` via P/Invoke rather than hardcoding `%USERPROFILE%\Downloads`, because
`Environment.SpecialFolder` has no Downloads value at all (it predates Vista's Known Folder system) and a
hardcoded guess breaks for anyone who relocated their Downloads folder.

`ShortcutCreator.cs` creates the `.lnk` via late-bound COM (`Type.GetTypeFromProgID("WScript.Shell")` +
`dynamic`), not a `IWshRuntimeLibrary` COM reference — a fixed interop reference would need that type
library registered on whatever machine builds the project (the GitHub Actions Windows runner, in this
case), while late binding only needs the `Microsoft.CSharp` in-box assembly (referenced in the csproj for
exactly this). Keep it late-bound if you touch this file.

**	eamviewer_qs_asct uses TeamViewer's custom-module download, not the generic one.** s-ct.de/tv -> get.teamviewer.com/ascomputertechnik (ConfigId `63943id`) -> https://customdesignservice.teamviewer.com/download/windows/v15/63943id/TeamViewerQS.exe, which works directly without a signature (the exe embeds the ConfigId; verified different from the generic installer). Earlier sessions wrongly concluded the link was unobtainable: the campaign page serves its JS only to a Windows user agent, and POST /api/CustomDesign needs a browser UA plus the session cookie from the page request. Full recipe in the catalog entry's `notes`.

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

**`assets/app.ico` is a generic download-arrow glyph in the brand accent color, not a crop of the
company logo.** First attempt used `assets/as-ct-logo.png` (the actual as-ct.de wordmark) padded into a
square — looked unreadable at 16/32px (reported directly: "Das Icon sieht Kacke aus"). The logo itself is
why: it's a thin vertical orange bar plus small, fine-detail text, designed as a wide website header
image, not as an icon source — there's no "mark" in it dense/bold enough to survive downsampling to
taskbar size. No better square mark was available, so `app.ico` was redrawn from scratch instead (PIL,
see git history for the generation script): a rounded square filled with `Theme.Accent`, a bold white
download-arrow-into-tray glyph — simple geometric shapes read fine even at 16px, unlike thin line art.
`assets/as-ct-logo.png` itself is unused in the running app (no raw logo image is displayed anywhere,
only `app.ico`) — kept in the repo only as the original source material.

Icon and the "ℹ Über / Kontakt" popup's contact details were pulled from `https://as-ct.de` and its
Impressum page. The AI-summarized fetch of that page initially rendered
the email as a literal "[email&nbsp;protected]"-style placeholder — a JS-cloaking artifact, not a real
value — and it was deliberately **not** guessed/fabricated at that point. The real address
(`info@as-ct.de`) was later recovered legitimately by downloading the Impressum page's raw HTML and
decoding its Cloudflare `data-cfemail` hex attribute — a standard, intentionally reversible
anti-scraping scheme (XOR each byte pair against the first byte as key), not a security measure to work
around. If this page's contact details ever need re-pulling, prefer fetching raw HTML and checking for
`data-cfemail` over trusting an AI-summarized fetch's rendering of the email field.

## Round four: live-test findings (13.10.2026)

Kompiliert wird ausschliesslich auf GitHub Actions (`release.yml`, auch per `workflow_dispatch` auf jedem
Branch). Test-Ablauf: Branch pushen -> `gh workflow run release.yml --ref <branch>` -> `gh run download`
-> ZIP entpacken -> mit `__COMPAT_LAYER=RunAsInvoker` starten (umgeht den UAC-Prompt, damit sich die App
ohne Admin-Klick testen laesst; System-Installationen schlagen dann natuerlich fehl) -> per
UI-Automation (`System.Windows.Automation`, ValuePattern liefert Zellwerte) und Screenshot pruefen.

- **`JavaScriptSerializer.DeserializeObject` liefert JSON-Arrays als `object[]`, NICHT `ArrayList`.**
  `AutoUpdateService` pruefte `assets is ArrayList` -> immer false -> "Release hat kein .zip-Asset", das
  Selbst-Update hat nie funktioniert. Jetzt `IEnumerable`. Der alte Kommentar behauptete das Gegenteil.
- **`Cell.Value = ...` an gebundenen Spalten invalidiert die Zelle nicht zuverlaessig**: die Spalte
  "Verfuegbar" zeigte "-", obwohl der Wert (per UIA verifiziert) "157.0.1" war. Nach dem Setzen
  `_grid.Invalidate()` aufrufen.
- **Leere Zahnrad-Zellen zeigten trotz `UseColumnTextForButtonValue=false` einen leeren Button-Rahmen**;
  `Grid_CellPainting` zeichnet fuer Zeilen ohne Optionen/Lizenz nur den Hintergrund.
- **`CheckBox` mit `AutoSize` + `MaximumSize` bricht Text nicht um, sondern schneidet ihn ab** - langen
  Text in ein separates `Label` mit `MaximumSize` auslagern.
- **Branch-Namen mit `/`** (z.B. `fix/...`) brachen ZIP-/Artifact-Namen im Workflow (`SAFE_REF`).

## Round five: end-to-end install tests on real Windows (13.10.2026)

Test harness: with an elevated shell the GitHub artifact can be started without `RunAsInvoker`; real
silent installs/uninstalls of small apps (7-Zip, WinRAR, Notepad++, IrfanView, VLC) were run through the
app and removed again with their registry `UninstallString` (`/S`, WinRAR `/s`, IrfanView `/silent`).
If the RDP session is locked (`LogonUI` running), UI automation and screenshots fail ("Das Handle ist
ungueltig") - then call the app's own code headless via reflection (`CatalogLoader.Load`,
`LanguageResolution.ResolveInstallerUrlAsync`, `Downloader.DownloadAsync`, `SilentInstaller.Run`).

- **Version variables in the catalog** (`version_url`, `version_regex`, `fallback_version`, placeholders
  `{version}` / `{version_nodots}` in `installer_url`) - see README. The regex is matched against the final
  URL after redirects first (`github.com/<o>/<r>/releases/latest` -> `.../tag/26.04`, no API rate limit),
  then against the body; highest match wins. `VersionResolver` caches per app id; "Status aktualisieren"
  clears the cache.
- **`AppRow.SameVersion`/`IsNewer`**: sources format versions differently (WinRAR registry `7.23.0` vs page
  `723`, 7-Zip `26.04` vs `2604`, LibreOffice `26.8.1.2` vs `26.8.1`). Compare with trailing-`.0`
  trimming, dot-less equality and part-prefix equality, and only call it an update when the available
  version is numerically newer (Acrobat was newer than Adobe's API version).
- **Some installers return before writing their Uninstall registry key** (WinRAR) - the scan right after
  the installer showed "-". The install run now rescans after 2.5 s.
- **Exit code 0 is not proof of success**: the old Adobe online stub hit HTTP 403 on `get3.adobe.com`,
  showed an error page and still exited 0; the app logged "OK". After a run, apps with
  `detect_display_name_contains` (not portable) that are still undetected get a log WARNUNG and the status
  line counts them as "nicht verifiziert".
- **Acrobat Reader**: use the full MUI installer
  `ardownload2.adobe.com/pub/adobe/acrobat/win/AcrobatDC/<ver-no-dots>/AcroRdrDCx64<ver-no-dots>_MUI.exe`
  (~820 MB, version from `rdc.adobe.io/reader/products?...api_key=dc-get-adobereader-cdn`), silent
  `-sfx_nu /sAll /rs /msi EULA_ACCEPT=YES` (winget manifest). Registry name is "Adobe Acrobat (64-bit)".
- **Referer**: catalog field `referer` -> `Downloader` header. IrfanView's `irfanview.info` serves an HTML
  "Click again" page without `Referer: https://www.irfanview.info/`. The downloader now rejects a
  `text/html` response for `.exe`/`.msi` targets instead of caching an HTML file. No User-Agent is sent on
  purpose (`get.videolan.org` returns an HTML page for browser UAs). Downloads retry 3x (flaky VLC mirrors).
- **TeamViewer custom QuickSupport** and the other items above are described in their catalog `notes`.
