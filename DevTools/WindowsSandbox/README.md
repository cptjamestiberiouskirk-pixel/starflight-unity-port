# Testing a build in Windows Sandbox

`test-build.ps1` tries a Windows build of the game on a clean copy of Windows: Windows Sandbox, the throwaway virtual machine built into Windows 10 and 11 Pro, Enterprise and Education. It shows whether a build starts, plays, saves and quits on a PC that has never had Unity or the project on it, and what it writes on the way. Nothing reaches the real saves: the game saves inside the Sandbox, and everything in there is thrown away when its window is closed.

It is also the way to run a build on a PC where Smart App Control is on. A build made on your own PC is not signed, so Smart App Control in enforce mode blocks `Starflight.exe` there ("An Application Control policy has blocked this file"). The Sandbox starts as a fresh Windows, with Smart App Control in evaluation mode, which blocks nothing; every run records the mode it found in `smart-app-control.txt`.

## Turning Windows Sandbox on (once)

In a PowerShell window run as administrator:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All
```

then restart Windows. (Or tick **Windows Sandbox** in `optionalfeatures`.) It needs virtualization switched on in the firmware, which Task Manager shows under Performance > CPU.

## Running it

Make a build (see "Make a build" in the main README), then from the project root:

```powershell
& "DevTools\WindowsSandbox\test-build.ps1" -BuildDir "Build"
```

The Sandbox opens after 20 to 30 seconds and the game starts by itself in a window, with a command window behind it and the watcher's PowerShell window minimized. Play, then quit the game with Escape and **Quit**: the script then copies the saves and the Code Integrity events out. Close the Sandbox window last.

| Option | What it does |
|---|---|
| `-BuildDir` | The folder with `Starflight.exe` in it (required) |
| `-Width`, `-Height` | The size of the game's window, 1280 x 720 by default |
| `-MemoryMB` | Memory for the Sandbox, 8192 by default |
| `-Gpu` | Share the graphics card with the Sandbox (off by default, see below) |
| `-NoLaunch` | Only write `%TEMP%\starflight-sandbox\Starflight.wsb`, to start by double-clicking it later |

Only one Sandbox can run at a time, so the script stops if one is open.

## Results

Everything goes to `%TEMP%\starflight-sandbox\out\last-run`, replacing the last run's:

| File | What it holds |
|---|---|
| `Player.log` | The game's log, written there directly, so it survives a crash of the Sandbox |
| `result.txt` | When the game started, and its exit code and time once it ends |
| `watch.txt` | Every 10 seconds, the game's CPU time, whether Windows sees it responding, its memory and its window title. CPU time that keeps growing and "responding True" mean the game is running, even if the picture looks stuck. |
| `smart-app-control.txt` | Smart App Control's state in the Sandbox: 1 enforce, 2 evaluation, 0 or missing off |
| `LocalLow\` | The save slots and their `.bak` backups, copied once the game ends |
| `code-integrity.txt` | The last 30 Code Integrity events, copied once the game ends. A blocked exe or DLL shows as event 3033 or 3077, one that was only audited as 3076 |

If the Sandbox is closed while the game is still running, only the first four are there.

## What the first runs showed

These were found with the 1.1.0 build on Windows 11 26H2 (build 26300) with AMD Radeon 780M and RX 9070 XT graphics, in October 2026.

- **The graphics card is not shared by default.** With it shared, the Sandbox's own Windows crashed 13 seconds after the game started (stop code 0x3B, which the Sandbox reports as error 0x80370106). Without it, Windows draws in software (Microsoft Basic Render Driver): the game falls back from Direct3D 12 to 11, since the project's Direct3D 12 filter wants 3.6 GB of graphics memory and the software renderer reports 511 MB, and it runs, slowly. Try `-Gpu` again on another PC or after a driver or Windows update.
- **The game always runs in a window.** In full screen the picture stayed on the intro's first page while the game ran on underneath: its log showed the Starport loading and the game saving after key presses. In a window it updates.
- **Software rendering takes about 14 of 16 processor cores** for as long as the game runs (`watch.txt` shows it). Everything else on the PC slows down, headless probe runs included, and their checks that measure real time can fail: close the Sandbox when you are done.
- **What it cannot test** is how the game looks and performs on a real graphics card. For that, play the build on a PC where it may run.

## Checking the flight without anyone at the keyboard

`flight-check.ps1` plays a build's flight by itself and times it, so a build can be checked before a release without anyone playing it:

```powershell
& "DevTools\WindowsSandbox\flight-check.ps1" -BuildDir "Build" -SaveFile "$env:TEMP\starflight-probe\playthrough\stage-2.json"
```

The save can be any of the game's own with a crew and a named ship; a playthrough stage save from the probe does. For each engine class (`-Classes`, 1, 3 and 5 by default) it makes a copy of the save with those engines, in Arth's star system 4000 units west of the sun (`make_flight_seed.py`), and runs the build from it in its own Sandbox. Inside, `flight-driver.ps1` sends the keys a player would press: Enter through the intro, then Navigation, Maneuver and the right arrow, held until the game's log shows the ship leaving the system, 12,192 units on. The time is set against what the speed rules give (`PlayerShip.GetTopSpeed` and `GetRampSeconds`), and a time within 0.3 s passes. A last run (left out with `-NoSteering`) takes a screenshot every quarter of a second as the ship turns from east to north, to look at: the ship should face north at once.

The results go to a new folder for each check, `%TEMP%\starflight-sandbox\flight-<date>-<time>`: `summary.txt`, and for each run the driver's `flight.txt`, its screenshots in `shots\` and the game's `Player.log`. The 1.3.0 build gave 14.48, 6.84 and 4.51 s for classes 1, 3 and 5, against 14.49, 6.86 and 4.45 s expected; the 1.2.0 build, whose ships all fly at the original's speed, took 4.49 s with class 1 engines.

Each run takes about two and a half minutes and needs the PC to itself while it runs (see the cores above). Two things learned while making it, which the scripts deal with:

- **A short folder path.** With the Sandbox's scripts under a long path (the scratchpad of a Claude Code session), the Sandbox started but never ran its logon command. They go under `%TEMP%\starflight-sandbox`.
- **The Sandbox closes itself.** Closing a Sandbox's window from outside (stopping `WindowsSandboxRemoteSession`) left its virtual machine (`vmmemWindowsSandbox`) and a `WindowsSandboxServer` running, and the next Sandbox then skipped its logon command. `flight-check.cmd` ends with `shutdown /s /t 3`, and `flight-check.ps1` waits for the window to go. A Sandbox whose scripts never start is closed and started again, three times at most.

## Files

| File | What it is |
|---|---|
| `test-build.ps1` | Runs on the host: checks the build and the Sandbox, writes the configuration and starts it |
| `run-starflight.cmd` | Runs in the Sandbox when it starts: runs the game in a window and collects the results |
| `watch-starflight.ps1` | Runs in the Sandbox beside the game: writes `watch.txt` |
| `flight-check.ps1` | Runs on the host: one Sandbox for each engine class, timed, and one for the steering screenshots |
| `make_flight_seed.py` | Runs on the host: the save a flight check starts from |
| `flight-check.cmd` | Runs in the Sandbox for a flight check: puts the save in place, runs the game and the driver, shuts the Sandbox down |
| `flight-driver.ps1` | Runs in the Sandbox beside the game: presses the keys, times the flight from the game's log, takes the screenshots |

`test-build.ps1` copies the two Sandbox scripts to `%TEMP%\starflight-sandbox\scripts` for each run and maps three folders into the Sandbox's desktop: the build (read-only), those scripts (read-only) and `out` (the only one the Sandbox can write to). The save folder in the Sandbox follows the project's company and product names (`DefaultCompany`, `Starflight`); `run-starflight.cmd` has to follow them if they ever change.
