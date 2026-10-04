# Headless Unity compile check for the current directory (a Unity project root: the main checkout or a
# worktree of starflight-unity-port). Fails if that same directory is open in a GUI Editor.
#
#   & "<this folder>\compile-check.ps1" -Name some-label
#
# Logs go to %TEMP%\starflight-probe.
param([string]$Name = 'compile')

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

$log = Join-Path $logDir "$Name.log"

$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $unity -ArgumentList '-batchmode', '-nographics', '-quit', '-projectPath', "`"$proj`"", '-logFile', "`"$log`"" -Wait -PassThru

"unity exit=$($p.ExitCode) elapsed=$([int]$sw.Elapsed.TotalSeconds)s log=$log"

"--- compiler / package errors ---"
$errs = Select-String -LiteralPath $log -Pattern 'error CS\d+|Scripts have compiler errors|Aborting batchmode|An error occurred while resolving packages|\[Package Manager\].*(error|cannot|failed)|Assembly with name .* already exists'
if ($errs) { $errs | Select-Object -First 40 | ForEach-Object { if ($_.Line.Length -gt 400) { $_.Line.Substring(0, 400) } else { $_.Line } } } else { 'none' }

"--- tundra ---"
Select-String -LiteralPath $log -Pattern 'Tundra build' | ForEach-Object { $_.Line }

"--- packages ---"
Select-String -LiteralPath $log -Pattern '\[Package Manager\] Registered \d+ packages' | ForEach-Object { $_.Line }

"--- warnings in project code ---"
$w = Select-String -LiteralPath $log -Pattern 'Assets[\\/](Scripts|Planet Generator|Tools|Shaders)[\\/].*warning CS\d+'
if ($w) { $w | Select-Object -First 20 | ForEach-Object { $_.Line } } else { 'none' }

"--- git status ---"
git status --short
