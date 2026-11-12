# দুই IDE একসাথে খোলে: backend → Visual Studio (default .slnx handler), frontend → VS Code।
# double-click: root-এর open-ide.bat দিয়ে চালাও (.ps1-এ সরাসরি double-click Notepad খোলে)।

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent   # scripts/ এর প্যারেন্ট = repo root

$solution = Join-Path $root 'backend\AiPlatform.slnx'
$frontend = Join-Path $root 'frontend'

# --- backend → Visual Studio ---
# .slnx-এর default app (VS 2026) দিয়ে খোলে — devenv path hardcode লাগে না
Write-Host "Opening backend in Visual Studio: $solution" -ForegroundColor Cyan
Start-Process $solution

# --- frontend → VS Code ---
# code.cmd PATH-এ; না থাকলে পরিষ্কার বার্তা
$code = Get-Command code -ErrorAction SilentlyContinue
if ($code) {
    Write-Host "Opening frontend in VS Code: $frontend" -ForegroundColor Cyan
    & code $frontend
} else {
    Write-Warning "VS Code-এর 'code' কমান্ড PATH-এ নেই। VS Code খুলে ম্যানুয়ালি folder খোলো: $frontend"
}

Write-Host "Done." -ForegroundColor Green
Start-Sleep -Seconds 1   # double-click window সাথে সাথে বন্ধ না হয়ে বার্তা দেখা যায়
