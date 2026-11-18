# dev stack এক কমান্ডে: Docker Desktop -> backing services (healthy পর্যন্ত) -> Ollama -> (ঐচ্ছিক) vLLM -> API + frontend আলাদা জানালায়।
# চালাও repo root বা যেকোনো জায়গা থেকে:
#   .\scripts\dev.ps1                  # Docker + Ollama নিজে তোলে, তারপর API + frontend
#   .\scripts\dev.ps1 -Vllm dbz-ai     # সাথে vLLM-ও (model id = scripts\vllm-models.json)
#   .\scripts\dev.ps1 -NoOllama        # Ollama লাগবে না
#   .\scripts\dev.ps1 -NoApps          # শুধু infra (Docker + Ollama + vLLM), API/frontend নয়
param(
    [string]$Vllm = '',
    [switch]$NoOllama,
    [switch]$NoApps
)

$root = Split-Path $PSScriptRoot -Parent

# Docker Desktop তোলা ও Ollama server ওঠার জন্য অপেক্ষার সীমা (সেকেন্ড)
$DockerStartTimeoutSec = 240
$OllamaStartTimeoutSec = 45
$PollIntervalSec = 3

function Wait-Until([scriptblock]$Test, [int]$TimeoutSec) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (& $Test) { Write-Host ''; return $true }
        Write-Host '.' -NoNewline
        Start-Sleep -Seconds $PollIntervalSec
    }
    Write-Host ''
    return $false
}

function Test-Docker {
    try { docker info *> $null; return ($LASTEXITCODE -eq 0) } catch { return $false }
}

# 127.0.0.1 ইচ্ছাকৃত: 'localhost' Windows PowerShell 5.1-এ প্রথম call-এ IPv6 (::1) fallback-এ ~2 সেকেন্ড নেয় — ছোট timeout-এ সবসময় ব্যর্থ মনে হয়।
function Test-Ollama {
    try { $null = Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/version' -TimeoutSec 5; return $true } catch { return $false }
}

# --- 1) Docker Desktop
Write-Host "[1/5] Docker Desktop..." -ForegroundColor Cyan
if (Test-Docker) {
    Write-Host "  already cholche."
}
else {
    $dockerExe = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (-not (Test-Path $dockerExe)) {
        Write-Host "Docker Desktop paoa jayni ($dockerExe). Install koro, tarpor abar chalao." -ForegroundColor Red
        exit 1
    }
    Write-Host "  Docker Desktop chalu korchi (1-2 minute lagte pare)" -NoNewline
    Start-Process $dockerExe
    if (-not (Wait-Until { Test-Docker } $DockerStartTimeoutSec)) {
        Write-Host "Docker $DockerStartTimeoutSec second-e ready holo na. Docker Desktop nije khule dekho." -ForegroundColor Red
        exit 1
    }
}

# --- 2) backing services
Write-Host "[2/5] backing services (sqlserver/redis/qdrant/otel dashboard)..." -ForegroundColor Cyan
docker compose -f "$root\docker-compose.yml" up -d --wait
if ($LASTEXITCODE -ne 0) {
    Write-Host "docker compose fail korlo. port 1433 error hole docs/troubleshooting.md dekho." -ForegroundColor Red
    exit 1
}

# --- 3) Ollama
if ($NoOllama) {
    Write-Host "[3/5] Ollama skip (-NoOllama)." -ForegroundColor DarkGray
}
else {
    Write-Host "[3/5] Ollama server..." -ForegroundColor Cyan
    if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
        Write-Host "  ollama install nei - skip korlam." -ForegroundColor Yellow
    }
    elseif (Test-Ollama) {
        Write-Host "  already cholche."
    }
    else {
        Write-Host "  chalu korchi" -NoNewline
        Start-Process ollama -ArgumentList 'serve' -WindowStyle Hidden
        if (Wait-Until { Test-Ollama } $OllamaStartTimeoutSec) {
            Write-Host "  Ollama ready."
        }
        else {
            Write-Host "  Ollama $OllamaStartTimeoutSec second-e uthlo na (API ollama chharaoi uthbe)." -ForegroundColor Yellow
        }
    }
}

# --- 4) vLLM (ঐচ্ছিক) — model load হতে কিছু মিনিট লাগে, তাই আলাদা জানালায়, এখানে আটকে থাকি না
if ($Vllm) {
    Write-Host "[4/5] vLLM '$Vllm' (notun janala, model load hote kichu minute)..." -ForegroundColor Cyan
    Start-Process powershell -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', "`"$root\scripts\start-vllm.ps1`"", '-Pick', $Vllm, '-NoPause'
}
else {
    Write-Host "[4/5] vLLM skip (chaile: .\scripts\dev.ps1 -Vllm dbz-ai)." -ForegroundColor DarkGray
}

# --- 5) API + frontend
if ($NoApps) {
    Write-Host "[5/5] API/frontend skip (-NoApps)." -ForegroundColor DarkGray
}
else {
    Write-Host "[5/5] API + frontend (notun janala)..." -ForegroundColor Cyan
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "dotnet run --project `"$root\backend\src\Api`""
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd `"$root\frontend`"; npm run dev"
}

Write-Host ""
Write-Host "Ready hole:" -ForegroundColor Green
Write-Host "  App:       http://localhost:5173"
Write-Host "  Swagger:   http://localhost:5076/swagger"
Write-Host "  Telemetry: http://localhost:18888"
if ($Vllm) { Write-Host "  vLLM:      http://localhost:8000/v1  (load shesh hole)" }
