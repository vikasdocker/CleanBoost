# Publish CleanBoost github release.  Windows PowerShell 5.1-safe (powerhell 7.
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File scripts/release-github.ps1
# Requires: gh.exe authed (`gh auth login`).  No other tools.

$ErrorActionPreference = "Stop"

$root  = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

$repo  = "vikasdocker/CleanBoost"
$filesDir = Join-Path $root "files/portable"
$distDir  = Join-Path $root "dist"

# version + tag straight from the csproj
$proj  = Join-Path $root "src/CleanBoost.App/CleanBoost.App.csproj"
$ver   = (Select-String -Path $proj -Pattern "<Version>([^<]+)</Version>").Matches.Groups[1].Value
$tag   = "v" + $ver
$title = "CleanBoost " + $ver

# bundle the two dist assets
$zip   = Join-Path $distDir "CleanBoost-x64-$ver-portable.zip"
$msix  = Join-Path $distDir "CleanBoost-x64-$ver.msix"

if (-not (Test-Path $zip))  { throw "Missing $zip" }
if (-not (Test-Path $msix)) { throw "Missing $msix" }

Write-Host "Releasing $title ($tag) ..."

if (-not (git rev-parse "$tag" 2>$null)) {
    git tag "$tag"
}

gh release create $tag "$zip" "$msix" --repo $repo --title "$title" `
    --notes ("MIT License. (c) 2026 vikas shelar / JS BlueFlutex.  Recycle-Bin first; deletions are never final.")

Write-Host "Release URL: https://github.com/$repo/releases/tag/$tag"
