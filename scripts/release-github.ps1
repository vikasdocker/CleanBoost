# CleanBoost GitHub release (0.2+). Version auto-derived from csproj.
# MSIX is attached as 10MB parts (GitHub 2GB cap is per-file; app sits ~85MB).
# Run in Windows PowerShell: powershell -ExecutionPolicy Bypass -File scripts/release-github.ps1
$ErrorActionPreference = "Stop"

$proj = "src/CleanBoost.App/CleanBoost.App.csproj"
$ver  = (Select-String -Path $proj -Pattern "<Version>([^<]+)</Version>").Matches.Groups[1].Value
$tag  = "v" + $ver
$repo = "vikasdocker/CleanBoost"

$dist = "dist"
$portable = "$dist/CleanBoost-x64-$ver-portable.zip"
$msixParts = Get-ChildItem -Path $dist -Filter "CleanBoost-x64-$ver.msix*" |
             Sort-Object Name

if (-not (Test-Path $portable)) { throw "Missing $portable" }
if (-not $msixParts) { throw "Missing CleanBoost-x64-$ver.msix* in $dist" }

Write-Host "Publishing CleanBoost $ver from $portable + $($msixParts.Count) msix chunk(s)"

# ensure the tag is on the remote (release REQUIRES it)
$have = git ls-remote --tags origin $tag
if (-not $have) {
    git push origin tag $tag
    if ($LASTEXITCODE -ne 0) { throw "Could not push tag $tag" }
}

$files = @($portable) + @($msixParts.FullName)
$first = $files[0]
$rest  = $files[1..($files.Length-1)]

$body = @"
CleanBoost $ver - by JS BlueFluteX / vikas shelar

CleanBoost is a one-click Windows cleaner and booster.

- Everything deleted goes to the Recycle Bin FIRST - nothing is permanent
  until you empty it.
- Registry cleaning is never performed.
- Elevation only ever happens after your consent.
- JSON community rules (winapp2.ini) are fully optional.

Files in this release:
- CleanBoost-x64-$ver-portable.zip  - unzip and run CleanBoost.exe
- CleanBoost-x64-$ver.msix(.partNN) - reassemble the parts, then sideload
  (Add-AppxPackage the reassembled single .msix)

MIT License - (c) 2026 vikas shelar / JS BlueFluteX
"@

# create with the FIRST asset so the release object pre-exists
Write-Host "Creating release $tag with $($rest.Length+1) asset(s)..."
gh release create $tag $first --repo $repo --title "CleanBoost $ver" --notes $body
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }

Write-Host "Uploading remaining asset(s)..."
foreach ($f in $rest) {
    gh release upload $tag $f --repo $repo --clobber
    if ($LASTEXITCODE -ne 0) { throw "upload failed: $f" }
}

Write-Host "Verifying..."
$json = gh release view $tag --repo $repo --json name,tagName,assets --jq "{name:.name,tag:.tagName,assets:[.assets[].name]}"
Write-Host $json
Write-Host "Release: https://github.com/$repo/releases/tag/$tag"
