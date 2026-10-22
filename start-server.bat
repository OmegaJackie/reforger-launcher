@echo off
rem Arma Reforger dedicated server - restarts automatically if the server exits.
rem Usage: start-server.bat [config file in this folder]   (default: config.json)
rem Reforger Launcher's START SERVER runs this with the server folder it found in SERVER_DIR. Started by hand, it uses
rem the folder below: change it if the Arma Reforger Server tool is in another Steam library.
setlocal
if not defined SERVER_DIR set "SERVER_DIR=C:\Program Files (x86)\Steam\steamapps\common\Arma Reforger Server"
set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "CONFIG=%ROOT%\config.json"
if not "%~1"=="" set "CONFIG=%ROOT%\%~1"
if not exist "%SERVER_DIR%\ArmaReforgerServer.exe" (
  echo ArmaReforgerServer.exe isn't in "%SERVER_DIR%".
  echo Install "Arma Reforger Server" from Steam ^(Library - Tools^), or set SERVER_DIR at the top of this file.
  pause
  exit /b 1
)

title Arma Reforger Server
rem The server finds the base game data relative to its working directory.
cd /d "%SERVER_DIR%"

:loop
echo [%date% %time%] Starting Arma Reforger server...
"%SERVER_DIR%\ArmaReforgerServer.exe" ^
  -config "%CONFIG%" ^
  -profile "%ROOT%\profile" ^
  -addonDownloadDir "%ROOT%" ^
  -maxFPS 60 ^
  -logStats 60000 ^
  -backendlog ^
  -nothrow
echo [%date% %time%] Server exited with code %ERRORLEVEL%. Restarting in 15 seconds - press Ctrl+C to stop.
timeout /t 15
goto loop