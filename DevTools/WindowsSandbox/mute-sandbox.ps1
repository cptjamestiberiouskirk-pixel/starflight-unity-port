# Runs inside Windows Sandbox before the game starts (run-starflight.cmd, flight-check.cmd): mutes the Sandbox's own speakers, so that a test
# makes no sound on the computer it runs on. The game has no volume setting of its own, and the Sandbox plays its sound through the host's speakers.
# It only ever mutes the Sandbox: as any other account than the Sandbox's it stops at once, so it can never mute the host's own speakers.
#   mute-sandbox.ps1 -Out <folder for mute.txt>
param( [string]$Out = '' )

function Report( [string]$text ) {
	if ( $Out -ne '' ) { Add-Content -LiteralPath ( Join-Path $Out 'mute.txt' ) -Value $text }
	$text
}

# Windows Sandbox always runs as WDAGUtilityAccount, on a computer named after nothing on the host
if ( $env:USERNAME -ne 'WDAGUtilityAccount' ) {
	Report "Not in Windows Sandbox (user $env:USERNAME): nothing muted."
	exit 0
}

# the Core Audio interfaces, only the parts used here (the slots before them are declared so the others are where Windows has them)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace SandboxMute
{
	[ComImport, Guid( "5CDF2C82-841E-4546-9722-0CF74078229A" ), InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
	interface IAudioEndpointVolume
	{
		int RegisterControlChangeNotify( IntPtr notify );
		int UnregisterControlChangeNotify( IntPtr notify );
		int GetChannelCount( out uint count );
		int SetMasterVolumeLevel( float levelDB, ref Guid context );
		int SetMasterVolumeLevelScalar( float level, ref Guid context );
		int GetMasterVolumeLevel( out float levelDB );
		int GetMasterVolumeLevelScalar( out float level );
		int SetChannelVolumeLevel( uint channel, float levelDB, ref Guid context );
		int SetChannelVolumeLevelScalar( uint channel, float level, ref Guid context );
		int GetChannelVolumeLevel( uint channel, out float levelDB );
		int GetChannelVolumeLevelScalar( uint channel, out float level );
		int SetMute( [MarshalAs( UnmanagedType.Bool )] bool mute, ref Guid context );
		int GetMute( [MarshalAs( UnmanagedType.Bool )] out bool mute );
	}

	[ComImport, Guid( "D666063F-1587-4E43-81F1-B948E807363F" ), InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
	interface IMMDevice
	{
		int Activate( ref Guid iid, int context, IntPtr parameters, [MarshalAs( UnmanagedType.IUnknown )] out object result );
		int OpenPropertyStore( int access, out IntPtr properties );
		int GetId( [MarshalAs( UnmanagedType.LPWStr )] out string id );
		int GetState( out int state );
	}

	[ComImport, Guid( "0BD7A1BE-7A1A-44DB-8397-CC5392387B5E" ), InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
	interface IMMDeviceCollection
	{
		int GetCount( out uint count );
		int Item( uint index, out IMMDevice device );
	}

	[ComImport, Guid( "A95664D2-9614-4F35-A746-DE8DB63617E6" ), InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
	interface IMMDeviceEnumerator
	{
		int EnumAudioEndpoints( int dataFlow, int stateMask, out IMMDeviceCollection devices );
	}

	[ComImport, Guid( "BCDE0395-E52F-467C-8E3D-C4579291692E" )]
	class MMDeviceEnumerator
	{
	}

	public static class Speakers
	{
		// mutes every active output device and gives back a line for each: its id, whether it is muted now and its volume
		public static string[] MuteAll()
		{
			var enumerator = (IMMDeviceEnumerator) new MMDeviceEnumerator();
			IMMDeviceCollection devices;

			// eRender, DEVICE_STATE_ACTIVE
			Marshal.ThrowExceptionForHR( enumerator.EnumAudioEndpoints( 0, 1, out devices ) );

			uint count;

			Marshal.ThrowExceptionForHR( devices.GetCount( out count ) );

			var lines = new string[ count ];
			var volumeId = typeof( IAudioEndpointVolume ).GUID;
			var context = Guid.Empty;

			for ( uint i = 0; i < count; i++ )
			{
				IMMDevice device;

				Marshal.ThrowExceptionForHR( devices.Item( i, out device ) );

				string id;
				object activated;

				device.GetId( out id );

				// CLSCTX_ALL
				Marshal.ThrowExceptionForHR( device.Activate( ref volumeId, 23, IntPtr.Zero, out activated ) );

				var volume = (IAudioEndpointVolume) activated;
				var muteResult = volume.SetMute( true, ref context );

				bool muted;
				float level;

				volume.GetMute( out muted );
				volume.GetMasterVolumeLevelScalar( out level );

				lines[ i ] = id + ": mute result 0x" + muteResult.ToString( "X8" ) + ", muted " + muted + ", volume " + level.ToString( "0.00" );
			}

			return lines;
		}
	}
}
'@

$silent = $false

try {
	$lines = [SandboxMute.Speakers]::MuteAll()

	if ( $lines.Count -eq 0 ) {
		Report "No active output device in the Sandbox: nothing to mute."
	}

	foreach ( $line in $lines ) { Report $line }

	$silent = @( $lines | Where-Object { $_ -notmatch 'muted True' } ).Count -eq 0
}
catch {
	Report "Could not mute the Sandbox's speakers: $( $_.Exception.Message )"
}

# a device that would not mute: the Sandbox's sound service is stopped instead, and the game then runs with no sound device at all
if ( -not $silent ) {
	try {
		Stop-Service -Name Audiosrv -Force -ErrorAction Stop
		Report "Stopped the Sandbox's Windows Audio service instead: the game runs with no sound device."
	}
	catch {
		Report "Could not stop the Sandbox's Windows Audio service either: $( $_.Exception.Message )"
	}
}
