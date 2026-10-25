# Shared helpers for Snip's end-to-end scripts. Each script dot-sources this file.
#
# These scripts drive the real desktop: they press keys, move the mouse and read the
# clipboard. Run them in an interactive session on a machine you are not using, with Snip
# already running.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extra);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  public struct POINT { public int X, Y; }
  public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
# Physical pixels, the same coordinates Snip works in.
[W]::SetProcessDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null

$script:ScreenshotsDir = [Environment]::GetFolderPath('MyPictures') + '\Screenshots'
$script:RecordingsDir = [Environment]::GetFolderPath('MyVideos') + '\Screen Recordings'
$script:SnipLogPath = Join-Path $env:LOCALAPPDATA 'Snip\snip.log'
$script:Failures = 0

if (-not (Get-Process Snip -ErrorAction SilentlyContinue)) { throw 'Start Snip before running the end-to-end scripts.' }

function Pump([int]$ms) {
  $end = (Get-Date).AddMilliseconds($ms)
  while ((Get-Date) -lt $end) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 }
}

function Tap([byte]$vk) { [W]::keybd_event($vk, 0, 0, [IntPtr]::Zero); [W]::keybd_event($vk, 0, 2, [IntPtr]::Zero); Pump 100 }

function WinShift([byte]$vk) {
  [W]::keybd_event(0x5B, 0, 0, [IntPtr]::Zero); [W]::keybd_event(0x10, 0, 0, [IntPtr]::Zero)
  [W]::keybd_event($vk, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 60
  [W]::keybd_event($vk, 0, 2, [IntPtr]::Zero); [W]::keybd_event(0x10, 0, 2, [IntPtr]::Zero); [W]::keybd_event(0x5B, 0, 2, [IntPtr]::Zero)
  Pump 1000
}

function MoveTo([int]$x, [int]$y) { [W]::SetCursorPos($x, $y) | Out-Null; Pump 50 }

function Click { [W]::mouse_event(0x2, 0, 0, 0, [IntPtr]::Zero); Pump 60; [W]::mouse_event(0x4, 0, 0, 0, [IntPtr]::Zero); Pump 100 }

function Drag([int]$x0, [int]$y0, [int]$x1, [int]$y1) {
  MoveTo $x0 $y0
  [W]::mouse_event(0x2, 0, 0, 0, [IntPtr]::Zero)
  for ($i = 1; $i -le 10; $i++) { MoveTo ([int]($x0 + ($x1 - $x0) * $i / 10)) ([int]($y0 + ($y1 - $y0) * $i / 10)) }
  [W]::mouse_event(0x4, 0, 0, 0, [IntPtr]::Zero)
}

function Wheel([int]$notches) { for ($i = 0; $i -lt [Math]::Abs($notches); $i++) { [W]::mouse_event(0x800, 0, 0, [Math]::Sign($notches) * 120, [IntPtr]::Zero); Pump 40 } }

function OverlayOpen { [W]::FindWindow([NullString]::Value, 'Snip') -ne [IntPtr]::Zero }
function HistoryOpen { [W]::FindWindow([NullString]::Value, 'Snip history') -ne [IntPtr]::Zero }

# Snip's log, from a mark taken with LogMark, so earlier sessions don't count.
function LogMark { if (Test-Path $SnipLogPath) { @(Get-Content $SnipLogPath -Encoding UTF8).Count } else { 0 } }
function LogSince([int]$mark) { @(Get-Content $SnipLogPath -Encoding UTF8 | Select-Object -Skip $mark) }
function WaitForLog([int]$mark, [string]$pattern, [int]$seconds = 30) {
  $deadline = (Get-Date).AddSeconds($seconds)
  while ((Get-Date) -lt $deadline) {
    $hit = LogSince $mark | Select-String $pattern | Select-Object -Last 1
    if ($hit) { return $hit }
    Pump 250
  }
  return $null
}
function SavedSince([int]$mark) {
  LogSince $mark | Select-String 'saved \d+x\d+ (.+\.png)$' | ForEach-Object { $_.Matches[0].Groups[1].Value }
}
function RecordingsSince([int]$mark) {
  LogSince $mark | Select-String 'recording start .* path=(.+\.mp4)$' | ForEach-Object { $_.Matches[0].Groups[1].Value }
}

function ImageSize([string]$path) {
  $img = [System.Drawing.Image]::FromFile($path)
  try { "$($img.Width)x$($img.Height)" } finally { $img.Dispose() }
}

function Check([string]$name, [bool]$ok, [string]$detail = '') {
  if ($ok) { Write-Host "PASS  $name $detail" -ForegroundColor Green }
  else { Write-Host "FAIL  $name $detail" -ForegroundColor Red; $script:Failures++ }
}

function Remove-Outputs([string[]]$paths) {
  foreach ($p in $paths) { if ($p -and (Test-Path -LiteralPath $p)) { Remove-Item -LiteralPath $p } }
}

function Complete {
  if ($script:Failures -gt 0) { Write-Host "$script:Failures check(s) failed" -ForegroundColor Red; exit 1 }
  Write-Host 'All checks passed' -ForegroundColor Green
}

# A borderless, topmost window filled with a labelled grid, so captures have known content
# and test clicks land on nothing that matters.
function New-GridWindow([System.Drawing.Rectangle]$bounds) {
  $form = New-Object System.Windows.Forms.Form
  $form.FormBorderStyle = 'None'; $form.StartPosition = 'Manual'; $form.TopMost = $true
  $form.Bounds = $bounds; $form.BackColor = [System.Drawing.Color]::White
  $form.Add_Paint({ param($s, $e)
    $g = $e.Graphics
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::LightGray), 1
    $font = New-Object System.Drawing.Font 'Segoe UI', 14
    for ($x = 0; $x -lt $s.Width; $x += 100) { $g.DrawLine($pen, $x, 0, $x, $s.Height) }
    for ($y = 0; $y -lt $s.Height; $y += 100) { $g.DrawLine($pen, 0, $y, $s.Width, $y) }
    for ($x = 0; $x -lt $s.Width; $x += 200) { for ($y = 0; $y -lt $s.Height; $y += 200) { $g.DrawString("$x,$y", $font, [System.Drawing.Brushes]::Black, $x + 4, $y + 4) } }
  })
  $form.Show(); Pump 800
  return $form
}
