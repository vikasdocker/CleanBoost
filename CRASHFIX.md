# CleanBoost v0.2.0 startup crash — FIXED locally, not yet committed/released

## Root cause
`src\CleanBoost.App\MainWindow.xaml:82` referenced `Style="{ThemeResource TextButtonStyle}"`.
`TextButtonStyle` does NOT exist in WinUI (byte-searched all 196 WinUI files in build output;
only our own CleanBoost.pri contains the name). Missing ThemeResource → `InitializeComponent`
throws XAML originate error `0x802B000A` → stowed as `0xC000027B` (`-1073741189`) in
`Microsoft.ui.xaml.dll`. Nothing ever renders.

Same class of bug at `src\CleanBoost.App\Pages\ServicesPage.xaml:38`: `TextFillColorCautionBrush`
is nonexistent; the real key is `SystemFillColorCautionBrush`.

## Changes made (working tree, uncommitted)
- MainWindow.xaml:82 — removed the bogus `Style="{ThemeResource TextButtonStyle}"`
  (button already has Background=Transparent, BorderThickness=0, Padding).
- ServicesPage.xaml:38 — `TextFillColorCautionBrush` → `SystemFillColorCautionBrush`.

## Verified
- `dotnet build` Release x64 → RUNS
- `dotnet publish` → publish\win-x64 (510 files) → RUNS
- `dotnet test tests\CleanBoost.Tests\CleanBoost.Tests.csproj` → 30/30 pass
- All 10 remaining ThemeResource keys verified present in WinUI resource files.

## Impact
The published v0.2.0 release assets (dist zip, msix, msi) all ship the crashing build
(shipped CleanBoost.pri still contains TextButtonStyle; shipped CleanBoost.dll = 240640
bytes, matching the crashing build). Remote `v0.2.0` release on GitHub is currently broken.

## Next steps (when ready)
1. `git add -A && git commit -m "fix: remove nonexistent WinUI theme resources"`
2. Rerun `scripts/build-release.ps1` (or `build-release.ps1 -Upload`).
3. Force-replace the three v0.2.0 assets on the existing release.
4. Optional cleanup: remove `%TEMP%\cb-*` scratch/worktree dirs; remove cb-wt worktree.
5. Consider telling user to update global `~/.gitconfig` identity + fix `credential.helper=`.
