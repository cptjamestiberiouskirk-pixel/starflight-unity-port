# Makes the sensor picture of one vessel from a screenshot of the original, puts it into the project and (with -Slot) into the sensor display's slots.
# Run from the project root (the Unity project need not be closed: an open Editor imports the new files when it gets focus).
#   & "DevTools\SensorPictures\make.ps1" -Vessel "Elowan Transport" -Slot 5 -Tolerance 0.6
param(
	# the vessel's name as in the file names: "Sensors - <Vessel>.png", "Research/Screenshots/Ships/<Vessel> Analysis.png"
	[Parameter(Mandatory = $true)][string]$Vessel,
	# the scan type (vessel id) whose slots get the picture; leave it out to make the files only
	[int]$Slot = -1,
	# how far (in pixels of the 320 x 200 screen) a simplified outline may stray from the traced pixels: 0.4 keeps single steps (machines), 0.6 turns them into slopes (organic hulls)
	[double]$Tolerance = 0.6,
	# canvas pixels per pixel of the 320 x 200 screen; the defaults are measured on the existing Spemin Scout picture (10.8 x 12.6, about the 1.2 pixel aspect of the original)
	[double]$ScaleX = 10.8,
	[double]$ScaleY = 12.96,
	# keep pixels that touch only at a corner apart instead of joining them
	[switch]$SplitDiagonals,
	# another screenshot than "<Vessel> Analysis.png"
	[string]$Screenshot = ""
)

$ErrorActionPreference = "Stop"

# the C# needs System.Drawing as Windows PowerShell 5.1 has it (PowerShell 7 splits it over private assemblies): run there
if ($PSVersionTable.PSEdition -eq "Core") {
	$invariant = [Globalization.CultureInfo]::InvariantCulture
	$arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath, "-Vessel", $Vessel, "-Slot", $Slot, "-Tolerance", $Tolerance.ToString($invariant), "-ScaleX", $ScaleX.ToString($invariant), "-ScaleY", $ScaleY.ToString($invariant))
	if ($SplitDiagonals) { $arguments += "-SplitDiagonals" }
	if ($Screenshot -ne "") { $arguments += @("-Screenshot", $Screenshot) }
	& powershell.exe @arguments
	exit $LASTEXITCODE
}

Add-Type -Path "$PSScriptRoot\SensorPicture.cs", "$PSScriptRoot\Vectorize.cs" -ReferencedAssemblies System.Drawing

$root = (Get-Location).Path
if ($Screenshot -eq "") { $Screenshot = Join-Path $root "Research\Screenshots\Ships\$Vessel Analysis.png" }
$folder = Join-Path $root "Assets\Game Objects\UI\Sensors"
$background = Join-Path $folder "Sensors - $Vessel.png"
$mask = Join-Path $folder "Sensors - $Vessel Mask.png"

if (-not (Test-Path $Screenshot)) { throw "no screenshot at $Screenshot" }
if (-not (Test-Path $folder)) { throw "run this from the project root (no $folder)" }

[SensorPicture]::Make($Screenshot, $background, $mask, $ScaleX, $ScaleY, $Tolerance, -not $SplitDiagonals.IsPresent)

# new files get the import settings of the Spemin Scout picture and a new guid; files that are there keep theirs
foreach ($pair in @(@($background, "Sensors - Spemin Scout.png.meta"), @($mask, "Sensors - Spemin Scout Mask.png.meta"))) {
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
	foreach ($pair in @(@("  m_backgroundTextures:", $background), @("  m_maskTextures:", $mask))) {
		$found = @(for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].TrimEnd("`r") -eq $pair[0]) { $i } })
		if ($found.Count -ne 1) { throw "expected one '$($pair[0])' in the scene, found $($found.Count)" }
		$index = $found[0] + 1 + $Slot
		if (-not $lines[$index].StartsWith("  - {")) { throw "slot $Slot of '$($pair[0])' is not an array entry: $($lines[$index])" }
		$ending = if ($lines[$index].EndsWith("`r")) { "`r" } else { "" }
		$lines[$index] = "  - {fileID: 2800000, guid: $(Get-Guid "$($pair[1]).meta"), type: 3}$ending"
	}
	[IO.File]::WriteAllText($scene, ($lines -join "`n"))
	"slot $Slot wired in Spaceflight.unity"
}

$previews = Join-Path $PSScriptRoot "previews"
if (-not (Test-Path $previews)) { New-Item -ItemType Directory $previews | Out-Null }
$sheet = Join-Path $previews (($Vessel.ToLower() -replace " ", "-") + ".png")
[SensorPicture]::Sheet($Screenshot, $background, $mask, $sheet)
"review sheet $sheet"
