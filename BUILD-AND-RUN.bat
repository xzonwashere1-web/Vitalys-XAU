@echo off
cd /d "%~dp0"
taskkill /f /im "Vitalys XAU.exe" >nul 2>&1
rmdir /s /q publish >nul 2>&1
dotnet publish "XAU\Vitalys XAU.csproj" -c Release -r win-x64 --self-contained true -o publish
if errorlevel 1 (echo BUILD FAILED - copy the red error lines & pause & exit /b 1)
start "" "%~dp0publish\Vitalys XAU.exe"
