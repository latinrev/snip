# Win+Shift+S in Screenshot mode: drag, Esc and Enter.
. "$PSScriptRoot\common.ps1"

$primary = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$grid = New-GridWindow $primary
$mark = LogMark
try {
  WinShift 0x53
  Check 'Win+Shift+S opens the overlay' (OverlayOpen)
  Drag 200 200 600 500
  Pump 1200
  Check 'overlay closes after a drag' (-not (OverlayOpen))
  $saved = @(SavedSince $mark)
  Check 'drag saves one PNG' ($saved.Count -eq 1) "($($saved.Count) saved)"
  if ($saved.Count -eq 1) { Check 'PNG is the dragged 400x300 area' ((ImageSize $saved[0]) -eq '400x300') (ImageSize $saved[0]) }
  $clip = [System.Windows.Forms.Clipboard]::GetImage()
  Check 'clipboard holds the same image' ($clip -and "$($clip.Width)x$($clip.Height)" -eq '400x300')

  Pump 5500
  $before = @(SavedSince $mark).Count
  WinShift 0x53
  Tap 0x1B
  Pump 500
  Check 'Esc closes the overlay' (-not (OverlayOpen))
  Check 'Esc saves nothing' (@(SavedSince $mark).Count -eq $before)

  Pump 5500
  MoveTo ($primary.X + $primary.Width / 2) ($primary.Y + $primary.Height / 2)
  WinShift 0x53
  Tap 0x0D
  Pump 1500
  $last = @(SavedSince $mark) | Select-Object -Last 1
  Check 'Enter captures the whole monitor' ((ImageSize $last) -eq "$($primary.Width)x$($primary.Height)") (ImageSize $last)
} finally {
  $grid.Close()
  Remove-Outputs (SavedSince $mark)
}
Complete
