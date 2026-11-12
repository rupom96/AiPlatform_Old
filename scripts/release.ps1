# দুটো image build করে GHCR-এ push করে — সার্ভারে source পাঠাতে হয় না।
# চালাও: release.bat double-click, বা
#   .\scripts\release.ps1 -PublicHost app.yourdomain.com -Tag 1.0.0 [-NoPush] [-NoPause]
#
# সার্ভারে তারপর:  docker compose -f docker-compose.prod.yml pull; ... up -d
param(
    [string]$PublicHost,
    [string]$Tag = 'latest',
    [string]$Registry = 'ghcr.io/saifulpavel',
    [string]$GoogleClientId = '',
    [switch]$NoPush,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$stateFile = Join-Path $root 'deploy\.release-host'

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

Write-Host ""
Write-Host "=== AiPlatform release ===" -ForegroundColor Cyan

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Fail "docker পাওয়া যায়নি। Docker Desktop চালু আছে?"
}
docker info 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "Docker daemon-এ পৌঁছানো যাচ্ছে না। Docker Desktop চালু করো।" }

# --- PUBLIC_HOST: argument > গতবারের মান > জিজ্ঞেস করো ------------------------
if (-not $PublicHost -and (Test-Path $stateFile)) {
    $saved = (Get-Content $stateFile -Raw).Trim()
    if ($saved) {
        if ($NoPause) { $PublicHost = $saved }
        else {
            $answer = Read-Host "PUBLIC_HOST = '$saved' — ঠিক আছে? (Enter = হ্যাঁ, নইলে নতুন লিখো)"
            $PublicHost = if ($answer) { $answer.Trim() } else { $saved }
        }
    }
}
if (-not $PublicHost) {
    $PublicHost = (Read-Host "সার্ভারের hostname লিখো (scheme ছাড়া, যেমন app.yourdomain.com)").Trim()
}
if (-not $PublicHost) { Fail "hostname ছাড়া frontend image বানানো যাবে না (URL bundle-এ বেক হয়)।" }
if ($PublicHost -match '^https?://') { Fail "scheme বাদ দাও। শুধু hostname: app.yourdomain.com" }
if ($PublicHost -match '[/\s]') { Fail "hostname-এ slash বা space থাকতে পারবে না: $PublicHost" }

# frontend এই URL-এ API খুঁজবে; Caddy একই origin-এ /api proxy করে, তাই scheme+host-ই যথেষ্ট
$apiBaseUrl = "https://$PublicHost"
$apiImage = "$Registry/aiplatform-api"
$webImage = "$Registry/aiplatform-web"

Write-Host "  host:     $PublicHost"
Write-Host "  registry: $Registry"
Write-Host "  tag:      $Tag"
Write-Host ""

# --- build --------------------------------------------------------------------
Write-Host "[1/3] backend image..." -ForegroundColor Cyan
docker build -t "${apiImage}:$Tag" -t "${apiImage}:latest" (Join-Path $root 'backend')
if ($LASTEXITCODE -ne 0) { Fail "backend image build ব্যর্থ।" }

Write-Host "[2/3] frontend image ($apiBaseUrl)..." -ForegroundColor Cyan
docker build -t "${webImage}:$Tag" -t "${webImage}:latest" `
    --build-arg "VITE_API_BASE_URL=$apiBaseUrl" `
    --build-arg "VITE_GOOGLE_CLIENT_ID=$GoogleClientId" `
    (Join-Path $root 'frontend')
if ($LASTEXITCODE -ne 0) { Fail "frontend image build ব্যর্থ।" }

# PDB যেন ভুল করে ঢুকে না যায় — ঢুকলে image থেকে সোজা GitHub source নামানো যায়
$pdbCheck = docker run --rm --entrypoint sh "${apiImage}:$Tag" -c "ls /app/*.pdb 2>/dev/null | wc -l"
if ($pdbCheck.Trim() -ne '0') {
    Fail "image-এ $($pdbCheck.Trim())টা .pdb আছে — SourceLink দিয়ে source ফাঁস হবে। backend/Dockerfile-এ DebugType=none আছে কি না দেখো।"
}
Write-Host "      pdb check: পরিষ্কার" -ForegroundColor DarkGray

# --- push ---------------------------------------------------------------------
if ($NoPush) {
    Write-Host "[3/3] push — skip (-NoPush)" -ForegroundColor DarkGray
}
else {
    Write-Host "[3/3] GHCR-এ push..." -ForegroundColor Cyan
    docker push --all-tags $apiImage
    if ($LASTEXITCODE -ne 0) {
        Fail "push ব্যর্থ। আগে একবার login করো:`n  `$env:CR_PAT='<github-token>'; `$env:CR_PAT | docker login ghcr.io -u <github-username> --password-stdin`nToken-এ write:packages scope লাগবে।"
    }
    docker push --all-tags $webImage
    if ($LASTEXITCODE -ne 0) { Fail "frontend image push ব্যর্থ।" }
}

New-Item -ItemType Directory -Path (Split-Path $stateFile) -Force | Out-Null
Set-Content -Path $stateFile -Value $PublicHost -Encoding UTF8

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  ${apiImage}:$Tag"
Write-Host "  ${webImage}:$Tag"
Write-Host ""
Write-Host "সার্ভারে (PowerShell, deploy ফোল্ডারে):" -ForegroundColor Yellow
Write-Host "  docker compose -f docker-compose.prod.yml pull"
Write-Host "  docker compose -f docker-compose.prod.yml up -d"
Write-Host ""
Pause-IfInteractive
