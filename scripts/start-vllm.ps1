<#
.SYNOPSIS
  vLLM (OpenAI-compatible LLM server) — মেনু থেকে model বেছে চালু; বন্ধ/অবস্থা/log।

.DESCRIPTION
  Docker Desktop (WSL2 + NVIDIA GPU) লাগে। কোন কোন model দেখাবে তা scripts\vllm-models.json-এ (নতুন model = নতুন entry)।
  একসাথে একটাই vLLM চলে (port ও VRAM ভাগ হয় না): নতুন model বাছলে চলমান অন্যটা নিজে থামিয়ে দেয়।
  প্রতিটা model-এর নিজস্ব নামযুক্ত container থাকে, তাই থামালেও থাকে; পরের বার `docker start` — দ্রুত, ডাউনলোড ছাড়া।

.EXAMPLE
  .\scripts\start-vllm.ps1                  # মেনু দেখাবে, নম্বর বাছো
  .\scripts\start-vllm.ps1 -Pick gemma4-e2b # মেনু ছাড়াই সরাসরি
  .\scripts\start-vllm.ps1 -List            # শুধু তালিকা + কোনটা চলছে
  .\scripts\start-vllm.ps1 -Status          # চলমান vLLM কোনটা, সাড়া দিচ্ছে কিনা
  .\scripts\start-vllm.ps1 -Logs            # লাইভ log (Ctrl+C = শুধু log বন্ধ, container চলে)
  .\scripts\start-vllm.ps1 -Stop            # চলমান vLLM থামায় (VRAM ফাঁকা)
  .\scripts\start-vllm.ps1 -Pick dbz-ai -Recreate -MaxModelLen 8192   # flag বদলে নতুন করে বানানো

.NOTES
  Explorer থেকে (right-click → Run with PowerShell) চালালে কাজ শেষে "যেকোনো key চাপলে window বন্ধ হবে" দেখায়, তাই ফল/error পড়া যায়।
  terminal থেকে চালালে pause করে না। জোর করতে -Pause, বন্ধ রাখতে -NoPause।
#>
[CmdletBinding()]
param(
    [string]$Pick = '',
    [string]$Catalog = '',
    [int]$Port = 8000,
    [int]$MaxModelLen = 4096,
    [double]$GpuMemoryUtilization = 0.85,
    # যে tag Gemma 4 সমর্থন করে বলে যাচাই করেছি; RTX 50-series (sm_120)-এ না চললে cu130 ধরনের tag চেষ্টা করো।
    [string]$Image = 'vllm/vllm-openai:gemma4',
    [int]$ReadyTimeoutSec = 1800,
    [switch]$List,
    [switch]$Stop,
    [switch]$Logs,
    [switch]$Status,
    [switch]$Recreate,
    # কাজ শেষে "যেকোনো key চাপো" — double-click/right-click চালালে নিজে থেকেই; -Pause জোর করে, -NoPause বন্ধ
    [switch]$Pause,
    [switch]$NoPause
)

# param default-এ $PSScriptRoot Windows PowerShell 5.1-এ খালি আসে, তাই এখানে বসাই।
if (-not $Catalog) { $Catalog = Join-Path $PSScriptRoot 'vllm-models.json' }

# 'Continue' ইচ্ছাকৃত: Windows PowerShell 5.1-এ 'Stop' থাকলে native command-এর stderr (যেমন docker-এর pull
# progress বা "no such object") ভুল করে terminating error হয়ে যায়। তাই native call-এর ফল $LASTEXITCODE দিয়ে যাচাই করি।
$ErrorActionPreference = 'Continue'
Set-StrictMode -Version Latest

# magic number নয়: GPU-তে এর বেশি আগে থেকে ভরা থাকলে vLLM-এর জায়গা কম পড়ার আশঙ্কা (MiB)
$VramWarnMiB = 3000
# vLLM বন্ধ হতে ডিফল্ট ১০ সেকেন্ডে কুলায় না (তখন docker জোর করে মারে, exit 137) — তাই বেশি সময় দিই
$StopGraceSec = 30
# মেনুতে [0] বাছলে true হয় (Select-Model সেট করে, main পড়ে)
$script:StopRequested = $false
# আমাদের সব vLLM container-এর নামের শুরু — এই নামেই চিনি, অন্য container ছুঁই না
$ContainerPrefix = 'vllm-'

function Assert-Docker {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    docker info *> $null
    $ok = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = $prev
    if (-not $ok) { throw 'Docker চলছে না। আগে Docker Desktop চালু করো, তারপর আবার চেষ্টা করো।' }
}

function Read-Catalog {
    if (-not (Test-Path -LiteralPath $Catalog)) { throw "model তালিকা নেই: $Catalog" }
    # Windows PowerShell 5.1: ConvertFrom-Json JSON array-কে একটাই object হিসেবে দেয়; ForEach-Object { $_ } দিয়ে উপাদান আলাদা করি
    $items = @(Get-Content -LiteralPath $Catalog -Raw -Encoding UTF8 | ConvertFrom-Json | ForEach-Object { $_ })
    if ($items.Count -eq 0) { throw "$Catalog খালি।" }
    foreach ($m in $items) {
        foreach ($field in 'id', 'label', 'name', 'servedName') {
            if (-not $m.PSObject.Properties[$field]) { throw "${Catalog}: একটা entry-তে '$field' নেই।" }
        }
        $hasPath = [bool]$m.PSObject.Properties['path']
        $hasHf = [bool]$m.PSObject.Properties['hfModel']
        if ($hasPath -eq $hasHf) { throw "${Catalog}: '$($m.id)'-এ ঠিক একটা দিতে হবে: path (লোকাল) অথবা hfModel (Hugging Face)।" }
        if (-not $m.name.StartsWith($ContainerPrefix)) { throw "${Catalog}: '$($m.id)'-এর name '$ContainerPrefix' দিয়ে শুরু হতে হবে।" }
    }
    return $items
}

# docker ps -a থেকে আমাদের container: @{ Name; State } তালিকা
function Get-VllmContainers {
    $rows = docker ps -a --filter "name=^/$ContainerPrefix" --format '{{.Names}}|{{.State}}' 2>$null
    $result = @()
    foreach ($row in @($rows)) {
        if ([string]::IsNullOrWhiteSpace($row)) { continue }
        $parts = "$row".Trim().Split('|')
        $result += [pscustomobject]@{ Name = $parts[0]; State = $parts[1] }
    }
    return $result
}

function Get-State([string]$ContainerName) {
    $c = Get-VllmContainers | Where-Object { $_.Name -eq $ContainerName } | Select-Object -First 1
    if ($null -eq $c) { return $null }
    return $c.State
}

function Test-Ready {
    try {
        $null = Invoke-RestMethod -Uri "http://localhost:$Port/v1/models" -TimeoutSec 3
        return $true
    } catch {
        return $false
    }
}

function Show-Models {
    $models = (Invoke-RestMethod -Uri "http://localhost:$Port/v1/models" -TimeoutSec 5).data
    foreach ($m in $models) { Write-Host "  model: $($m.id)  (max_model_len: $($m.max_model_len))" }
}

function Show-Menu($items) {
    Write-Host ''
    Write-Host 'কোন model চালাবে?' -ForegroundColor Cyan
    for ($i = 0; $i -lt $items.Count; $i++) {
        $m = $items[$i]
        $state = Get-State $m.name
        $tag = switch ($state) {
            'running' { ' ● চলছে' }
            $null { '' }
            default { ' ○ বানানো আছে (থামানো)' }
        }
        Write-Host ("  [{0}] {1}{2}" -f ($i + 1), $m.label, $tag)
        if ($m.PSObject.Properties['note']) { Write-Host "      $($m.note)" -ForegroundColor DarkGray }
    }
    Write-Host '  [0] চলমান vLLM বন্ধ করো (VRAM ফাঁকা)'
    Write-Host ''
}

# চলমান সব vLLM container থামায় — `-Stop` ও মেনুর [0], দুটোই এটা ডাকে
function Stop-AllRunning {
    $running = @(Get-VllmContainers | Where-Object { $_.State -eq 'running' })
    if ($running.Count -eq 0) { Write-Host 'চলমান vLLM নেই।'; return }
    foreach ($c in $running) {
        docker stop -t $StopGraceSec $c.Name | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "docker stop ব্যর্থ: $($c.Name)" }
        Write-Host "'$($c.Name)' থামানো হয়েছে।"
    }
    Write-Host 'VRAM ফাঁকা হবে। আবার চালাতে: .\scripts\start-vllm.ps1'
}

function Select-Model($items) {
    if ($Pick) {
        $found = $items | Where-Object { $_.id -eq $Pick } | Select-Object -First 1
        if ($null -eq $found) { throw "'$Pick' নামে model নেই। আছে: $(($items | ForEach-Object { $_.id }) -join ', ')" }
        return $found
    }
    Show-Menu $items
    $answer = Read-Host "নম্বর লিখে Enter চাপো (খালি রাখলে বাতিল)"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $null }
    if ($answer.Trim() -eq '0') {
        $script:StopRequested = $true
        return $null
    }
    $n = 0
    if (-not [int]::TryParse($answer.Trim(), [ref]$n) -or $n -lt 1 -or $n -gt $items.Count) {
        throw "ভুল নম্বর: '$answer' (১ থেকে $($items.Count) লিখতে হবে)"
    }
    return $items[$n - 1]
}

function Stop-Others([string]$KeepName) {
    foreach ($c in (Get-VllmContainers | Where-Object { $_.State -eq 'running' -and $_.Name -ne $KeepName })) {
        Write-Host "অন্য vLLM '$($c.Name)' চলছিল — থামাচ্ছি (port/VRAM খালি করতে)..."
        docker stop -t $StopGraceSec $c.Name | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "docker stop ব্যর্থ: $($c.Name)" }
    }
}

function Warn-IfGpuBusy {
    $smi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if (-not $smi) { return }
    $used = [int](& nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits | Select-Object -First 1)
    if ($used -gt $VramWarnMiB) {
        Write-Warning "GPU-তে আগে থেকেই $used MiB ভরা। Ollama model লোড থাকলে নামাও: ollama stop <model>  (তালিকা: ollama ps)"
    }
}

function Wait-Ready([string]$ContainerName) {
    Write-Host "vLLM তৈরি হওয়ার অপেক্ষা (প্রথমবার ডাউনলোড/লোডে কিছু মিনিট; সর্বোচ্চ $ReadyTimeoutSec সেকেন্ড)..." -NoNewline
    $deadline = (Get-Date).AddSeconds($ReadyTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ((Get-State $ContainerName) -ne 'running') {
            Write-Host ''
            throw "container থেমে গেছে। কারণ দেখতে: docker logs $ContainerName"
        }
        if (Test-Ready) {
            Write-Host ' তৈরি।'
            return
        }
        Write-Host '.' -NoNewline
        Start-Sleep -Seconds 5
    }
    Write-Host ''
    throw "$ReadyTimeoutSec সেকেন্ডে তৈরি হয়নি। দেখো: docker logs $ContainerName"
}

function New-Container($m) {
    $dockerArgs = @('run', '-d', '--name', $m.name, '--gpus', 'all', '--ipc=host', '-p', "${Port}:8000")

    if ($m.PSObject.Properties['hfModel']) {
        # Hugging Face থেকে: cache ফোল্ডার mount করি, যাতে বড় ডাউনলোড container মুছলেও থাকে
        $hfCache = Join-Path $env:USERPROFILE '.cache\huggingface'
        New-Item -ItemType Directory -Force -Path $hfCache | Out-Null
        $dockerArgs += @('-v', "${hfCache}:/root/.cache/huggingface")
        if ($env:HF_TOKEN) { $dockerArgs += @('-e', 'HF_TOKEN') }   # নাম দিলেই docker মান নেয়, এখানে ছাপা হয় না
        $modelArg = $m.hfModel
    }
    else {
        if (-not (Test-Path -LiteralPath (Join-Path $m.path 'config.json'))) {
            throw "model ফোল্ডারে config.json নেই: $($m.path)"
        }
        $modelPath = (Resolve-Path -LiteralPath $m.path).Path
        $dockerArgs += @('-v', "${modelPath}:/models/served:ro")
        $modelArg = '/models/served'
    }

    $maxLen = if ($m.PSObject.Properties['maxModelLen']) { $m.maxModelLen } else { $MaxModelLen }
    $vllmArgs = @('--model', $modelArg, '--served-model-name', $m.servedName,
        '--max-model-len', $maxLen, '--gpu-memory-utilization', $GpuMemoryUtilization,
        '--enable-auto-tool-choice', '--tool-call-parser', 'gemma4', '--reasoning-parser', 'gemma4')
    if ($m.PSObject.Properties['quantization']) { $vllmArgs += @('--quantization', $m.quantization) }

    Write-Host "নতুন container '$($m.name)' বানাচ্ছি (model: $modelArg)..."
    docker @dockerArgs $Image @vllmArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'docker run ব্যর্থ। উপরের error দেখো (port ব্যস্ত? image নেই? GPU দেখা যাচ্ছে না?)।' }
}

# right-click → "Run with PowerShell" বা double-click করলে parent process explorer.exe — তখন কাজ শেষে window নিজে
# বন্ধ হয়ে যায় (ফল/error পড়ার সুযোগ থাকে না)। terminal থেকে চালালে এর দরকার নেই।
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
    catch { $null = Read-Host }   # key পড়া না গেলে Enter-ই যথেষ্ট
}
# ---------------------------------------------------------------- main
$failed = $false
try {
    Assert-Docker
    $items = Read-Catalog

    if ($List) {
        Show-Menu $items
        return
    }

    if ($Status) {
        $all = @(Get-VllmContainers)
        if ($all.Count -eq 0) { Write-Host 'কোনো vLLM container নেই।'; return }
        foreach ($c in $all) { Write-Host "container '$($c.Name)': $($c.State)" }
        if (@($all | Where-Object { $_.State -eq 'running' }).Count -gt 0) {
            if (Test-Ready) { Show-Models } else { Write-Host "  API এখনো সাড়া দিচ্ছে না (http://localhost:$Port/v1/models)" }
        }
        return
    }

    if ($Logs) {
        $running = Get-VllmContainers | Where-Object { $_.State -eq 'running' } | Select-Object -First 1
        if ($null -eq $running) { Write-Host 'চলমান vLLM নেই (থেমে থাকা container-এর log: docker logs <নাম>)।'; return }
        docker logs -f --tail 50 $running.Name
        return
    }

    if ($Stop) {
        Stop-AllRunning
        return
    }

    $selected = Select-Model $items
    if ($script:StopRequested) {
        Stop-AllRunning
        return
    }
    if ($null -eq $selected) { Write-Host 'বাতিল।'; return }

    if ($Recreate -and $null -ne (Get-State $selected.name)) {
        Write-Host "পুরোনো '$($selected.name)' মুছছি..."
        docker rm -f $selected.name | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "docker rm ব্যর্থ: $($selected.name)" }
    }

    Stop-Others $selected.name

    switch (Get-State $selected.name) {
        'running' {
            Write-Host "'$($selected.name)' আগে থেকেই চলছে।"
        }
        $null {
            Warn-IfGpuBusy
            New-Container $selected
        }
        default {
            Warn-IfGpuBusy
            Write-Host "থামানো '$($selected.name)' আবার চালু করছি..."
            docker start $selected.name | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'docker start ব্যর্থ।' }
        }
    }

    Wait-Ready $selected.name
    Show-Models
    Write-Host "প্রস্তুত: http://localhost:$Port/v1   (app-এ provider 'vllm', model '$($selected.servedName)')"
    Write-Host 'log: .\scripts\start-vllm.ps1 -Logs    বন্ধ: .\scripts\start-vllm.ps1 -Stop'

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
