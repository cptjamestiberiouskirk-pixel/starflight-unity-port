# Checks the ship's flight in a Windows build, in Windows Sandbox, with nobody at the keyboard.
#
#   & "<this folder>\flight-check.ps1" -BuildDir "Build" -SaveFile "$env:TEMP\starflight-probe\playthrough\stage-2.json"
#
# For each engine class it starts the build from a copy of the save, put in Arth's star system 4000 units west of the sun with those
# engines, and has flight-driver.ps1 press Navigation, Maneuver and the right arrow until the ship leaves the system (12,192 units), timed
# from the game's own log. The times are set against what the speed rules give (PlayerShip.GetTopSpeed, GetRampSeconds). A last run
# takes screenshots of a turn from east to north, to look at. One Sandbox at a time; each closes itself when its run is done.
param(
	# the folder with Starflight.exe in it
	[Parameter( Mandatory = $true )]
	[string]$BuildDir,
	# a save with a crew and a named ship (any of the game's own saves; a playthrough stage save from the probe does)
	[Parameter( Mandatory = $true )]
	[string]$SaveFile,
	# the engine classes to time
	[int[]]$Classes = @( 1, 3, 5 ),
	# leave out the steering screenshots
	[switch]$NoSteering,
	# memory for the Sandbox, in MB
	[ValidateRange( 2048, 65536 )]
	[int]$MemoryMB = 8192
)

$sandboxExe = Join-Path $env:windir 'System32\WindowsSandbox.exe'
if ( -not ( Test-Path -LiteralPath $sandboxExe ) ) { "Windows Sandbox is not turned on (see README.md)."; exit 2 }
if ( -not ( Test-Path -LiteralPath ( Join-Path $BuildDir 'Starflight.exe' ) ) ) { "No Starflight.exe in $BuildDir"; exit 2 }
if ( -not ( Test-Path -LiteralPath $SaveFile ) ) { "No such save: $SaveFile"; exit 2 }
if ( Get-Process -Name WindowsSandboxRemoteSession, WindowsSandboxClient -ErrorAction SilentlyContinue ) { "A Windows Sandbox is already open, and only one can run at a time. Close it first."; exit 2 }

$build = ( Resolve-Path -LiteralPath $BuildDir ).Path

# short paths: a mapped folder under a long path left the Sandbox's logon command unrun
$root = Join-Path $env:TEMP 'starflight-sandbox'
$scripts = Join-Path $root 'flight-scripts'
$out = Join-Path $root 'flight-out'
$results = Join-Path $root ( 'flight-' + ( Get-Date -Format 'yyyyMMdd-HHmmss' ) )
New-Item -ItemType Directory -Force -Path $scripts, $out, $results | Out-Null

# the Sandbox's scripts, with Windows line endings for cmd.exe
foreach ( $name in 'flight-check.cmd', 'flight-driver.ps1', 'watch-starflight.ps1', 'mute-sandbox.ps1' ) {
	$text = [IO.File]::ReadAllText( ( Join-Path $PSScriptRoot $name ) ) -replace "`r?`n", "`r`n"
	[IO.File]::WriteAllText( ( Join-Path $scripts $name ), $text, [Text.Encoding]::ASCII )
}

# the time to the system's edge the speed rules give (PlayerShip): class 5's top speed is the original's at GOG's pace, 12 units of 20.48 a step
# at 12.2 steps a second, class 1's a third of it; from a twelfth of it to all of it in 5 s over the acceleration in G, between 0.25 and 5 s
function Predicted( [int]$class, [int]$acceleration ) {
	$top = 12.2 * 12 * ( 8192.0 / 400.0 ) * ( 1.0 / 3.0 + ( 2.0 / 3.0 ) * ( [Math]::Min( [Math]::Max( $class, 1 ), 5 ) - 1 ) / 4.0 )
	$ramp = if ( $acceleration -gt 0 ) { [Math]::Min( [Math]::Max( 5.0 / $acceleration, 0.25 ), 5.0 ) } else { 5.0 }
	$distance = 4000.0 + 8192.0
	$rampDistance = ( $top / 12.0 + $top ) / 2.0 * $ramp
	if ( $rampDistance -ge $distance ) { return $ramp * $distance / $rampDistance }
	return $ramp + ( $distance - $rampDistance ) / $top
}

$desktop = 'C:\Users\WDAGUtilityAccount\Desktop'

# one run in its own Sandbox: gives back the driver's report, or nothing if the Sandbox never ran the scripts
function Run( [string]$mode, [string]$label ) {
	$wsb = @"
<Configuration>
  <vGPU>Disable</vGPU>
  <MemoryInMB>$MemoryMB</MemoryInMB>
  <MappedFolders>
    <MappedFolder><HostFolder>$( [Security.SecurityElement]::Escape( $build ) )</HostFolder><SandboxFolder>$desktop\Starflight</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$( [Security.SecurityElement]::Escape( $scripts ) )</HostFolder><SandboxFolder>$desktop\Scripts</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$( [Security.SecurityElement]::Escape( $out ) )</HostFolder><SandboxFolder>$desktop\Out</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>cmd.exe /c $desktop\Scripts\flight-check.cmd $mode</Command></LogonCommand>
</Configuration>
"@
	[xml]$wsb | Out-Null
	$wsbPath = Join-Path $root 'flight.wsb'
	[IO.File]::WriteAllText( $wsbPath, $wsb, [Text.Encoding]::UTF8 )

	$lastRun = Join-Path $out 'last-run'
	$session = @()

	# a Sandbox started right after another has sometimes not run its logon command: then it is closed and started again, three tries at most
	for ( $try = 1; $try -le 3; $try++ ) {
		Remove-Item -Recurse -Force $lastRun -ErrorAction SilentlyContinue
		$before = @( Get-Process -Name WindowsSandboxRemoteSession, WindowsSandboxClient -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id )
		Start-Process -FilePath $wsbPath
		Start-Sleep -Seconds 15
		$session = @( Get-Process -Name WindowsSandboxRemoteSession, WindowsSandboxClient -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id } )

		for ( $i = 0; ( $i -lt 70 ) -and -not ( Test-Path ( Join-Path $lastRun 'flight.txt' ) ); $i++ ) { Start-Sleep -Seconds 2 }
		if ( Test-Path ( Join-Path $lastRun 'flight.txt' ) ) { break }

		Write-Host "  $label, try ${try}: the Sandbox did not run the scripts; closing it"
		foreach ( $p in $session ) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
		Start-Sleep -Seconds 30
		$session = @()
	}

	if ( $session.Count -eq 0 ) { return $null }

	# the run, at most 10 minutes; then the Sandbox shuts itself down (flight-check.cmd), and only if it has not after two minutes is it closed
	$result = Join-Path $lastRun 'result.txt'
	for ( $i = 0; ( $i -lt 300 ) -and -not ( ( Test-Path $result ) -and ( ( Get-Content $result -Raw ) -match 'Done' ) ); $i++ ) { Start-Sleep -Seconds 2 }
	for ( $i = 0; ( $i -lt 60 ) -and @( $session | Where-Object { Get-Process -Id $_.Id -ErrorAction SilentlyContinue } ).Count; $i++ ) { Start-Sleep -Seconds 2 }
	foreach ( $p in $session ) { if ( Get-Process -Id $p.Id -ErrorAction SilentlyContinue ) { Write-Host "  ${label}: the Sandbox did not close itself; closing it"; Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }

	$keep = Join-Path $results $label
	New-Item -ItemType Directory -Force -Path $keep | Out-Null
	Copy-Item -Recurse -Force ( Join-Path $lastRun '*' ) $keep -ErrorAction SilentlyContinue
	Start-Sleep -Seconds 10

	return Get-Content ( Join-Path $keep 'flight.txt' ) -ErrorAction SilentlyContinue
}

$summary = @()

foreach ( $class in $Classes ) {
	$made = & python ( Join-Path $PSScriptRoot 'make_flight_seed.py' ) $SaveFile $class ( Join-Path $scripts 'SaveGame0.json' )
	$mass, $acceleration, $star = "$made".Trim() -split '\s+'
	$expected = Predicted $class ( [int]$acceleration )
	"class $class engines ($acceleration G, $mass tons): expecting $( '{0:F2}' -f $expected ) s"

	$report = Run 'timed' "class$class"
	$line = $report | Select-String 'RESULT left the star system ([0-9.]+) s'
	$measured = if ( $line ) { [double]$line.Matches[ 0 ].Groups[ 1 ].Value } else { $null }
	$verdict = if ( $null -eq $measured ) { 'NO RESULT' } elseif ( [Math]::Abs( $measured - $expected ) -le 0.3 ) { 'PASS' } else { 'FAIL' }
	$summary += ( '{0}  class {1} ({2} G): {3} s measured, {4:F2} s expected' -f $verdict, $class, $acceleration, ( $( if ( $null -eq $measured ) { '-' } else { '{0:F2}' -f $measured } ) ), $expected )
}

if ( -not $NoSteering ) {
	$made = & python ( Join-Path $PSScriptRoot 'make_flight_seed.py' ) $SaveFile 3 ( Join-Path $scripts 'SaveGame0.json' )
	$report = Run 'steer' 'steering'
	$summary += $( if ( $report -match 'RESULT steering' ) { "LOOK  steering: screenshots in $( Join-Path $results 'steering\shots' )" } else { 'NO RESULT  steering' } )
}

$summary | Tee-Object -FilePath ( Join-Path $results 'summary.txt' )
"Results: $results"
