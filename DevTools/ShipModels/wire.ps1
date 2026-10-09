# Puts the ship models of a manifest into Spaceflight.unity, headless, in the current directory (a Unity project root:
# a worktree of starflight-unity-port; the project must not be open in an Editor).
#
#   & "<this folder>\wire.ps1" -Manifest "DevTools\ShipModels\manifest-veloxi.json"
#
# It copies ShipModelWiring.cs into Assets/, runs Unity in batch mode, prints the tool's log lines and removes the tool
# again. Logs go to %TEMP%\starflight-probe.
param(
	[Parameter(Mandatory = $true)][string]$Manifest,
	[int]$TimeoutSeconds = 600
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

$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$logDir = Join-Path $env:TEMP 'starflight-probe'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$log = Join-Path $logDir 'ship-model-wiring.log'
$toolSource = Join-Path $PSScriptRoot 'ShipModelWiring.cs'
$toolTarget = Join-Path $proj 'Assets\ShipModelWiring.cs'

Copy-Item -LiteralPath $toolSource -Destination $toolTarget -Force

try {
	$sw = [Diagnostics.Stopwatch]::StartNew()
	$p = Start-Process -FilePath $unity -ArgumentList '-batchmode', '-nographics', '-projectPath', "`"$proj`"", '-logFile', "`"$log`"", '-executeMethod', 'ShipModelWiring.Run', '-shipManifest', "`"$manifestPath`"" -PassThru

	if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
		"unity still running after ${TimeoutSeconds}s, stopping pid $($p.Id)"
		Stop-Process -Id $p.Id -Force
		$p.WaitForExit(30000) | Out-Null
	}

	"unity exit=$($p.ExitCode) elapsed=$([int]$sw.Elapsed.TotalSeconds)s log=$log"
}
finally {
	[IO.File]::Delete($toolTarget)
	[IO.File]::Delete("$toolTarget.meta")
}

"--- compiler errors ---"
$errs = Select-String -LiteralPath $log -Pattern 'error CS\d+|Scripts have compiler errors'
if ($errs) { $errs | Select-Object -First 20 | ForEach-Object { $_.Line } } else { 'none' }

"--- tool ---"
Select-String -LiteralPath $log -Pattern '^\[ShipModelWiring\]' | ForEach-Object { $_.Line }

"--- git status ---"
git status --short
