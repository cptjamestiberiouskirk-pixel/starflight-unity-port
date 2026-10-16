# Starflight: The Remaking of a Legend

[![Latest release](https://img.shields.io/github/v/release/cptjamestiberiouskirk-pixel/starflight-unity-port?label=download)](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/releases/latest)
![Platform: Windows 64-bit](https://img.shields.io/badge/Platform-Windows%2064--bit-blue)
![Unity 6](https://img.shields.io/badge/Unity-6000.3-blue.svg)
[![Compile check](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/actions/workflows/compile-check.yml/badge.svg)](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/actions/workflows/compile-check.yml)
[![License: Unlicense](https://img.shields.io/badge/License-Unlicense-lightgrey.svg)](LICENSE)

**A complete, faithful remake of the 1986 space exploration classic *Starflight*, rebuilt from the ground up in Unity 6.**

*Starflight* was one of the first games to hand players an entire galaxy and simply let them loose in it. This project brings it back in full: recruit a crew, outfit your starship and set out across 270 star systems and 811 planets. Mine worlds for minerals, catalogue strange lifeforms, talk (or fight) your way past the races that share the galaxy, and find out why the stars are flaring before Arth's own sun does the same.

The galaxy is the original's, down to the last detail: every star, planet, price and line of alien dialogue comes straight from the 1986 game, and the rules follow the original code. What's new is the way it looks, with fully 3D planets, stars and terrain, a redesigned ship's console and 15 color schemes to choose from.

Version 1.0.0 was the first complete release, playable from the first launch to the final ending. Version 1.1.0 brings the original's combat: hits that wear down the shields, the armor and then the hull, the original's weapon damage, and a navigator whose skill decides whether your shots hit.

<p align="center">
  <img src="docs/images/orbit.jpg" width="49%" alt="A planet seen from orbit, with the System Map and the ship's console">
  <img src="docs/images/starport.jpg" width="49%" alt="The Starport, where the astronaut walks between the rooms">
</p>
<p align="center">
  <img src="docs/images/ship-configuration.jpg" width="60%" alt="Ship Configuration, where the ship is outfitted">
</p>

---

## 🎮 Download and play

1. Download **Starflight-1.1.0-win64.zip** (600 MB) from the [latest release](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/releases/latest).
2. Unzip it anywhere, for example into `Documents\Games`. Keep the files together: `Starflight.exe` needs the folders next to it.
3. Run `Starflight.exe`.

There is no installer. To uninstall, delete the folder, along with the saved games in `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Starflight\` if you want those gone too.

**Requirements:** Windows 10 or 11 (64-bit), a DirectX 11 graphics card and 1.1 GB of free disk space.

**If Windows blocks the game.** The game is not code-signed, so Windows treats it as an unknown program:

- *"Windows protected your PC"* (Microsoft Defender SmartScreen): choose **More info**, then **Run anyway**.
- *"An Application Control policy has blocked this file"* (Smart App Control, Windows 11): Smart App Control does not allow exceptions for individual programs, so the game cannot start while it is switched on. You can still play it in the Unity Editor (see [Build from source](#-build-from-source)).

The release page lists the zip's SHA-256 checksum. To verify your download, run `Get-FileHash Starflight-1.1.0-win64.zip` in PowerShell and compare the two.

## 📖 How to play

The **[Player's Manual](docs/MANUAL.md)** covers everything you need: the story, the controls, the Starport's rooms, every console button, flight, planets, aliens, combat and money. It is completely spoiler-free, so what the galaxy holds is still yours to discover.

| Action | Keys |
|---|---|
| Move and steer (8 directions) | Arrow keys with Home, Page Up, End and Page Down, or the numeric keypad |
| Select a console button | Up and Down |
| Use it, confirm, stop | Enter |
| Pause: save slots, color schemes, quit | Escape |

The game saves automatically. On the Escape panel, **Copy** keeps a spare copy of your game in another of the five save slots.

New to *Starflight*? Read the notices in Operations, create and assign a crew of six, name your ship, buy a few cargo pods and launch. Your first few trips should be about making money.

## ✅ Status: version 1.1.0

The whole game is playable, from a new game to the ending. Every item on the [roadmap to parity with the original](ROADMAP.md) is done or decided (59 of 59), and every screen has been compared side by side with the original's.

Every change is compile-checked in CI and exercised by a headless test harness that plays the real game scenes: more than 200 scenarios with over 1,100 checks, all passing on this release, plus an automated playthrough that plays a new game all the way to the ending and Interstel's bonus.

The known differences from the original are listed at the end of the [manual](docs/MANUAL.md#13-this-port-and-the-1986-original). The biggest one left is that shields recharge at the port's own pace during a fight. Found a bug? Please [open an issue](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/issues).

The full history is in [CHANGELOG.md](CHANGELOG.md).

## 🙏 Credits

- ***Starflight*** (1986) was created by Binary Systems and published by Electronic Arts. This remake is an unofficial fan project and is not affiliated with or endorsed by either company.
- **The original Unity remake** was started by Marvin Herbold in 2017, with contributions from Jeffrey Graw, Aaron Franke and Andrew Steeley.
- **The Unity 6 port** and its completion to 1.0.0: @cptjamestiberiouskirk-pixel. Claude AI assisted with development.
- **Fonts:** Orbitron, Rajdhani and Share Tech Mono, under the SIL Open Font License (`Assets/Fonts/`).

---

# For developers

## 🚀 Under the hood

The port runs on **Unity 6 (6000.3.13f1)** and has been modernized throughout:

- **Unity Registry packages.** Deprecated third-party libraries (`JsonDotNet`, the legacy `FbxExporters`) were replaced with Unity Registry packages, and the UI runs on Unity UI and TextMeshPro.
- **Input System.** All input goes through the Input System package (Active Input Handling is "Input System Package (New)"). `Assets/Input/Starflight Input.inputactions` keeps the original key layout: the numpad, or the arrow, Home, End, Page Up and Page Down keys, steer; Return or the numpad Enter fires; Escape cancels; and the UI moves on the arrows and numpad 8, 2, 4 and 6. Bindings can be changed in that asset.
- **Data-driven design.** The original game's data (stars, planets, aliens, prices, dialogue) lives in JSON under `Assets/Resources/`, and player progress goes through an `ISaveSystem` that writes crash-safe JSON saves.
- **Deterministic galaxy.** The 270 star systems and 811 planets are the original's fixed data, never random generation, and every planet's surface objects are placed the same way every time.

## 🎮 Features

- **Combat**: laser cannons, missile launchers, shields and armor, with alien tactics and victory and defeat detection. Alien ships have their own armor and shield points, any race turns hostile when fired on (the Uhlek attack on sight), aliens hold their fire while a comm link is up, and every shot burns a little Endurium.
- **Encounters in orbit**: home fleets, drones and a derelict wait in orbit around their planets and meet the ship when it arrives. Leaving the encounter returns the ship to the star system, as in the original.
- **Ship systems over time**: shields keep their charge and recharge slowly, and the engineer's repairs and the crew's healing take time, going faster with higher skill: everyone on board heals slowly, and the doctor's patient twice as fast. The doctor asks which crew member to examine or treat, just like the original.
- **Living suns**: each star system's sun churns, its corona flickers and small flares rise from its rim and fall back, all in the color of real stars of its class.
- **Planets lit by their star**: in the 3D star system view every planet is lit by its own star, in that star's color, with a day side, a night side and an atmosphere that glows along the sunlit edge.
- **A Milky Way in the sky**: a faint band of the Milky Way crosses the spaceflight sky beneath the nebula of the territory you are in, and turns with the sky as the ship flies.
- **Orbit paths and engine glow**: faint orbit paths in the star's color trail each planet in the 3D star system view, brightest just behind the planet, and the ship's engines glow brighter as it speeds up.
- **Ship destruction**: full death sequences with explosions, debris and salvage.
- **Game over**: losing the ship brings up the game over screen, and returning to the title screen restores the last save.
- **Terrain scanning**: world-space labels on the terrain, mineral detection and a transporter-style beam effect.
- **Cargo management**: real-time volume tracking for minerals and elements, with automatic transfer to the ship. A mineral deposit holds 1.0 to 9.9 cubic meters, as in the original.
- **The original economy**: a new game in a build starts with the original 12,000 MU (the Editor starts with 1,000,000 for testing), and the bank and the notices show stardates in the original's format (`26-03-4620`).
- **Debris scanning**: scan the wreckage of destroyed vessels for salvage.
- **Modern UI kit**: the ship's console, the Starport rooms and the save game panel use a sci-fi UI kit designed in Figma, set in Orbitron, Rajdhani and Share Tech Mono. The console shows an icon for whoever is in charge (each officer, the terrain vehicle, the ship's logo on the bridge). In encounters a bracket marks the targeted alien ship with its range, or its shields once the encounter turns hostile. In hyperspace the visible star systems show their coordinates, in a star system the planets show their orbit and surface, and the viewport's frame names the ship's location. Colors, text styles and sprites all live in one theme (`Resources/UI Kit Theme`), and each element takes its look from a role (`UIKitStyle`).
- **Sci-fi buttons**: every selectable action is a sci-fi button with two clipped corners and a thin frame in the kit's accent color, lit when selected. The console's menu rows, the Starport and save panel buttons and the Yes and No of alien questions all share the style, in both dark and light modes.
- **Color schemes**: the UI kit comes in fifteen color schemes, each with a dark and a light mode: Classic (the kit as originally drawn, with a steel grey light mode), the original Starflight's EGA colors, Starflight 2's colors, and twelve more (Endurium, Interstel Amber, Phosphor, Titanium, Heritage EGA, Solar Gold, Spec-Ops, Azure Drive, Arctic Glass, Starliner, Night Ops and Hi-Vis). The save game panel (Escape, at any time) has a Colors button whose screen shows every scheme with swatches of its colors and both modes, and the whole game switches the moment you pick one. The choice is stored on the computer, not in a saved game. The starmap, the ship's log, the Starport's hall, the station the ship launches from and the docking bay follow the scheme too (the two original games' schemes give the station the 1986 Starport's EGA colors and the bay its colors from before the new look), while the other 3D views keep their own colors. The schemes are theme assets in `Resources/UI Kit Themes/`, generated by `DevTools/UIKitSchemes/make_themes.py`.
- **Realistic System Map**: the console's System Map shows every planet with its own surface, lit by its star and sized by its mass, around a glowing star in the color of real stars of its class (matching the sun in the star system view), with orbit trails, the star's ecosphere, a star field and the ship's heading with its engine glow, wake and sensor ping (built by `DevTools/SystemMap`, see its [README](DevTools/SystemMap/README.md)).

## 👽 Ships, aliens and lifeforms

- **The player's ship**: the Arth ship is a new textured model (a hull plus a sheet of parts, assembled by the import scripts in `DevTools/ShipModels`). Its 16 cargo pods, laser turret and missile launchers are separate parts that appear as the ship is outfitted: in flight, on the status display, parked on a planet and in the Ship Configuration room.
- **Alien ships**: the Spemin ships, the Mechan Scout and the Mysterion are hand-built models. Every other vessel (the Thrynn and Elowan transports, scouts and warships, the four Veloxi vessels, the Gazurtoid and Uhlek scouts and warships, the Noah 9 derelict and the Enterprise) is a new textured model based on the original's sensor pictures, imported by the scripts in `DevTools/ShipModels` (see its [README](DevTools/ShipModels/README.md)). All of them are sized according to STRINFO. The Nomad probe and the Minstrel use their comm screen models.
- **Comm portraits**: each race's comm screen is a small 3D scene of its own (the race's model, a light and a camera under the race's group in the Encounter location). The Spemin portrait is animated after the Genesis version (`SpeminCommAnimation`: blinking eyes, spinning discs, a wobbling body and dripping, splashing slime), and so is the Elowan's (`ElowanCommAnimation`: curling vines, a swaying stalk, fluttering leaves and blinking eyes). The other races are next.
- **Sensor pictures**: the sensor window has a picture for every vessel in the game data. The Spemin ships, the Mechan Scout, the Velox drone, the Mysterion and the Minstrel are drawn by hand; the Elowan, Thrynn, Veloxi, Gazurtoid and Uhlek ships, the Nomad probe, the Enterprise and the Noah 9 derelict are traced from screenshots of the original (`DevTools/SensorPictures`, see its [README](DevTools/SensorPictures/README.md)). An unknown object leaves the window black inside its magenta border with no readout, and the science officer reports an unidentified object, as in the original. Every wreck shows the debris of its own vessel (`DevTools/SensorPictures/make-debris.ps1`). A scanned planet shows its own surface, from the maps the planet generator made for it, on a slowly turning sphere; the scan starts as the original's static and clears into the picture.
- **Lifeform models**: one model for each body plan in the 1984 design notes and two for each plant size, 17 in all, in `Assets/Game Objects/Lifeforms/`. They are built by the Blender scripts in `DevTools/LifeformModels` (see its [README](DevTools/LifeformModels/README.md) and the review renders in its `previews/`), each fits a 1 unit cube, and each has a skin material that is tinted with its species color in the game.
- **Known asset issue**: `Thrynn.fbx` is about 74 MB and would benefit from extracting its materials and reducing its polygon count.

## 🪐 Planet rendering

Planets are rendered at runtime from the original game's data through a **deterministic visualization system**:

- **Preservation first**: there is no random world generation. The original's 8-bit heightmaps and biome data are translated into modern 3D meshes and textures.
- **Unity Jobs and Burst**: high-performance terrain generation keeps planetary landings smooth.
- **Vertex coloring**: a custom `TerrainJob` assigns biome colors from the original's fixed height and temperature data.

## 🛠 Build from source

### Prerequisites
- **Unity 6 (6000.3.13f1)** with **Windows Build Support**, installed through Unity Hub. The project uses Unity 6 features such as `Awaitable`.
- **VS Code** with the C# Dev Kit is the recommended code editor.

### Open and play in the Editor
1. Clone the repository:
   ```bash
   git clone https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port.git
   ```
2. Open **Unity Hub**, click **Add**, then **Add project from disk**, and select the repository's root folder (the one containing this README). The first import takes a while.
3. Open any of the four scenes in `Assets/Scenes/` (`Persistent`, `Intro`, `Starport`, `Spaceflight`) and press **Play**: each one loads `Persistent` first.

In the Editor a new game starts with 1,000,000 MU for testing (a build starts with the original's 12,000), and two debug keys are available: **F9** spawns a hostile Spemin encounter and **F10** destroys the ship.

### Make a build
Use **File > Build Profiles > Windows > Build**, or run this from a shell (with no Editor open on the project):

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.13f1/Editor/Unity.exe" -batchmode -quit -projectPath . -buildTarget Win64 -buildWindows64Player "Build/Starflight.exe" -logFile -
```

Leave the `Starflight_BurstDebugInformation_DoNotShip` folder out of anything you distribute. A build can leave baked lighting files in `Assets/Scenes/Starport/` (`LightingData.asset`, lightmaps, reflection probes); don't commit them.

### Continuous integration
Every pull request and every push to `master` runs a headless Unity compile check (`.github/workflows/compile-check.yml`, via [GameCI](https://game.ci)). It needs repository secrets under **Settings > Secrets and variables > Actions**: `UNITY_EMAIL` and `UNITY_PASSWORD`, plus either `UNITY_SERIAL` (a Pro or Student plan serial number) or `UNITY_LICENSE` (Personal plan: the contents of `Unity_lic.ulf` after activating in Unity Hub; `UnityEntitlementLicense.xml` is a different format and won't work). See the [GameCI activation guide](https://game.ci/docs/github/activation). To test a different editor version before upgrading, run the workflow manually from the **Actions** tab and enter a `unityVersion`.

### Local testing
`DevTools/HeadlessProbe/` contains a headless compile check and a play-mode test harness for Windows. The harness runs the real Spaceflight or Starport scene in batch mode with an in-memory save system and checks the game's behaviour through scenarios, one for every fix made since October 2026. It lives outside `Assets/`, so it is never part of a build. See its [README](DevTools/HeadlessProbe/README.md).

`DevTools/WindowsSandbox/` tries a build on a clean copy of Windows: it runs the game in Windows Sandbox and collects its log, its saves and whether anything was blocked, without touching your own saves. It also runs a build on a PC where Smart App Control blocks unsigned programs. See its [README](DevTools/WindowsSandbox/README.md).

### Optional world-event prototype
`Assets/Scripts/AI/` holds an experimental, editor-only prototype that asks a language model, through a command line client (`ClaudeCliChatService`), to generate world events such as a border dispute between the Thrynn and the Elowan. It is not part of the game: no game scene uses it, and its assemblies compile only while its packages are present. NuGetForUnity (in `Packages/manifest.json`) restores `Microsoft.SemanticKernel.Core` and its dependencies from `Assets/packages.config` into `Assets/Packages/`, and `SemanticKernelDefineSync` then sets the `STARFLIGHT_SEMANTIC_KERNEL` define. Commit `Assets/Packages/`, `Assets/packages.config` and `ProjectSettings/ProjectSettings.asset` together, since the define without the DLLs fails the CI compile. **Starflight Remake > AI** has menu items to add the prototype to a scene and to import its test crate (`DevTools/Blender/make_scifi_crate.py`); the `AI Sandbox` scene is set up for trying it.

### Contributing
Contributions are welcome. [CONTRIBUTING.md](CONTRIBUTING.md) explains how the project is run: one change per pull request, the code style, the changelog and how changes are tested.

## 📂 Project structure

```text
Assets/
├── _MyCombatAssets/       # Combat audio, materials and weapon VFX
├── 3rd Party/             # External plugins and legacy effect packages
├── Exported/              # Debug textures and terrain/heightmap exports
├── Fonts/                 # Orbitron, Rajdhani and Share Tech Mono for the UI kit, with their OFL licences
├── Game Objects/          # Prefab categories and 3D models
│   └── Aliens/            # Alien models (Thrynn, Spemin and more)
├── Music/                 # Game music and atmospheric tracks
├── Planet Generator/      # Deterministic generation data and editor-only tools
├── Resources/             # Runtime-loaded assets
│   ├── Planets/           # The original's binary data for the 811 fixed planets (0.bytes to 810.bytes)
│   ├── Prefabs/           # Core managers (PlanetManager, Atmosphere)
│   └── Starflight Game Data.json # The original game's data
├── Scenes/                # Game scenes (Persistent, Intro, Starport, Spaceflight)
├── Scripts/               # Game logic and architecture
│   ├── AI/                # Optional, experimental world-event prototype (not used by the game)
│   ├── Game Data/         # Game data classes (GD_Planet, GD_Star, GD_Vessel)
│   ├── Persistent/        # Global managers (DataController, SoundController)
│   └── Spaceflight/       # Combat, navigation and console logic
├── Shaders/               # Custom shaders (atmosphere, clouds, skybox)
├── Shared/                # Common materials, global colors and noise textures
├── Sounds/                # UI feedback and environmental sound effects
├── TextMesh Pro/          # TMP font assets and style definitions
├── Tools/                 # Editor utilities and batch generation tools
├── UI/                    # Bridge graphics and UI sprites
├── UI Kit/                # Sliced sprites of the UI kit (UIKitTheme, Starflight Remake > UI Kit > Rebuild)
├── UI Toolkit/            # UI Toolkit themes and UXML documents
└── Unity Player/          # Build assets (icons and splash screens)
```

## 🎯 Everything in version 1.1.0

**Spoiler warning.** This list names the game's secrets, its artifacts and its ending. Skip it if you'd rather discover them yourself.

<details>
<summary>Show the full feature list (spoilers)</summary>

- Full Starport operations: Personnel, Crew Assignment, Ship Configuration, the Trade Depot, the Bank and Operations
- Hyperspace navigation and star system exploration
- Planetary landings and terrain vehicle exploration
- Ruins on the planets where the original game left its messages, recorded in the ship's log by the terrain vehicle
- The original's special artifacts in the ruins of their sites, picked up by the terrain vehicle, with the Most Magnificent Hexagon and the City of the Ancients around two of them
- The terrain vehicle can drop cargo and pick it up again, with a cargo display while the cargo list is open
- The Crystal Planet's field damages any ship in orbit that does not carry the Crystal Orb
- The Black Egg: the terrain vehicle takes it from the ship's hold when it heads out (as it does the Flat Device); dropped on the surface it arms itself, and once the ship is back in orbit it counts down and destroys the planet (the Crystal Planet only at its control nexus)
- The Crystal Cone locates the Crystal Planet's control nexus from orbit
- The Tesseract halves the fuel the engines burn in hyperspace
- The Whining Orb translates the Spemin
- The Rod Device cuts the damage of alien lasers to a quarter
- The Crystal Pearl warps a critically damaged ship out of an encounter
- Alien ships come after you as in the original: every two star hours they jump toward your ship, half the way in hyperspace and three quarters in a star system, and in hyperspace they stay near their home ground; the Dodecahedron draws them farther and faster
- The Shimmering Ball cloaks the ship in combat until it fires
- The Flat Device shields the terrain vehicle: lifeforms hit its crew half as often
- The Red Cylinder flashes on the landing map near a planet's ancient ruins
- The ancient ruins placed by STRINFO's editors, on the Mechans' ice planet and deep in Elowan space, hold their Endurium
- Continuum fluxes show on the screen to a navigator trained beyond 150, or with the Ring Device aboard
- The price of Endurium rises with the notices of 20-02 and 15-05, and the Starport buys it back at its selling price
- Nebulae keep the shields down
- The Starport repairs the hull and damaged systems at the original's prices, and buys parts back according to their condition
- A confirmed distress call gets the ship towed home, for a fine that grows with the distance from Arth
- Alien ships have the armor, shields and speed of their vessels in the game data
- The starmap shows the fuel needed to fly to the cursor
- Planets can be recommended for colonization and named by the player; Interstel pays a bonus or fines the crew at the Starport, and each evaluation appears under its recommendation's number, date, system, orbit and name, one per page on the Operations evaluation screen
- The 35 planets with life have lifeforms of their own species, which Scan reads and the terrain vehicle's laser and stunner affect. Dangerous lifeforms come after the vehicle and attack the crew, while timid ones flee. A crew member whose vitality reaches 0 dies, a dead captain's command passes down the chain of command, and the game is lost when the whole crew has died. Lifeforms hunt, flee and eat one another, and some fly or float out of reach. The terrain vehicle's Cargo button records a lifeform's data, which Interstel pays for at the Starport, or captures it into stasis as a specimen the Trade Depot will buy. Look describes the nearest lifeform and what it is doing, along with the minerals, ruins and dropped cargo next to the vehicle
- Lava destroys a terrain vehicle that drives into it, and the Starport installs a new one for a fine
- Terrain vehicle fuel as the original's: every kilometer driven costs fuel by the ground under the vehicle, little at the shore, more the higher it climbs and about seven times as much on a liquid; a storm makes each kilometer dearer without slowing the vehicle
- Surface weather as the original's: spells of weather rolled from the planet's weather activity, with storms picked by its surface; a storm halves the terrain vehicle's efficiency and can injure the crew, heavy snow, fog and rain cost a quarter of it, and storms and heavy snow lose the bearing to the ship unless the navigator is highly skilled
- A quarter of the planets hold random ruins: ancient crystal ruins with Endurium or truly amazing artifacts, and Old Empire ruins with artifacts
- Destroyed ships leave debris that the ship can salvage by flying over it
- The science officer's skill decides how much of an analysis is revealed, and each planet's readings are analyzed once
- The bank pays the original's 12% annual interest on the balance when the crew boards at the Starport and with each new colony recommendation, shown as an Interest row in the ledger
- Hits that get past the shields fall on the armor, and only once the armor is gone on the hull, the ship's systems (engines, sensors, comms, shield generator, missile launcher, laser cannon) and the crew, as in the original. The ship is lost when the hull's integrity is gone, and damaged systems work in proportion to their health (damaged engines slow the ship only in a fight, as in the original). Damage shows the original's DAMAGE display, with a bar for the hull and each system, and Repair lets the player choose what the engineer repairs (the hull and the systems, never the armor), using minerals from the hold
- The game can be won: destroying the Crystal Planet ends the flares, and Interstel pays its 500,000 MU bonus and a supplemental evaluation at the Starport
- Arth's sun flares on 01-01-4621 unless the game has been won: the Starport is destroyed, and any ship in Arth's system is incinerated
- 10 alien races, each with its own encounter behavior
- Encounters that guard planets from orbit, fought over the planet's own surface; a Veloxi drone grants permission to orbit to a crew that answers its numbers the Veloxi way
- The Spemin, Thrynn and Veloxi home fleets warn ships away from their homeworlds, more sternly each time, and attack after the last warning
- The Spemin, Thrynn and Veloxi give up after losing half their ships: they stop fighting and surrender
- The crew reports when aliens raise or lower their shields and arm or disarm their weapons, and Analysis gives the class of their shields and whether they are up
- The Elowan give 15 cubic meters of Endurium to a crew whose need for energy is dire, and see through one that already has 20 or more
- The Veloxi demand a tribute of Endurium, and the Crystal Orb or the Ellipsoid if the ship carries one, and attack a crew that refuses
- The Thrynn buy artifacts at their own prices and plutonium for Endurium, and sell the Black Box for 30 Endurium
- The Elowan, Thrynn, Spemin and Veloxi have moods: the player's posture calms or angers them, they grovel before a stronger ship, grow tired of talking, and fight or flee once they are too agitated to talk
- The Elowan and the Thrynn hold the crew's friendship with the other race against it, and a crew member of the aliens' own race understands every word they say
- The Minstrels sing, the Nomad probes transmit their planetary data, and the Noah 9 derelict sends its distress beacon and recorded message
- A complete combat system with lasers, missiles, shields and armor
- Ship destruction with debris
- A model with debris for every alien vessel in the game data
- Game over and restart
- A JSON save and load system

</details>

### 📊 Completion: 59 of 59 roadmap items

Every item in [ROADMAP.md](ROADMAP.md) is done or decided, and so is every item in the screen-by-screen comparison with the original (`Research/Screen comparison.md`), the UI kit included. The headless test harness covers every one of them, and its playthrough plays a new game through to the win. A full playthrough of 1.0.0 by hand is still to come.

Still open from the comparison with the original's code: how fast alien ships move against the player's ship and how often an alien distress call brings in another wave, both of which need a measurement in the original game.

See [CHANGELOG.md](CHANGELOG.md) for the detailed version history.

---

> *Original concept by the BraveArmy team. Unity 6 port and modernization by @cptjamestiberiouskirk-pixel.*
