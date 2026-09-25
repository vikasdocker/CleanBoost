# Rebuild + re-sign dist msix from the FIXED packaging/msix/layout manifest,
# then re-upload assets to the github release.  Windows PS 5.1-safe.
# Usage (repo root):  powershell -ExecutionPolicy Bypass -File scripts/rebuild-reupload.ps1

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

$proj   = Join-Path $root "src/CleanBoost.App/CleanBoost.App.csproj"
$ver    = (Select-String -Path $proj -Pattern "<Version>([^<]+)</Version>").Matches.Groups[1].Value
$tag    = "v" + $ver
$dist   = Join-Path $root "dist"
$layout = Join-Path $root "packaging/msix/layout"
$msix   = Join-Path $dist "CleanBoost-x64-$ver.msix"
$zip    = Join-Path $dist "CleanBoost-x64-$ver-portable.zip"
$pfx    = Join-Path $root "packaging/JSBlueFlutexDev.pfx"

if (-not (Test-Path $layout)) { throw "missing layout $layout" }
if (-not (Test-Path $pfx))    { throw "missing pfx $pfx" }
if (-not (Test-Path $zip))    { throw "missing zip $zip" }

function Find-KitTool([string]$name) {
    $kits = "C:\Program Files (x86)\Windows Kits\10\bin"
    if (-not (Test-Path $kits)) { throw "Windows Kits bin not found" }
    $hit = Get-ChildItem $kits -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$name" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if (-not $hit) { throw "$name not found under $kits" }
    return $hit
}

$makeappx = Find-KitTool "makeappx.exe"
$signtool = Find-KitTool "signtool.exe"
Write-Host "makeappx: $makeappx"
Write-Host "signtool: $signtool"

Write-Host "Repacking msix ($ver) from fixed manifest ..."
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

# JSBlueFlutexDev.pfx is exported without a password.
& $signtool sign /f $pfx /fd SHA256 /a $msix
if ($LASTEXITCODE -ne 0) { throw "signtool sign failed" }
Write-Host "signed with $pfx"

# Dev cert is self-signed, so chain trust always fails (signtool reports
# "root which is not trusted"). What actually matters: the signature is intact
# and it was issued by the expected publisher subject.
$sig = Get-AuthenticodeSignature $msix
if (-not $sig.SignerCertificate) { throw "msix carries no signature" }
if ($sig.SignerCertificate.Subject -ne "CN=JS BlueFlutex") {
    throw "unexpected msix signer: $($sig.SignerCertificate.Subject)"
}
if ($sig.Status -eq "HashMismatch" -or $sig.Status -eq "NotSigned") {
    throw "msix signature invalid: $($sig.Status)"
}

$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$verifyOut = (& $signtool verify /pa /v $msix 2>&1 | Out-String)
$ErrorActionPreference = $prevEAP
if ($verifyOut -match 'No signature was present|corrupt|has been modified') {
    throw "signtool reports a broken signature:`n$verifyOut"
}
Write-Host "verified: signer = CN=JS BlueFlutex (self-signed, chain untrusted = expected)"

Write-Host "Uploading to release $tag ..."
& gh release upload $tag "$msix" "$zip" --repo vikasdocker/CleanBoost --clobber
if ($LASTEXITCODE -ne 0) { throw "gh upload failed" }

Write-Host "Done. Re-install with:  Add-AppxPackage -Path $msix"
