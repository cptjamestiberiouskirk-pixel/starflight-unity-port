$shipsPath = "E:\Code\Unity Projects\starflight-unity-port\Assets\Game Objects\Ships"
$debrisPath = "E:\Code\Unity Projects\starflight-unity-port\Assets\Game Objects\Ships Debris"

$ships = Get-ChildItem -Path "$shipsPath\*.fbx" -Recurse
foreach ($ship in $ships) {
    $relativePath = $ship.FullName.Substring($shipsPath.Length + 1)
    $outputPath = Join-Path $debrisPath $relativePath
    $outputDir = Split-Path $outputPath -Parent
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    blender --background --python process_ship.py -- $ship.FullName $outputPath
}
