<#
.SYNOPSIS
  Stops everything dev.ps1 started: API + frontend -> vLLM -> Ollama -> backing services (-> Docker Desktop).

.DESCRIPTION
  Never deletes data: backing services get only `docker compose stop` (containers and volumes stay, so the next dev.ps1 starts fast).
  To see what would be stopped first, use -DryRun (stops nothing, only lists).
  Only touches this repo's own processes (command line / path contains this repo); other projects' dotnet/node are left alone.

.EXAMPLE
  .\scripts\dev-stop.ps1                 # stops everything (Docker Desktop stays running)
  .\scripts\dev-stop.ps1 -DryRun         # list what would be stopped
  .\scripts\dev-stop.ps1 -KeepOllama     # keep Ollama running
  .\scripts\dev-stop.ps1 -KeepInfra      # keep SQL Server/Redis/Qdrant running
  .\scripts\dev-stop.ps1 -QuitDocker     # also quit Docker Desktop at the end (frees RAM)

.NOTES
  When launched from Explorer (right-click -> Run with PowerShell) it ends with "press any key to close the window";
  it does not in a terminal. Force it with -Pause, suppress it with -NoPause.
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$KeepOllama,
    [switch]$KeepInfra,
    [switch]$QuitDocker,
    [switch]$Pause,
    [switch]$NoPause
)

# 'Continue' is intentional: in Windows PowerShell 5.1 a native command's stderr wrongly becomes a terminating error under 'Stop'.
$ErrorActionPreference = 'Continue'
Set-StrictMode -Version Latest

$root = Split-Path $PSScriptRoot -Parent
# The default 10 s is not enough for vLLM/DB to stop (a forced kill gives exit 137) - allow more time.
$StopGraceSec = 30

function Test-LaunchedFromExplorer {
    try {
        $parentId = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
        return ((Get-Process -Id $parentId -ErrorAction Stop).Name -eq 'explorer')
    } catch {
        return $false
    }
}

function Wait-KeyIfNeeded {
    if ($NoPause) { return }
    if (-not ($Pause -or (Test-LaunchedFromExplorer))) { return }
    Write-Host ''
    Write-Host 'Done. Press any key to close this window...' -ForegroundColor Cyan
    # ReadKey hangs when input is redirected (piped/automated), so read a line instead.
    if ([Console]::IsInputRedirected) { $null = Read-Host; return }
    try { $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown') }
    catch { $null = Read-Host }
}

function Test-Docker {
    try { docker info *> $null; return ($LASTEXITCODE -eq 0) } catch { return $false }
}

# A process plus all its children (dotnet run -> Api.exe, npm -> node) together.
function Stop-Tree([int]$ProcessId, [string]$Label) {
    if ($DryRun) { Write-Host "  [dry-run] would stop: $Label (PID $ProcessId)"; return }
    taskkill /PID $ProcessId /T /F *> $null
    Write-Host "  Stopped: $Label (PID $ProcessId)"
}

# Processes (by name) whose CommandLine matches a pattern.
function Find-ByCommandLine([string]$Name, [string[]]$Like) {
    $found = @()
    foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name='$Name'")) {
        $cmd = "$($p.CommandLine)"
        foreach ($pattern in $Like) {
            if ($cmd -like $pattern) { $found += $p; break }
        }
    }
    return $found
}

function Stop-Apps {
    Write-Host '[1/5] API + frontend...' -ForegroundColor Cyan
    $any = $false

    # Windows opened by dev.ps1: powershell -NoExit -Command "dotnet run --project <root>\backend\src\Api" / "cd <root>\frontend; npm run dev"
    $windows = @()
    $windows += Find-ByCommandLine 'powershell.exe' @("*dotnet run --project*$root\backend\src\Api*", "*$root\frontend*npm run dev*")
    $windows += Find-ByCommandLine 'pwsh.exe' @("*dotnet run --project*$root\backend\src\Api*", "*$root\frontend*npm run dev*")
    foreach ($w in $windows) { Stop-Tree $w.ProcessId 'dev window (API/frontend)'; $any = $true }

    # This repo's API and vite started by hand / from Visual Studio (no dev window).
    foreach ($p in @(Get-Process -Name 'AiPlatform.Api' -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $p.Path } catch { }
        if ($path -and $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { Stop-Tree $p.Id 'AiPlatform.Api'; $any = $true }
    }
    foreach ($n in (Find-ByCommandLine 'node.exe' @("*$root\frontend\node_modules*"))) { Stop-Tree $n.ProcessId 'frontend (vite/node)'; $any = $true }

    if (-not $any) { Write-Host '  Not running.' }
}

function Stop-Vllm {
    Write-Host '[2/5] vLLM...' -ForegroundColor Cyan
    if (-not (Test-Docker)) { Write-Host '  Docker is not running, skipped.'; return }
    $names = @(docker ps --filter 'name=^/vllm-' --format '{{.Names}}' 2>$null | Where-Object { $_ })
    if ($names.Count -eq 0) { Write-Host '  Not running.'; return }
    foreach ($n in $names) {
        if ($DryRun) { Write-Host "  [dry-run] would stop: container $n"; continue }
        docker stop -t $StopGraceSec $n | Out-Null
        Write-Host "  Stopped: $n"
    }
}

function Stop-Ollama {
    Write-Host '[3/5] Ollama...' -ForegroundColor Cyan
    if ($KeepOllama) { Write-Host '  Kept running (-KeepOllama).'; return }
    # Only the server/runner (ollama.exe); the tray app ('ollama app') is left alone - it is the user's own.
    $procs = @(Get-Process -Name 'ollama' -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { Write-Host '  Not running.'; return }
    foreach ($p in $procs) { Stop-Tree $p.Id 'ollama server' }
}

function Stop-Infra {
    Write-Host '[4/5] backing services (sqlserver/redis/qdrant/otel)...' -ForegroundColor Cyan
    if ($KeepInfra) { Write-Host '  Kept running (-KeepInfra).'; return }
    if (-not (Test-Docker)) { Write-Host '  Docker is not running, skipped.'; return }
    if ($DryRun) { Write-Host '  [dry-run] would run: docker compose stop (data is kept)'; return }
    # --profile rerank: the optional reranker container is stopped too (a plain `stop` ignores profile services)
    docker compose -f "$root\docker-compose.yml" --profile rerank stop
    if ($LASTEXITCODE -ne 0) { Write-Host '  docker compose stop failed.' -ForegroundColor Yellow } else { Write-Host '  Stopped (data intact).' }
}

function Stop-DockerDesktop {
    Write-Host '[5/5] Docker Desktop...' -ForegroundColor Cyan
    if (-not $QuitDocker) { Write-Host '  Staying up (use -QuitDocker to quit it).'; return }
    $procs = @(Get-Process -Name 'Docker Desktop' -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { Write-Host '  Not running.'; return }
    if ($DryRun) { Write-Host '  [dry-run] would quit: Docker Desktop'; return }
    foreach ($p in $procs) { $null = $p.CloseMainWindow() }
    Write-Host '  Quit requested (takes a few seconds).'
}

$failed = $false
try {
    if ($DryRun) { Write-Host 'DRY-RUN: nothing will be stopped, listing only.' -ForegroundColor Yellow }
    Stop-Apps
    Stop-Vllm
    Stop-Ollama
    Stop-Infra
    Stop-DockerDesktop
    Write-Host ''
    Write-Host $(if ($DryRun) { 'List complete.' } else { 'Everything stopped. To start again: .\scripts\dev.ps1' }) -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    $failed = $true
}
finally {
    Wait-KeyIfNeeded
}
if ($failed) { exit 1 }
