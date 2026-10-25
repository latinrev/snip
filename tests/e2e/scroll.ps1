# Scrolling mode against a window with a sticky header and footer around a 960x6000 image.
# Stitching must reproduce the image pixel for pixel, with the header and footer once each.
. "$PSScriptRoot\common.ps1"

$headerH = 70; $footerH = 50; $imageH = 6000
$src = New-Object System.Drawing.Bitmap 960, $imageH
$g = [System.Drawing.Graphics]::FromImage($src)
$rng = New-Object System.Random 7
$font = New-Object System.Drawing.Font 'Segoe UI', 16
for ($y = 0; $y -lt $imageH; $y += 40) {
  $c = [System.Drawing.Color]::FromArgb(255, $rng.Next(150, 256), $rng.Next(150, 256), $rng.Next(150, 256))
  $g.FillRectangle((New-Object System.Drawing.SolidBrush $c), 0, $y, 960, 40)
  $g.FillRectangle([System.Drawing.Brushes]::DarkSlateBlue, $rng.Next(0, 900), $y + 8, $rng.Next(10, 60), 24)
  $g.DrawString("row $y", $font, [System.Drawing.Brushes]::Black, 10, $y + 8)
}
$g.Dispose()

$area = New-Object System.Drawing.Rectangle 200, 150, 1000, 900
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'; $form.StartPosition = 'Manual'; $form.TopMost = $true; $form.Bounds = $area
$header = New-Object System.Windows.Forms.Label
$header.Dock = 'Top'; $header.Height = $headerH; $header.Text = 'STICKY HEADER'; $header.Font = $font
$header.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 60); $header.ForeColor = [System.Drawing.Color]::White
$footer = New-Object System.Windows.Forms.Label
$footer.Dock = 'Bottom'; $footer.Height = $footerH; $footer.Text = 'STICKY FOOTER'; $footer.Font = $font
$footer.BackColor = [System.Drawing.Color]::DarkOrange
$panel = New-Object System.Windows.Forms.Panel
$panel.Dock = 'Fill'; $panel.AutoScroll = $true; $panel.BackColor = [System.Drawing.Color]::Gray
$pic = New-Object System.Windows.Forms.PictureBox
$pic.Location = New-Object System.Drawing.Point 0, 0; $pic.Size = New-Object System.Drawing.Size 960, $imageH; $pic.Image = $src
$panel.Controls.Add($pic)
$form.Controls.Add($panel); $form.Controls.Add($footer); $form.Controls.Add($header)
$form.Show(); Pump 1000

$mark = LogMark
try {
  WinShift 0x53
  Tap 0x09
  Drag $area.Left $area.Top $area.Right $area.Bottom
  $done = WaitForLog $mark 'scroll capture (\d+)x(\d+)' 120
  Check 'scrolling capture finishes' ($done -ne $null) "$done"
  Pump 1000
  $saved = @(SavedSince $mark) | Select-Object -Last 1
  Check 'result saved' ($saved -ne $null)
  if ($saved) {
    $res = New-Object System.Drawing.Bitmap $saved
    try {
      Check 'height is header + whole image + footer' ($res.Height -eq $headerH + $imageH + $footerH) "($($res.Height) px)"
      $bad = 0
      for ($y = 0; $y -lt [Math]::Min($imageH, $res.Height - $headerH); $y++) {
        foreach ($x in 5, 333, 640, 955) {
          if ($res.GetPixel($x, $headerH + $y).ToArgb() -ne $src.GetPixel($x, $y).ToArgb()) { $bad++; break }
        }
      }
      Check 'every image row matches the source' ($bad -eq 0) "($bad rows differ)"
      Check 'sticky header at the top' ($res.GetPixel(500, 30).ToArgb() -eq $header.BackColor.ToArgb())
      Check 'sticky footer at the bottom' ($res.GetPixel(500, $res.Height - 20).ToArgb() -eq $footer.BackColor.ToArgb())
    } finally { $res.Dispose() }
  }
} finally {
  $form.Close()
  Remove-Outputs (SavedSince $mark)
}
Complete
