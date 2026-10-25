# Win+Shift+A: the 24-hour history panel, its paging, Esc, and picking a screenshot.
. "$PSScriptRoot\common.ps1"

$now = Get-Date
$made = @()
New-Item -ItemType Directory -Force $ScreenshotsDir | Out-Null
for ($i = 0; $i -lt 30; $i++) {
  $bmp = New-Object System.Drawing.Bitmap (300 + $i), 200
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::FromArgb(40 + $i * 6, 90, 200 - $i * 5))
  $g.DrawString("e2e $i", (New-Object System.Drawing.Font 'Segoe UI', 40), [System.Drawing.Brushes]::White, 20, 50)
  $g.Dispose()
  $path = Join-Path $ScreenshotsDir ('snip-e2e-{0:D2}.png' -f $i)
  $bmp.Save($path); $bmp.Dispose(); $made += $path
  (Get-Item $path).LastWriteTime = $now.AddMinutes(-40 * $i)
}
$old = Join-Path $ScreenshotsDir 'snip-e2e-old.png'
$bmp = New-Object System.Drawing.Bitmap 999, 99; $bmp.Save($old); $bmp.Dispose(); $made += $old
(Get-Item $old).LastWriteTime = $now.AddHours(-25)
$expected = @(Get-ChildItem $ScreenshotsDir -Filter *.png | Where-Object { $_.LastWriteTime -ge $now.AddHours(-24) }).Count

$primary = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$grid = New-GridWindow $primary
$grid.Activate(); Pump 500
$mark = LogMark
try {
  MoveTo ($primary.X + $primary.Width / 2) ($primary.Y + $primary.Height / 2)
  WinShift 0x41
  Check 'Win+Shift+A opens the panel' (HistoryOpen)
  $open = WaitForLog $mark 'history open: (\d+) screenshots'
  Check 'panel lists every PNG from the last 24 hours, not older' ($open -and [int]$open.Matches[0].Groups[1].Value -eq $expected) "(expected $expected, log: $open)"

  Wheel -40; Pump 1500
  $pages = @(LogSince $mark | Select-String 'history page: showing (\d+)/')
  $shown = if ($pages) { [int]$pages[-1].Matches[0].Groups[1].Value } else { 0 }
  Check 'scrolling loads more cards' ($shown -gt 12) "(showing $shown)"

  Tap 0x1B; Pump 500
  Check 'Esc closes the panel' (-not (HistoryOpen))

  WinShift 0x41
  Tap 0x28; Tap 0x28; Tap 0x0D
  Pump 800
  $pick = WaitForLog $mark 'history pick index=\d+ name=(.+)$'
  Check 'Enter picks a screenshot' ($pick -ne $null)
  if ($pick) {
    $file = Join-Path $ScreenshotsDir $pick.Matches[0].Groups[1].Value
    $clip = [System.Windows.Forms.Clipboard]::GetImage()
    Check 'clipboard holds the picked image' ($clip -and "$($clip.Width)x$($clip.Height)" -eq (ImageSize $file))
  }
  Check 'panel closes after a pick' (-not (HistoryOpen))

  WinShift 0x41
  MoveTo ($primary.X + 40) ($primary.Y + 40); Click; Pump 600
  Check 'clicking outside closes the panel' (-not (HistoryOpen))
} finally {
  $grid.Close()
  Remove-Outputs $made
}
Complete
