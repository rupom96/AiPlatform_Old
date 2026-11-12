# dev stack এক কমান্ডে: backing services (healthy পর্যন্ত) → API + frontend আলাদা জানালায়।
# চালাও repo root বা যেকোনো জায়গা থেকে: .\scripts\dev.ps1
$root = Split-Path $PSScriptRoot -Parent

Write-Host "[1/3] backing services (sqlserver/redis/qdrant/otel dashboard)..." -ForegroundColor Cyan
docker compose -f "$root\docker-compose.yml" up -d --wait
if ($LASTEXITCODE -ne 0) {
    Write-Host "docker compose fail korlo. Docker Desktop cholche? port 1433 error hole docs/troubleshooting.md dekho." -ForegroundColor Red
    exit 1
}

Write-Host "[2/3] API (notun janala)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "dotnet run --project `"$root\backend\src\Api`""

Write-Host "[3/3] frontend (notun janala)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd `"$root\frontend`"; npm run dev"

Write-Host ""
Write-Host "Ready hole:" -ForegroundColor Green
Write-Host "  App:       http://localhost:5173"
Write-Host "  Swagger:   http://localhost:5076/swagger"
Write-Host "  Telemetry: http://localhost:18888"
