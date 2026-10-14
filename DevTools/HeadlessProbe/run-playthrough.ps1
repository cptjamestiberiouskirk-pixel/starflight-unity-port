# the playthrough: runs its stages (playthrough-1 to playthrough-21) one after another from a new game, each from the save the one before left
#   & "<this folder>\run-playthrough.ps1"            all stages
#   & "<this folder>\run-playthrough.ps1" -From 8     from stage 8, with the saves of an earlier run
param( [int]$From = 1, [int]$To = 21 )
$proj = Resolve-Path ( Join-Path $PSScriptRoot '..\..' )
Set-Location $proj
$folder = Join-Path $env:TEMP 'starflight-probe\playthrough'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
if ( $From -eq 1 ) { Get-ChildItem $folder -Filter 'stage-*.json' | Remove-Item -Force; Remove-Item "$folder\summary.txt", "$folder\chain.txt" -ErrorAction SilentlyContinue }
for ( $n = $From; $n -le $To; $n++ ) {
	$out = & "DevTools\HeadlessProbe\probe.ps1" -Scenario "playthrough-$n" -Name "chain-playthrough-$n"
	$result = ( $out | Select-String -Pattern 'RESULT' | Select-Object -Last 1 ).Line
	$fails = ( $out | Select-String -Pattern 'CHECK FAIL' ).Count
	$passes = ( $out | Select-String -Pattern 'CHECK PASS' ).Count
	"stage $n : pass $passes fail $fails : $result" | Tee-Object -FilePath "$folder\chain.txt" -Append
	if ( -not ( Test-Path "$folder\stage-$n.json" ) ) { "stage $n left no save; the chain stops" | Tee-Object -FilePath "$folder\chain.txt" -Append; break }
}
