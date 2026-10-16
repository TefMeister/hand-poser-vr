@echo off
rem Builds Hand Poser into dist\: the window (C#, the compiler that ships with Windows' .NET Framework 4) with the in-game
rem script and the icon beside it. Run install.ps1 afterwards for the desktop icon. Then `dist\HandPoser.exe --selftest
rem build\selftest` checks the parts that need no game.
setlocal
set HERE=%~dp0
if not exist "%HERE%dist" mkdir "%HERE%dist"
if not exist "%HERE%build" mkdir "%HERE%build"

"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /win32icon:"%HERE%app\hand_poser.ico" ^
   /out:"%HERE%dist\HandPoser.exe" "%HERE%app\*.cs"
if errorlevel 1 (echo APP BUILD FAILED & exit /b 1)
copy /y "%HERE%app\hand_poser.ico" "%HERE%dist\" >nul
copy /y "%HERE%reframework\autorun\hand_poser.lua" "%HERE%dist\" >nul
echo BUILD OK: %HERE%dist
