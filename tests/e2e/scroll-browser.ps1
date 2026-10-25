# Scrolling mode against a real browser: page.html in a throwaway Edge InPrivate window.
# The page has a sticky nav, a fixed chat button, smooth scrolling, fade-in content and a
# long blank stretch. Prints the result path so you can look at the stitched image.
. "$PSScriptRoot\common.ps1"

$edgeExe = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edgeExe)) { throw 'Microsoft Edge is required for this script.' }
$profileDir = Join-Path $env:TEMP 'snip-e2e-edge'
$page = 'file:///' + ((Join-Path $PSScriptRoot 'page.html') -replace '\\', '/')
Start-Process $edgeExe -ArgumentList "--user-data-dir=$profileDir", '--no-first-run', '--no-default-browser-check', '--inprivate', '--new-window', '--window-position=100,100', '--window-size=1100,1000', $page | Out-Null

$mark = LogMark
try {
  $win = [IntPtr]::Zero
  for ($i = 0; $i -lt 40 -and $win -eq [IntPtr]::Zero; $i++) {
    Pump 500
    $p = Get-Process msedge -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like '*Snip scroll test*' } | Select-Object -First 1
    if ($p) { $win = $p.MainWindowHandle }
  }
  Check 'test page opened in Edge' ($win -ne [IntPtr]::Zero)
  [W]::SetWindowPos($win, [IntPtr]::new(-1), 0, 0, 0, 0, 0x3) | Out-Null
  Pump 2000
  $r = New-Object W+RECT; [W]::GetWindowRect($win, [ref]$r) | Out-Null

  WinShift 0x53
  Tap 0x09
  MoveTo (($r.Left + $r.Right) / 2) (($r.Top + $r.Bottom) / 2)
  Click
  $done = WaitForLog $mark 'scroll capture (\d+)x(\d+) from (\d+) frames(.*)$' 180
  Check 'scrolling capture finishes' ($done -ne $null) "$done"
  if ($done) {
    $h = [int]$done.Matches[0].Groups[2].Value
    $note = $done.Matches[0].Groups[4].Value
    Check 'reaches the end of the page without losing track' ($note -notmatch 'lost|backwards|blank') $note
    Check 'result is many screens tall' ($h -gt ($r.Bottom - $r.Top) * 8) "($h px)"
  }
  Pump 1000
  $saved = @(SavedSince $mark) | Select-Object -Last 1
  if ($saved) {
    $keep = Join-Path $env:TEMP 'snip-e2e-browser.png'
    Copy-Item -LiteralPath $saved $keep -Force
    Write-Host "Stitched page: $keep"
  }
} finally {
  Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
    Where-Object { $_.CommandLine -like '*snip-e2e-edge*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  Remove-Outputs (SavedSince $mark)
}
Complete
