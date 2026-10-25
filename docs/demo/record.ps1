# Records the README demo: stages a clean scene on the primary monitor, drives Snip through
# each feature with eased mouse movement while ffmpeg records, and saves stills of the key
# moments.
#
# Snip keeps its own windows out of every capture, so the preview and the history panel only
# show up in a build with that turned off. Point Snip's folders at an empty demo folder too,
# so the history panel shows only the demo's captures.
#
#   powershell -ExecutionPolicy Bypass -File docs\demo\record.ps1 -Ffmpeg C:\path\to\ffmpeg.exe -Out C:\demo-out
#
# -Setup stages the scene, saves one still and stops, for measuring coordinates.
param(
  [Parameter(Mandatory)] [string]$Ffmpeg,
  [Parameter(Mandatory)] [string]$Out,
  [string]$DemoShots = (Join-Path $env:USERPROFILE 'snip-demo\Screenshots'),
  [string]$DemoVideos = (Join-Path $env:USERPROFILE 'snip-demo\Screen Recordings'),
  [int]$OutputIndex = 0,
  [switch]$Setup
)
. "$PSScriptRoot\..\..\tests\e2e\common.ps1"
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class D {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@

New-Item -ItemType Directory -Force $Out, "$Out\stills" | Out-Null
$mon = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$clock = $null
function Event([string]$name) {
  $t = if ($clock) { $clock.Elapsed.TotalSeconds } else { 0 }
  Add-Content "$Out\events.txt" ("{0:0.00} {1}" -f $t, $name)
}

function Glide([int]$x, [int]$y, [int]$ms = 700) {
  $p = New-Object W+POINT; [W]::GetCursorPos([ref]$p) | Out-Null
  $steps = [Math]::Max(1, [int]($ms / 12))
  for ($i = 1; $i -le $steps; $i++) {
    $t = $i / $steps
    $e = if ($t -lt 0.5) { 4 * $t * $t * $t } else { 1 - [Math]::Pow(-2 * $t + 2, 3) / 2 }
    [W]::SetCursorPos([int]($p.X + ($x - $p.X) * $e), [int]($p.Y + ($y - $p.Y) * $e)) | Out-Null
    Start-Sleep -Milliseconds 12
  }
  Pump 30
}

function GlideDrag([int]$x0, [int]$y0, [int]$x1, [int]$y1, [int]$ms = 900) {
  Glide $x0 $y0
  Pump 250
  [W]::mouse_event(0x2, 0, 0, 0, [IntPtr]::Zero)
  Pump 80
  Glide $x1 $y1 $ms
  Pump 150
  [W]::mouse_event(0x4, 0, 0, 0, [IntPtr]::Zero)
  Pump 100
}

function Still([string]$name) {
  $bmp = New-Object System.Drawing.Bitmap $mon.Width, $mon.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($mon.Location, [System.Drawing.Point]::Empty, $mon.Size)
  $g.Dispose()
  $bmp.Save("$Out\stills\$name.png", [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
}

# A full-monitor backdrop hides the desktop and the taskbar.
$backdrop = New-Object System.Windows.Forms.Form
$backdrop.FormBorderStyle = 'None'; $backdrop.StartPosition = 'Manual'; $backdrop.TopMost = $true
$backdrop.ShowInTaskbar = $false; $backdrop.Bounds = $mon
$backdrop.Add_Paint({ param($s, $e)
  $r = New-Object System.Drawing.Rectangle 0, 0, $s.Width, $s.Height
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $r, ([System.Drawing.Color]::FromArgb(30, 27, 75)), ([System.Drawing.Color]::FromArgb(14, 116, 144)), 35
  $e.Graphics.FillRectangle($brush, $r)
  $brush.Dispose()
})
$backdrop.Show(); Pump 500

# The page in a chrome-less Edge window with a throwaway InPrivate profile, which also keeps
# Edge from signing in with the Windows account.
$edgeExe = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$profileDir = Join-Path $env:TEMP 'snip-demo-edge'
$page = 'file:///' + ((Join-Path $PSScriptRoot 'page.html') -replace '\\', '/')
Start-Process $edgeExe -ArgumentList "--user-data-dir=$profileDir", '--no-first-run', '--no-default-browser-check', '--inprivate', "--app=$page" | Out-Null
$win = [IntPtr]::Zero
for ($i = 0; $i -lt 40 -and $win -eq [IntPtr]::Zero; $i++) {
  Pump 500
  $p = Get-Process msedge -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like '*Orbit*' } | Select-Object -First 1
  if ($p) { $win = $p.MainWindowHandle }
}
if ($win -eq [IntPtr]::Zero) { throw 'The demo page did not open.' }
$ew = [int]($mon.Width * 0.72); $eh = [int]($mon.Height * 0.80)
$ex = $mon.X + [int](($mon.Width - $ew) / 2); $ey = $mon.Y + [int](($mon.Height - $eh) / 2) - [int]($mon.Height * 0.02)
[W]::SetWindowPos($win, [IntPtr]::new(-1), $ex, $ey, $ew, $eh, 0x40) | Out-Null
Pump 2500
$r = New-Object W+RECT; [W]::GetWindowRect($win, [ref]$r) | Out-Null
"edge $($r.Left),$($r.Top),$($r.Right),$($r.Bottom) monitor $mon" | Set-Content "$Out\layout.txt"

function PageTop { [D]::SetForegroundWindow($win) | Out-Null; Pump 200; Tap 0x24; Pump 800 }
function OverPage { [W]::SetCursorPos($r.Left + 300, $r.Top + 400) | Out-Null; Pump 50 }
function Rel([double]$fx, [double]$fy) { , @([int]($r.Left + ($r.Right - $r.Left) * $fx), [int]($r.Top + ($r.Bottom - $r.Top) * $fy)) }

$recorder = $null
try {
  [W]::SetCursorPos($mon.X + $mon.Width - 200, $mon.Y + $mon.Height - 200) | Out-Null
  if ($Setup) {
    Still 'setup'
    OverPage; Wheel -4; Pump 1200; Still 'setup-scrolled'
    return
  }

  # Fill the history with a few captures of the page before recording.
  PageTop
  foreach ($notches in 0, 5, 5, 6) {
    OverPage; Wheel (-$notches); Pump 900
    WinShift 0x53; Tap 0x0D; Pump 900
  }
  PageTop
  [W]::SetCursorPos($mon.X + $mon.Width - 260, $mon.Y + $mon.Height - 220) | Out-Null
  Pump 5500

  $psi = New-Object System.Diagnostics.ProcessStartInfo $Ffmpeg
  $psi.Arguments = "-y -hide_banner -loglevel error -f lavfi -i ddagrab=output_idx=${OutputIndex}:framerate=30:draw_mouse=1 -vf hwdownload,format=bgra,format=yuv420p -c:v libx264 -preset ultrafast -crf 12 `"$Out\screen.mkv`""
  $psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.CreateNoWindow = $true
  $recorder = [System.Diagnostics.Process]::Start($psi)
  Pump 1500
  $clock = [System.Diagnostics.Stopwatch]::StartNew()
  Event 'start'
  Pump 1200

  # Screenshot: drag over the headline and the stat cards.
  Event 'screenshot'
  WinShift 0x53
  $a = Rel 0.025 0.25; $b = Rel 0.86 0.64
  Glide $a[0] $a[1] 900
  Pump 300; Still 'overlay'
  GlideDrag $a[0] $a[1] $b[0] $b[1] 1100
  Pump 700; Still 'toast'
  Pump 2200

  # Scrolling: switch modes with the bar, then click the page.
  Event 'scrolling'
  WinShift 0x53
  $m = Rel 0.5 0.05
  Glide $m[0] ($mon.Y + 70) 800
  Tap 0x09; Pump 900; Still 'modes'
  $c = Rel 0.5 0.5
  Glide $c[0] $c[1] 800
  Pump 300
  $mark = LogMark
  Click
  Pump 2500; Still 'scrolling'
  $done = WaitForLog $mark 'scroll capture' 180
  Event 'scrolled'
  Pump 800; Still 'scroll-toast'
  Pump 2500
  $shot = Get-ChildItem $DemoShots -Filter *.png | Sort-Object LastWriteTime | Select-Object -Last 1
  Copy-Item $shot.FullName "$Out\scroll-result.png" -Force

  # Video: record the chart and click through its tabs.
  Event 'video'
  PageTop
  OverPage; Wheel -4; Pump 1200
  WinShift 0x53
  Tap 0x09; Pump 250; Tap 0x09; Pump 600
  $a = Rel 0.03 0.08; $b = Rel 0.97 0.95
  Glide $a[0] $a[1] 800
  GlideDrag $a[0] $a[1] $b[0] $b[1] 1000
  Pump 1500; Still 'recording'
  Event 'recording'
  foreach ($tab in (Rel 0.242 0.413), (Rel 0.29 0.413), (Rel 0.188 0.413)) {
    Glide $tab[0] $tab[1] 900
    Pump 250; Click; Pump 1600
  }
  $q = Rel 0.6 0.7
  Glide $q[0] $q[1] 1000
  Pump 2600
  WinShift 0x53
  Event 'stopped'
  Pump 1200; Still 'video-toast'
  Pump 2500
  $video = Get-ChildItem $DemoVideos -Filter *.mp4 | Sort-Object LastWriteTime | Select-Object -Last 1
  Copy-Item $video.FullName "$Out\snip-recording.mp4" -Force

  # History.
  Event 'history'
  [W]::SetCursorPos($mon.X + [int]($mon.Width / 2), $mon.Y + [int]($mon.Height / 2)) | Out-Null
  WinShift 0x41
  Pump 1200; Still 'history'
  Wheel -3; Pump 1200; Wheel 3; Pump 1000
  Tap 0x1B
  Pump 1500
  Event 'end'
} finally {
  if ($recorder) {
    $recorder.StandardInput.Write('q'); $recorder.StandardInput.Close()
    $recorder.WaitForExit(20000) | Out-Null
  }
  Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
    Where-Object { $_.CommandLine -like '*snip-demo-edge*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  $backdrop.Close()
}
