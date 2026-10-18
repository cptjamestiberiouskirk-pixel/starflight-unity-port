# Runs inside Windows Sandbox, started by flight-check.cmd: plays the ship's flight with keys sent to the game's window, as a player at the
# keyboard would, and writes what happened to flight.txt (and screenshots to shots\) in the Out folder.
#   timed: Navigation, Maneuver, then the right arrow held until the game's log shows the switch to hyperspace (the system's edge), timed
#   steer: Navigation, Maneuver, the right arrow for 1.5 s, then the up arrow, with a screenshot every quarter of a second
param( [string]$Out, [string]$Mode = 'timed' )

$shots = Join-Path $Out 'shots'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
$report = Join-Path $Out 'flight.txt'
$log = Join-Path $Out 'Player.log'

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class FlightKeys {
	[DllImport("user32.dll")] public static extern void keybd_event( byte vk, byte scan, uint flags, UIntPtr extra );
	[DllImport("user32.dll")] public static extern uint MapVirtualKey( uint code, uint mapType );
	[DllImport("user32.dll")] public static extern bool SetForegroundWindow( IntPtr window );
	public static void Down( byte vk, bool extended ) { keybd_event( vk, (byte) MapVirtualKey( vk, 0 ), extended ? 1u : 0u, UIntPtr.Zero ); }
	public static void Up( byte vk, bool extended ) { keybd_event( vk, (byte) MapVirtualKey( vk, 0 ), ( extended ? 1u : 0u ) | 2u, UIntPtr.Zero ); }
}
"@

$clock = [Diagnostics.Stopwatch]::StartNew()

function Note( [string]$text ) { ( '{0,8:F2}  {1}' -f $clock.Elapsed.TotalSeconds, $text ) | Add-Content -Path $report }

function Shot( [string]$name ) {
	try {
		$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
		$bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
		$graphics = [System.Drawing.Graphics]::FromImage( $bitmap )
		$graphics.CopyFromScreen( $bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size )
		$bitmap.Save( ( Join-Path $shots "$name.png" ), [System.Drawing.Imaging.ImageFormat]::Png )
		$graphics.Dispose()
		$bitmap.Dispose()
		Note "shot $name"
	}
	catch { Note "shot $name failed: $_" }
}

# the game's log, which the game is still writing
function LogText {
	try {
		$stream = New-Object IO.FileStream( $log, 'Open', 'Read', 'ReadWrite' )
		$reader = New-Object IO.StreamReader( $stream )
		$text = $reader.ReadToEnd()
		$reader.Close()
		return $text
	}
	catch { return '' }
}

function Focus {
	$game = Get-Process -Name Starflight -ErrorAction SilentlyContinue | Select-Object -First 1
	if ( $game -and ( $game.MainWindowHandle -ne 0 ) ) { [void][FlightKeys]::SetForegroundWindow( $game.MainWindowHandle ) }
}

# the arrows are extended keys (not the numeric keypad's); a press is held long enough for the slow software-drawn frames to see it
$keys = @{ Left = 0x25; Up = 0x26; Right = 0x27; Down = 0x28; Enter = 0x0D }
function Tap( [string]$key ) { Focus; $extended = $key -ne 'Enter'; [FlightKeys]::Down( $keys[ $key ], $extended ); Start-Sleep -Milliseconds 300; [FlightKeys]::Up( $keys[ $key ], $extended ); Start-Sleep -Milliseconds 400; Note "tap $key" }
function Hold( [string]$key ) { Focus; [FlightKeys]::Down( $keys[ $key ], $true ); Note "down $key" }
function Release( [string]$key ) { [FlightKeys]::Up( $keys[ $key ], $true ); Note "up $key" }

Note "driver mode $Mode"

# the game's window
for ( $i = 0; $i -lt 240; $i++ ) {
	$game = Get-Process -Name Starflight -ErrorAction SilentlyContinue | Select-Object -First 1
	if ( $game -and ( $game.MainWindowHandle -ne 0 ) ) { break }
	Start-Sleep -Milliseconds 500
}
Note "game window $( $game.MainWindowHandle )"

# the intro: Enter skips its pages until the Spaceflight scene loads (the seed save is in a star system); no Enter after that, where it would press a button
for ( $i = 0; $i -lt 60; $i++ ) {
	if ( ( LogText ) -match 'Loading scene Spaceflight' ) { break }
	Start-Sleep -Seconds 3
	if ( ( LogText ) -match 'Loading scene Spaceflight' ) { break }
	Tap 'Enter'
}
Note "Spaceflight loading"

# the star system shown, and its planets made (the game is paused meanwhile)
for ( $i = 0; $i -lt 120; $i++ ) {
	if ( ( LogText ) -match 'Showing the star system location' ) { break }
	Start-Sleep -Milliseconds 500
}
Note "star system shown"
Start-Sleep -Seconds 25
Shot '01-loaded'

# Navigation (the third bridge button), then Maneuver (the first of the navigator's)
Tap 'Down'
Tap 'Down'
Tap 'Enter'
Start-Sleep -Seconds 2
Shot '02-navigation'
Tap 'Enter'
Start-Sleep -Seconds 2
Shot '03-maneuver'

if ( $Mode -eq 'timed' ) {
	$before = ( [regex]::Matches( ( LogText ), 'Switching to location Hyperspace' ) ).Count
	Hold 'Right'
	$start = $clock.Elapsed.TotalSeconds
	$nextShot = 1.0
	$left = $null

	while ( $clock.Elapsed.TotalSeconds - $start -lt 60 ) {
		$text = LogText
		if ( ( [regex]::Matches( $text, 'Switching to location Hyperspace' ) ).Count -gt $before ) { $left = $clock.Elapsed.TotalSeconds - $start; break }
		if ( $text -match 'Switching to location Encounter' ) { Note 'an encounter began on the way'; break }
		if ( $clock.Elapsed.TotalSeconds - $start -ge $nextShot ) { Shot ( 'fly-{0:00}s' -f [int]$nextShot ); $nextShot += 1.0 }
		Start-Sleep -Milliseconds 50
	}

	Release 'Right'

	if ( $left ) { Note ( 'RESULT left the star system {0:F2} s after the right arrow went down' -f $left ) } else { Note 'RESULT did not leave the star system' }

	Start-Sleep -Seconds 3
	Shot '09-hyperspace'
}
else {
	Hold 'Right'
	Start-Sleep -Milliseconds 1500
	Shot 'steer-0-east'
	Release 'Right'
	Hold 'Up'

	foreach ( $t in 1..6 ) {
		Start-Sleep -Milliseconds 250
		Shot ( 'steer-{0}-up-{1}ms' -f $t, ( $t * 250 ) )
	}

	Release 'Up'
	Start-Sleep -Milliseconds 500
	Shot 'steer-9-stopped'
	Note 'RESULT steering screenshots taken'
}

Note 'done: closing the game'
Get-Process -Name Starflight -ErrorAction SilentlyContinue | Stop-Process -Force
