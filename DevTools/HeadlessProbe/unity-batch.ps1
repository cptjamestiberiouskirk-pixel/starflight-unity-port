# Launches a batch Unity for the probe scripts (dot-source it: . "$PSScriptRoot\unity-batch.ps1"), so that a stuck licensing client cannot block every
# batch Unity on the PC:
# - one launch at a time across every worktree and session of this Windows user, held until Unity says its licensing is initialized;
# - never while the shared licensing client is starting or shutting down, and nothing at all while one is stuck (it says which processes to end);
# - stopped at the first licensing failure, before Unity's own retries start a client each.
#
# Every batch Unity of a Windows user shares one licensing client, which serves the pipe \\.\pipe\Unity-LicenseClient-<user> (the Hub and its Editor
# have a client of their own). On 2026-10-17 a probe run launched Unity a second after the previous scenario's Unity quit, while that one's client was
# still shutting down: the new client took the client's lock and never served its pipe, so every batch Unity after it, of every session, failed to
# license, and each tried six times, leaving a client behind each time.

# the shared licensing client's pipe
function Get-LicensingPipeName {
	"Unity-LicenseClient-$env:USERNAME"
}

# the licensing clients started for that pipe
function Get-BatchLicensingClients([string]$PipeName = (Get-LicensingPipeName)) {
	$pattern = [regex]::Escape($PipeName) + '("|\s|$)'

	@(Get-CimInstance Win32_Process -Filter "Name = 'Unity.Licensing.Client.exe'" | Where-Object { $_.CommandLine -match $pattern })
}

# true while a licensing client serves the pipe
function Test-LicensingPipe([string]$PipeName = (Get-LicensingPipeName)) {
	Test-Path -LiteralPath "\\.\pipe\$PipeName"
}

# waits until a new batch Unity can license: no client runs (Unity starts one), or one serves the pipe. A client that runs without serving it is starting
# or shutting down; if it stays so for this long it is stuck, and the clients are returned (nothing when all is well)
function Wait-LicensingSettled([string]$PipeName = (Get-LicensingPipeName), [int]$Seconds = 45) {
	$deadline = (Get-Date).AddSeconds($Seconds)
	$waited = $false

	while ($true) {
		$clients = Get-BatchLicensingClients $PipeName

		if (($clients.Count -eq 0) -or (Test-LicensingPipe $PipeName)) {
			# (a client that has just gone may still hold its lock for a moment)
			if ($waited) { Start-Sleep -Seconds 2 }

			return @()
		}

		if ((Get-Date) -ge $deadline) {
			return $clients
		}

		$waited = $true
		Start-Sleep -Seconds 2
	}
}

# what to tell the user about stuck licensing clients
function Format-LicensingStuck($Clients, [string]$PipeName, [string]$What) {
	$Clients = @($Clients | Where-Object { $null -ne $_ })

	if ($Clients.Count -eq 0) {
		return "LICENSING STUCK: $What, but no licensing client of \\.\pipe\$PipeName is left running; run again"
	}

	$list = ($Clients | ForEach-Object { "$($_.ProcessId) (started $($_.CreationDate.ToString('HH:mm:ss')))" }) -join ', '
	$ids = ($Clients | ForEach-Object { $_.ProcessId }) -join ','

	"LICENSING STUCK: $What. The Unity licensing client(s) $list run without serving \\.\pipe\$PipeName, so every batch Unity on this PC would fail to license. End them (Task Manager, or Stop-Process -Id $ids -Force) and run again; see %LOCALAPPDATA%\Unity\Unity.Licensing.Client.log"
}

# the text of a log Unity may still be writing ("" if there is none yet)
function Read-SharedLog([string]$Path) {
	try {
		$stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)

		try { (New-Object IO.StreamReader($stream)).ReadToEnd() } finally { $stream.Dispose() }
	}
	catch {
		''
	}
}

# runs Unity with these arguments until it quits, or stops it after TimeoutSeconds, and returns ExitCode, Seconds, LicensingStuck and Messages. When
# LicensingStuck is true nothing ran (or the run was stopped at its licensing failure), and the caller should stop: every further launch adds a client
function Invoke-UnityBatch([string]$Unity, [string[]]$Arguments, [string]$Log, [int]$TimeoutSeconds, [string]$PipeName = (Get-LicensingPipeName), [int]$SettleSeconds = 45) {
	$result = [pscustomobject]@{ ExitCode = $null; Seconds = 0; LicensingStuck = $false; Messages = New-Object Collections.Generic.List[string] }
	$sw = [Diagnostics.Stopwatch]::StartNew()
	$launchLock = New-Object Threading.Mutex($false, 'Local\StarflightUnityBatchLaunch')
	$holding = $false

	try {
		# one launch at a time (a run whose script was stopped leaves the lock abandoned, which is as good as free)
		try { $holding = $launchLock.WaitOne([TimeSpan]::FromMinutes(15)) } catch [Threading.AbandonedMutexException] { $holding = $true }

		if (-not $holding) {
			$result.Messages.Add('another batch Unity has held the launch for 15 minutes; launching anyway')
		}

		$stuck = Wait-LicensingSettled $PipeName $SettleSeconds

		if ($stuck.Count -gt 0) {
			$result.LicensingStuck = $true
			$result.Messages.Add((Format-LicensingStuck $stuck $PipeName 'nothing was launched'))

			return $result
		}

		$p = Start-Process -FilePath $Unity -ArgumentList $Arguments -PassThru
		$licensed = $false

		while (-not $p.WaitForExit(2000)) {
			if ($sw.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
				$result.Messages.Add("unity still running after ${TimeoutSeconds}s, stopping pid $($p.Id)")
				Stop-Process -Id $p.Id -Force
				$p.WaitForExit(30000) | Out-Null

				break
			}

			if ($licensed) { continue }

			$text = Read-SharedLog $Log

			if ($text -match 'Licensing is initialized') {
				# licensed: the next launch can go
				$licensed = $true

				if ($holding) { $launchLock.ReleaseMutex(); $holding = $false }
			}
			elseif ($text -match 'Licensing initialization failed') {
				# stopped before its retries start more clients
				Stop-Process -Id $p.Id -Force
				$p.WaitForExit(30000) | Out-Null

				$result.LicensingStuck = $true
				$result.Messages.Add((Format-LicensingStuck (Get-BatchLicensingClients $PipeName | Where-Object { -not (Test-LicensingPipe $PipeName) }) $PipeName "unity (pid $($p.Id)) failed to license and was stopped"))

				break
			}
		}

		$result.ExitCode = $p.ExitCode
	}
	finally {
		if ($holding) { $launchLock.ReleaseMutex() }

		$launchLock.Dispose()
		$result.Seconds = [int]$sw.Elapsed.TotalSeconds
	}

	return $result
}
