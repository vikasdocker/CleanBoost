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

From a **Windows terminal** (does NOT work purely from WSL without the
workarounds below; see §7):

```bash
# Release publish (self-contained, x64) → publish/win-x64
dotnet publish src/CleanBoost.App/CleanBoost.App.csproj -c Release \
  -r win-x64 --self-contained -p:PublishSingleFile=false -o publish/win-x64

# Tests (Core + System) — run from WSL (works, no GUI needed)
dotnet test tests/CleanBoost.Tests/CleanBoost.Tests.csproj
# On Windows-only TFM tests (elevation helper):
dotnet test tests/CleanBoost.Tests -c Release -p:RunWindowsTests=true
```

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

## 5. Done (verified 29 passing + 2 windows-only)

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
- 29 Core unit tests (ScanEngine, PathGuard, PathGuardTests, WinappParser,
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

Tests: 29/29 net8.0 + 2 elevation (opt-in `-p:RunWindowsTests=true`,
31/31 on Windows).

Packaging (partial, see §6):
- `dist/CleanBoost-x64-0.1.0-portable.zip` (85.6MB, self-contained)
- `dist/CleanBoost-x64-0.1.0.msix` (85.3MB) — **signed** with a dev cert,
  `makeappx` + `signtool`. Not yet Store-ready; sideload only.
- `tools/generate-icons.ps1` produces the app assets (Square44x44Logo,
  Square150x150Logo, Square310x310Logo, StoreLogo, Wide310x150Logo)
- `packaging/msix/` layout = publish/win-x64 + AppxManifest
- `packaging/msi/` WiX project `.wxs` source is ready but the **WiX build
  itself is BLOCKED by a WiX-on-WSL issue** (see §7 env trap #2). The WiX MSI
  has NOT been produced yet.

---

## 6. What remains (roadmap for the next agent)

### P0 — Finish standalone installer (blocked by §7#2)
- Deliver `CleanBoost-x64-0.1.0.msi` from `packaging/msi`.
- Approaches to take (choose by tooling available):
  1. Build WiX on **native Windows** (e.g. `dotnet tool install --tool-path C:\wix wix`)
     — avoids the WSL remote-path bug entirely.
  2. Or switch to another installer (Inno Setup / NSIS / Advanced Installer) — spec
     unchanged.
- Then add an install smoke check.

### P1 — Store packaging (MSIX real Store submission)
- `dist` currently uses a dev self-signed cert. For Store:
  - Generate proper Store asset sizes (see generate-icons.ps1; use the 10-panel set),
  - Switch MSIX signing to the Store att honey (Partner Center) or a real EV cert
  - Add `PublisherId`, proper `PhoneProductId`, `PublisherDisplayName`.
  - Create the **Store listing**: description, screenshots, category "Utilities".
- Verify the full MSIX passes App Submission checks (the dev-cert MSIX only
  sideloads; Store needs the 10.0-panel icon set + proper version).

### P2 — CI + reproducible builds
- Add a `build.ps1`/`pack.cmd` that: restore → test (Core suite) → publish →
  gen icons → pack MSIX → sign → build WiX MSI → zip. Commands must match §4.
- Optional: GitHub Actions `windows-latest` job (self-hosted not required).
- Tag `v0.1.0`.

### P3 — Polish
- Add `Asset Files` + `App icon` to the .csproj so the MSIX `AppxManifest`
  references the right logo names (currently uses `Assets\StoreLogo.png`).
- Version bump to 0.1.0, `Version`/`AssemblyFileVersion` synced to a single
  `Directory.Build.props` var.
- Add LICENSE (the `rules/winapp2.ini` is CC-BY-SA — include attribution).
- Wire the WiX `files.wxs` to be generated by `tools/gen-wix-files` from the
  publish folder (already exists; unblocked once WiX build works).

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

2. **WiX build on WSL fails to resolve `/mnt/c/...` paths.** The WiX toolset
   running under WSL cannot `File.IsExists` files at `/mnt/c/...` (they exist,
   but paths are handled by the .NET host under a different mount). Symptoms:
   `error WIX0103: Cannot find the File ... The following paths were checked:
   /mnt/c/...` — even though the file exists. **Workaround: build WiX on
   native Windows** (hosted dotnet resolves `C:\...` correctly). Do not spend
   time debugging WSL WiX path handling. (Current: `packaging/msi/*.wxs`
   uses `/mnt/c/...` absolute paths — regenerate from `tools/gen-wix-files`
   into `C:\...` form when building natively.)

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

---

## 8. Success criteria / quality gate for "done"

- [ ] `dotnet test` on WSL: 29/29 green.
- [ ] `dotnet test -p:RunWindowsTests=true` on Windows: 31/31 green.
- [ ] `dotnet build CleanBoost.App -c Release -r win-x64` → 0 errors/0 warnings.
- [ ] `dist/CleanBoost-x64-0.1.0.msi` installs to `Program Files\CleanBoost`,
      launches, uninstalls cleanly (P4 checklist).
- [ ] `dist/CleanBoost-x64-0.1.0.msix` signed + sideload-installable
      (Store listing can follow).
- [ ] All 3 deliverables (portable zip, MSIX, MSI) in `dist/` with the same
      code revision tagged.
```
