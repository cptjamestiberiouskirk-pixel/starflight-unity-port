# Runs every probe scenario in the current directory (a Unity project root) and prints one summary block per scenario.
#   & "<this folder>\run-all-scenarios.ps1" -Tag combined
param(
	[string]$Tag = 'all',
	[string[]]$Scenarios = @('batch1', 'starport', 'h6', 'm15', 'm13', 'm14', 'm7', 'm8', 'm24', 'starport-m22', 'starport-ship', 'h5', 'm19', 'h7', 'm12', 'm20', 'h8', 'm10', 'm23', 'missiles', 'm11', 'm18', 'm17', 'm16', 'nomaps', 'm25', 'm26', 'm27', 'starport-ledger', 'cargo', 'savedata', 'combat', 'encounters', 'comms', 'terrain', 'savepanel', 'visual', 'perframe', 'starport-transport', 'shipslog', 'leaks', 'latent', 'editortools', 'starport-savedata', 'commlink', 'deposits', 'unmapped', 'orbit', 'erosion', 'smallfixes', 'encounterdata', 'drones', 'gameclock', 'shipmodels', 'recovereddata', 'flaredata', 'calendar', 'sensorpictures', 'pickups', 'ruins', 'artifactsites', 'formations', 'dropcargo', 'cargodisplay', 'crystalfield', 'blackegg', 'crystalcone')
)

$sk = $PSScriptRoot

foreach ($scenario in $Scenarios) {
	$output = & "$sk\probe.ps1" -Scenario $scenario -Name "$Tag-$scenario"

	$exit = ($output | Where-Object { $_ -match '^unity exit=' }) -join ' '
	$errors = @($output | Where-Object { $_ -match 'error CS' })
	$fails = @($output | Where-Object { $_ -match 'CHECK FAIL' })
	$passes = @($output | Where-Object { $_ -match 'CHECK PASS' }).Count
	$exceptions = ($output | Where-Object { $_ -match 'distinct exceptions' }) -join ' '
	$result = ($output | Where-Object { $_ -match '\] RESULT ' }) -join ' '

	"=== $scenario | $exit"
	"    checks passed=$passes failed=$($fails.Count) | $exceptions"
	"    $result"

	foreach ($line in $errors | Select-Object -First 5) { "    COMPILE: $line" }
	foreach ($line in $fails) { "    $line" }
	foreach ($line in ($output | Where-Object { $_ -match '^\[ClaudeProbe\]\s+x\d+ ' })) { "    $line" }
}

git status --short
