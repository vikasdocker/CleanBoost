# CleanBoost — screenshot driver (dev tool, not shipped).
#
#   powershell -ExecutionPolicy Bypass -File tools/capture-screenshots.ps1
#
# Drives the real app and writes PNGs to docs/screenshots/. Uses PrintWindow so
# nothing from other windows bleeds into the capture.
param(
    [string]$Exe = "$PSScriptRoot\..\publish\win-x64\CleanBoost.exe",
    [string]$OutDir = "$PSScriptRoot\..\docs\screenshots"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Shot {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int t, bool r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
}
"@

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

# Fixed size so every screenshot lines up in the README.
$W = 1400
$H = 880

function Capture([IntPtr]$hwnd, [string]$name) {
    $bmp = Grab $hwnd
    $path = Join-Path $OutDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $name.png"
}

function Grab([IntPtr]$hwnd) {
    $r = New-Object Shot+RECT
    [Shot]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    # 0x2 = PW_RENDERFULLCONTENT (needed for composited/WinUI surfaces)
    [Shot]::PrintWindow($hwnd, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    return $bmp
}

<#
Finds the y of the first *ticked* category checkbox by probing the checkbox
column for a saturated fill. Lets the script click a real, populated category
instead of guessing a row index -- row positions move with findings.

Note: WinUI draws the checked box in the *system* accent (blue), not the app's
olive accent, so the test is "is this pixel strongly coloured" rather than a
match against a specific hue.
#>
function Find-TickedRows([IntPtr]$hwnd) {
    $bmp = Grab $hwnd
    $rows = New-Object System.Collections.Generic.List[int]
    $inRow = $false
    for ($y = 375; $y -lt ($bmp.Height - 30); $y++) {
        $lit = $false
        for ($x = 378; $x -le 394; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $max = [Math]::Max($c.R, [Math]::Max($c.G, $c.B))
            $min = [Math]::Min($c.R, [Math]::Min($c.G, $c.B))
            if ($max -gt 90 -and ($max - $min) -gt 55) { $lit = $true; break }
        }
        if ($lit -and -not $inRow) { $rows.Add($y); $inRow = $true }
        elseif (-not $lit) { $inRow = $false }
    }
    $bmp.Dispose()
    return $rows
}

$proc = Start-Process -FilePath $Exe -PassThru -WorkingDirectory (Split-Path $Exe)
Start-Sleep -Seconds 9
$hwnd = $proc.MainWindowHandle
[Shot]::MoveWindow($hwnd, 60, 60, $W, $H, $true) | Out-Null
[Shot]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Seconds 2

# Nav coordinates are relative to the window origin, measured off the fixed 1400x880
# layout the script forces.
$nav = @{ Cleaner = 148; Booster = 188; History = 228; Services = 268 }
$btnScan = @(388, 268)
$btnSelectAll = @(388, 311)
$btnClear = @(507, 311)
$btnCleanSelected = @(480, 268)
$btnCleanAll = @(603, 268)
$btnTurbo = @(381, 243)
$btnConsent = @(371, 359)
$btnAbout = @(1364, 853)
# ContentDialog buttons move with the window width; these are the 1400px values.
$btnCleanAnyway = @(570, 500)

function Click([int]$bx, [int]$by, [int]$waitMs = 900) {
    $r = New-Object Shot+RECT
    [Shot]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    [Shot]::SetCursorPos(($r.Left + $bx), ($r.Top + $by)) | Out-Null
    Start-Sleep -Milliseconds 350
    [Shot]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [Shot]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds $waitMs
}

Write-Host "capturing..."

Capture $hwnd "cleaner-idle"

Click $btnScan[0] $btnScan[1] 1000
# A ~40k-item scan takes 40-60s on a warm cache; do not capture mid-scan.
Start-Sleep -Seconds 80
Capture $hwnd "cleaner-scan"

# Start a cleanup of one populated category. Picking a single non-ConfirmFirst
# category avoids the ConfirmFirst consent dialog entirely, which is conditional
# and would otherwise swallow the next click.
Click $btnSelectAll[0] $btnSelectAll[1]
$rows = @(Find-TickedRows $hwnd)
if ($rows.Count -eq 0) { throw "no populated category found - scan probably returned nothing" }
# Seeded junk lands in the first category (User temporary files), which is both the
# first populated row and large enough to still be running when we capture.
$rowY = $rows[0]
Write-Host "  $($rows.Count) populated rows; cleaning the first one (y=$rowY)"
Click $btnClear[0] $btnClear[1]
Click 386 $rowY                       # tick just that category
Click $btnCleanSelected[0] $btnCleanSelected[1] 900
Start-Sleep -Milliseconds 700
Capture $hwnd "cleaner-live"
Write-Host "  (letting the cleanup finish)"
Start-Sleep -Seconds 25
Capture $hwnd "cleaner-result"

Click 79 $nav.Booster
Capture $hwnd "booster-light"
Click $btnTurbo[0] $btnTurbo[1]
Click $btnConsent[0] $btnConsent[1]
Capture $hwnd "booster-turbo"

Click 79 $nav.History
Start-Sleep -Seconds 1
Capture $hwnd "history"

Click 79 $nav.Services
Start-Sleep -Seconds 3
Capture $hwnd "services"

Click 79 $nav.Cleaner
Click $btnAbout[0] $btnAbout[1]
Start-Sleep -Milliseconds 900
Capture $hwnd "about"

$proc.Kill()
$proc.WaitForExit(5000) | Out-Null
Write-Host "done -> $OutDir"