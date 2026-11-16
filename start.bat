@echo off
setlocal
cd /d "%~dp0"
rem Source checkout: build the native client once, then start it.
rem The installed EXE from the GitHub release is the recommended way to run the app.
if not exist "%~dp0dist\CodexUsage.exe" (
  echo Building CodexUsage.exe from source...
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop\build.ps1"
  if errorlevel 1 (
    echo [ERROR] Build failed. Please download CodexUsage.exe from the GitHub release instead.
    pause
    exit /b 1
  )
)
start "" "%~dp0dist\CodexUsage.exe"
endlocal
exit /b 0
