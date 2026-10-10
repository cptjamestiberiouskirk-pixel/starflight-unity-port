# Makes the debris picture of one vessel from its sensor picture, puts it into the project and (with -Slot) into the sensor display's debris slots.
# Run from the project root, with a Python that has Pillow (process_texture_debris.py needs it):
#   & "DevTools\SensorPictures\make-debris.ps1" -Vessel "Elowan Transport" -Slot 5 -Python "C:\path\to\python.exe"
# The settings are the script's "Light Battle Damage" preset, which reproduces all eight debris pictures made before this script, and their masks,
# pixel for pixel (the Spemin ships, the Mechan scout, the Velox drone, the Mysterion, the Minstrel, and the Nomad probe from its first picture;
# checked on 2026-10-09). The seed is set for every picture, so a picture always gives the same debris.
param(
	# the vessel's name as in "Assets/Game Objects/UI/Sensors/Sensors - <Vessel>.png"
	[Parameter(Mandatory = $true)][string]$Vessel,
	# the vessel id whose entries in m_debrisBackgroundTextures and m_debrisMaskTextures get the picture; leave it out to make the files only
	[int]$Slot = -1,
	# a Python with Pillow
	[string]$Python = "python"
)

$ErrorActionPreference = "Stop"

$root = (Get-Location).Path
$source = Join-Path $root "Assets\Game Objects\UI\Sensors\Sensors - $Vessel.png"
$folder = Join-Path $root "Assets\Game Objects\UI\Sensors Debris"
$background = Join-Path $folder "Sensors - $($Vessel)_debris.png"
$mask = Join-Path $folder "Sensors - $($Vessel)_debris_mask.png"

if (-not (Test-Path $source)) { throw "no sensor picture at $source" }
if (-not (Test-Path $folder)) { throw "run this from the project root (no $folder)" }

# the script prints check marks, which a console in another code page cannot show
$env:PYTHONIOENCODING = "utf-8"
& $Python (Join-Path $root "process_texture_debris.py") -s 12345 -d 25 --scatter --scatter-amount 2 --min-piece-size 150 --invert-mask $source $background | Out-Null
if ($LASTEXITCODE -ne 0) { throw "process_texture_debris.py failed (exit code $LASTEXITCODE)" }
if (-not ((Test-Path $background) -and (Test-Path $mask))) { throw "process_texture_debris.py did not write $background and its mask" }

# new files get the import settings of the Spemin Scout debris and a new guid; files that are there keep theirs
foreach ($pair in @(@($background, "Sensors - Spemin Scout_debris.png.meta"), @($mask, "Sensors - Spemin Scout_debris_mask.png.meta"))) {
	$meta = "$($pair[0]).meta"
	if (-not (Test-Path $meta)) {
		$template = [IO.File]::ReadAllText((Join-Path $folder $pair[1]))
		$guid = [guid]::NewGuid().ToString("N")
		[IO.File]::WriteAllText($meta, ($template -replace "(?m)^guid: [0-9a-f]{32}", "guid: $guid"))
		"new meta $(Split-Path $meta -Leaf) guid $guid"
	}
}

function Get-Guid([string]$meta) {
	$line = (Get-Content $meta | Where-Object { $_ -match "^guid: " } | Select-Object -First 1)
	return $line.Substring(6).Trim()
}

if ($Slot -ge 0) {
	$scene = Join-Path $root "Assets\Scenes\Spaceflight.unity"
	$lines = [IO.File]::ReadAllText($scene) -split "`n"
	foreach ($pair in @(@("  m_debrisBackgroundTextures:", $background), @("  m_debrisMaskTextures:", $mask))) {
		$found = @(for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].TrimEnd("`r") -eq $pair[0]) { $i } })
		if ($found.Count -ne 1) { throw "expected one '$($pair[0])' in the scene, found $($found.Count)" }
		$index = $found[0] + 1 + $Slot
		if (-not $lines[$index].StartsWith("  - {")) { throw "slot $Slot of '$($pair[0])' is not an array entry: $($lines[$index])" }
		$ending = if ($lines[$index].EndsWith("`r")) { "`r" } else { "" }
		$lines[$index] = "  - {fileID: 2800000, guid: $(Get-Guid "$($pair[1]).meta"), type: 3}$ending"
	}
	[IO.File]::WriteAllText($scene, ($lines -join "`n"))
	"debris slot $Slot wired in Spaceflight.unity"
}

"debris $(Split-Path $background -Leaf)"
