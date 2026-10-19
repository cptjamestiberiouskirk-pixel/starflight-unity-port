# Starflight: The Remaking of a Legend

[![Latest release](https://img.shields.io/github/v/release/cptjamestiberiouskirk-pixel/starflight-unity-port?label=download)](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/releases/latest)
![Platform: Windows 64-bit](https://img.shields.io/badge/Platform-Windows%2064--bit-blue)
![Unity 6](https://img.shields.io/badge/Unity-6000.3-blue.svg)
[![Compile check](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/actions/workflows/compile-check.yml/badge.svg)](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/actions/workflows/compile-check.yml)
[![License: Unlicense](https://img.shields.io/badge/License-Unlicense-lightgrey.svg)](LICENSE)

**A complete, faithful remake of the 1986 space exploration classic *Starflight*, rebuilt from the ground up in Unity 6.**

*Starflight* was one of the first games to hand players an entire galaxy and simply let them loose in it. This project brings it back in full: recruit a crew, outfit your starship and set out across 270 star systems and 811 planets. Mine worlds for minerals, catalogue strange lifeforms, talk (or fight) your way past the races that share the galaxy, and find out why the stars are flaring before Arth's own sun does the same.

The galaxy is the original's, down to the last detail: every star, planet, price and line of alien dialogue comes straight from the 1986 game, and the rules follow the original code. What's new is the way it looks, with fully 3D planets, stars and terrain, a redesigned ship's console and 15 color schemes to choose from.

Version 1.0.0 was the first complete release, playable from the first launch to the final ending. Version 1.1.0 brings the original's combat: hits that wear down the shields, the armor and then the hull, the original's weapon damage, and a navigator whose skill decides whether your shots hit. Version 1.2.0 brings a richer ground game: a mining laser, a Return order and a cargo transporter for the terrain vehicle, and grass and finer ground up close. The planets can now be drawn in natural colors, the science officer surveys a planet from orbit, and the ship flies at the original's pace. Version 1.3.0 makes the ship easier to fly: it steers the moment you press a direction, as in the original, and its speed follows its engines. Version 1.4.0 gives every ship, the terrain vehicle, the ruins, the rocks and the artifacts textured models, brings new sound effects made for this port, and adds the original's equipment malfunctions and shield recharge.

<p align="center">
  <img src="docs/images/orbit.jpg" width="49%" alt="A planet seen from orbit, with the System Map and the ship's console">
  <img src="docs/images/starport.jpg" width="49%" alt="The Starport, where the astronaut walks between the rooms">
</p>
<p align="center">
  <img src="docs/images/ship-configuration.jpg" width="60%" alt="Ship Configuration, where the ship is outfitted">
</p>

---

## 🎮 Download and play

1. Download **Starflight-1.4.0-win64.zip** (670 MB) from the [latest release](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/releases/latest).
2. Unzip it anywhere, for example into `Documents\Games`. Keep the files together: `Starflight.exe` needs the folders next to it.
3. Run `Starflight.exe`.

There is no installer. To uninstall, delete the folder, along with the saved games in `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Starflight\` if you want those gone too.

**Requirements:** Windows 10 or 11 (64-bit), a DirectX 11 graphics card and 1.2 GB of free disk space.

**If Windows blocks the game.** The game is not code-signed, so Windows treats it as an unknown program:

- *"Windows protected your PC"* (Microsoft Defender SmartScreen): choose **More info**, then **Run anyway**.
- *"An Application Control policy has blocked this file"* (Smart App Control, Windows 11): Smart App Control does not allow exceptions for individual programs, so the game cannot start while it is switched on. You can still play it in the Unity Editor (see [Build from source](#-build-from-source)).

The release page lists the zip's SHA-256 checksum. To verify your download, run `Get-FileHash Starflight-1.4.0-win64.zip` in PowerShell and compare the two.

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

## ✅ Status: version 1.4.0

The whole game is playable, from a new game to the ending. Every item of the first seven phases of the [roadmap to parity with the original](ROADMAP.md) is done or decided (59 of 59), and every screen has been compared side by side with the original's. A reading of the shipped game's own code in October 2026 found 37 more of its behaviours to build (Phase 8), and they are under way.

Every change is compile-checked in CI and exercised by a headless test harness that plays the real game scenes: 276 scenarios with 1,513 checks, all passing on this release, plus an automated playthrough that plays a new game all the way to the ending and Interstel's bonus.

The known differences from the original are listed at the end of the [manual](docs/MANUAL.md#13-this-port-and-the-1986-original). The biggest one is by design: the ship's speed follows its engines, where every ship in the original flew at the same speed. Found a bug? Please [open an issue](https://github.com/cptjamestiberiouskirk-pixel/starflight-unity-port/issues).

The full history is in [CHANGELOG.md](CHANGELOG.md).

## 🙏 Credits

- ***Starflight*** (1986) was created by Binary Systems and published by Electronic Arts. This remake is an unofficial fan project and is not affiliated with or endorsed by either company.
- **The original Unity remake** was started by Marvin Herbold in 2017, with contributions from Jeffrey Graw, Aaron Franke and Andrew Steeley.
- **The Unity 6 port** and its completion to 1.0.0: @cptjamestiberiouskirk-pixel. Claude AI assisted with development.
- **Fonts:** Orbitron, Rajdhani and Share Tech Mono, under the SIL Open Font License (`Assets/Fonts/`).
- **Sound effects:** made for this port (`Assets/Sounds/`, `Assets/_MyCombatAssets/Sounds/`): every sound the game plays, from the console clicks to the docking bay doors and the explosions.
- **Music:** made for this port (`Assets/Music/`): a title theme and a track for each place the game plays music, the Starport, the docking bay, a star system, orbit, hyperspace, an encounter and a planet's surface.
- **Licences:** the Unlicense (`LICENSE`) covers the code and what was made for this port, the music and the sound effects among it.

---

# For developers

## 🚀 Under the hood

The port runs on **Unity 6 (6000.3.13f1)** and has been modernized throughout:

- **Unity Registry packages.** Deprecated third-party libraries (`JsonDotNet`, the legacy `FbxExporters`) were replaced with Unity Registry packages, and the UI runs on Unity UI and TextMeshPro.
- **Input System.** All input goes through the Input System package (Active Input Handling is "Input System Package (New)"). `Assets/Input/Starflight Input.inputactions` keeps the original key layout: the numpad, or the arrow, Home, End, Page Up and Page Down keys, steer; Return or the numpad Enter fires; Escape cancels; and the UI moves on the arrows and numpad 8, 2, 4 and 6. Bindings can be changed in that asset.
- **Data-driven design.** The original game's data (stars, planets, aliens, prices, dialogue) lives in JSON under `Assets/Resources/`, and player progress goes through an `ISaveSystem` that writes crash-safe JSON saves.
- **Deterministic galaxy.** The 270 star systems and 811 planets are the original's fixed data, never random generation, and every planet's surface objects are placed the same way every time.

## 🎮 Features

- **Combat**: laser cannons, missile launchers, shields and armor, with alien tactics and victory and defeat detection. Alien ships have their own armor and shield points, any race turns hostile when fired on (the Uhlek attack on sight), aliens hold their fire while a comm link is up, and every shot burns a little Endurium. Aliens act by their posture, as the original's combat rules: fighting aliens open fire, hostile ones close in and may open fire, friendly and neutral ones leave once the talking is over, grovelling ones flee, and an encounter ends when every ship is far enough away.
- **Encounters in orbit**: home fleets, drones and a derelict wait in orbit around their planets and meet the ship when it arrives. Leaving the encounter returns the ship to the star system, as in the original.
- **Speed by the engines**: the ship steers the moment you press a direction and stops when you let go, as in the original. Its top speed in hyperspace and in a star system follows its engines: class 1 flies at a third of the original's top speed, class 5 at all of it, and the ship's acceleration (engines against mass) sets how quickly it gets there, so a heavily loaded ship is sluggish.
- **Ship systems over time**: shields keep their charge and, as in the original, regain it on the ship's turns in an encounter and in full when it ends, and the engineer's repairs and the crew's healing take time, going faster with higher skill: everyone on board heals slowly, and the doctor's patient twice as fast. The doctor asks which crew member to examine or treat, just like the original.
- **Living suns**: each star system's sun churns, its corona flickers and small flares rise from its rim and fall back, all in the color of real stars of its class.
- **Planets lit by their star**: in the 3D star system view every planet is lit by its own star, in that star's color, with a day side, a night side and an atmosphere that glows along the sunlit edge.
- **Natural planets**: planets can be drawn in natural colors, worked out from the original's data for each planet: seas colored by what they are made of, land by temperature, moisture and life, regolith on worlds without air and ice on frozen ones, a sky of sunlight scattered by the planet's own gases that glows along its edge, and clouds from its weather that drift over the ground and cast shadows. The maps are made on the graphics card from the planet's own maps, so the coastlines are where the terrain vehicle finds them (`NaturalPlanet` and the `Natural` shaders in `Resources/Shaders/`). Gas giants have turbulent bands and storms in the colors of their gases, and molten worlds a dark crust over glowing lava. The System Map, the sensor picture and the ground on the surface show the planets in the same colors (the landing map keeps its height colors). The Colors screen of the save game panel switches between the natural and the original look.
- **Ground detail**: the ground the terrain vehicle drives over follows its hills smoothly, with heights worked out on the graphics card as the game works them out for the vehicle, and is drawn more finely by the detail chosen on the Colors screen of the save game panel (Ground: Auto by the graphics card's memory, or Low, Medium or High; `GroundDetail`). At Medium and High the ground up close has grit and pebbles over the planet's colours (`GroundGrain`), and is shaded by its own shape, its valleys darker and its ridges lighter, with the shadows set by the detail too. Grass and pebbles stand on the ground about the vehicle at Medium and High, in the colours of the ground under them (`GroundClutter`).
- **A Milky Way in the sky**: a faint band of the Milky Way crosses the spaceflight sky beneath the nebula of the territory you are in, and turns with the sky as the ship flies.
- **Orbit paths and engine glow**: faint orbit paths in the star's color trail each planet in the 3D star system view, brightest just behind the planet, and the ship's engines glow brighter as it speeds up.
- **Ship destruction**: full death sequences with explosions, debris and salvage.
- **Game over**: losing the ship brings up the game over screen, and returning to the title screen restores the last save.
- **Terrain scanning**: world-space labels on the terrain, mineral detection and a transporter-style beam effect.
- **Ground sounds**: the mining laser hums and crackles as it cuts, a mined-out deposit crumbles, a capture plays the stasis beam's pulsing hum, and a floating artifact hums softly beside its ruin, louder as the terrain vehicle comes near.
- **Sound effects made for the port**: every sound in the game is made for this port in one grounded, industrial style, as if heard from inside a working spacecraft: metal hatches with servos and latches, hydraulic bay doors, magnetic boots on deck plates, a drive that rumbles through the hull at launch and landing, soft console chirps, comm channels broken up by digital noise, and concussive explosions. The loops (the rover's engine, the mining laser, the capture beam, an artifact's hum) repeat without a seam.
- **Music made for the port**: a modern science fiction score, dark and grounded, in low strings, deep synth bass, sparse woodwinds and metallic percussion: a title theme, and a track for the Starport, the docking bay, star systems, orbit, hyperspace, encounters and planet surfaces, each looping without a seam and streamed from disk.
- **Survey from orbit**: the science officer's analysis of a planet in orbit reports its mineral fields and lifeforms, as many as the officer's skill and the sensors find, and the sensor picture and the landing map mark them where the officer placed them. Their places are worked out from the planet's own seeds exactly as the surface places its deposits and lifeforms (`PlanetSurvey`, `SurveyMarks`).
- **Cargo management**: real-time volume tracking for minerals and elements, with automatic transfer to the ship. A mineral deposit holds 1.0 to 9.9 cubic meters, as in the original.
- **The original economy**: a new game in a build starts with the original 12,000 MU (the Editor starts with 1,000,000 for testing), and the bank and the notices show stardates in the original's format (`26-03-4620`).
- **Debris scanning**: scan the wreckage of destroyed vessels for salvage.
- **Modern UI kit**: the ship's console, the Starport rooms and the save game panel use a sci-fi UI kit designed in Figma, set in Orbitron, Rajdhani and Share Tech Mono. The console shows an icon for whoever is in charge (each officer, the terrain vehicle, the ship's logo on the bridge). In encounters a bracket marks the targeted alien ship with its range, or its shields once the encounter turns hostile. In hyperspace the visible star systems show their coordinates, in a star system the planets show their orbit and surface, and the viewport's frame names the ship's location. Colors, text styles and sprites all live in one theme (`Resources/UI Kit Theme`), and each element takes its look from a role (`UIKitStyle`).
- **Sci-fi buttons**: every selectable action is a sci-fi button with two clipped corners and a thin frame in the kit's accent color, lit when selected. The console's menu rows, the Starport and save panel buttons and the Yes and No of alien questions all share the style, in both dark and light modes.
- **Color schemes**: the UI kit comes in fifteen color schemes, each with a dark and a light mode: Classic (the kit as originally drawn, with a steel grey light mode), the original Starflight's EGA colors, Starflight 2's colors, and twelve more (Endurium, Interstel Amber, Phosphor, Titanium, Heritage EGA, Solar Gold, Spec-Ops, Azure Drive, Arctic Glass, Starliner, Night Ops and Hi-Vis). The save game panel (Escape, at any time) has a Colors button whose screen shows every scheme with swatches of its colors and both modes, and the whole game switches the moment you pick one. The choice is stored on the computer, not in a saved game. The starmap, the ship's log, the Starport's hall, the station the ship launches from and the docking bay follow the scheme too (the two original games' schemes give the station the 1986 Starport's EGA colors and the bay its colors from before the new look), while the other 3D views keep their own colors. The schemes are theme assets in `Resources/UI Kit Themes/`, generated by `DevTools/UIKitSchemes/make_themes.py`.
- **Realistic System Map**: the console's System Map shows every planet with its own surface, lit by its star and sized by its mass, around a glowing star in the color of real stars of its class (matching the sun in the star system view), with orbit trails, the star's ecosphere, a star field and the ship's heading with its engine glow, wake and sensor ping (built by `DevTools/SystemMap`, see its [README](DevTools/SystemMap/README.md)).

## 👽 Ships, aliens and lifeforms

- **The player's ship**: the Arth ship is a new textured model (a hull plus a sheet of parts, assembled by the import scripts in `DevTools/ShipModels`). Its 16 cargo pods, laser turret and missile launchers are separate parts that appear as the ship is outfitted: in flight, on the status display, parked on a planet and in the Ship Configuration room.
- **Alien ships**: every vessel of the game (the Spemin, Thrynn and Elowan transports, scouts and warships, the four Veloxi vessels, the Gazurtoid and Uhlek scouts and warships, the Mechan scout, the Mysterion, the Nomad probe, the Minstrel, the Noah 9 derelict and the Enterprise) is a textured model based on the original's sensor pictures, imported by the scripts in `DevTools/ShipModels` (see its [README](DevTools/ShipModels/README.md)), and leaves a wreck of its own. All of them are sized according to STRINFO.
- **Comm portraits**: each race's comm screen is a small 3D scene of its own (the race's model, a light and a camera under the race's group in the Encounter location). The Spemin portrait is animated after the Genesis version (`SpeminCommAnimation`: blinking eyes, spinning discs, a wobbling body and dripping, splashing slime), and so is the Elowan's (`ElowanCommAnimation`: curling vines, a swaying stalk, fluttering leaves and blinking eyes). The other races are next.
- **Sensor pictures**: the sensor window has a picture for every vessel in the game data. The Spemin ships, the Mechan Scout, the Velox drone, the Mysterion and the Minstrel are drawn by hand; the Elowan, Thrynn, Veloxi, Gazurtoid and Uhlek ships, the Nomad probe, the Enterprise and the Noah 9 derelict are traced from screenshots of the original (`DevTools/SensorPictures`, see its [README](DevTools/SensorPictures/README.md)). An unknown object leaves the window black inside its magenta border with no readout, and the science officer reports an unidentified object, as in the original. Every wreck shows the debris of its own vessel (`DevTools/SensorPictures/make-debris.ps1`). A scanned planet shows its own surface, from the maps the planet generator made for it, on a slowly turning sphere; the scan starts as the original's static and clears into the picture.
- **The terrain vehicle**: a new textured model in the look of the Arth ship, its body and one wheel made from a concept picture and placed in the old model's node tree by `DevTools/ShipModels/import_meshy_tv.py`, so its six wheels still turn and steer and its radar still turns.
- **Ancient ruins**: five textured models of crumbling pyramids of dark stone with faintly glowing glyphs, placed where the old ruins were by `DevTools/ShipModels/import_meshy_props.py`.
- **Rocks and mineral deposits**: nine textured rocks, which twenty kinds of deposit share in their element's colour, a glowing Endurium crystal cluster and a mercury pool (`DevTools/ShipModels/import_meshy_props.py`).
- **Artifacts**: each of the 15 special artifacts has a textured model of its own, and shows beside the ruin that holds it, hovering and turning under a soft light, until the terrain vehicle takes it. Selected on a Trade Depot list or marked on the terrain vehicle's cargo list, it turns in a small window beside the list (`ArtifactPreview`; the models come from `DevTools/ShipModels/import_meshy_artifacts.py`).
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

## 🎯 Everything in version 1.4.0

**Spoiler warning.** This list names the game's secrets, its artifacts and its ending. Skip it if you'd rather discover them yourself.

<details>
<summary>Show the full feature list (spoilers)</summary>

- Full Starport operations: Personnel, Crew Assignment, Ship Configuration, the Trade Depot, the Bank and Operations
- Hyperspace navigation and star system exploration; the edge of hyperspace holds the ship, as in the original
- Planetary landings and terrain vehicle exploration
- The terrain vehicle offers to go back into the ship when it stops within 3 km of it on the way back, and Tools, Return drives it back by itself
- The terrain vehicle mines mineral deposits with a mining laser (Tools, Mine), a tenth of a cubic meter at a time on the vehicle's energy
- The terrain vehicle's cargo list sends its cargo to the ship by transporter from within 20 km (Send)
- Ruins on the planets where the original game left its messages, recorded in the ship's log by the terrain vehicle
- The original's special artifacts in the ruins of their sites, picked up by the terrain vehicle, with the Most Magnificent Hexagon and the City of the Ancients around two of them
- The terrain vehicle can drop cargo and pick it up again, with a cargo display while the cargo list is open
- Without the Crystal Orb a ship in orbit around the Crystal Planet begins to vibrate and loses its armor, its systems wear down, its hull warns three times and halves each time, and it melts unless it leaves first; its landing computer does not respond and a distress call is jammed. With the orb aboard, the orb glows and none of this happens
- The Black Egg: the terrain vehicle takes it from the ship's hold when it heads out (as it does the Flat Device); dropped on the surface it arms itself, and once the ship is back in orbit it counts down and destroys the planet (the Crystal Planet only at its control nexus)
- The Crystal Cone locates the Crystal Planet's control nexus from orbit
- The Tesseract halves the fuel the engines burn in hyperspace
- The Whining Orb translates the Spemin
- The Rod Device cuts the damage of alien lasers to a quarter
- The Crystal Pearl warps a critically damaged ship out of an encounter
- The ship steers the moment you press a direction and stops when you let go, as in the original, and its top speed follows its engines: class 5 engines fly at the original's pace in hyperspace and in star systems, as measured in GOG's version of the game, class 1 at a third of it, and the ship's acceleration (engines against mass) sets how quickly it gets there
- Alien ships come after you as in the original: every two star hours they jump toward your ship, half the way in hyperspace and three quarters in a star system, and in hyperspace they stay near their home ground; the Crystal Pearl draws them farther and faster
- The Shimmering Ball cloaks the ship in combat until it fires
- The Flat Device shields the terrain vehicle: lifeforms hit its crew half as often
- The Red Cylinder flashes a planet's message and artifact sites on the landing map when it opens, and marks its ancient ruins: a box on each fixed one, a light tint over the regions that hold the random ones
- The ancient ruins placed by STRINFO's editors, on the Mechans' ice planet and deep in Elowan space, hold their Endurium
- Continuum fluxes show on the screen as the original's twinkling dots: to a navigator trained beyond 150, 1 to 7 of them by skill, and 16 with the Ring Device aboard; a flux the ship has been through is remembered, and the starmap draws it as a line between its ends
- The price of Endurium rises with the notices of 20-02 and 15-05, and the Starport buys it back at its selling price
- Nebulae keep the shields down
- The Starport repairs the hull and damaged systems at the original's prices, and buys parts back according to their condition
- A confirmed distress call gets the ship towed home, for a fine that grows with the distance from Arth
- Alien ships have the armor and shields of their vessels in the game data, and move at the original's pace: the more of them, the slower each
- The starmap shows the fuel needed to fly to the cursor
- Planets can be recommended for colonization and named by the player; Interstel pays a bonus or fines the crew at the Starport, and each evaluation appears under its recommendation's number, date, system, orbit and name, one per page on the Operations evaluation screen
- The 35 planets with life have lifeforms of their own species, which Scan reads and the terrain vehicle's laser and stunner affect. Dangerous lifeforms come after the vehicle and attack the crew, while timid ones flee. A crew member whose vitality reaches 0 dies, a dead captain's command passes down the chain of command, and the game is lost when the whole crew has died. Lifeforms hunt, flee and eat one another, and some fly or float out of reach. The terrain vehicle's Cargo button records a lifeform's data, which Interstel pays for at the Starport, and Tools, Capture puts a stunned one into stasis with a capture beam, as a specimen the Trade Depot will buy. Look describes the nearest lifeform and what it is doing, along with the minerals, ruins and dropped cargo next to the vehicle
- Lava destroys a terrain vehicle that drives into it, and the Starport installs a new one for a fine
- Terrain vehicle fuel as the original's: every kilometer driven costs fuel by the ground under the vehicle, little at the shore, more the higher it climbs and about seven times as much on a liquid; a storm makes each kilometer dearer without slowing the vehicle
- Surface weather as the original's: spells of weather rolled from the planet's weather activity, with storms picked by its surface; a storm halves the terrain vehicle's efficiency and can injure the crew, heavy snow, fog and rain cost a quarter of it, and storms and heavy snow lose the bearing to the ship unless the navigator is highly skilled
- Random ruins as the original places them: none on the common planets, many on the planets of ruins, region by region as the terrain vehicle drives; ancient crystal ruins with Endurium beside them, and recent ruins, both sometimes holding a curio
- Destroyed ships leave debris that the ship can salvage by flying over it
- The science officer's skill decides how much of an analysis is revealed, and each planet's readings are analyzed once
- The bank pays the original's 12% annual interest on the balance when the crew boards at the Starport and with each new colony recommendation, shown as an Interest row in the ledger
- Hits that get past the shields fall on the armor, and only once the armor is gone on the hull, the ship's systems (engines, sensors, comms, shield generator, missile launcher, laser cannon) and the crew, as in the original. The ship is lost when the hull's integrity is gone, and damaged systems work in proportion to their health (damaged engines slow the ship only in a fight, as in the original) and can malfunction when they are used, with a chance of their damage, after which they stay inoperative until they are repaired, as in the original. Damage shows the original's DAMAGE display, with a bar for the hull and each system, and Repair lets the player choose what the engineer repairs (the hull and the systems, never the armor), using minerals from the hold
- The game can be won: destroying the Crystal Planet ends the flares, Interstel pays its 500,000 MU bonus at once, and a supplemental evaluation waits at the Starport
- Arth's sun flares on 01-01-4621 unless the game has been won: the Starport is destroyed, and any ship in Arth's system is incinerated
- 10 alien races, each with its own encounter behavior
- Aliens with only a ship or two left call for help, and more of their ships warp in
- Encounters that guard planets from orbit, fought over the planet's own surface; a Veloxi drone grants permission to orbit to a crew that answers its six numbers the Veloxi way, each within 15 seconds
- The Spemin, Thrynn and Veloxi home fleets warn ships away from their homeworlds, more sternly each time, and attack after the last warning
- The Spemin, Thrynn and Veloxi give up after losing half their ships: they stop fighting and surrender
- The crew reports when aliens raise or lower their shields and arm or disarm their weapons, and Analysis gives the class of their shields and whether they are up
- The Elowan give 15 cubic meters of Endurium to a crew whose need for energy is dire, and see through one that already has 20 or more
- As the comm link opens the Veloxi demand the Crystal Orb and the Ellipsoid if the ship carries them (not when they grovel), then a tribute of Endurium, and attack a crew that refuses; a refused Orb is war with the Veloxi for good, the Ellipsoid given or refused calms or angers the race, and a Crystal Orb they take goes back to where it was found
- The Thrynn buy artifacts at their own prices and plutonium for Endurium, and sell the Black Box for 30 Endurium
- The Elowan, Thrynn, Spemin and Veloxi have moods: the player's posture calms or angers them, they grovel before a stronger ship, grow tired of talking, and fight or flee once they are too agitated to talk; and each race remembers how its last encounters ended, and meets the ship again in that mood
- The Elowan and the Thrynn end the comm link and fight a crew that is friends with the other race, or has one of them aboard, and a crew member of the aliens' own race helps the communications officer understand them
- The Minstrels sing, the Nomad probes transmit their planetary data, and the Noah 9 derelict sends its distress beacon and recorded message
- Aliens asked about the Old Empire or the Ancients can refuse to say more about it for the rest of the encounter, one question in five, as in the original
- A complete combat system with lasers, missiles, shields and armor
- Ship destruction with debris
- A model with debris for every alien vessel in the game data
- Game over and restart
- A JSON save and load system

</details>

### 📊 Completion: 59 of 96 roadmap items

Every item of Phases 0 to 7 in [ROADMAP.md](ROADMAP.md) is done or decided, and so is every item in the screen-by-screen comparison with the original (`Research/Screen comparison.md`), the UI kit included. The headless test harness covers every one of them, and its playthrough plays a new game through to the win. A full playthrough of 1.0.0 by hand is still to come.

Nothing is open from the comparison with the original's code: its last two items, the speeds in an encounter and the aliens' distress calls, were measured in the original game.

See [CHANGELOG.md](CHANGELOG.md) for the detailed version history.

---

> *By @cptjamestiberiouskirk-pixel. Built on the Starflight Remake Project by Marvin Herbold, released into the public domain.*
