# Generates packaging/msi/files.wxs from a published (self-contained) app folder.
# Native-Windows port of tools/gen-wix-files (bash) so Source= paths come out as
# C:\... instead of /mnt/c/... (WiX under Windows cannot resolve /mnt/c).
# Usage: powershell -File tools/gen-wix-files.ps1 -Src publish/win-x64 -Out packaging/msi/files.wxs
param(
    [Parameter(Mandatory = $true)][string]$Src,
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = "Stop"
$Src = (Resolve-Path $Src).Path
if (-not (Test-Path $Src)) { throw "publish dir not found: $Src" }

function XmlEscape([string]$s) {
    return $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="ProductComponents">')

# One component per file: WiX refuses Guid="*" when a component holds several
# files and a non-keypath file is versioned (WIX0368). One-file components keep
# auto-GUIDs legal and give per-file refcounting for free.
function Emit-Component([string]$dir, [string]$did) {
    $scan = if ([string]::IsNullOrEmpty($dir)) { $Src } else { Join-Path $Src $dir }
    $files = @(Get-ChildItem -LiteralPath $scan -File |
               Where-Object { $_.Extension -ne '.pdb' } |
               Sort-Object Name)
    if ($files.Count -eq 0) { return }

    for ($i = 0; $i -lt $files.Count; $i++) {
        $f = $files[$i]
        $cid = "cmp_${did}_$i"
        [void]$sb.AppendLine("      <Component Id=`"$cid`" Directory=`"$did`" Guid=`"*`">")
        [void]$sb.AppendLine("        <File Source=`"$(XmlEscape $f.FullName)`" KeyPath=`"yes`" />")
        [void]$sb.AppendLine('      </Component>')
    }
}

Emit-Component "" "INSTALLFOLDER"

$dirs = @(Get-ChildItem -LiteralPath $Src -Directory | Sort-Object Name)
foreach ($d in $dirs) {
    $safe = ($d.Name -replace '[^A-Za-z0-9_]', '_')
    Emit-Component $d.Name "DIR_$safe"
}

[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')
foreach ($d in $dirs) {
    $safe = ($d.Name -replace '[^A-Za-z0-9_]', '_')
    [void]$sb.AppendLine("      <Directory Id=`"DIR_$safe`" Name=`"$(XmlEscape $d.Name)`" />")
}
[void]$sb.AppendLine('    </DirectoryRef>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

$outPath = [System.IO.Path]::GetFullPath($Out)
$enc = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($outPath, $sb.ToString(), $enc)

$components = ([regex]::Matches($sb.ToString(), '<Component ')).Count
Write-Host "generated $outPath ($components components)"
