# Runs inside Windows Sandbox, started by run-starflight.cmd. Writes the game's state every 10 seconds,
# so that a picture that stops updating can be told apart from a game that is slow or hung: CPU time
# that keeps growing and "responding True" mean the game is still running.
param(
	# the folder to write watch.txt into
	[string]$Out
)

$file = Join-Path $Out 'watch.txt'

# at most two hours, then the watcher stops by itself
for ( $i = 0; $i -lt 720; $i++ ) {
	Start-Sleep -Seconds 10
	$game = Get-Process -Name Starflight -ErrorAction SilentlyContinue | Select-Object -First 1

	if ( -not $game ) {
		"{0} game not running" -f ( Get-Date -Format HH:mm:ss ) | Add-Content $file
		break
	}

	"{0} cpu {1:N1} s, responding {2}, memory {3:N0} MB, window '{4}'" -f ( Get-Date -Format HH:mm:ss ), $game.CPU, $game.Responding, ( $game.WorkingSet64 / 1MB ), $game.MainWindowTitle | Add-Content $file
}
