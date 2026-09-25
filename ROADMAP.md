# CleanBoost — Build Handoff Roadmap

The goal, the locked spec, the exact working build commands, everything that is
already done, what remains, and every environment trap discovered so far — so a
fresh agent can rebuild this project from scratch without re-deriving
anything.

---

## 1. Mission

CleanBoost is a **WinUI 3 (Windows App SDK) desktop app** for Windows 10/11
that does two jobs:

1. **Cleaner** — scan-preview junk (temp files, browser caches, Windows Update
   leftovers, thumbnails, shader caches, Recent-documents history, WER reports,
   WinSxS update files, delivery optimization) and delete it **to the Recycle
   Bin** (never permanent), including opt-in **community rules** parsed from
   `winapp2.ini`.
2. **Booster** — one-click boost: Light mode (remove dead startup entries,
   curated debloat, disable Delivery Optimization) and Turbo mode (Recycle Bin
   empty, DNS flush, flush RAM). Never touches the registry for cleanliness;
   only writes registry settings the user explicitly chose.

Constraints that are NOT negotiable (safety model):
- Everything goes to the **Recycle Bin first**. Nothing is permanent.
- **Never** delete anything under protected system roots without explicit
  handler-approved carve-outs (winapp2 is only ever "ConfirmFirst-worthy").
- **No registry cleaning module** — ever. Registry-only winapp2 rules are
  skipped by the parser.
- Deletions must record an **audit log**. Locked/in-use files are skipped, not
  forced.
- Requires **elevation** (admin token) for system-level categories; the app
  shows an auto-admin banner and can relaunch itself elevated via UAC.

---

## 2. Locked decisions (the spec)

| Area | Decision |
| --- | --- |
| UI framework | WinUI 3, Windows App SDK 1.8.260804001, C#/.NET 8, MVVM-lite (CommunityToolkit.Mvvm) |
| Target | net8.0-windows10.0.19041.0, min 10.0.17763.0, x64/ARM64 |
| Packaging | MSIX (Store) + standalone MSI (WiX) + portable zip (x64) |
| Deletion | Recycle Bin (`SHEmptyRecycleBinW` for emptying; per-file delete via `SafeDeleter`) |
| Community rules | `winapp2.ini` parsed by `WinappParser`; registry-only & wildcard-root entries dropped; risk = WaCaution/ConfirmFirst |
| Boost modes | Light (safe) + Turbo (RAM flush behind consent, off by default) |
| Boosting steps | Startup orphans, debloat curated, disable Delivery Optimization, empty Recycle Bin, flush DNS, flush RAM |
| Safety | `PathGuard` with protected roots (Windows, ProgramFiles, ProgramData, System32, WinSxS, DriverStore, Packages) + carve-outs (`Windows\Temp`, SoftwareDistribution\*, Prefetch, Minidump, MEMORY.DMP, WER) |
| Deletion types | Only `FileKey`-based removals; symbolic links refused; empty-dir pruning bounded |
| Risk levels | Safe / Caution / ConfirmFirst / Protected (locked) |
| Registry writes | Only: disable Run entries (user-chosen), ServiceTuner autostart toggles, Delivery Optimization policy |
| Store | MSIX + Store listing; self-signed cert for dev sideload |

---

## 3. Repository layout

```
CleanBoost/
├─ src/
│  ├─ CleanBoost.Core/        # platform-agnostic engine (pure C#)
│  │  ├─ Catalog/             # BuiltInCatalog, CleanCategory, CleanTarget, RiskLevel
│  │  ├─ Scan/                # ScanEngine, ScanReport, PathResolver, PathMatcher
│  │  ├─ Safety/              # PathGuard, PathGuard.PathGuard, GuardFactory, RiskLevel
│  │  ├─ Deletion/            # SafeDeleter, IDeleter, RecycleBinDeleter, FileInfo
│  │  ├─ Rules/               # WinappParser (winapp2.ini), ParseRulesFile
│  │  └─ Audit/               # AuditLog
│  ├─ CleanBoost.System/      # Windows-only (Win API) — referenced by App
│  │  ├─ Boost/               # BoostCatalog, BoostExecutor
│  │  ├─ Debloat/             # AppxManager (AppxDeleter)
│  │  ├─ Elevation/           # ElevationHelper (IsElevated / RelaunchElevated)
│  │  ├─ Startup/             # StartupManager, ServiceTuner
│  │  ├─ Directx/             # CommonMarkupBuildWorker / placeholder (thin)
│  │  ├─ Interop/             # NativeMethods (ELEVATION?, shell32, advapi32, wininet)
│  │  ├─ Recycle/             # RecycleBin
│  │  ├─ System/              # process trim
│  │  └─ ...
│  └─ CleanBoost.App/         # WinUI 3 shell
│     ├─ App.xaml(.cs)
│     ├─ MainWindow.xaml(.cs)          # nav shell (Cleaner/History/Services/Booster)
│     ├─ Pages/
│     │  ├─ CleanerPage.xaml(.cs)      # main list + scan/clean
│     │  ├─ BoosterPage.xaml(.cs)      # Light/Turbo boost step list
│     │  ├─ HistoryPage.xaml(.cs)      # audit log viewer
│     │  └─ ServicesPage.xaml(.cs)     # startup/service toggles
│     └─ Assets/ (icons; WinUI uses Assets/Square44x44Logo etc.)
├─ rules/winapp2.ini          # community database (winapp2.2)
├─ packaging/
│   ├─ msix/                  # AppxManifest + build+sign cmd
│   └─ msi/                   # WiX .wxs (product + files)
├─ publish/ win-x64/          # `dotnet publish` output (self-contained)
├─ dist/                      # release artifacts (zip / msix / msi)
├─ tools/                     # dev scripts (gen-wix-files, publish, cert utils)
└─ tests/CleanBoost.Tests/    # xunit (net8.0), + optional WinAppSDK TFM
────────────────────────────────
```

---

## 4. How to build & test (commands that are known to work)

**One shot — restore, test, publish, icons, MSIX, sign, zip, MSI:**

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1          # build all three
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1 -SkipTests
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1 -Upload  # + push to gh release
```

Version comes from `<Version>` in `src/CleanBoost.App/CleanBoost.App.csproj`;
the script *asserts* the published exe, the AppxManifest `Identity/@Version`
and the WiX `Package/@Version` all agree with it, so they cannot silently
drift again.

Individual steps, from a **Windows** terminal:

```bash
# Release publish (self-contained, x64) → publish/win-x64
dotnet publish src/CleanBoost.App/CleanBoost.App.csproj -c Release \
  -r win-x64 --self-contained -p:PublishSingleFile=false -o publish/win-x64

# Tests (Core + System) — run from WSL (works, no GUI needed)
dotnet test tests/CleanBoost.Tests/CleanBoost.Tests.csproj
# On Windows-only TFM tests (elevation helper):
dotnet test tests/CleanBoost.Tests -c Release -p:RunWindowsTests=true

# WiX MSI only (native Windows; see §7 env trap #2)
powershell -File tools/gen-wix-files.ps1 -Src publish/win-x64 -Out packaging/msi/files.wxs
wix build packaging/msi/product.wxs packaging/msi/files.wxs -arch x64 -out dist/CleanBoost-x64-0.2.0.msi

# Re-pack + re-sign MSIX only
powershell -File scripts/rebuild-reupload.ps1
```

Prereqs: `dotnet` 8.0.425 (see `global.json`), Windows 10 SDK (for
`makeappx.exe`/`signtool.exe`, auto-discovered under
`C:\Program Files (x86)\Windows Kits\10\bin\<version>\x64\`), `wix`
(`dotnet tool install --global wix`), `gh` authed.

Absolute paths used in CI:

```
/mnt/c/Users/vikas/CleanBoost
C:\Users\vikas\CleanBoost
```

Thumbprint note: the app project (CleanBoost.App.csproj) is
`WindowsAppSDKSelfContained=true`, self-contained, and requires the
WindowsAppSDK 1.8 package (`Microsoft.WindowsAppSDK` 1.8.260804001,
`microsoft.windowsappsdk.winui` transitive), NOT the 1.6 line of
WindowsAppSDK (see §7 env trap #1).

---

## 5. Done (verified 30 passing; +2 windows-only opt-in)

Core engine (CleanBoost.Core) — no OS dependencies, unit-tested:
- BuiltInCatalog with 8 Safe +  capitalised categories; Files / FilesElevated /
  SystemCommand kinds (recycle-bin, dnsflush)
- ScanEngine with wildcard path symbols (+`?`), recursion, `.gitignore`-style
  pruning, `OlderThan` age filter, per-category/global progress, cancellation
- PathResolver: `%AppData%` etc. to real paths, `%var%` reverse lookup,
  `PathGuard` safe carve-outs
- `SafeDeleter` / `IDeleter` / `RecycleBinDeleter`; deletes go to Recycle Bin;
  audit log; parallel delete via `Parallel.ForEachAsync`
- `WinappParser` — parses winapp2.ini into CleanCategory list (FileKey only,
  skips registry-only), plus `LocateRulesFile()` + `ParseFile()`
- 30 net8.0 unit tests (ScanEngine, PathGuard, PathResolver, WinappParser,
  SafeDeleter) green

System layer (CleanBoost.System) — compiles clean (net8.0-windows TFM):
- `ElevationHelper` (IsElevated via token check + `RelaunchElevated` runas)
- `StartupManager` (StartupMonitor), `ServiceTuner` (service autostart toggles),
  `DnsFlusher`, `AppxManager` (AppxDeleter), `RecycleBin`, `ProcessTrimmer`
- `BoostCatalog`/`BoostExecutor`: Light + Turbo step lists with per-step
  results, run on background threads
- App (WinUI 3, WindowsAppSDK 1.8): CleanerPage scan/clean with category list,
  BoosterPage with Light/Turbo toggle + Turbo-vs-Caution banner, ServicesPage
  with ServiceTuner toggles, admin banner + restart-as-admin, history page
  backed by audit log. Builds clean (0 warnings).

Tests: 30/30 net8.0 + 2 elevation (opt-in `-p:RunWindowsTests=true`,
31/31 on Windows).

Packaging — all three deliverables exist for 0.2.0 (see §6 for what is left):
- `dist/CleanBoost-x64-0.2.0-portable.zip` (81.7MB, self-contained)
- `dist/CleanBoost-x64-0.2.0.msix` (83.3MB) — signed `CN=JS BlueFlutex` with
  `packaging/JSBlueFlutexDev.pfx` (self-signed, **sideload only**). Inside the
  package: `Identity/@Version=0.2.0.0`, exe 0.2.0.0, 516 files.
- `dist/CleanBoost-x64-0.2.0.msi` (67.6MB) — WiX 7, 505 files, admin-install
  smoke-checked (extracts to 506 files incl. `rules\winapp2.ini`).
- `scripts/build-release.ps1` produces all three from source in one run.
- `tools/generate-icons.ps1` produces the app assets (Square44x44Logo,
  Square150x150Logo, Square310x310Logo, StoreLogo, Wide310x150Logo)
- `packaging/msix/` layout = publish/win-x64 + AppxManifest + Assets
- `packaging/msi/` WiX `product.wxs` + generated `files.wxs`
- Uploaded to GitHub release `v0.2.0` (repo `vikasdocker/CleanBoost`, public).

---

## 6. What remains (roadmap for the next agent)

### P0 — Standalone installer — DONE
- `dist/CleanBoost-x64-0.2.0.msi` builds from `packaging/msi` with native WiX 7
  (`wix` on PATH). The WSL path bug (§7#2) is bypassed by
  `tools/gen-wix-files.ps1`, which emits `C:\...` paths.
- Still outstanding: a real **install → launch → uninstall** run as admin
  (only a non-elevated *administrative* extract has been automated so far),
  and signing the MSI (currently unsigned, unlike the MSIX).

### P1 — Store packaging (MSIX real Store submission)
- `dist` uses a dev self-signed cert. For Store:
  - Generate proper Store asset sizes (see generate-icons.ps1; use the 10-panel set),
  - Switch MSIX signing to the Store att honey (Partner Center) or a real EV cert
  - Add `PublisherId`, proper `PhoneProductId`, `PublisherDisplayName`.
  - Create the **Store listing**: description, screenshots, category "Utilities".
- Verify the full MSIX passes App Submission checks (the dev-cert MSIX only
  sideloads; Store needs the 10.0-panel icon set + proper version).

### P2 — CI + reproducible builds — DONE locally, CI still open
- `scripts/build-release.ps1` does restore → test → publish → gen icons →
  pack MSIX → sign → gen WiX files → build MSI → zip, with `-Upload`.
- Optional: GitHub Actions `windows-latest` job (self-hosted not required).
- The commit for a release should be tagged `v<Version>` from the csproj;
  `build-release.ps1 -Upload` creates the tag if missing.

### P3 — Polish
- Add `Asset Files` + `App icon` to the .csproj so the MSIX `AppxManifest`
  references the right logo names (currently uses `Assets\StoreLogo.png`).
- `Directory.Build.props` still carries `Authors`/`Product`/`RepositoryUrl`
  defaults (`RepositoryUrl` is a placeholder `example/cleanboost`); the App
  csproj overrides Version/Company/Authors itself. Worth collapsing to one
  source of truth.
- Add LICENSE attribution for `rules/winapp2.ini` (CC-BY-SA) — MIT LICENSE
  exists but does not mention it.
- Signing keys: `.gitignore` now blocks `*.pfx`, but `packaging/CleanBoostDev.pfx`
  is **already in history** at commit `f2ed5c5`. It is password-protected; treat
  it as burned.

### P4 — Verification checklist (manual, on a real Windows machine)
1. Launch → admin banner shows → "Restart as administrator" → UAC → banner
   flips to "running elevated".
2. Cleaner: scan (Safe categories only) → sizes appear → clean → items are in
   Recycle Bin → audit log entry present.
3. Enable "Community rules" → winapp2 categories load → scan covers them.
4. Booster: Light boost applies the 4 light steps; Turbo (after consent)
   empties Recycle Bin + flushes DNS + trims RAM.
5. Services page: toggling a service autostart requires admin and writes.
6. Uninstall cleanly removes shortcuts + app (MSI) / package (MSIX).

---

## 7. Environment traps (read BEFORE starting — these cost hours)

1. **WindowsAppSDK version crashed the XAML compiler on this machine.**
   The default SDK line (1.6.250602001) makes `XamlCompiler.exe` fail with
   exit-code 1 during WinUI build. **Must use WindowsAppSDK 1.8.x** —
   specifically `Microsoft.WindowsAppSDK` **1.8.260804001** (the 1.8
   `microsoft.windowsappsdk.winui` package). Verify with:
   ```
   ls ~/AppData/Local/Microsoft/WindowsApps/x64/microsoft.windowsappsdk/ 2>/dev/null
   ```
   Update `CleanBoost.App.csproj` `PackageReference Microsoft.WindowsAppSDK`
   to match whatever is on disk in the `microsoft.windowsappsdk.winui`

2. **WiX build on WSL fails to resolve `/mnt/c/...` paths — SOLVED, do not
   re-derive.** WiX running under WSL cannot `File.IsExists` files at
   `/mnt/c/...`. Symptoms: `error WIX0103: Cannot find the File ... The
   following paths were checked: /mnt/c/...` even though the file exists.
   **Resolution: run WiX on native Windows** (`wix` 7.0.0 on PATH) and
   generate `files.wxs` with `tools/gen-wix-files.ps1`, which writes `C:\...`
   paths. The bash `tools/gen-wix-files` writes `/mnt/c/...` and must NOT be
   used for a native build. Do not spend time debugging WSL WiX path handling.

3. **WSL dev loop**: use `/mnt/c/...` for file edits + `dotnet test` from WSL
   (Core tests are pure, run fine); use Windows `dotnet` (`/mnt/c/.../dotnet.exe`)
   for **all** Windows-TFM builds and the WinUI app. Keep `dotnet test` net8.0
   on WSL for CI speed; run the 2 elevation tests on Windows with
   `-p:RunWindowsTests=true`.

4. **Global.json pins SDK 8.0.425** — WSL `~/.dotnet` has 8.0.424; use the
   Windows `dotnet (8.0.425)` for test runner consistency, or bump global.json.

5. **No UAC from WSL** — elevation can only be verified on a Windows host;
   the automated elevation tests only check `IsElevated()` returns a coherent
   boolean (not `throw`).

6. **Publish needs `-p:PublishSingleFile=false`** so `rules/winapp2.ini` and
   locale MUI files keep their directory structure (needed for the MSIX/MSI
   harvest and for `WinappParser.LocateRulesFile()` which looks next to the
   executable).

7. **`makeappx.exe`/`signtool.exe` live under a versioned dir, not `bin\x64`.**
   Picking "the newest directory in `Windows Kits\10\bin`" by string sort
   yields `x86` and then `bin\x86\x64\signtool.exe` — which does not exist.
   Filter to `^\d+\.\d+\.` and sort as `[version]`. See the `Find-KitTool`
   function in `scripts/build-release.ps1`.

8. **WiX WIX0368: `Guid="*" is not valid`** when one `<Component>` holds
   several files and a *non*-keypath file is versioned. Emit **one component
   per file** — that is what `tools/gen-wix-files.ps1` does (505 components).

9. **PowerShell `-replace` is case-insensitive.** Replacing `Version="..."`
   also hits the XML declaration's `version="1.0"` and `InstallerVersion="500"`,
   which makes WiX report `WIX0104: Syntax for an XML declaration is invalid`
   or `WIX0008: InstallerVersion is not a legal integer`. Anchor the pattern:
   `(<Package\b[^>]*?\bVersion=")[^"]*"`.

10. **`signtool verify /pa` always "fails" for the dev MSIX** — self-signed
    root, so you get "certificate chain ... terminated in a root which is not
    trusted". That is expected, not a build failure. Verify with
    `Get-AuthenticodeSignature` and assert the signer subject
    `CN=JS BlueFlutex` instead. Also note `$ErrorActionPreference = "Stop"`
    turns signtool's *stderr* into a terminating error — suspend it around
    the verify call.

11. **Git does not untrack a file just because you added it to `.gitignore`.**
    `packaging/CleanBoostDev.pfx` stayed in HEAD after `*.pfx` was ignored;
    it needs an explicit `git rm --cached`. Same for `publish/` and
    `packaging/msix/layout/`, which held ~850 tracked build-output files.

---

## 8. Success criteria / quality gate for "done"

Current state (0.2.0) — all verified:

- [x] `dotnet test`: **30/30** green.
- [ ] `dotnet test -p:RunWindowsTests=true` on Windows: 31/31 green
      (2 elevation tests are opt-in and were not re-run this pass).
- [x] `dotnet build CleanBoost.App -c Release -r win-x64` → 0 errors/0 warnings.
- [x] `dist/CleanBoost-x64-0.2.0.msi` (67.6MB) builds; administrative install
      extracts 506 files incl. `rules\winapp2.ini`. **Still needs a real
      elevated install/launch/uninstall (P4).**
- [x] `dist/CleanBoost-x64-0.2.0.msix` (83.3MB) signed `CN=JS BlueFlutex`,
      unpacks to `Identity 0.2.0.0` + exe 0.2.0.0 (516 files), sideloadable.
- [x] All 3 deliverables (portable zip 81.7MB, MSIX, MSI) in `dist/` and
      uploaded to GitHub release `v0.2.0`.
- [x] One command reproduces all three: `scripts/build-release.ps1`.
- [ ] No signing key tracked in git (old `CleanBoostDev.pfx` still in history
      at `f2ed5c5` — password-protected, treat as burned).
