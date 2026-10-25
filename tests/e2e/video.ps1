# Win+Shift+S in Video mode: record the full monitor, click to zoom, stop with Win+Shift+S.
. "$PSScriptRoot\common.ps1"

$primary = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$grid = New-GridWindow $primary
$mark = LogMark
try {
  MoveTo ($primary.X + $primary.Width / 2) ($primary.Y + $primary.Height / 2)
  WinShift 0x53
  Tap 0x09; Tap 0x09
  Tap 0x0D
  Pump 1500
  $start = WaitForLog $mark 'overlay selected mode=Video'
  Check 'Tab twice then Enter starts a video' ($start -ne $null)
  MoveTo ($primary.X + 600) ($primary.Y + 400); Click
  Pump 2500
  WinShift 0x53
  $stop = WaitForLog $mark 'recording stop frames=(\d+) duration=([\d.]+)s fps=([\d.]+)'
  Check 'Win+Shift+S stops the recording' ($stop -ne $null) "$stop"
  if ($stop) {
    $fps = [double]::Parse($stop.Matches[0].Groups[3].Value, [Globalization.CultureInfo]::InvariantCulture)
    Check 'records at least 20 fps' ($fps -ge 20) "($fps fps)"
  }
  $mp4 = @(RecordingsSince $mark) | Select-Object -Last 1
  Check 'MP4 file written' ($mp4 -and (Test-Path -LiteralPath $mp4) -and (Get-Item -LiteralPath $mp4).Length -gt 10000) "$mp4"
  $files = [System.Windows.Forms.Clipboard]::GetFileDropList()
  Check 'clipboard holds the MP4 as a file' ($files.Count -eq 1 -and $files[0] -eq $mp4)
} finally {
  $grid.Close()
  Pump 500
  Remove-Outputs (RecordingsSince $mark)
}
Complete
