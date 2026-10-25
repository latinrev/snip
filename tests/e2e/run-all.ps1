# Runs every end-to-end script in turn and reports which ones failed.
$failed = @()
foreach ($name in 'screenshot', 'history', 'video', 'scroll', 'scroll-browser') {
  Write-Host "== $name" -ForegroundColor Cyan
  & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "$name.ps1")
  if ($LASTEXITCODE -ne 0) { $failed += $name }
}
if ($failed) { Write-Host "Failed: $($failed -join ', ')" -ForegroundColor Red; exit 1 }
Write-Host 'All end-to-end scripts passed' -ForegroundColor Green
