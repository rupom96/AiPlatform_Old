# One-command dev stack: Docker Desktop -> backing services (until healthy) -> Ollama -> (optional) vLLM -> API + frontend in separate windows.
# Run from the repo root or anywhere:
#   .\scripts\dev.ps1                  # starts Docker + Ollama itself, then API + frontend
#   .\scripts\dev.ps1 -Vllm dbz-ai     # also vLLM (model id = scripts\vllm-models.json)
#   .\scripts\dev.ps1 -NoOllama        # skip Ollama
#   .\scripts\dev.ps1 -NoApps          # infra only (Docker + Ollama + vLLM), no API/frontend
#   .\scripts\dev.ps1 -NoRerank        # skip the reranker container
#   .\scripts\dev.ps1 -Rerank          # force-start the reranker even if no NVIDIA GPU is detected (it will most likely fail)
# The reranker (bge-reranker-v2-m3, for the Chat "Rerank" modes) starts automatically when an NVIDIA GPU is present;
# on machines without one it is skipped silently. dev-stop.ps1 always stops it.
param(
    [string]$Vllm = '',
    [switch]$NoOllama,
    [switch]$NoApps,
    [switch]$Rerank,
    [switch]$NoRerank
)

if ($Rerank -and $NoRerank) {
    Write-Host "-Rerank and -NoRerank contradict each other - use one." -ForegroundColor Red
    exit 1
}

$root = Split-Path $PSScriptRoot -Parent

# Wait limits (seconds) for Docker Desktop and the Ollama server to come up.
$DockerStartTimeoutSec = 240
$OllamaStartTimeoutSec = 45
$RerankStartTimeoutSec = 90
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

# 127.0.0.1 is intentional: in Windows PowerShell 5.1 'localhost' takes ~2 s on the first call (IPv6 ::1 fallback), so a short timeout always looks like a failure.
function Test-Ollama {
    try { $null = Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/version' -TimeoutSec 5; return $true } catch { return $false }
}

# The reranker container needs an NVIDIA GPU (Docker --gpus). nvidia-smi ships with the driver; no driver = no GPU for containers.
function Test-NvidiaGpu {
    if (-not (Get-Command nvidia-smi -ErrorAction SilentlyContinue)) { return $false }
    try { nvidia-smi -L *> $null; return ($LASTEXITCODE -eq 0) } catch { return $false }
}

# --- 1) Docker Desktop
Write-Host "[1/5] Docker Desktop..." -ForegroundColor Cyan
if (Test-Docker) {
    Write-Host "  Already running."
}
else {
    $dockerExe = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (-not (Test-Path $dockerExe)) {
        Write-Host "Docker Desktop not found ($dockerExe). Install it, then run this again." -ForegroundColor Red
        exit 1
    }
    Write-Host "  Starting Docker Desktop (can take 1-2 minutes)" -NoNewline
    Start-Process $dockerExe
    if (-not (Wait-Until { Test-Docker } $DockerStartTimeoutSec)) {
        Write-Host "Docker was not ready after $DockerStartTimeoutSec seconds. Open Docker Desktop manually and check." -ForegroundColor Red
        exit 1
    }
}

# --- 2) backing services
Write-Host "[2/5] backing services (sqlserver/redis/qdrant/otel dashboard)..." -ForegroundColor Cyan
docker compose -f "$root\docker-compose.yml" up -d --wait
if ($LASTEXITCODE -ne 0) {
    Write-Host "docker compose failed. For a port 1433 error see docs/troubleshooting.md." -ForegroundColor Red
    exit 1
}

# --- 2b) Reranker (compose profile "rerank"): automatic when an NVIDIA GPU is present, -NoRerank to skip
if ($NoRerank) {
    Write-Host "[2b] reranker skipped (-NoRerank)." -ForegroundColor DarkGray
}
elseif (-not ($Rerank -or (Test-NvidiaGpu))) {
    Write-Host "[2b] reranker skipped (no NVIDIA GPU detected; -Rerank forces it)." -ForegroundColor DarkGray
}
else {
    Write-Host "[2b] reranker (bge-reranker-v2-m3)..." -ForegroundColor Cyan
    docker compose -f "$root\docker-compose.yml" --profile rerank up -d reranker
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  reranker failed to start (see the reranker block in docker-compose.yml). Continuing without it." -ForegroundColor Yellow
    }
    else {
        # Short wait only: once the model is cached it is ready in ~30 s. The very first run downloads ~2.2 GB (~5 min) -
        # do not block the whole dev stack for that; it keeps loading in the background and the Chat UI warns while it is not ready.
        Write-Host "  waiting for it" -NoNewline
        $ready = Wait-Until { try { $null = Invoke-RestMethod -Uri 'http://127.0.0.1:8082/health' -TimeoutSec 3; $true } catch { $false } } $RerankStartTimeoutSec
        if ($ready) { Write-Host "  Reranker ready." }
        else { Write-Host "  Still loading in the background (the first run downloads ~2.2 GB) - Rerank modes work once it is ready. Check: docker logs aiplatform-reranker" -ForegroundColor Yellow }
    }
}

# --- 3) Ollama
if ($NoOllama) {
    Write-Host "[3/5] Ollama skipped (-NoOllama)." -ForegroundColor DarkGray
}
else {
    Write-Host "[3/5] Ollama server..." -ForegroundColor Cyan
    if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
        Write-Host "  ollama is not installed - skipped." -ForegroundColor Yellow
    }
    elseif (Test-Ollama) {
        Write-Host "  Already running."
    }
    else {
        Write-Host "  Starting" -NoNewline
        Start-Process ollama -ArgumentList 'serve' -WindowStyle Hidden
        if (Wait-Until { Test-Ollama } $OllamaStartTimeoutSec) {
            Write-Host "  Ollama ready."
        }
        else {
            Write-Host "  Ollama did not come up within $OllamaStartTimeoutSec seconds (the API still starts without it)." -ForegroundColor Yellow
        }
    }
}

# --- 4) vLLM (optional) - the model takes a few minutes to load, so it gets its own window and we do not wait here
if ($Vllm) {
    Write-Host "[4/5] vLLM '$Vllm' (new window, model takes a few minutes to load)..." -ForegroundColor Cyan
    Start-Process powershell -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', "`"$root\scripts\start-vllm.ps1`"", '-Pick', $Vllm, '-NoPause'
}
else {
    Write-Host "[4/5] vLLM skipped (to include it: .\scripts\dev.ps1 -Vllm dbz-ai)." -ForegroundColor DarkGray
}

# --- 5) API + frontend
if ($NoApps) {
    Write-Host "[5/5] API/frontend skipped (-NoApps)." -ForegroundColor DarkGray
}
else {
    Write-Host "[5/5] API + frontend (new windows)..." -ForegroundColor Cyan
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "dotnet run --project `"$root\backend\src\Api`""
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd `"$root\frontend`"; npm run dev"
}

Write-Host ""
Write-Host "When ready:" -ForegroundColor Green
Write-Host "  App:       http://localhost:5173"
Write-Host "  Swagger:   http://localhost:5076/swagger"
Write-Host "  Telemetry: http://localhost:18888"
if ($Vllm) { Write-Host "  vLLM:      http://localhost:8000/v1  (after it finishes loading)" }
