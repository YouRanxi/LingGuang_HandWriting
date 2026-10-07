@echo off
setlocal
cd /d "%~dp0"

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "OUT=LingGuangInk.Build.exe"
set "DEPLOY=LingGuangInk.App.exe"
set "ICO=app.ico"

if not exist "%CSC%" (
    echo [ERROR] csc.exe not found: %CSC%
    exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:%ICO% /out:%OUT% ^
 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
 /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll ^
 /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll ^
 /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll ^
 /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll ^
 BoardWindow.cs HistoryManager.cs LaserTrailRenderer.cs MainWindow.cs Models.cs ^
 NativeMethods.cs PalmRejectionManager.cs Program.cs SettingsWindow.cs ^
 ShapeGenerator.cs ShapePreviewRenderer.cs SurfacePenHandler.cs TwoFingerTapDetector.cs ^
 VectorIcons.cs

if errorlevel 1 (
    echo [ERROR] Compilation failed.
    exit /b 1
)

rem Deploy to the ASCII-named entry point only. The old script also tried to
rem overwrite LingGuangInk.exe, which fails whenever a stale instance holds it.
copy /y "%OUT%" "%DEPLOY%" >nul
if errorlevel 1 (
    echo [WARN] Cannot overwrite %DEPLOY% - close the running instance and retry.
    exit /b 1
)

echo [OK] Built and deployed: %DEPLOY%
echo      Start it with Start.bat or Reset.bat
exit /b 0
