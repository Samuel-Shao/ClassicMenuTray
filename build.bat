@echo off
setlocal

set "ROOT=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found.
  exit /b 1
)

if not exist "%ROOT%dist" mkdir "%ROOT%dist"

"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /out:"%ROOT%dist\ClassicMenuTray.exe" ^
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
  /win32manifest:"%ROOT%src\app.manifest" "%ROOT%src\TrayApp.cs"
if errorlevel 1 exit /b 1

"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /out:"%ROOT%dist\ClassicMenuHelper.exe" ^
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
  /win32manifest:"%ROOT%src\helper.manifest" "%ROOT%src\Helper.cs"
if errorlevel 1 exit /b 1

echo Build OK.
dir /b "%ROOT%dist"
exit /b 0
