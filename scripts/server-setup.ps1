# 🖥️ SERVER bootstrap — Windows Server 2022-এ Linux container চালানোর ব্যবস্থা।
#
# কেন এত কিছু: Docker Desktop Windows Server-এ সমর্থিত নয় (Docker-এর নিজের docs),
# আর আমাদের সব image Linux। তাই WSL2 বসিয়ে তার ভেতরে Docker CE।
#
# চালাও Administrator PowerShell-এ:
#   powershell -ExecutionPolicy Bypass -File .\server-setup.ps1
#
# দুই ধাপে চলে — প্রথমবার Windows feature চালু করে reboot চায়, তারপর আবার চালাও।
# idempotent: বারবার চালালে যা হয়ে আছে তা এড়িয়ে যায়।
param(
    [string]$Distro = 'Ubuntu-22.04',
    [string]$AppDir = 'C:\aiplatform',
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'

function Pause-IfInteractive {
    if (-not $NoPause) { Read-Host "বন্ধ করতে Enter" | Out-Null }
}

function Fail($message) {
    Write-Host ""
    Write-Host "FAIL: $message" -ForegroundColor Red
    Write-Host ""
    Pause-IfInteractive
    exit 1
}

function Step($n, $text) { Write-Host "[$n] $text" -ForegroundColor Cyan }
function Ok($text)       { Write-Host "      $text" -ForegroundColor DarkGray }

Write-Host ""
Write-Host "=== AiPlatform server setup ===" -ForegroundColor Cyan

# --- Administrator বাধ্যতামূলক: Windows feature, firewall, portproxy সবই লাগে ----
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Fail "Administrator PowerShell-এ চালাও (ডান-ক্লিক → Run as administrator)।" }

$os = Get-CimInstance Win32_OperatingSystem
Write-Host "  OS: $($os.Caption) (build $($os.BuildNumber))"
Write-Host ""

# =================================================================================
# ধাপ ১ — Windows feature (reboot লাগে)
# =================================================================================
Step '1/7' 'Windows feature (WSL + Virtual Machine Platform)...'

$needReboot = $false
foreach ($feature in 'Microsoft-Windows-Subsystem-Linux', 'VirtualMachinePlatform') {
    $state = (Get-WindowsOptionalFeature -Online -FeatureName $feature).State
    if ($state -eq 'Enabled') {
        Ok "$feature — আগেই চালু"
        continue
    }
    Ok "$feature — চালু করছি..."
    $result = Enable-WindowsOptionalFeature -Online -FeatureName $feature -NoRestart -All
    if ($result.RestartNeeded) { $needReboot = $true }
}

if ($needReboot) {
    Write-Host ""
    Write-Host "REBOOT লাগবে।" -ForegroundColor Yellow
    Write-Host "  1. সার্ভার restart করো"
    Write-Host "  2. আবার এই script চালাও — বাকি ধাপ থেকে শুরু হবে"
    Write-Host ""
    Pause-IfInteractive
    exit 0
}

# =================================================================================
# ধাপ ২ — WSL নিজেই update (Server 2022-এর inbox wsl.exe পুরোনো, --install জানে না)
# =================================================================================
Step '2/7' 'WSL kernel/runtime...'

$wslOk = $false
try {
    wsl.exe --status 2>&1 | Out-Null
    $version = (wsl.exe --version 2>&1 | Out-String)
    if ($version -match 'WSL') { $wslOk = $true; Ok "WSL আগে থেকেই আধুনিক সংস্করণে" }
}
catch { }

if (-not $wslOk) {
    # Microsoft-এর নিজের GitHub release (Store-বিহীন Server-এ এটাই অফিসিয়াল পথ)
    $msi = Join-Path $env:TEMP 'wsl.x64.msi'
    Ok "wsl.x64.msi নামাচ্ছি (Microsoft/WSL release)..."
    Invoke-WebRequest -Uri 'https://github.com/microsoft/WSL/releases/latest/download/wsl.x64.msi' `
        -OutFile $msi -UseBasicParsing
    Ok "install করছি..."
    Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn" -Wait -NoNewWindow
    Remove-Item $msi -Force -ErrorAction SilentlyContinue
}

wsl.exe --set-default-version 2 2>&1 | Out-Null

# =================================================================================
# ধাপ ৩ — Ubuntu distro
# =================================================================================
Step '3/7' "Linux distro ($Distro)..."

# wsl --list-এর output UTF-16; PowerShell-এ সরাসরি match করলে ফাঁকা দেখায়
$installed = (wsl.exe --list --quiet) -replace "`0", '' -split "`r?`n" | ForEach-Object { $_.Trim() }
if ($installed -contains $Distro) {
    Ok "$Distro আগেই বসানো"
}
else {
    Ok "$Distro নামাচ্ছি (কয়েক মিনিট)..."
    # --no-launch: নইলে প্রথম চালুতে username/password interactively চায় আর script আটকে যায়।
    # আমরা root হিসেবেই সব করব — এই distro শুধু docker চালানোর জন্য, মানুষ ব্যবহার করবে না।
    wsl.exe --install -d $Distro --no-launch
    if ($LASTEXITCODE -ne 0) { Fail "$Distro install ব্যর্থ। 'wsl --list --online' দিয়ে নামটা মিলিয়ে দেখো।" }
}

# systemd দরকার — নইলে docker service boot-এ উঠবে না।
# ⚠️ PowerShell থেকে wsl-এ pipe করে লিখি না: PS 5.1 ASCII + CRLF পাঠায়, wsl.conf ভাঙে।
# বদলে bash-কেই printf দিয়ে লিখতে বলি — LF নিশ্চিত।
wsl.exe -d $Distro -u root -e bash -lc "printf '[boot]\nsystemd=true\n\n[network]\ngenerateResolvConf=true\n' > /etc/wsl.conf"
if ($LASTEXITCODE -ne 0) { Fail "/etc/wsl.conf লেখা যায়নি।" }
Ok "systemd চালু করা হলো (/etc/wsl.conf)"

Ok "distro restart..."
wsl.exe --terminate $Distro 2>&1 | Out-Null
Start-Sleep -Seconds 3

# =================================================================================
# ধাপ ৪ — Docker CE (Ubuntu-র ভেতরে, Docker-এর অফিসিয়াল apt repo থেকে)
# =================================================================================
Step '4/7' 'Docker CE...'

$dockerCheck = (wsl.exe -d $Distro -u root -e bash -lc "command -v docker || true") -replace "`0", ''
if ($dockerCheck -match 'docker') {
    Ok "Docker আগেই বসানো"
}
else {
    Ok "apt repo যোগ করে install করছি (কয়েক মিনিট)..."
    # get.docker.com-এর শর্টকাট নিইনি — অফিসিয়াল repo + GPG key, যাচাইযোগ্য পথ
    $install = @'
set -e
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq ca-certificates curl gnupg
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
chmod a+r /etc/apt/keyrings/docker.gpg
echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" > /etc/apt/sources.list.d/docker.list
apt-get update -qq
apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
systemctl enable docker
systemctl start docker
'@
    # C:\ ড্রাইভে লিখি → WSL-এ পথটা সবসময় /mnt/c/... , wslpath অনুবাদের দরকার নেই।
    # LF বাধ্যতামূলক — CRLF হলে bash "\r: command not found" দেয়।
    New-Item -ItemType Directory -Path $AppDir -Force | Out-Null
    $script = Join-Path $AppDir 'install-docker.sh'
    [IO.File]::WriteAllText($script, ($install -replace "`r`n", "`n"), (New-Object Text.UTF8Encoding $false))

    $drive = ($AppDir.Substring(0, 1)).ToLower()
    $wslPath = "/mnt/$drive" + ($AppDir.Substring(2) -replace '\\', '/') + '/install-docker.sh'
    wsl.exe -d $Distro -u root -e bash $wslPath
    if ($LASTEXITCODE -ne 0) { Fail "Docker install ব্যর্থ। উপরের error দেখো।" }
    Remove-Item $script -Force -ErrorAction SilentlyContinue
}

# =================================================================================
# ধাপ ৫ — ফোল্ডার + সহায়ক ফাইল
# =================================================================================
Step '5/7' "$AppDir + helper..."

New-Item -ItemType Directory -Path $AppDir -Force | Out-Null

# WSL2-র IP প্রতি boot-এ বদলায়, তাই portproxy প্রতিবার নতুন করে বসাতে হয়।
# (Windows 11-এর mirrored networking এটা অপ্রয়োজনীয় করত, কিন্তু Server 2022-এ নেই।)
$portProxy = @"
# WSL2-র বর্তমান IP-তে LAN থেকে আসা 80/443 পাঠায়। boot-এ scheduled task চালায়।
`$distro = '$Distro'
wsl.exe -d `$distro -u root -e systemctl start docker 2>&1 | Out-Null
`$ip = ((wsl.exe -d `$distro -e hostname -I) -replace "``0", '').Trim().Split(' ')[0]
if (-not `$ip) { exit 1 }
foreach (`$port in 80, 443) {
    netsh interface portproxy delete v4tov4 listenport=`$port listenaddress=0.0.0.0 2>&1 | Out-Null
    netsh interface portproxy add v4tov4 listenport=`$port listenaddress=0.0.0.0 connectport=`$port connectaddress=`$ip | Out-Null
}
"@
$portProxyPath = Join-Path $AppDir 'wsl-portproxy.ps1'
[IO.File]::WriteAllText($portProxyPath, $portProxy, (New-Object Text.UTF8Encoding $true))

# রোজকার কমান্ড সহজ রাখতে wrapper: `aip ps`, `aip logs api`, `aip up`
$aip = @"
@echo off
REM docker WSL-এর ভেতরে চলে, তাই কমান্ড ওখানেই পাঠাই।
REM ব্যবহার:  aip ps   |   aip up   |   aip logs api   |   aip pull
chcp 65001 >nul
if "%~1"=="up"   goto :up
if "%~1"=="pull" goto :pull
wsl -d $Distro -u root -e bash -lc "cd /mnt/c/aiplatform && docker compose -f docker-compose.prod.yml %*"
goto :eof
:up
wsl -d $Distro -u root -e bash -lc "cd /mnt/c/aiplatform && docker compose -f docker-compose.prod.yml up -d"
powershell -NoProfile -ExecutionPolicy Bypass -File "$portProxyPath"
goto :eof
:pull
wsl -d $Distro -u root -e bash -lc "cd /mnt/c/aiplatform && docker compose -f docker-compose.prod.yml pull"
"@
[IO.File]::WriteAllText((Join-Path $AppDir 'aip.cmd'), $aip, (New-Object Text.UTF8Encoding $false))
Ok "aip.cmd + wsl-portproxy.ps1 লেখা হলো"

# =================================================================================
# ধাপ ৬ — firewall + boot-এ auto start
# =================================================================================
Step '6/7' 'firewall + startup task...'

foreach ($rule in @(@{Name = 'AiPlatform HTTP'; Port = 80 }, @{Name = 'AiPlatform HTTPS'; Port = 443 })) {
    if (Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue) {
        Ok "$($rule.Name) — আগেই আছে"
    }
    else {
        New-NetFirewallRule -DisplayName $rule.Name -Direction Inbound `
            -LocalPort $rule.Port -Protocol TCP -Action Allow | Out-Null
        Ok "$($rule.Name) — যোগ করা হলো"
    }
}

# WSL boot-এ নিজে ওঠে না; task distro চালু করে + portproxy ঠিক করে।
$taskName = 'AiPlatform WSL bootstrap'
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$portProxyPath`""
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
# boot-এর পর WSL উঠতে সময় লাগে — ১ মিনিট দেরিতে চালাই
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
$trigger.Delay = 'PT1M'
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings | Out-Null
Ok "startup task বসানো হলো (boot + ১ মিনিট)"

& $portProxyPath
Ok "portproxy এখনই বসানো হলো"

# =================================================================================
# ধাপ ৭ — যাচাই
# =================================================================================
Step '7/7' 'যাচাই...'

$hello = (wsl.exe -d $Distro -u root -e bash -lc "docker run --rm hello-world 2>&1 | tail -5") -replace "`0", ''
if ($hello -match 'working correctly') {
    Ok "docker চলছে ✓"
}
else {
    Write-Host ""
    Write-Host "docker run hello-world পাস করল না:" -ForegroundColor Yellow
    Write-Host $hello
    Write-Host "'wsl -d $Distro -u root -e systemctl status docker' দিয়ে দেখো।" -ForegroundColor Yellow
}

$proxy = netsh interface portproxy show v4tov4 | Out-String
Write-Host ""
Write-Host "portproxy:" -ForegroundColor DarkGray
Write-Host $proxy

Write-Host "Done." -ForegroundColor Green
Write-Host ""
Write-Host "পরের ধাপ:" -ForegroundColor Yellow
Write-Host "  1. GHCR-এ login (WSL-এর ভেতরের docker-এ, Windows-এ নয়):"
Write-Host "     wsl -d $Distro -u root -e bash -lc `"echo <TOKEN> | docker login ghcr.io -u Saifulpavel --password-stdin`""
Write-Host "  2. $AppDir-এ ৩টা ফাইল কপি করো:"
Write-Host "     docker-compose.prod.yml, Caddyfile, .env"
Write-Host "  3. চালু করো:"
Write-Host "     cd $AppDir"
Write-Host "     .\aip.cmd pull"
Write-Host "     .\aip.cmd up"
Write-Host ""
Write-Host "রোজকার: aip ps | aip logs api | aip restart api | aip down" -ForegroundColor DarkGray
Write-Host ""
Pause-IfInteractive
