# Sets up the console's system map in Spaceflight.unity, headless, in the current directory (a Unity project root:
# a worktree of starflight-unity-port; the project must not be open in an Editor).
#
#   & "<this folder>\wire.ps1"
#
# It copies SystemMapWiring.cs into Assets/, runs Unity in batch mode, prints the tool's log lines and removes the tool
# again. Logs go to %TEMP%\starflight-probe.
param(
	[int]$TimeoutSeconds = 600
)

# Unity is launched through the probe's unity-batch.ps1: one launch at a time on this PC, none while the licensing client is stuck (exit code 3)
. (Join-Path $PSScriptRoot '..\HeadlessProbe\unity-batch.ps1')

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

$log = Join-Path $logDir 'system-map-wiring.log'
$toolSource = Join-Path $PSScriptRoot 'SystemMapWiring.cs'
$toolTarget = Join-Path $proj 'Assets\SystemMapWiring.cs'

Copy-Item -LiteralPath $toolSource -Destination $toolTarget -Force

try {
	# an earlier run's log would be read as this run's (the licensing guard reads it too), so it goes first; Burst stays off, as in the probe
	if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force -ErrorAction Stop }

	$run = Invoke-UnityBatch -Unity $unity -Arguments @('-batchmode', '-nographics', '--burst-disable-compilation', '-projectPath', "`"$proj`"", '-logFile', "`"$log`"", '-executeMethod', 'SystemMapWiring.Run') -Log $log -TimeoutSeconds $TimeoutSeconds

	$run.Messages
	"unity exit=$($run.ExitCode) elapsed=$($run.Seconds)s log=$log"
}
finally {
	[IO.File]::Delete($toolTarget)
	[IO.File]::Delete("$toolTarget.meta")
}

if ($run.LicensingStuck) {
	exit 3
}

"--- compiler errors ---"
$errs = Select-String -LiteralPath $log -Pattern 'error CS\d+|Scripts have compiler errors'
if ($errs) { $errs | Select-Object -First 20 | ForEach-Object { $_.Line } } else { 'none' }

"--- tool ---"
Select-String -LiteralPath $log -Pattern '^\[SystemMapWiring\]' | ForEach-Object { $_.Line }

"--- git status ---"
git status --short
