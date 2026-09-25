# One-shot GitHub release for CleanBoost. Run from Windows PowerShell.
$ErrorActionPreference = 'Stop'

$repo = 'vikasdocker/CleanBoost'
$proj = 'src/CleanBoost.App/CleanBoost.App.csproj'
$dist = 'dist'

$version = (Select-String -Path $proj -Pattern '<Version>([^<]+)</Version>').Matches.Groups[1].Value
Write-Host "Releasing CleanBoost v$version"

$tag = "v$version"
$zip = Get-ChildItem -Path $dist -Filter "CleanBoost-x64-$version-portable.zip" | Select-Object -First 1
$msix = Get-ChildItem -Path $dist -Filter "CleanBoost-x64-$version.msix" | Select-Object -First 1

if (-not $zip -or -not $msix) {
    Write-Error "Missing artifacts in $dist for $version"
}

# Fat binary chunking for MSIX (10MB boundaries)
$chunkSize = 10 * 1024 * 1024
$parts = @()
$bytes = [System.IO.File]::ReadAllBytes($msix.FullName)
$count = [Math]::Ceiling($bytes.Length / $chunkSize)
for ($i = 0; $i -lt $count; $i++) {
    $name = "{0}.part{1:D2}" -f $msix.Name, ($i + 1)
    $off = $i * $chunkSize
    $len = [Math]::Min($chunkSize, $bytes.Length - $off)
    $chunk = New-Object byte[] $len
    [Array]::Copy($bytes, $off, $chunk, 0, $len)
    $path = Join-Path $dist $name
    [System.IO.File]::WriteAllBytes($path, $chunk)
    $parts += $path
}

# Publish notes (Markdown body)
$body = @"

CleanBoost `$version

by **vikas shelar** (`JS BlueFluteX`)

### What this is
A one-click Windows cleaner and booster. Deletions go to the **Recycle Bin first**; nothing is permanent until you empty it.

### Files
| Asset | Purpose |
| --- | --- |
| `CleanBoost-x64-$version-portable.zip` | No-install portable |
| `CleanBoost-x64-$version.msix` | MSIX for sideload |

### Install
- **Portable**: unzip, run `CleanBoost.exe`
- **MSIX**: right-click install (needs the self-signed dev cert on first sideload)

### Safety
- Recycle Bin first, always
- Elevation only after consent
- No registry cleaning, no forced deletions

MIT License. (c) 2026 vikas shelar / JS BlueFluteX.
"@

# Create release with the zip + the first MSIX part
$release = gh release create $tag `
    "$($zip.FullName)" `
    "$($parts[0])" `
    --repo $repo `
    --title "CleanBoost $version" `
    --notes $body

Write-Host "Release created. Uploading $($parts.Count - 1) remaining MSIX parts..."
for ($i = 1; $i -lt $parts.Count; $i++) {
    gh release upload $tag "$($parts[$i])" --repo $repo --clobber
}

Write-Host "Done."
Write-Host "URL: https://github.com/$repo/releases/tag/$tag"
