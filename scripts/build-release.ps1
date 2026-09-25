# CleanBoost one-shot release pipeline (Windows PowerShell 5.1-safe).
#
#   powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
#   ... -SkipTests   ...   ... -Upload
#
# Steps: restore -> test -> publish -> icons -> msix layout -> pack+sign msix
#        -> zip -> gen wix files -> build msi -> verify.  -Upload also pushes
#        the three artifacts to the GitHub release tagged v<Version>.
#
# Version comes from src/CleanBoost.App/CleanBoost.App.csproj <Version>.

param(
    [switch]$SkipTests,
    [switch]$Upload
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

function Step([string]$msg) { Write-Host ""; Write-Host "=== $msg ===" -ForegroundColor Cyan }

$proj    = Join-Path $root "src/CleanBoost.App/CleanBoost.App.csproj"
$ver     = (Select-String -Path $proj -Pattern "<Version>([^<]+)</Version>").Matches.Groups[1].Value
$tag     = "v$ver"
$dist    = Join-Path $root "dist"
$pub     = Join-Path $root "publish/win-x64"
$layout  = Join-Path $root "packaging/msix/layout"
$assets  = Join-Path $root "packaging/assets"
$msiSrc  = Join-Path $root "packaging/msi"
$pfx     = Join-Path $root "packaging/JSBlueFlutexDev.pfx"
$publisher = "CN=JS BlueFlutex"

$zip  = Join-Path $dist "CleanBoost-x64-$ver-portable.zip"
$msix = Join-Path $dist "CleanBoost-x64-$ver.msix"
$msi  = Join-Path $dist "CleanBoost-x64-$ver.msi"

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

New-Item -ItemType Directory -Path $dist -Force | Out-Null

Step "1/8 restore + test ($ver)"
dotnet restore $proj
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }
if (-not $SkipTests) {
    $testProj = Join-Path $root "tests/CleanBoost.Tests/CleanBoost.Tests.csproj"
    dotnet test $testProj --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

Step "2/8 publish self-contained x64"
if (Test-Path $pub) { Remove-Item -Recurse -Force $pub }
dotnet publish $proj -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=false -o $pub --nologo -v m
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
$exeVer = (Get-Item (Join-Path $pub "CleanBoost.exe")).VersionInfo.FileVersion
if ($exeVer -ne "$ver.0") { throw "published exe is $exeVer, expected $ver.0" }
Write-Host "published exe version $exeVer"

Step "3/8 icons"
powershell -ExecutionPolicy Bypass -File (Join-Path $root "tools/generate-icons.ps1") -OutDir $assets
if ($LASTEXITCODE -ne 0) { throw "icon generation failed" }

Step "4/8 msix layout"
$keep = Join-Path $env:TEMP "cb-layout-keep"
if (Test-Path $keep) { Remove-Item -Recurse -Force $keep }
New-Item -ItemType Directory -Path $keep | Out-Null
Copy-Item (Join-Path $layout "AppxManifest.xml") $keep
Copy-Item (Join-Path $layout "Assets") -Recurse $keep
Remove-Item -Recurse -Force $layout
New-Item -ItemType Directory -Path $layout | Out-Null
# robocopy exit codes 0-7 are success
robocopy $pub $layout /E /XF *.pdb /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy layout failed ($LASTEXITCODE)" }
Copy-Item (Join-Path $keep "AppxManifest.xml") $layout -Force
New-Item -ItemType Directory -Path (Join-Path $layout "Assets") -Force | Out-Null
Copy-Item (Join-Path $keep "Assets/*") (Join-Path $layout "Assets/") -Force

# Keep the manifest identity coherent with the signing cert and the csproj.
$manifestPath = Join-Path $layout "AppxManifest.xml"
$manifest = Get-Content $manifestPath -Raw
$manifest = $manifest -replace 'Publisher="[^"]*"', "Publisher=`"$publisher`""
$manifest = $manifest -replace '<PublisherDisplayName>[^<]*</PublisherDisplayName>',
    "<PublisherDisplayName>JS BlueFlutex</PublisherDisplayName>"
# Only <Identity>'s Version= -- MinVersion/MaxVersionTested must survive.
$manifest = [regex]::Replace($manifest,
    '(<Identity\b[^>]*?\bVersion=")[^"]*"', "`${1}$ver.0`"")
[System.IO.File]::WriteAllText($manifestPath, $manifest)
if ((Select-String -Path $manifestPath -Pattern ([regex]::Escape($publisher)) -Quiet) -ne $true) {
    throw "manifest publisher not set to $publisher"
}
$gotVer = ([regex]::Match((Get-Content $manifestPath -Raw), '<Identity\b[^>]*?\bVersion="([^"]*)"')).Groups[1].Value
if ($gotVer -ne "$ver.0") { throw "manifest version is $gotVer, expected $ver.0" }
Write-Host "manifest: $publisher / $gotVer"

Step "5/8 pack + sign msix"
$makeappx = Find-KitTool "makeappx.exe"
$signtool = Find-KitTool "signtool.exe"
& $makeappx pack /d $layout /p $msix /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }
& $signtool sign /f $pfx /fd SHA256 /a $msix | Out-Null
if ($LASTEXITCODE -ne 0) { throw "signtool sign failed" }
$sig = Get-AuthenticodeSignature $msix
if (-not $sig.SignerCertificate) { throw "msix has no signature" }
if ($sig.SignerCertificate.Subject -ne $publisher) {
    throw "msix signer is $($sig.SignerCertificate.Subject), expected $publisher"
}
Write-Host "msix signed by $($sig.SignerCertificate.Subject)"

Step "6/8 portable zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pub "*") -DestinationPath $zip -CompressionLevel Optimal

Step "7/8 msi"
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw "wix not found. Install with:  dotnet tool install --global wix"
}
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "tools/gen-wix-files.ps1") `
    -Src $pub -Out (Join-Path $msiSrc "files.wxs")
if ($LASTEXITCODE -ne 0) { throw "gen-wix-files failed" }
# product.wxs Version must match the csproj, otherwise the MSI is mislabelled.
# Match only <Package ... Version=...> -- a plain 'Version=' replace is
# case-insensitive in PowerShell and would also rewrite the XML declaration's
# 'version="1.0"', producing an invalid source file.
$prodPath = Join-Path $msiSrc "product.wxs"
$prod = Get-Content $prodPath -Raw
$prod = [regex]::Replace($prod, '(<Package\b[^>]*?\bVersion=")[^"]*"', "`${1}$ver`"")
[System.IO.File]::WriteAllText($prodPath, $prod)
& wix build (Join-Path $msiSrc "product.wxs") (Join-Path $msiSrc "files.wxs") `
    -arch x64 -out $msi
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

Step "8/8 verify"
foreach ($f in @($zip, $msix, $msi)) {
    if (-not (Test-Path $f)) { throw "missing artifact $f" }
    "{0,-46} {1,8:N1} MB" -f (Split-Path $f -Leaf), ((Get-Item $f).Length / 1MB)
}

if ($Upload) {
    Step "upload to release $tag"
    if (-not (git rev-parse "$tag" 2>$null)) { git tag "$tag" }
    & gh release upload $tag $zip $msix $msi --repo vikasdocker/CleanBoost --clobber
    if ($LASTEXITCODE -ne 0) { throw "gh upload failed" }
    Write-Host "https://github.com/vikasdocker/CleanBoost/releases/tag/$tag"
}

Write-Host ""
Write-Host "Release $ver complete." -ForegroundColor Green
