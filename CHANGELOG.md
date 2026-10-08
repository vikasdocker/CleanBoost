# Changelog

All notable changes to CleanBoost. Dates are release dates.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.1] - 2026-10-08

### Fixed
- **The application had no icon at all.** The MSI-installed app showed a blank
  icon in Programs and Features. Three separate causes: `CleanBoost.exe` carried
  zero icon resources (`ExtractIconExW` returned 0, against 4 for `notepad.exe`),
  no `.ico` was ever generated because `tools/generate-icons.ps1` only emitted
  PNGs and a PNG cannot be a Windows application icon, and the MSI shortcut set
  no `Icon` so it had no embedded icon to fall back to.
- **The release pipeline shipped it.** `build-release.ps1` published at step 2 and
  generated icons at step 3, but `ApplicationIcon` is a compile-time input — so
  the exe was compiled before the icon existed. Icon generation now runs first and
  asserts the `.ico` was produced.

### Added
- `packaging/assets/CleanBoost.ico`, a multi-frame icon (16/24/32/48/64/128/256)
  assembled without any new build dependency. Small frames use a single "C"
  because "CB" is unreadable at 16px; the wordmark starts at 32px.
- `ApplicationIcon` in `CleanBoost.App.csproj`, so the icon is embedded in the
  exe and the MSI shortcut inherits it.

## [0.3.0] - 2026-10-08

Live cleanup progress, a Turbo button that does what it says, select/delete-all,
and no more tab-switch resets.

### Added
- **Live per-file cleanup log.** Every file is shown as it is processed — path,
  category and outcome (Deleted / Locked / Refused / Gone / Error), newest first —
  alongside a determinate progress bar and running totals for freed space and
  per-outcome counts.
- **Cancel button.** Scanning and cleaning are separately cancellable; a cleanup
  stops between batches.
- **Select all, Clear selection, Clean all found.** `Select all` ticks only
  categories that actually have findings. `Clean all found` applies the same
  consent gates as `Clean selected`.
- **Turbo badge and live per-step status** on the Booster, with a button that
  relabels to "Apply Turbo" when Turbo is enabled.
- `AuditLog.ReadTail` — reads only the newest N records by seeking to the end of
  the journal, so History no longer scales with journal size.
- Audit journal rotation past 24 MB.
- Refresh button on History.
- `IBatchDeleter` — optional batch capability for deletion backends.

### Changed
- **Deletions are batched**, 64 paths per `SHFileOperationW` call instead of one
  call per file. This is the main reason large cleanups no longer appear to hang.
- `BoostCatalog.Turbo()` now owns Empty Recycle Bin, Flush DNS and Flush RAM.
  **Behaviour change:** Light mode is 3 steps rather than 5 — those two steps moved
  behind the Turbo consent, where the ROADMAP always said they belonged.
- `SafeDeleter.Delete` validates every item individually, then dispatches survivors
  in chunks. Safety decisions are never made on a batch.
- `AppxManager.Remove` → `RemoveAsync`; `DnsFlusher.Flush` → `FlushAsync`. Boost
  runs are now genuinely cancellable.
- Process trimming skips session-critical processes.

### Fixed
- **Switching tabs no longer discards Cleaner state.** Pages are built once and
  reused; the frame no longer keeps a navigation stack. Previously every tab click
  constructed a new page, throwing away the selection, scan results, and any
  in-flight cleanup.
- **History no longer freezes.** It read and deserialised the entire audit journal
  on the UI thread.
- **Unbounded directory pruning.** Empty-folder pruning ascended past the cleanup
  target and all the way up the tree. `CleanTarget.RemoveSelf` existed but was read
  by nothing. Pruning is now bounded to targets that opt in.
- **Turbo preview/execute mismatch.** The step list showed Turbo steps as pending
  while the executor silently skipped them, because the preview ignored the consent
  checkbox that execution required.
- Locked files are reported as Locked rather than a generic error, with a readable
  reason in the journal.
- Cleaner no longer fires an un-awaited background re-scan after every cleanup.
- The "needs administrator mode" warning on Services showed even when already
  elevated.
- Audit records retain the backend error reason.

## [0.2.0] - 2026-09-25

- Real release pipeline: MSIX (signed) + MSI (WiX) + portable zip from one command.
- Corrected MSIX publisher identity to `CN=JS BlueFlutex`.
- SLSA generic-generator publish workflow.
- Version welded to the csproj; the release script derives the tag and asserts the
  csproj, AppxManifest and WiX versions cannot drift.

## [0.1.0] - 2026-09-20

- First release: Cleaner, Booster, Services.
- Recycle-Bin-first deletion.
- Consent-gated elevation.
- WINAPP2 community rules.

[0.3.1]: https://github.com/vikasdocker/CleanBoost/releases/tag/v0.3.1
[0.3.0]: https://github.com/vikasdocker/CleanBoost/releases/tag/v0.3.0
[0.2.0]: https://github.com/vikasdocker/CleanBoost/releases/tag/v0.2.0
[0.1.0]: https://github.com/vikasdocker/CleanBoost/releases/tag/v0.1.0