@echo off
echo Killing any process on port 5244...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5244 ^| findstr LISTENING') do taskkill /PID %%a /F 2>nul
timeout /t 1 /nobreak >nul
echo Starting Rockflix API...
cd /d "%~dp0Rockflix.API"
dotnet run
