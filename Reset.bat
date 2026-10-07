@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo   [ERROR] Administrator privileges required.
    echo   Right-click Reset.bat and choose "Run as administrator".
    echo.
    pause
    exit /b 1
)

rem All names ever used by this project. Only the first two are the current build;
rem the rest are legacy artifacts that may still exist on other machines and are
rem equally capable of becoming a full-screen topmost overlay that eats all input.
set "NAMES=LingGuangInk.App.exe 灵光画笔App.exe LingGuangInk.Build.exe LingGuangInk.exe LingGuangScreenInk.exe LingGuangInk.Fixed.exe LingGuangInk.Review.exe LingGuangInk.TouchForward.exe LingGuangInk.TouchForward.Check.exe 灵光画笔.exe 灵光画笔TouchForward.exe"

echo.
echo   [1/3] Killing leftover processes ...
for %%P in (%NAMES%) do (
    taskkill /F /IM "%%P" >nul 2>&1
)
timeout /t 2 /nobreak >nul

echo   [2/3] Verifying ...
set STILL=0
for %%P in (%NAMES%) do (
    tasklist /NH /FI "IMAGENAME eq %%P" 2>nul | find /I "%%P" >nul && set STILL=1
)
if "!STILL!"=="1" (
    echo         WARNING - some processes are still alive.
    echo         Close them from Task Manager before testing.
) else (
    echo         OK - no leftovers.
)

echo   [3/3] Starting current build ...
if exist "LingGuangInk.App.exe" (
    start "" "LingGuangInk.App.exe"
    echo         Started.
    echo         Diagnostic log: nchittest.log
    echo         Settings file : %%LOCALAPPDATA%%\LingGuangInk\settings.ini
) else (
    echo         ERROR - LingGuangInk.App.exe not found. Run Build.bat first.
)

echo.
echo   Test checklist:
echo     - Auto hand/pen split ON   -^> finger scrolls, pen draws, no button press
echo     - Passthrough mode         -^> finger/mouse reaches windows below
echo     - Board button (More panel)-^> floating chalkboard opens
echo     - Toolbar menus            -^> single click opens the popup and it stays
echo.
timeout /t 4 /nobreak >nul
exit /b 0
