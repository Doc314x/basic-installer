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

## Configuration / Data Files

All under `%APPDATA%\BasicInstaller\`:
- `config.json` — last-used selection/paths/languages/options (auto-saved on close, no secrets).
- `profiles.json` — named snapshots of the same shape, saved explicitly via "💾 Speichern als…".
- `user_apps.json` — optional user-authored catalog additions/overrides, same shape as
  `catalog/apps.json`.
- `logs\install_<timestamp>.log` — one file per app run, persistent copy of the on-screen log.

Download cache: `%LOCALAPPDATA%\BasicInstaller\cache\` (`Downloader.CacheDirectory`).

Secrets: Windows Credential Manager, target name `BasicInstaller_license_<app id>` — never a file.

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
