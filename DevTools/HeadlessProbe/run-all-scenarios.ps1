# Runs every probe scenario in the current directory (a Unity project root) and prints one summary block per scenario.
#   & "<this folder>\run-all-scenarios.ps1" -Tag combined
param(
	[string]$Tag = 'all',
	[string[]]$Scenarios = @('batch1', 'starport', 'h6', 'm15', 'm13', 'm14', 'm7', 'm8', 'm24', 'starport-m22', 'starport-ship', 'h5', 'm19', 'h7', 'm12', 'm20', 'h8', 'm10', 'm23', 'missiles', 'm11', 'm18', 'm17', 'm16', 'systemmap', 'maplife', 'nomaps', 'm25', 'm26', 'm27', 'starport-ledger', 'cargo', 'savedata', 'combat', 'encounters', 'comms', 'terrain', 'savepanel', 'visual', 'explosionpictures', 'perframe', 'starport-transport', 'shipslog', 'leaks', 'latent', 'editortools', 'starport-savedata', 'commlink', 'deposits', 'unmapped', 'orbit', 'erosion', 'smallfixes', 'encounterdata', 'drones', 'gameclock', 'shipmodels', 'recovereddata', 'flaredata', 'sunlife', 'calendar', 'sensorpictures', 'sensorplanet', 'colournames', 'pickups', 'ruins', 'artifactsites', 'formations', 'dropcargo', 'cargodisplay', 'crystalfield', 'blackegg', 'crystalcone', 'win', 'starport-win', 'arthflare', 'gameover', 'racelosses', 'tesseract', 'whiningorb', 'roddevice', 'crystalpearl', 'dodecahedron', 'flatdevice', 'redcylinder', 'shimmeringball', 'ringdevice', 'starport-endurium', 'nebula', 'starport-repair', 'distress', 'vesselstats', 'starmapfuel', 'colonies', 'systemdamage', 'systemeffects', 'systemrepairs', 'repairchoice', 'homeworlds', 'cowardice', 'elowangift', 'veloxdemands', 'thrynntrades', 'mood', 'rivals', 'transmissions', 'starstability', 'analysisskill', 'salvage', 'randomruins', 'lava', 'aliennames', 'lifeforms', 'tvweapons', 'lifebehaviour', 'crewdeath', 'mates', 'lifeformdata', 'capture', 'starport-specimens', 'starport-interest', 'starport-notices', 'look', 'hitpictures', 'weaponpictures', 'transporterpictures', 'debrispictures', 'shippictures', 'atmospherepictures', 'lifeformmodels', 'lifeformpictures', 'uphill', 'storms', 'orbitplanet', 'pushback', 'statusdisplay', 'screenpictures', 'starport-screenpictures', 'tvdisplay', 'coordinates', 'landingsite', 'distressconfirm', 'coordinatebar', 'alienanalysis', 'systemtravel', 'examinetreat', 'starfieldpictures', 'alienshields', 'landinglines', 'ancientsites', 'disembarklines', 'noreading', 'encounterend', 'tvtakesegg', 'launchconfirm', 'shippedlines', 'flyers', 'systemviewpictures', 'shippedmoods', 'lineactions', 'shippedstrength', 'postureeffects', 'phraselimit', 'alienwaiting', 'heaven', 'gazurtoidmood', 'shieldmessages', 'orbitlines', 'analysisheading', 'analysissurface', 'lookfields', 'floaters', 'combatwounds', 'encounterviewscale', 'commpicture', 'commspemin', 'commelowan', 'starport-noticetyping', 'tvdistance', 'starport-training', 'motiondetected', 'surfacepictures', 'bridgepictures', 'launchlines', 'acceleration', 'starport-analyzelist', 'starport-buyorder', 'starport-supplementalfine', 'starport-preflight', 'flightpictures', 'intropictures', 'logplanet', 'savecoords', 'combatmessages', 'starport-tradetexts', 'starport-noyes', 'consolemenus', 'starport-ledgerrows', 'lightmode', 'scifibuttons', 'sensorlines', 'reboard', 'outoffuel', 'orbitpermission', 'orbitentry', 'towbonus', 'nexusegg', 'firedonsilent', 'shieldoutline', 'starport-shieldoutline', 'colorschemes', 'starmapcolors', 'encountergrace', 'landinggravity', 'landingwarning', 'fuelwarnings', 'starport-tradeclamp', 'alienmissiles', 'viewoverlays', 'alienshieldhits', 'clockrate', 'starport-hallcolors', 'stationcolors', 'dockingbaycolors', 'hullintegrity', 'weapondamage', 'aimandfuel', 'starport-doorlabels', 'starport-lighting', 'starport-armorequivalency', 'starport-unknownartifacts', 'hyperspacefuel', 'fluxjump', 'starport-enduriumlaunch', 'tvfuel', 'starport-artifactstock', 'alienpursuit', 'colonyrating', 'starport-knownartifacts', 'shipspeeds', 'encounterwaves', 'crewposts', 'launchreport', 'starport-reportrooms', 'alienreadings', 'starport-rest', 'starport-deadcrew', 'starport-personnelrules', 'starport-evaluationonce', 'distresscalls')
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
