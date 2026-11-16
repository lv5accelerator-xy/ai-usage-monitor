@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
title Claude - Login

echo =============================================
echo  Codex + Claude Usage - Claude account login
echo =============================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-claude.ps1" -NoPause
set "EC=%ERRORLEVEL%"
echo.
if not "%EC%"=="0" (
  echo [FAILED] Claude setup did not finish. The error is shown above.
) else (
  echo [OK] Claude login step finished.
)
echo.
pause
exit /b %EC%
