@echo off
REM Double-click এই ফাইলে → backend VS-এ, frontend VS Code-এ খোলে।
REM আসল কাজ scripts\open-ide.ps1-এ; এটা শুধু নির্ভরযোগ্যভাবে চালানোর wrapper।
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\open-ide.ps1"
