@echo off
chcp 936 >nul
rem ==========================================================
rem  PlainSpaceflightLauncher (PSL) build script
rem  Output: PSL\bin\Release\net48\PlainSpaceflightLauncher.exe
rem ==========================================================
setlocal
cd /d "%~dp0"

echo.
echo [1/1] Building PlainSpaceflightLauncher ...
echo.

dotnet build PSL\PSL.csproj -c Release --nologo
if errorlevel 1 goto fail

echo.
echo Build OK.
echo   PSL\bin\Release\net48\PlainSpaceflightLauncher.exe
echo   (pack the whole net48 folder: exe + exe.config + runtimes)
echo.
goto end

:fail
echo.
echo Build FAILED. See messages above.
echo.

:end
pause
endlocal
