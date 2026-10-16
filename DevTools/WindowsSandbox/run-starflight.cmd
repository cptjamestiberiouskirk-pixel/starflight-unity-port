@echo off
rem Runs inside Windows Sandbox when test-build.ps1 starts it: runs the build mapped
rem to the desktop in a window, then copies its saves and the Code Integrity events
rem to the Out folder, which is a folder on the host.
rem   run-starflight.cmd [width] [height]
set "GAME=C:\Users\WDAGUtilityAccount\Desktop\Starflight\Starflight.exe"
set "OUT=C:\Users\WDAGUtilityAccount\Desktop\Out\last-run"
set "DATA=%USERPROFILE%\AppData\LocalLow\DefaultCompany\Starflight"
set "WIDTH=%~1"
set "HEIGHT=%~2"
if "%WIDTH%"=="" set "WIDTH=1280"
if "%HEIGHT%"=="" set "HEIGHT=720"

if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"

rem Is Smart App Control on in here? 1 = enforce, 2 = evaluation, 0 or missing = off
reg query "HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy" /v VerifiedAndReputablePolicyState > "%OUT%\smart-app-control.txt" 2>&1

rem Every 10 seconds, whether the game runs and answers, in watch.txt
start "" /min powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0watch-starflight.ps1" -Out "%OUT%"

echo Started %DATE% %TIME% > "%OUT%\result.txt"
rem The log goes straight to the host folder, so it survives a crash of the Sandbox.
rem A window, not full screen: in full screen the picture stopped updating.
start "" /wait "%GAME%" -logFile "%OUT%\Player.log" -screen-fullscreen 0 -screen-width %WIDTH% -screen-height %HEIGHT%
echo Exit code %ERRORLEVEL% at %DATE% %TIME% >> "%OUT%\result.txt"

rem Any save slots written during the run, with their backups
if exist "%DATA%" robocopy "%DATA%" "%OUT%\LocalLow" /E /NFL /NDL /NJH /NJS > nul

rem A blocked exe or DLL shows up here as event 3033 or 3077, one that was only audited as 3076
wevtutil qe Microsoft-Windows-CodeIntegrity/Operational /c:30 /rd:true /f:text > "%OUT%\code-integrity.txt" 2>&1

echo Done. The results are in the Out folder on the desktop (%%TEMP%%\starflight-sandbox\out\last-run on the host).
