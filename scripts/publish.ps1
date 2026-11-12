# deploy artifact এক কমান্ডে: publish\api (backend) + publish\web (frontend)।
# চালাও: publish.bat double-click, বা .\scripts\publish.ps1 [-ApiBaseUrl https://api.example.com]
#
# ⚠️ frontend-এ VITE_API_BASE_URL build-এর সময়ে bundle-এ ঢুকে যায় (baseApi.ts) — runtime-এ
# বদলানো যায় না। তাই URL এখানেই লাগে; ভুল দিলে সার্ভারে আবার build করতে হবে।
param(
    [string]$ApiBaseUrl,
    [switch]$SkipFrontend,
    [switch]$SkipBackend,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'publish'
$apiOut = Join-Path $out 'api'
$webOut = Join-Path $out 'web'

function Pause-IfInteractive {
    # double-click-এ জানালা সাথে সাথে বন্ধ হলে error পড়া যায় না; CI/script-এ -NoPause দাও।
    if (-not $NoPause) { Read-Host "বন্ধ করতে Enter" | Out-Null }
}

function Fail($message) {
    Write-Host ""
    Write-Host "FAIL: $message" -ForegroundColor Red
    Write-Host ""
    Pause-IfInteractive
    exit 1
}

function Need($exe, $hint) {
    if (-not (Get-Command $exe -ErrorAction SilentlyContinue)) { Fail "$exe পাওয়া যায়নি। $hint" }
}

Write-Host ""
Write-Host "=== AiPlatform publish ===" -ForegroundColor Cyan
Write-Host "output: $out"
Write-Host ""

if (-not $SkipBackend) { Need 'dotnet' 'https://dotnet.microsoft.com/download/dotnet/10.0' }
if (-not $SkipFrontend) { Need 'npm' 'https://nodejs.org' }

# --- API URL: argument > .env.production > .env > জিজ্ঞেস করো -------------------
if (-not $SkipFrontend) {
    $urlFromFile = $false
    if (-not $ApiBaseUrl) {
        foreach ($envFile in @('.env.production', '.env')) {
            $path = Join-Path $root "frontend\$envFile"
            if (-not (Test-Path $path)) { continue }
            $line = Select-String -Path $path -Pattern '^\s*VITE_API_BASE_URL\s*=\s*(.+)$' | Select-Object -First 1
            if ($line) {
                $ApiBaseUrl = $line.Matches[0].Groups[1].Value.Trim()
                Write-Host "VITE_API_BASE_URL পাওয়া গেল $envFile-এ: $ApiBaseUrl" -ForegroundColor DarkGray
                $urlFromFile = $true
                break
            }
        }
    }

    if ($urlFromFile -and -not $NoPause) {
        $answer = Read-Host "API base URL = '$ApiBaseUrl' — ঠিক আছে? (Enter = হ্যাঁ, নইলে নতুন URL লিখো)"
        if ($answer) { $ApiBaseUrl = $answer.Trim() }
    }
    elseif (-not $ApiBaseUrl) {
        $ApiBaseUrl = (Read-Host "API base URL লিখো (যেমন https://api.yourserver.com)").Trim()
    }

    if (-not $ApiBaseUrl) { Fail "API base URL ছাড়া frontend build করা যাবে না।" }
    # শেষের slash থাকলে কোডে `${url}/api/v1` → `//api/v1` হয়ে যায়
    $ApiBaseUrl = $ApiBaseUrl.TrimEnd('/')
    if ($ApiBaseUrl -notmatch '^https?://') { Fail "URL-এ http:// বা https:// লাগবে। পেলাম: $ApiBaseUrl" }
    if ($ApiBaseUrl -match '^http://' -and $ApiBaseUrl -notmatch '://localhost') {
        Write-Host "⚠️  http:// — production-এ refresh cookie Secure=true, মানে HTTPS ছাড়া login টিকবে না।" -ForegroundColor Yellow
    }
}

# --- backend ------------------------------------------------------------------
if (-not $SkipBackend) {
    Write-Host "[1/2] backend publish (Release)..." -ForegroundColor Cyan
    if (Test-Path $apiOut) { Remove-Item $apiOut -Recurse -Force }
    dotnet publish (Join-Path $root 'backend\src\Api') -c Release -o $apiOut --nologo
    if ($LASTEXITCODE -ne 0) { Fail "dotnet publish ব্যর্থ। উপরের error দেখো." }
}
else { Write-Host "[1/2] backend — skip" -ForegroundColor DarkGray }

# --- frontend -----------------------------------------------------------------
if (-not $SkipFrontend) {
    Write-Host "[2/2] frontend build ($ApiBaseUrl)..." -ForegroundColor Cyan
    Push-Location (Join-Path $root 'frontend')
    try {
        # Google Sign-In ঐচ্ছিক — .env-এ থাকলে বয়ে নিই, নইলে খালি (button placeholder দেখায়)
        $googleClientId = ''
        foreach ($envFile in @('.env.production', '.env')) {
            if (-not (Test-Path $envFile)) { continue }
            $line = Select-String -Path $envFile -Pattern '^\s*VITE_GOOGLE_CLIENT_ID\s*=\s*(.*)$' | Select-Object -First 1
            if ($line) { $googleClientId = $line.Matches[0].Groups[1].Value.Trim(); break }
        }

        @(
            "# publish.ps1 নিজে তৈরি করে — হাতে বদলালে পরের publish-এ মুছে যাবে।",
            "VITE_API_BASE_URL=$ApiBaseUrl",
            "VITE_GOOGLE_CLIENT_ID=$googleClientId"
        ) | Set-Content -Path '.env.production' -Encoding UTF8

        if (-not (Test-Path 'node_modules')) {
            Write-Host "      node_modules নেই — npm ci..." -ForegroundColor DarkGray
            npm ci
            if ($LASTEXITCODE -ne 0) { Fail "npm ci ব্যর্থ।" }
        }

        npm run build
        if ($LASTEXITCODE -ne 0) { Fail "npm run build ব্যর্থ। উপরের error দেখো." }
    }
    finally { Pop-Location }

    if (Test-Path $webOut) { Remove-Item $webOut -Recurse -Force }
    New-Item -ItemType Directory -Path $webOut -Force | Out-Null
    Copy-Item (Join-Path $root 'frontend\dist\*') $webOut -Recurse -Force

    # SPA fallback — এটা ছাড়া React router-এর deep link refresh করলে IIS 404 দেয়।
    # IIS-এ URL Rewrite module বসানো থাকতে হবে।
    $webConfig = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="SPA fallback" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
          </conditions>
          <action type="Rewrite" url="/index.html" />
        </rule>
      </rules>
    </rewrite>
    <staticContent>
      <remove fileExtension=".json" />
      <mimeMap fileExtension=".json" mimeType="application/json" />
      <remove fileExtension=".webmanifest" />
      <mimeMap fileExtension=".webmanifest" mimeType="application/manifest+json" />
    </staticContent>
  </system.webServer>
</configuration>
'@
    Set-Content -Path (Join-Path $webOut 'web.config') -Value $webConfig -Encoding UTF8
}
else { Write-Host "[2/2] frontend — skip" -ForegroundColor DarkGray }

# --- সারসংক্ষেপ ---------------------------------------------------------------
function SizeOf($path) {
    if (-not (Test-Path $path)) { return 'n/a' }
    $bytes = (Get-ChildItem $path -Recurse -File | Measure-Object -Property Length -Sum).Sum
    return "{0:N1} MB" -f ($bytes / 1MB)
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  backend:  $apiOut   ($(SizeOf $apiOut))"
Write-Host "  frontend: $webOut   ($(SizeOf $webOut))"
Write-Host ""
Write-Host "সার্ভারে নেওয়ার আগে:" -ForegroundColor Yellow
Write-Host "  1. সার্ভারে machine-level environment variable দাও: ConnectionStrings__Default,"
Write-Host "     ConnectionStrings__Erp, Redis__Connection, Qdrant__Url, Jwt__SecretKey, Llm__Providers__*__ApiKey"
Write-Host "     (publish\api\web.config প্রতি publish-এ নতুন লেখা হয় — ওখানে secret বসালে মুছে যাবে)"
Write-Host "  2. app pool: Idle Time-out = 0, Regular Time Interval = 0, Start Mode = AlwaysRunning"
Write-Host "     (নইলে BackgroundService মরে যায় — ingestion চুপচাপ বন্ধ হবে)"
Write-Host "  3. frontend site-এ URL Rewrite module লাগবে (web.config রাখা হয়েছে)"
Write-Host ""
Pause-IfInteractive
