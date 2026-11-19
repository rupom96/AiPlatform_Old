<#
.SYNOPSIS
  dev.ps1 যা যা তুলেছে তা বন্ধ করে: API + frontend -> vLLM -> Ollama -> backing services (-> Docker Desktop)।

.DESCRIPTION
  ডেটা মোছে না: backing services শুধু `docker compose stop` (container ও volume থাকে, পরের dev.ps1 দ্রুত ওঠে)।
  কী কী থামাবে আগে দেখতে: -DryRun (কিছু থামায় না, শুধু তালিকা দেখায়)।
  শুধু repo-র নিজের process ধরে (কমান্ডে/পথে এই repo-র নাম থাকলে); অন্য প্রোজেক্টের dotnet/node ছোঁয় না।

.EXAMPLE
  .\scripts\dev-stop.ps1                 # সব থামায় (Docker Desktop চালু থাকে)
  .\scripts\dev-stop.ps1 -DryRun         # কী থামত তার তালিকা
  .\scripts\dev-stop.ps1 -KeepOllama     # Ollama চালু রাখে
  .\scripts\dev-stop.ps1 -KeepInfra      # SQL Server/Redis/Qdrant চালু রাখে
  .\scripts\dev-stop.ps1 -QuitDocker     # শেষে Docker Desktop-ও বন্ধ (RAM ফেরত আসে)

.NOTES
  Explorer থেকে (right-click -> Run with PowerShell) চালালে শেষে "যেকোনো key চাপলে window বন্ধ হবে" দেখায়;
  terminal-এ দেখায় না। জোর করতে -Pause, বন্ধ রাখতে -NoPause।
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

# 'Continue' ইচ্ছাকৃত: Windows PowerShell 5.1-এ native command-এর stderr 'Stop' থাকলে ভুল করে terminating error হয়।
$ErrorActionPreference = 'Continue'
Set-StrictMode -Version Latest

$root = Split-Path $PSScriptRoot -Parent
# docker stop-এ vLLM/DB বন্ধ হতে ডিফল্ট ১০ সেকেন্ডে কুলায় না (জোর করে মারলে exit 137) — বেশি সময় দিই
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
    Write-Host 'কাজ শেষ। যেকোনো key চাপলে window বন্ধ হবে...' -ForegroundColor Cyan
    # input redirect (পাইপ/স্বয়ংক্রিয়) হলে ReadKey আটকে যায়, তাই তখন Enter পড়ি
    if ([Console]::IsInputRedirected) { $null = Read-Host; return }
    try { $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown') }
    catch { $null = Read-Host }
}

function Test-Docker {
    try { docker info *> $null; return ($LASTEXITCODE -eq 0) } catch { return $false }
}

# process + তার সব child (dotnet run -> Api.exe, npm -> node) একসাথে
function Stop-Tree([int]$ProcessId, [string]$Label) {
    if ($DryRun) { Write-Host "  [dry-run] থামত: $Label (PID $ProcessId)"; return }
    taskkill /PID $ProcessId /T /F *> $null
    Write-Host "  থামানো হয়েছে: $Label (PID $ProcessId)"
}

# CommandLine-এ pattern মেলে এমন process (নাম ধরে)
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

    # dev.ps1-র খোলা জানালা: powershell -NoExit -Command "dotnet run --project <root>\backend\src\Api" / "cd <root>\frontend; npm run dev"
    $windows = @()
    $windows += Find-ByCommandLine 'powershell.exe' @("*dotnet run --project*$root\backend\src\Api*", "*$root\frontend*npm run dev*")
    $windows += Find-ByCommandLine 'pwsh.exe' @("*dotnet run --project*$root\backend\src\Api*", "*$root\frontend*npm run dev*")
    foreach ($w in $windows) { Stop-Tree $w.ProcessId 'dev জানালা (API/frontend)'; $any = $true }

    # হাতে/Visual Studio থেকে চালানো এই repo-র API ও vite (জানালা ছাড়া)
    foreach ($p in @(Get-Process -Name 'AiPlatform.Api' -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $p.Path } catch { }
        if ($path -and $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { Stop-Tree $p.Id 'AiPlatform.Api'; $any = $true }
    }
    foreach ($n in (Find-ByCommandLine 'node.exe' @("*$root\frontend\node_modules*"))) { Stop-Tree $n.ProcessId 'frontend (vite/node)'; $any = $true }

    if (-not $any) { Write-Host '  চলছে না।' }
}

function Stop-Vllm {
    Write-Host '[2/5] vLLM...' -ForegroundColor Cyan
    if (-not (Test-Docker)) { Write-Host '  Docker চলছে না, বাদ।'; return }
    $names = @(docker ps --filter 'name=^/vllm-' --format '{{.Names}}' 2>$null | Where-Object { $_ })
    if ($names.Count -eq 0) { Write-Host '  চলছে না।'; return }
    foreach ($n in $names) {
        if ($DryRun) { Write-Host "  [dry-run] থামত: container $n"; continue }
        docker stop -t $StopGraceSec $n | Out-Null
        Write-Host "  থামানো হয়েছে: $n"
    }
}

function Stop-Ollama {
    Write-Host '[3/5] Ollama...' -ForegroundColor Cyan
    if ($KeepOllama) { Write-Host '  রাখা হলো (-KeepOllama).'; return }
    # শুধু server/runner (ollama.exe); tray app ('ollama app') ছুঁই না — ওটা আপনার নিজস্ব
    $procs = @(Get-Process -Name 'ollama' -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { Write-Host '  চলছে না।'; return }
    foreach ($p in $procs) { Stop-Tree $p.Id 'ollama server' }
}

function Stop-Infra {
    Write-Host '[4/5] backing services (sqlserver/redis/qdrant/otel)...' -ForegroundColor Cyan
    if ($KeepInfra) { Write-Host '  রাখা হলো (-KeepInfra).'; return }
    if (-not (Test-Docker)) { Write-Host '  Docker চলছে না, বাদ।'; return }
    if ($DryRun) { Write-Host '  [dry-run] চালানো হতো: docker compose stop (ডেটা থাকে)'; return }
    docker compose -f "$root\docker-compose.yml" stop
    if ($LASTEXITCODE -ne 0) { Write-Host '  docker compose stop ব্যর্থ।' -ForegroundColor Yellow } else { Write-Host '  থামানো হয়েছে (ডেটা অক্ষত)।' }
}

function Stop-DockerDesktop {
    Write-Host '[5/5] Docker Desktop...' -ForegroundColor Cyan
    if (-not $QuitDocker) { Write-Host '  চালু থাকবে (বন্ধ করতে -QuitDocker).'; return }
    $procs = @(Get-Process -Name 'Docker Desktop' -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { Write-Host '  চলছে না।'; return }
    if ($DryRun) { Write-Host '  [dry-run] বন্ধ হতো: Docker Desktop'; return }
    foreach ($p in $procs) { $null = $p.CloseMainWindow() }
    Write-Host '  বন্ধ করার অনুরোধ পাঠানো হয়েছে (কয়েক সেকেন্ড লাগে)।'
}

$failed = $false
try {
    if ($DryRun) { Write-Host 'DRY-RUN: কিছুই থামানো হবে না, শুধু তালিকা।' -ForegroundColor Yellow }
    Stop-Apps
    Stop-Vllm
    Stop-Ollama
    Stop-Infra
    Stop-DockerDesktop
    Write-Host ''
    Write-Host $(if ($DryRun) { 'তালিকা শেষ।' } else { 'সব বন্ধ। আবার চালাতে: .\scripts\dev.ps1' }) -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host "ত্রুটি: $($_.Exception.Message)" -ForegroundColor Red
    $failed = $true
}
finally {
    Wait-KeyIfNeeded
}
if ($failed) { exit 1 }
