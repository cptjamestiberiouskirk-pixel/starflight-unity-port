# Runs one scenario of the headless play mode probe in the current directory (a Unity project root: the
# main checkout or a worktree of starflight-unity-port).
#
#   & "<this folder>\probe.ps1" -Scenario h6
#
# It copies ClaudeProbe.cs into Assets/, runs Unity in batch mode, prints the probe's log lines and removes
# the probe again. The probe never touches the real save files. Logs go to %TEMP%\starflight-probe.
# Unity is launched through unity-batch.ps1: one launch at a time on this PC, and none while the shared licensing
# client is stuck; then this script prints LICENSING STUCK and exits with 3, and the runners stop.
param(
	[string]$Scenario = 'h6',
	[string]$Name = '',
	# how long to wait for Unity before stopping it. A scenario that shows a hang on the old code never comes back, so give it a short limit.
	[int]$TimeoutSeconds = 420,
	# run with a graphics device (without -nographics), for a scenario that draws pictures (explosionpictures). Slower to start, and it still opens no window.
	[switch]$Graphics,
	# play the game's sounds (the runs are silent without it)
	[switch]$Sound
)

. (Join-Path $PSScriptRoot 'unity-batch.ps1')

$proj = (Get-Location).Path
$versionFile = Join-Path $proj 'ProjectSettings\ProjectVersion.txt'

if (-not (Test-Path -LiteralPath $versionFile)) {
	"Not a Unity project root: $proj"
	exit 2
}

$version = (Select-String -LiteralPath $versionFile -Pattern 'm_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
$unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"

if (-not (Test-Path -LiteralPath $unity)) {
	"Unity $version is not installed at $unity"
	exit 2
}

$logDir = Join-Path $env:TEMP 'starflight-probe'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

if (-not $Name) { $Name = "probe-$Scenario" }

$log = Join-Path $logDir "$Name.log"
$probeSource = Join-Path $PSScriptRoot 'ClaudeProbe.cs'
$probeTarget = Join-Path $proj 'Assets\ClaudeProbe.cs'

Copy-Item -LiteralPath $probeSource -Destination $probeTarget -Force

try {
	# Burst stays off: its JIT writes unsigned DLLs to Library\BurstCache\JIT, which Smart App Control blocks
	# (Code Integrity event 3033), and only the experimental TerrainJob of Test.unity uses it
	$unityArgs = @('-batchmode', '--burst-disable-compilation')
	if (-not $Graphics) { $unityArgs += '-nographics' }
	if ($Sound) { $unityArgs += '-probeSound' }
	$unityArgs += '-projectPath', "`"$proj`"", '-logFile', "`"$log`"", '-executeMethod', 'ClaudeProbe.Run', '-probeScenario', $Scenario
	# an earlier run's log of this name would be read as this run's if Unity wrote none, so it goes first
	# (one that cannot be deleted is open in another Unity, and this run stops here)
	if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force -ErrorAction Stop }

	# the probe has its own 120 s watchdog; this is the outer limit
	$run = Invoke-UnityBatch -Unity $unity -Arguments $unityArgs -Log $log -TimeoutSeconds $TimeoutSeconds

	$run.Messages
	"unity exit=$($run.ExitCode) elapsed=$($run.Seconds)s scenario=$Scenario log=$log"
}
finally {
	[IO.File]::Delete($probeTarget)
	[IO.File]::Delete("$probeTarget.meta")
}

# licensing is stuck: nothing ran, or the run was stopped before the probe started (the runners stop on exit code 3)
if ($run.LicensingStuck) {
	"CHECK FAIL licensing stuck: no scenario ran"
	exit 3
}

$logWritten = Test-Path -LiteralPath $log

if ($logWritten) {
	"--- compiler errors ---"
	$errs = Select-String -LiteralPath $log -Pattern 'error CS\d+|Scripts have compiler errors'
	if ($errs) { $errs | Select-Object -First 20 | ForEach-Object { $_.Line } } else { 'none' }

	"--- probe ---"
	Select-String -LiteralPath $log -Pattern '^\[ClaudeProbe\]' | ForEach-Object { if ($_.Line.Length -gt 600) { $_.Line.Substring(0, 600) } else { $_.Line } }
}
else {
	# Unity quit before it wrote a log, so this run has nothing to show: a failed check for the runners to count, and no RESULT line
	$exitCode = if ($run) { $run.ExitCode } else { 'none, not started' }
	"CHECK FAIL no log written: unity exit=$exitCode"
}

"--- git status ---"
git status --short

if (-not $logWritten) { exit 1 }
