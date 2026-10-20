@echo off
rem Runs inside Windows Sandbox when flight-check.ps1 starts it: puts the seed save in place, starts the driver that flies the ship
rem and the watcher, runs the build in a window, copies the results out, then shuts the Sandbox down.
rem   flight-check.cmd [timed or steer]
set "GAME=C:\Users\WDAGUtilityAccount\Desktop\Starflight\Starflight.exe"
set "OUT=C:\Users\WDAGUtilityAccount\Desktop\Out\last-run"
set "DATA=%USERPROFILE%\AppData\LocalLow\DefaultCompany\Starflight"
set "MODE=%~1"
if "%MODE%"=="" set "MODE=timed"

if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"
mkdir "%DATA%"

rem silent: the Sandbox's own speakers are muted before the game starts (mute.txt says how)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0mute-sandbox.ps1" -Out "%OUT%" > nul

copy /y "%~dp0SaveGame0.json" "%DATA%\SaveGame0.json" > nul

start "" /min powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0watch-starflight.ps1" -Out "%OUT%"
start "" /min powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0flight-driver.ps1" -Out "%OUT%" -Mode %MODE%

echo Started %DATE% %TIME% mode %MODE% > "%OUT%\result.txt"
start "" /wait "%GAME%" -logFile "%OUT%\Player.log" -screen-fullscreen 0 -screen-width 1280 -screen-height 720
echo Exit code %ERRORLEVEL% at %DATE% %TIME% >> "%OUT%\result.txt"

if exist "%DATA%" robocopy "%DATA%" "%OUT%\LocalLow" /E /NFL /NDL /NJH /NJS > nul
echo Done >> "%OUT%\result.txt"

rem shut the Sandbox's Windows down, so that the Sandbox closes itself: a forced close of its window can leave its virtual machine
rem running, and the next Sandbox then skips its logon command
shutdown /s /t 3
