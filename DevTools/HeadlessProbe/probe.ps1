# Runs one scenario of the headless play mode probe in the current directory (a Unity project root: the
# main checkout or a worktree of starflight-unity-port).
#
#   & "<this folder>\probe.ps1" -Scenario h6
#
# It copies ClaudeProbe.cs into Assets/, runs Unity in batch mode, prints the probe's log lines and removes
# the probe again. The probe never touches the real save files. Logs go to %TEMP%\starflight-probe.
param(
	[string]$Scenario = 'h6',
	[string]$Name = ''
)

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
	$sw = [Diagnostics.Stopwatch]::StartNew()
	$p = Start-Process -FilePath $unity -ArgumentList '-batchmode', '-nographics', '-projectPath', "`"$proj`"", '-logFile', "`"$log`"", '-executeMethod', 'ClaudeProbe.Run', '-probeScenario', $Scenario -PassThru

	# the probe has its own 120 s watchdog; this is the outer limit
	if (-not $p.WaitForExit(420000)) {
		"unity still running after 420s, stopping pid $($p.Id)"
		Stop-Process -Id $p.Id -Force
		$p.WaitForExit(30000) | Out-Null
	}

	"unity exit=$($p.ExitCode) elapsed=$([int]$sw.Elapsed.TotalSeconds)s scenario=$Scenario log=$log"
}
finally {
	[IO.File]::Delete($probeTarget)
	[IO.File]::Delete("$probeTarget.meta")
}

"--- compiler errors ---"
$errs = Select-String -LiteralPath $log -Pattern 'error CS\d+|Scripts have compiler errors'
if ($errs) { $errs | Select-Object -First 20 | ForEach-Object { $_.Line } } else { 'none' }

"--- probe ---"
Select-String -LiteralPath $log -Pattern '^\[ClaudeProbe\]' | ForEach-Object { if ($_.Line.Length -gt 600) { $_.Line.Substring(0, 600) } else { $_.Line } }

"--- git status ---"
git status --short
