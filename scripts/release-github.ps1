# CleanBoost - one-shot GitHub release (run in Windows PowerShell).
# Derives the version from the csproj so 0.3/0.4 need zero edits here.
$ErrorActionPreference = "Stop"

$repo = "vikasdocker/CleanBoost"
$proj = "src/CleanBoost.App/CleanBoost.App.csproj"
$tag  = (Select-String -Path $proj -Pattern "<Version>(.*?)</Version>").Matches.Groups[1].Value
$pkg  = "CleanBoost-x64-$tag-portable.zip"
$msix = "CleanBoost-x64-$tag.msix"

if (-not (Test-Path "dist/$pkg") -or -not (Test-Path "dist/$msix")) {
    throw "Run publish first: scripts\publish.ps1 (produces dist\$pkg + dist\$msix)"
}

gh release create "v$tag" "dist/$pkg" "dist/$msix" `
    --repo $repo `
    --title "CleanBoost $tag" `
    --notes (@"
CleanBoost $tag

A one-click Windows cleaner and booster.
Owner: vikas shelar
Company: JS BlueFluteX
License: MIT (see LICENSE)

What is in this release:
- $pkg  = portable, no install
- $msix = MSIX installer (sideload; dev-signed)

All deletions go to the Recycle Bin first. Deletions are never permanent
until the user empties the Recycle Bin. Elevation only happens after
consent. No registry cleaning. No unforced deletions.
"@)

Write-Host "Release posted: https://github.com/$repo/releases/tag/v$tag"
