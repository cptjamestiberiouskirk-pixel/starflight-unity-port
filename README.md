# Starflight: The Remaking of a Legend

[![Latest release](https://img.shields.io/github/v/release/cptjamestiberiouskirk-pixel/starflight-unity-port?label=download)](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/releases/latest)
![Platform: Windows 64-bit](https://img.shields.io/badge/Platform-Windows%2064--bit-blue)
![Unity 6](https://img.shields.io/badge/Unity-6000.3-blue.svg)
[![Compile check](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/actions/workflows/compile-check.yml/badge.svg)](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/actions/workflows/compile-check.yml)
[![License: Unlicense](https://img.shields.io/badge/License-Unlicense-lightgrey.svg)](LICENSE)

A complete remake of the 1986 space exploration classic *Starflight*, in Unity 6. Recruit a crew, outfit a starship and explore 270 star systems and 811 planets: mine them, record their lifeforms, meet the races that share the galaxy, talk or fight your way past them, and find out why the stars are flaring before Arth's own sun does.

The galaxy is the original's: every star, planet, price and alien line comes from the 1986 game, and its rules follow the original's own code. The look is new: 3D planets, stars and terrain, and a new ship's console in 15 color schemes.

<p align="center">
  <img src="docs/images/orbit.jpg" width="49%" alt="A planet seen from orbit, with the System Map and the ship's console">
  <img src="docs/images/starport.jpg" width="49%" alt="The Starport, where the astronaut walks between the rooms">
</p>
<p align="center">
  <img src="docs/images/ship-configuration.jpg" width="60%" alt="Ship Configuration, where the ship is outfitted">
</p>

---

## 🎮 Download and play

1. Download **Starflight-1.0.0-win64.zip** (600 MB) from the [latest release](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/releases/latest).
2. Unzip it anywhere, for example into `Documents\Games`. Keep the files together: `Starflight.exe` needs the folders next to it.
3. Run `Starflight.exe`.

There is no installer. To uninstall, delete the folder, and the saved games in `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Starflight\` if you want them gone too.

**Requirements:** Windows 10 or 11 (64-bit), a graphics card with DirectX 11, and 1.1 GB of disk space.

**If Windows blocks the game.** Version 1.0.0 is not code-signed, so Windows treats it as a program it does not know:

- *"Windows protected your PC"* (Microsoft Defender SmartScreen): choose **More info**, then **Run anyway**.
- *"An Application Control policy has blocked this file"* (Smart App Control, Windows 11): Smart App Control makes no exception for a single program, so the game cannot start while it is on. You can still play it in the Unity Editor (see [Build from source](#-build-from-source)).

The release page lists the zip's SHA-256 checksum. To check your download, run `Get-FileHash Starflight-1.0.0-win64.zip` in PowerShell and compare.

## 📖 How to play

The **[Player's Manual](docs/MANUAL.md)** explains it all: the story, the controls, the Starport's rooms, every console button, flight, planets, aliens, combat and money. It gives nothing away: what the galaxy holds is yours to discover.

| Action | Keys |
|---|---|
| Move and steer (8 directions) | Arrow keys with Home, Page Up, End and Page Down, or the numeric keypad |
| Pick a console button | Up and Down |
| Use it, confirm, stop | Enter |
| Pause: save slots, color schemes, quit | Escape |

The game saves by itself. On the Escape panel, **Copy** keeps a spare copy of your game in another of the five slots.

New captains: read the notices in Operations, create and assign a crew of six, name the ship, buy a few cargo pods, and launch. Your first trips should earn money.

## ✅ Status: version 1.0.0

The whole game can be played, from a new game to its ending. Every item of the [roadmap to parity with the original](ROADMAP.md) is done or decided (59 of 59), and every screen has been compared with the original's.

Every change is compile-checked in CI and tested by a headless probe that plays the real scenes: more than 200 scenarios with over 1,100 checks, all passing on the code of this release, and an automated playthrough that plays a new game through to the ending and Interstel's bonus.

The known differences from the original are listed at the end of the [manual](docs/MANUAL.md#13-this-port-and-the-1986-original). The largest is combat, which is slower than the original's. Found a problem? Please [open an issue](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/issues).

[CHANGELOG.md](CHANGELOG.md) has the full history.

## 🙏 Credits

- ***Starflight*** (1986) was created by Binary Systems and published by Electronic Arts. This remake is an unofficial fan project, not affiliated with or endorsed by them.
- **The Unity remake** was begun by Marvin Herbold in 2017, with contributions from Jeffrey Graw, Aaron Franke and Andrew Steeley.
- **The Unity 6 port** and its completion: @cptjamestiberiouskirk-pixel.
- **Fonts:** Orbitron, Rajdhani and Share Tech Mono, under the SIL Open Font License (`Assets/Fonts/`).

---

# For developers

## 🚀 2026 Modernization
The project has been refactored to a clean, **root-directory architecture** to eliminate legacy "zombie" assets and streamline development.

### Key Engineering Changes
- **Engine Upgrade**: Fully migrated to Unity 6 (6000.3.13f1).
- **Dependency Management**: Removed deprecated third-party libraries (`JsonDotNet`, legacy `FbxExporters`) in favor of official Unity Registry packages.
- **UI System**: Migrated to official Unity UI & TextMeshPro packages.
- **Input Handling**: All input goes through the Input System package (Active Input Handling is "Input System Package (New)", so the Input Manager deprecation warning is gone). `Assets/Input/Starflight Input.inputactions` keeps the original key layout: the numpad or the arrow, Home, End, Page Up and Page Down keys steer, Return or the numpad Enter fires, Escape cancels, and the UI moves on the arrows and numpad 8, 2, 4 and 6. Bindings can be changed in that asset.

---

## 🎮 Features

- **Deterministic Exploration**: Navigate a fixed map of **270 star systems and approximately 800 predetermined planets**, preserving the exact coordinates and data of the 1986 original.
- **Data-Driven Architecture**: Uses JSON-based definitions for game assets and player progress via the `ISaveSystem`.
- **Combat System**: Laser cannons, missile launchers, shield absorption, armor damage, and AI combat behaviors with victory/defeat detection. Alien ships have armor and shield points, any race turns hostile when fired on (the Uhlek attack on sight), aliens hold their fire while a comm link is up, and every shot uses a little Endurium.
- **Encounters in Orbit**: Home fleets, drones and a derelict wait in orbit around their planets and meet the ship when it goes into orbit there. The ship drops into the encounter and comes back at the level of the star system, as in the original.
- **Ship Systems Over Time**: Shields keep their charge and recharge slowly, and the engineer's repairs and the doctor's treatment take time, faster with higher skill. The doctor asks which crew member to examine or treat, as in the original.
- **Living Suns**: The sun of each star system churns, its corona flickers, and small flares rise from its rim and fall back, in the colour of real stars of its class.
- **Planets Lit by Their Star**: In the 3D star system view each planet is lit from its own star, in the star's colour, with a day side and a night side, and its atmosphere glows on the sunlit edge
- **A Milky Way in the Sky**: A faint Milky Way crosses the sky of the spaceflight views under the nebula of the territory the ship is in, and turns with the sky as the ship flies
- **Orbit Paths and Engine Glow**: In the 3D star system view a faint path in the star's colour runs along each planet's orbit, brightest just behind the planet, and the ship's engines glow softly, brighter as it speeds up
- **Ship Destruction**: Complete death sequences with explosion effects, debris spawning, and salvage opportunities.
- **Game Over System**: Player ship destruction triggers the game over screen. Returning to the title screen goes back to the last save.
- **Terrain Scanning**: Enhanced exploration with 3D world-space labels, mineral detection, and Star Trek-style transporter effects.
- **Cargo Management**: Real-time volume tracking for minerals and elements with automatic transfer to the ship. A mineral deposit holds 1.0 to 9.9 cubic meters, as in the original.
- **Original Economy in Builds**: A new game in a build starts with the original 12,000 MU (the Editor keeps 1,000,000 for testing), and the bank and the notices show stardates as the original did (`26-03-4620`).
- **Debris Scanning**: Scan destroyed vessel wreckage for salvage analysis.
- **Modern UI Kit**: the ship console, the Starport rooms and the save game panel use the remake's sci-fi UI kit (designed in Figma): Orbitron, Rajdhani and Share Tech Mono, navy panels, cyan and amber accents and a steel blue sensor window. The console shows an icon for whoever has it (each officer, the terrain vehicle, the ship's logo on the bridge). In encounters a bracket marks the targeted alien ship, with its range, or its shields when the encounter is hostile. In hyperspace the star systems in view show their coordinates, in a star system the planets show their orbit and surface, and the viewport's frame names where the ship is. Its colours, text styles and sprites live in one theme (`Resources/UI Kit Theme`), and each element takes its look from a role (`UIKitStyle`).
- **Sci-Fi Buttons**: every action the player can pick is a sci-fi button: two corners cut off, a thin frame in the kit's accent colour, lit when selected. The console's menu rows, the Starport rooms' and the save panel's buttons and the questions' Yes and No all have it, in both modes.
- **Color Schemes**: the UI kit comes in fifteen color schemes, each with a dark and a light mode: Classic (the kit as it was drawn, with its steel grey light mode), the original Starflight's EGA colors and Starflight 2's colors, and twelve more (Endurium, Interstel Amber, Phosphor, Titanium, Heritage EGA, Solar Gold, Spec-Ops, Azure Drive, Arctic Glass, Starliner, Night Ops and Hi-Vis). The save game panel (Escape, at any time in the game) has a Colors button: its screen shows every scheme with swatches of its colors and the two modes, and the whole game takes a scheme the moment it is picked. The choice is remembered on the computer (not in a saved game). The starmap, the ship's log and the Starport station (the hall the astronaut walks in, and the station seen at launch) take the scheme's colors too, in its dark mode (in the Starflight and Starflight 2 schemes the station has the 1986 Starport's colors); the other 3D views stay as they are. The schemes are theme assets in `Resources/UI Kit Themes/`, made by `DevTools/UIKitSchemes/make_themes.py`.
- **Realistic System Map**: The console's System Map shows each planet with its own surface, lit from its star and sized by its mass, a glowing star in the colour of real stars of its class (the sun of the star system view has the same colour), orbit trails, the star's ecosphere, a star field, and the ship's heading, with its engine glow, wake and sensor ping (built by `DevTools/SystemMap`, see its [README](DevTools/SystemMap/README.md)).

---

## 🛠 Build from source

### Prerequisites
- **Unity 6 (6000.3.13f1)** with **Windows Build Support**, installed through Unity Hub. The project uses Unity 6 features like `Awaitable`.
- **VS Code** with the "C# Dev Kit" is the recommended code editor.

### Open and play in the Editor
1. Clone the repository:
   ```bash
   git clone https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port.git
   ```
2. Open **Unity Hub**, click **Add**, **Add project from disk**, and select the repository's root folder (where this README is). The first import takes a while.
3. Open any of the four scenes in `Assets/Scenes/` (`Persistent`, `Intro`, `Starport`, `Spaceflight`) and press **Play**: each one loads `Persistent` first.

In the Editor a new game starts with 1,000,000 M.U. for testing (a build starts with the original's 12,000), and two debug keys work: **F9** brings a hostile Spemin encounter and **F10** destroys the ship.

### Make a build
**File > Build Profiles > Windows > Build**, or from a shell (with no Editor open on the project):

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.13f1/Editor/Unity.exe" -batchmode -quit -projectPath . -buildTarget Win64 -buildWindows64Player "Build/Starflight.exe" -logFile -
```

Leave the `Starflight_BurstDebugInformation_DoNotShip` folder out of anything you share. A build can leave baked lighting files in `Assets/Scenes/Starport/` (`LightingData.asset`, lightmaps, reflection probes): do not commit them.

### Continuous Integration
Every pull request and push to `master` runs a headless Unity compile check (`.github/workflows/compile-check.yml`, via [GameCI](https://game.ci)). It needs repo secrets under **Settings > Secrets and variables > Actions**: `UNITY_EMAIL` and `UNITY_PASSWORD`, plus either `UNITY_SERIAL` (Pro or Student plan serial number) or `UNITY_LICENSE` (Personal plan: the contents of `Unity_lic.ulf` after activating in Unity Hub; `UnityEntitlementLicense.xml` is a different format and won't work). See the [GameCI activation guide](https://game.ci/docs/github/activation). To check a different editor before upgrading, run the workflow by hand from the **Actions** tab and enter a `unityVersion`.

### Local Checks
`DevTools/HeadlessProbe/` holds a headless compile check and a play-mode probe for Windows. The probe runs the real Spaceflight or Starport scene in batch mode with an in-memory save system and checks game behaviour through scenarios, one for every fix made since 2026-10-03. It sits outside `Assets/`, so it is not part of the build. See its [README](DevTools/HeadlessProbe/README.md).

---

## 📂 Project Structure

```text
Assets/
├── _MyCombatAssets/       # Custom combat audio, materials, and weapon VFX
├── 3rd Party/             # External plugins and legacy effect packages
├── Exported/              # Debug textures and terrain/heightmap exports
├── Fonts/                 # Project fonts (Orbitron, Rajdhani and Share Tech Mono of the UI kit, with their OFL licences)
├── Game Objects/          # Prefab categories and 3D models
│   └── Aliens/            # High-fidelity alien assets (Thrynn, Spemin, etc.)
├── Music/                 # Game music and atmospheric tracks
├── Planet Generator/      # Deterministic generation data and editor-only tools
├── Resources/             # Runtime loadable assets (Required for deterministic system)
│   ├── Planets/           # Legacy binary data for ~800 fixed planets (0.bytes - 810.bytes)
│   ├── Prefabs/           # Critical managers (PlanetManager, Atmosphere)
│   └── Starflight Game Data.json # Primary project configuration
├── Scenes/                # Game scenes (Intro, Spaceflight, Starport, Persistent)
├── Scripts/               # Core game logic and system architecture
│   ├── Game Data/         # C# Data Classes/Models (GD_Planet, GD_Star, GD_Vessel)
│   ├── Persistent/        # Global managers (DataController, SoundController)
│   └── Spaceflight/       # Combat, Navigation, and UI interaction logic
├── Shaders/               # Custom Unity 6 shaders (Atmosphere, Clouds, Skybox)
├── Shared/                # Common materials, global colors, and noise textures
├── Sounds/                # UI feedback and environmental sound effects
├── TextMesh Pro/          # TMP Font assets and style definitions
├── Tools/                 # Editor utilities and mass-generation tools
├── UI/                    # Bridge graphics and UI specific sprite assets
├── UI Kit/                # Sliced sprites of the remake's UI kit (UIKitTheme, Starflight Remake > UI Kit > Rebuild)
├── UI Toolkit/            # Unity 6 UI Toolkit themes and UXML documents
└── Unity Player/          # Build-specific assets (Icons and Splash screens)
```

---

## 🪐 Planet Rendering Architecture
The project features a **deterministic visualization system** that renders planet meshes and textures at runtime based on legacy data.

- **Preservation First**: It does NOT use random world generation. Procedural techniques (Jobs & Burst) translate original 8-bit heightmaps and biomes into modern 3D meshes.
- **Unity Jobs & Burst**: High-performance terrain generation ensures smooth planetary landings.
- **Vertex Coloring**: A custom `TerrainJob` assigns biome colors based on original fixed height and temperature data.

---

## 👽 Alien Asset Status
- **The player's ship**: the Arth ship is a textured model made with Meshy (a hull and a sheet of parts, put together by `DevTools/ShipModels/import_meshy_arth.py`), its 16 cargo pods, laser turret and missile launchers separate parts that the ship's equipment shows, in flight, on the status display, parked on a planet and in the Ship Configuration room.
- **Alien ship models**: the Spemin ships, the Mechan Scout and the Mysterion are hand-made models. Every other vessel (the Thrynn and the Elowan transports, scouts and warships, the four Veloxi vessels, the Gazurtoid and Uhlek scouts and warships, the Noah 9 derelict and the Enterprise) is a textured model made with Meshy from the original's sensor pictures and brought in by `DevTools/ShipModels/import_meshy.py` (see its [README](DevTools/ShipModels/README.md)). All are sized after STRINFO. The Nomad probe and the Minstrel use their comm-screen models.
- **Thrynn.fbx**: Currently ~74MB. Requires manual extraction of materials and potential polygon reduction to optimize performance.
- **Comm portraits**: each race's comm screen is a small 3D scene of its own (the race's model, a light and a camera under the race's group of the Encounter location). The Spemin's moves as the Genesis version's portrait does (`SpeminCommAnimation`: blinking eyes, spinning discs, a wobbling body, slime that drips and splashes), and so does the Elowan's (`ElowanCommAnimation`: vines that curl, a swaying stalk, fluttering leaves, blinking eyes); the other races are to follow.
- **Sensor pictures**: the sensor window shows a picture for the Spemin ships, the Mechan Scout, the Velox drone, the Mysterion and the Minstrel (drawn by hand) and for the Elowan, Thrynn, Veloxi, Gazurtoid and Uhlek ships, the Nomad probe, the Enterprise and the Noah 9 derelict (traced from the original's screenshots by `DevTools/SensorPictures`, see its [README](DevTools/SensorPictures/README.md)): every vessel of the game data. An unknown object leaves the window black inside its magenta border with no readout, and the science officer reports an unidentified object, as in the original; every wreck shows the debris of its own vessel (made from its picture by `DevTools/SensorPictures/make-debris.ps1`). A scanned planet shows its own surface, from the maps the planet generator made for it, on a lit sphere that turns slowly; the scan starts as the original's noise and clears into it.
- **Lifeform models**: one model for each body plan of the 1984 design notes and two for each plant size, in the game since the lifeforms are placed as their models, tinted with their species colour, 17 in all, in `Assets/Game Objects/Lifeforms/`, built by the Blender scripts in `DevTools/LifeformModels` (see its [README](DevTools/LifeformModels/README.md) and the review renders in its `previews/`). Each fits a 1 unit cube and has a skin material to tint with the species colour.

---

## 🎯 What the port implements (v1.0.0)

**Spoilers.** This list names the game's secrets, its artifacts and its ending. Skip it if you want to discover them in play.

<details>
<summary>Show the full feature list (spoilers)</summary>

- Full starport operations (Personnel, Ship Config, Trading, Banking)
- Hyperspace navigation and star system exploration
- Planetary landing and terrain vehicle exploration
- Ruins on the planets where the original game left its messages; the terrain vehicle records them in the ship's log
- The original's special artifacts in the ruins of their sites, picked up by the terrain vehicle, with the Most Magnificent Hexagon and the City of the Ancients around two of them
- The terrain vehicle drops cargo from its hold and picks it up again, with a cargo display while the cargo list is open
- The Crystal Planet's field damages a ship in orbit that does not carry the Crystal Orb
- The Black Egg: the terrain vehicle takes it out of the ship's hold when it goes out (as it takes the Flat Device), dropped by the terrain vehicle it is armed, and back in orbit it counts down and destroys the planet (the Crystal Planet only at its control nexus)
- The Crystal Cone locates the Crystal Planet's control nexus from orbit
- The Tesseract halves the fuel the engines use in hyperspace
- The Whining Orb translates the Spemin
- The Rod Device cuts the damage of alien lasers to a quarter
- The Crystal Pearl warps a critically wounded ship out of an encounter
- The Dodecahedron draws alien ships from twice as far
- The Shimmering Ball cloaks the ship in combat until it fires
- The Flat Device shields the terrain vehicle: lifeforms hit its crew half as often
- The Red Cylinder flashes on the landing map near the planet's ancient ruins
- The ancient ruins STRINFO's editors place, on the Mechans' ice planet and deep in Elowan space, hold their Endurium
- Continuum fluxes can be seen only with the Ring Device aboard
- The price of Endurium rises with the notices of 20-02 and 15-05, and the Starport buys it back for what it sells it for
- Nebulae keep the shields down
- The Starport repairs the hull and damaged systems at the original's prices, and buys parts back by their condition
- A distress call, once confirmed, gets the ship towed home, for a fine that grows with the distance from Arth
- Alien ships have the armor, shields and speed of their vessels in the game data
- The starmap shows the fuel needed to fly to the cursor
- Planets can be recommended for colonization and named by the player, and Interstel pays a bonus or fines the crew at the Starport, each evaluation under its recommendation's number, date, system, orbit and name, one to a page on the Operations evaluation screen
- The 35 planets with life have lifeforms of their own species, which Scan reads the terrain vehicle's laser and stunner act on; dangerous ones come at the vehicle and attack the crew, and wimpy ones flee; a crew member whose vitality reaches 0 is dead, a dead captain's command passes down the chain of command, and the game is lost when the whole crew has died; lifeforms hunt, flee and eat one another, and some fly or float out of reach; the terrain vehicle's Cargo records a lifeform's data, which Interstel pays for at the Starport, or captures it into stasis as a specimen the Trade Depot buys; Look describes the nearest lifeform and what it is doing, and the minerals, ruins and dropped cargo next to the vehicle
- Lava destroys a terrain vehicle that drives into it; the Starport installs a new one for a fine
- Going uphill is slow: the terrain vehicle slows on a rising slope, and a slow climb takes more fuel for the distance
- Storms on the surface, as the original has them: the sky changes every game hour, and a storm halves the terrain vehicle's efficiency, loses the bearing to the ship and injures the crew
- A quarter of the planets hold random ruins: ancient crystal ruins with Endurium or truly amazing artifacts, and Old Empire ruins with artifacts
- Destroyed ships leave debris the ship can salvage by flying over it
- The science officer's skill decides how much of an analysis is known, and a planet's reading is analyzed once
- The bank pays the original's 12% interest a year on the balance, when the crew goes aboard at the Starport and on a new colony recommendation, with an Interest row in the ledger
- Hits damage the ship's systems (engines, sensors, comm, shield generator, missile launcher, laser cannon) as well as the hull, and damaged systems work in proportion to their health; Damage shows the original's DAMAGE display, a bar for the hull and each system, and Repair lets the player choose on it what the engineer repairs, with minerals from the hold
- The game can be won: destroying the Crystal Planet ends the flares, and Interstel pays its 500,000 MU bonus and its supplemental evaluation at the Starport
- Arth's sun flares on 01-01-4621 unless the game has been won: the Starport is destroyed, and a ship in Arth's system is incinerated
- 10 alien races with unique encounter behaviors
- Encounters that guard planets from orbit, fought over the planet's own surface; a Veloxi drone grants permission to orbit to a crew that answers its numbers the Veloxi way
- The Spemin, Thrynn and Veloxi home fleets warn a ship off their homeworlds, more sternly each time, and attack after the last warning
- The Spemin, Thrynn and Veloxi give up after losing half of their ships: they stop fighting and surrender
- The crew tells the captain when the aliens raise or lower their shields and arm or disarm their weapons, and Analysis gives their shields' class and whether they are up
- The Elowan give 15 cubic meters of Endurium to a crew whose need for energy is dire, and see through one that has 20 or more
- The Veloxi demand a tribute of Endurium, and the Crystal Orb or the Ellipsoid when the ship carries one, and attack a crew that refuses
- The Thrynn buy artifacts at their own prices and plutonium for Endurium, and sell the Black Box for 30 Endurium
- The Elowan, Thrynn, Spemin and Veloxi have moods: the player's posture calms or angers them, they grovel to a stronger ship, tire of talking, and fight or flee once too agitated to talk
- The Elowan and the Thrynn hold the crew's friendship with the other race against it, and a crew member of the aliens' own race understands every word they say
- The Minstrels sing, the Nomad probes send their planetary data, and the Noah 9 derelict sends its distress beacon and recorded message
- Complete combat system with lasers, missiles, shields, armor
- Ship destruction with debris spawning
- A model with debris for every alien vessel of the game data (hand-made, made with Meshy, or procedural stand-ins)
- Game over and restart functionality
- Save/load system with JSON persistence

</details>

### 📊 Completion: 59 of the 59 items of the roadmap to parity

Every item of [ROADMAP.md](ROADMAP.md) is done or decided. Every item of the comparison of the screens with the original's (`Research/Screen comparison.md`) is fixed or decided as well, the UI kit's included. The headless probe plays every scenario of it, and its playthrough plays a new game through to the win; nobody has yet played 1.0.0 through to the end by hand.

Still open from the comparison with the original's code: the combat damage model (the original's weapons hit about five times harder, and its ship fights on at 0 armor until the hull gives out), and smaller details: alien pursuit through hyperspace, hyperspace speeds and fuel per step, flux jumps and the navigator's skill, the terrain vehicle's fuel by terrain, buying armor, and when the price of Endurium changes.

See [CHANGELOG.md](CHANGELOG.md) for detailed version history.

---

> *Original concept by the BraveArmy team. Unity 6 Port and Modernization by @cptjamestiberiouskirk-pixel.*