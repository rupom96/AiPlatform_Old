@echo off
REM Double-click এই ফাইলে → publish\api + publish\web তৈরি হবে।
REM আসল কাজ scripts\publish.ps1-এ; এটা শুধু নির্ভরযোগ্যভাবে চালানোর wrapper।
REM chcp 65001 = console UTF-8, নইলে Windows PowerShell-এ বাংলা লেখা ভেঙে যায়।
chcp 65001 >nul
where pwsh >nul 2>&1
if %ERRORLEVEL%==0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" %*
)