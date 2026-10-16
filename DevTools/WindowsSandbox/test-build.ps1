# Tries a Windows build of the game on a clean copy of Windows, in Windows Sandbox.
#
#   & "<this folder>\test-build.ps1" -BuildDir "Build"
#
# It writes a Windows Sandbox configuration that maps the build folder in read-only, starts the Sandbox
# and runs the game there in a window. The results go to %TEMP%\starflight-sandbox\out\last-run: the
# game's log, its saves once it is quit, and a record every 10 seconds of whether it was running and
# responding. The Sandbox, and everything the game wrote in it, is thrown away when its window is closed.
param(
	# the folder with Starflight.exe in it
	[Parameter( Mandatory = $true )]
	[string]$BuildDir,
	# the size of the game's window. The game always runs in a window here: in full screen the picture stopped updating.
	[ValidateRange( 320, 7680 )]
	[int]$Width = 1280,
	[ValidateRange( 200, 4320 )]
	[int]$Height = 720,
	# memory for the Sandbox, in MB
	[ValidateRange( 2048, 65536 )]
	[int]$MemoryMB = 8192,
	# share the graphics card with the Sandbox. Off by default: on the PC this was first tried on it made the Sandbox crash as the game started.
	[switch]$Gpu,
	# write the configuration and stop, without starting the Sandbox
	[switch]$NoLaunch
)

$sandboxExe = Join-Path $env:windir 'System32\WindowsSandbox.exe'

if ( -not ( Test-Path -LiteralPath $sandboxExe ) ) {
	"Windows Sandbox is not turned on. In a PowerShell window run as administrator, run"
	"  Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All"
	"then restart Windows. It needs Windows 10 or 11 Pro, Enterprise or Education."
	exit 2
}

if ( -not ( Test-Path -LiteralPath $BuildDir -PathType Container ) ) {
	"No such folder: $BuildDir"
	exit 2
}

$build = ( Resolve-Path -LiteralPath $BuildDir ).Path

if ( -not ( Test-Path -LiteralPath ( Join-Path $build 'Starflight.exe' ) ) ) {
	"No Starflight.exe in $build"
	exit 2
}

# only one Sandbox can run at a time (WindowsSandboxClient before Windows 11 24H2)
if ( -not $NoLaunch -and ( Get-Process -Name WindowsSandboxRemoteSession, WindowsSandboxClient -ErrorAction SilentlyContinue ) ) {
	"A Windows Sandbox is already open, and only one can run at a time. Close it first."
	exit 2
}

$root = Join-Path $env:TEMP 'starflight-sandbox'
$scripts = Join-Path $root 'scripts'
$out = Join-Path $root 'out'
New-Item -ItemType Directory -Force -Path $scripts, $out | Out-Null

# fresh copies of the two scripts that run in the Sandbox, with Windows line endings for cmd.exe
foreach ( $name in 'run-starflight.cmd', 'watch-starflight.ps1' ) {
	$text = [IO.File]::ReadAllText( ( Join-Path $PSScriptRoot $name ) ) -replace "`r?`n", "`r`n"
	[IO.File]::WriteAllText( ( Join-Path $scripts $name ), $text, [Text.Encoding]::ASCII )
}

# the Sandbox's desktop, where the three folders show up
$desktop = 'C:\Users\WDAGUtilityAccount\Desktop'
$vgpu = if ( $Gpu ) { 'Enable' } else { 'Disable' }

$wsb = @"
<Configuration>
  <vGPU>$vgpu</vGPU>
  <MemoryInMB>$MemoryMB</MemoryInMB>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$( [Security.SecurityElement]::Escape( $build ) )</HostFolder>
      <SandboxFolder>$desktop\Starflight</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$( [Security.SecurityElement]::Escape( $scripts ) )</HostFolder>
      <SandboxFolder>$desktop\Scripts</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$( [Security.SecurityElement]::Escape( $out ) )</HostFolder>
      <SandboxFolder>$desktop\Out</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>cmd.exe /c $desktop\Scripts\run-starflight.cmd $Width $Height</Command>
  </LogonCommand>
</Configuration>
"@

# check that it is well formed before Windows Sandbox reads it
[xml]$wsb | Out-Null

$wsbPath = Join-Path $root 'Starflight.wsb'
[IO.File]::WriteAllText( $wsbPath, $wsb, [Text.Encoding]::UTF8 )

if ( $NoLaunch ) {
	"Wrote $wsbPath (double-click it to start the Sandbox)"
	exit 0
}

# the same as double-clicking the .wsb file
Start-Process -FilePath $wsbPath

"Windows Sandbox is starting with $build (graphics card shared: $Gpu)."
"The game starts by itself in a $Width x $Height window. Quit it with Escape, then Quit, and close the Sandbox."
"Results, replacing the last run's: $out\last-run"
