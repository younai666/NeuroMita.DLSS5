@echo off
rem ============================================================================
rem  NeuroMita.DLSS5 - one click installer
rem
rem  Double click this file (or drop the whole package into the game folder and
rem  run it). It installs the BepInEx plugin, merges the recommended ReShade
rem  settings and then verifies the result. No administrator rights are needed.
rem
rem  Options are forwarded to tools\Install-DLSS5.ps1, e.g.:
rem     install.bat -PatchSplash          remove the ReShade startup banner
rem     install.bat -InstallStack         also install ReShade/Feeder/RenoDX
rem     install.bat -Uninstall            remove the plugin again
rem ============================================================================
setlocal EnableExtensions
chcp 65001 >nul
title NeuroMita.DLSS5 installer

set "PKG=%~dp0"
set "GAME="

rem 1) the package sits inside the game folder?
if exist "%PKG%NeuroMita.exe" set "GAME=%PKG%"

rem 2) a game.dir file next to the package?
if not defined GAME if exist "%PKG%game.dir" (
    set /p GAMEDIR=<"%PKG%game.dir"
    if exist "%GAMEDIR%\NeuroMita.exe" set "GAME=%GAMEDIR%"
)

rem 3) ask the user
if not defined GAME (
    echo.
    echo NeuroMita.exe was not found next to this installer.
    set /p GAMEDIR=Path to the game folder ^(contains NeuroMita.exe^): 
    if exist "%GAMEDIR%\NeuroMita.exe" set "GAME=%GAMEDIR%"
)

if not defined GAME (
    echo.
    echo ERROR: could not locate the game folder.
    pause
    exit /b 1
)

echo.
echo Game folder: %GAME%
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%PKG%tools\Install-DLSS5.ps1" -GameDir "%GAME%" %*
set "RC=%ERRORLEVEL%"

echo.
if "%RC%"=="0" (echo Installation finished.) else (echo Installation finished with issues ^(exit %RC%^).)
pause
exit /b %RC%
