# puro test suite ek command-e. E2E shoho chalate: .\scripts\test.ps1 -E2E
param([switch]$E2E)

$root = Split-Path $PSScriptRoot -Parent
$failed = $false

Write-Host "=== backend: unit + integration (Testcontainers) ===" -ForegroundColor Cyan
dotnet test "$root\backend\AiPlatform.slnx"
if ($LASTEXITCODE -ne 0) { $failed = $true }

Write-Host "=== frontend: lint + build ===" -ForegroundColor Cyan
Push-Location "$root\frontend"
npm run lint
if ($LASTEXITCODE -ne 0) { $failed = $true }
npm run build
if ($LASTEXITCODE -ne 0) { $failed = $true }

if ($E2E) {
    Write-Host "=== E2E (Playwright) ===" -ForegroundColor Cyan
    npm run e2e
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}
Pop-Location

if ($failed) { Write-Host "FAILED" -ForegroundColor Red; exit 1 }
Write-Host "ALL GREEN" -ForegroundColor Green
