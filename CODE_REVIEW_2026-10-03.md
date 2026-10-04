# Code review, 2026-10-03

Review of `master` at 4648ce3. Items fixed since then are listed under "Fixed since the review"; everything else in this file is still open as of PR #32 (2026-10-04).

**Scope:** all of `Assets/Scripts` (225 files, about 32k lines), plus `Assets/Planet Generator/Editor`, `Assets/Tools/Editor` and `Assets/Shaders/Editor`.

**Method:**
- Six reviewers read the code in parallel, read-only, one per area.
- Each reviewer checked data ranges against `Starflight Game Data.json`, and scene wiring through script GUIDs where a finding depended on them.
- The "Checked" column says whether the finding was re-verified against the code in a second pass:
  - **yes:** the failure path holds.
  - **part:** the code holds; one downstream claim comes from the first pass only.
  - **no:** traced in the first pass only.

**Not covered:**
- Compiler warnings (the compile check passes; warnings were not collected).
- `3rd Party`.
- Scenes and prefabs, except where a finding needed them.

**Labels:**
- **CONFIRMED:** the failure path is traced in code.
- **INFERRED:** one link is unverified; the link is named.
- **HYPOTHESIZED:** plausible but unverified.

## Fixed since the review

Fixed in PR #3 (bf0b1a7) unless noted. None of these changed the save version.

| # | Was |
|---|---|
| H1 | Encounters were picked by array index although `Radar` sorts `m_encounterList` by distance; now `PlayerData.FindEncounter()` (regression from b9727c5) |
| H2 | `PD_ShipsLog.m_alienComms` (`List<Entry>[]`) was never saved and the AlienComms buttons threw after a load; now `EntryList[]` behind `GetAlienComms()` |
| H3 | Deleting assigned crew locked Crew Assignment and the Docking Bay; roles are unassigned on delete and repaired on load |
| H4 | Trade depot sold more than the hold contained; now refused, and `PD_ElementStorage.Remove` never goes negative |
| M1 | Garbled alien words were dropped (regression from 70445da) |
| M2 | Game data key `m_immuneToMissles` never loaded; renamed to `m_immuneToMissiles` |
| M3 | Trade depot amount parsing (`5.25` became 7.5, `-` threw); now `decimal.TryParse` with the invariant culture |
| M4 | Training compared only the medicine skill |
| M6 | Raising shields with no Endurium threw every game hour |
| M9 | Progress since the last location change was lost on quit; now saved in `OnApplicationQuit` (skipped for a destroyed ship) |
| H6 (PR #7) | Combat used the target's position in the ship list as its model slot, but `ResetAlienShipModels` packs only living ships into the slots. After re-entering an encounter, debris landed in an empty slot and `Encounter.Update` threw every frame; now mapped through `Encounter.GetAlienShipModel()` |
| M15 (PR #8) | The combat target carried over between encounters, and firing did not check that the target was alive and in the encounter, so a ship that was not on screen could be destroyed. The target is now cleared on entering an encounter and checked by `CombatController.GetValidTarget()` before firing |
| M13 (PR #10) | A fully destroyed encounter kept moving toward the player, stayed on the radar and started again as an empty encounter; `UpdateEncounters` now skips encounters with no living ships |
| M14 (PR #11) | Destroyed ships were still moved every frame, and ships with no debris template (all but vessels 1-4 and 20) kept their normal model; dead ships now stay put, a ship with no debris model is hidden, and the camera only frames living ships |
| M7 (PR #12) | A missing sensor picture replaced the scan type with `Unknown` (15 of 23 vessel types, and debris); only the picture falls back now |
| M8 (PR #13) | Saves overwrote the file in place and an unreadable save silently became a new game; saves now go through a `.tmp` file and keep a `.bak`, loading falls back to the backup, and an unreadable save is moved to `.corrupt` |
| M24 (PR #15) | Gravity text dropped the leading zero of the hundredths (105 planets) |
| M22 (PR #16) | Panels had no closing guard: a double Exit logged bank transactions twice and the buttons worked during the slide-out |
| M5 (PR #17) | Selling armor kept the armor points; it now returns the ship to the bare hull's 250 (`PD_PlayerShip.c_bareHullArmorPoints`) |
| M21 (PR #18) | A cargo pod could be sold while the cargo needed its space, leaving a negative free volume |
| H5 (PR #21) | Alien damage was `vessel.m_armorClass - damage / 10`, worked out from scratch on every hit and never stored, so a hit either killed outright or did nothing. Alien ships now have saved armor and shield points, 100 per class, and shields absorb first |
| M19 (PR #22) | Lowering and raising the shields refilled them. The charge is kept now, lowered shields do not protect, and the charge comes back at 1% of the maximum every 5 seconds (in full at Starport) |
| H7 (PR #23) | `RestartGame()` only loaded Intro, so the title screen led back into the lost fight with 0 armor. It now reloads the active slot from its last save, and `DataController.SavePlayerData` never writes a destroyed ship. A save that an older build wrote with a destroyed ship loads with 1 armor point |
| M12 (PR #24) | Only Mechans ever turned hostile, and the Uhlek had no update at all. Firing on an alien makes the encounter hostile until the player leaves it (`PD_Encounter.m_attackedByPlayer`), the Uhlek attack on sight and never talk, and a race with no update of its own shoots back |
| M20 (PR #25) | Repair and Treat were instant and free, and Treat healed everyone who was hired. Both take time now (0.02 points a second per point of skill, never less than 0.5), and Treat handles one patient at a time from the crew on board |
| H8 (PR #26) | `m_missilesRemaining` was never stocked, so a missile launcher could not fire. Missiles are no longer counted: a launch uses 0.02 m³ of Endurium and a laser shot 0.01, and neither fires without Endurium |
| M10 (PR #28) | A mineral deposit stayed pickable during the 1.5 s of its transporter effect, so pressing Cargo twice put it in the hold twice; `TerrainElement` now remembers that it has been picked up |
| M23 (PR #29) | `DescendButton.Update` returned false, so the console stayed live for the whole landing (35 s, not the 12 s first estimated) and Abort brought the bridge buttons back in the middle of the descent; it returns true now, as the launch does |
| (PR #30) | The Descend button printed "Autopilot engaged. Descending..." and "Safe landing, captain." on a 12 s timer, on top of the landing animation's own messages, so the first appeared twice and the second 23 s before the ship was down; the button now only prints "Topography net locked on." |
| Low (PR #32) | Missiles in flight outlived the encounter they were fired in and still did their damage on arrival: an alien missile hit the player in the next encounter, and a player missile damaged the ship with the same index there. The encounter now takes every missile out of the air when it begins and when it ends (`CombatController.ClearMissiles`) |
| Low (PR #14) | `Viewport` fade wrote into the shared `Black.mat` asset on every play session in the Editor |
| (PR #2) | `PD_General.m_lastCommIds` (`int[,]`) was never saved; three compile errors from 70445da |
| (PR #4) | `com.unity.ai.generators` (deprecated) and `com.unity.2d.enhancers` removed |
| (PR #5) | `com.unity.ai.assistant` and `com.unity.ai.inference` removed, with the App UI leftovers in `ProjectSettings` |
| (PR #9, #19) | Unity CLI editor bridge `com.unity.pipeline` added; `com.coplaydev.unity-mcp` removed |

### How the fixes were checked

On 2026-10-03 the fixes were run in a headless play-mode probe: the real Spaceflight or Starport scene, new-game data, and an in-memory save system so no save file was touched. The probe is in the repository under `DevTools/HeadlessProbe/` (it was added after these runs).

- **PR #3 (batch 1):** H1, H2, H3, H4, M2, M3, M4, M6, M9 and the `m_lastCommIds` fix from PR #2 passed 17 checks. M1 was checked by reading the code only.
- **PR #7 onward:** H6, M15, M13, M14, M7, M22, M24, M5, M21 and the `Viewport` fix were each run before and after the change; the bug reproduced on the old code and was gone with the fix. M8 was run after the change only, because the old class could only write to the real save folder.
- **PR #21 to #26 (batch 2):** H5, M19, H7, M12, M20 and H8 were each run before and after the change, through the real buttons or the real fire and damage methods. The design of each was decided by the project owner on 2026-10-03. Where the original design notes give no number (hit points per class, the repair and treatment rates, the fuel per shot), the number is a named constant in the code.
- **PR #28 to #30 (2026-10-04):** M10, M23 and the landing messages were each run before and after the change through the real flow: into orbit, down to the surface, the Disembark and Cargo buttons, and the button controller with the stick or the fire button held for one frame.
- **PR #32 (2026-10-04):** the missile fix was run before and after in both directions (an alien missile and a player missile in the air when the player leaves), together with a check that missiles still hit inside their own encounter.
- **Not done:** nothing was played by hand in the GUI Editor, and the probe has no graphics.

## Regressions from earlier "fix" commits

| Commit | What it broke | Finding |
|---|---|---|
| b9727c5 | Replaced the lookup by `m_encounterId` with indexing by array position. Its comment says the id "is not serialized", but it's a plain `public int` and is saved. | H1 |
| 70445da | Deleted the body of the garble capitalization `if`. The bare `if` now guards `garbledCommText.Add( garbledWord )`. | M1 |
| 70445da | Introduced the three compile errors fixed in PR #2. | (fixed) |

## High

None open. H1 to H8 are all under "Fixed since the review".

## Medium

| # | Where | Problem and failure | Fix | Label | Checked |
|---|---|---|---|---|---|
| M11 | `StatusDisplay.cs:57-60, 138-141`; `DamageButton`, `RepairButton` | The gauges use hard-coded maxima of 1500/2500. A new ship shows "83% Hull Damage", and class-0 armor (250 points) shows "None installed" and can't be repaired. | One source of truth for max armor and shields. | CONFIRMED | no |
| M16 | `Planet.cs:93`, `PlanetGenerator.cs:517-616` | The old generator's runtime textures are never destroyed: roughly 40 MB or more leaked per star system until docking. | `PlanetGenerator.Release()` plus `Resources.UnloadAsset`. | CONFIRMED (2 reviewers) | no |
| M17 | `PlanetGenerator.cs:139-223` | No try/catch in the async task, and the version-mismatch path falls through. A bad or corrupt planet file soft-locks the game on the penetration popup, and the auto-save makes it permanent. Latent: all 811 shipped files are valid. | Catch, then abort and return; poll `IsCompleted`. | CONFIRMED path / INFERRED trigger | no |
| M18 | `PlanetGenerator.cs:146` | `Task.Wait()` runs on the main thread the next frame, so all planet processing blocks it and the progress bar freezes. | Poll `IsCompleted`. | CONFIRMED | no |
| M25 | `PG_AlbedoMap.cs:120, 128-146` | The x blur's `x0 == x1`, so it's asymmetric. The y blur allocates 32 MB, then its result is thrown away. | Fix x0 and delete the y blur (slight visual change). | CONFIRMED | no |
| M26 | `PG_EditorWindow.cs:766-768` (editor) | The south-pole padding reads the north row's heights. This is baked into every generated `.bytes` file. | Use row `c_height - 1`. Takes effect only after regenerating the files. | CONFIRMED | no |
| M27 | `PG_EditorWindow.cs:228-297`, `PG_HydraulicErosion.cs:180` (editor) | The progress bar isn't cleared on exceptions, so the editor looks hung. Erosion has an unbounded loop when evaporation is 0, and a write race between threads. Zero-valued sliders produce NaN. | try/finally, iteration caps, validate settings. | CONFIRMED / INFERRED loop | no |

## Low

Grouped. All are CONFIRMED unless marked otherwise.

**Economy and UI text**
- Bank ledger shows `-1400+`: `TradeDepotPanel.cs:108-114`, `ShipConfigurationPanel.cs:98-104`.
- Buy maximum overflows `int` above about 214M MU: `TradeDepotPanel.cs:1208` (INFERRED reachable).
- Mass ratio uses integer division: `AnalysisButton.cs:69, 123`.
- Cargo unit mistakes (tenths of m³ printed as m³): `TVCargoButton.cs:199, 240, 257`, `ShipCargoButton.cs:34`.
- The partial pickup destroys the whole deposit: `TVCargoButton.cs:176, 195`.

**Save and data**
- Missing `Disembarked` case in `SaveGamePanel.cs:322-350` and `DataController.GetCurrentSceneName` (`DataController.cs:228-238`).
- Encounters 144-146 match no star and default to star 0: `PD_Encounter.cs:67-80`.
- `PlayerData.Reset` never calls `m_terrainVehicle.Reset()` (hidden by later refuels).
- Stardate string depends on the culture: `PD_General.cs:222-223` (INFERRED).

**Encounters and combat**
- Incoming messages show a raw `*` placeholder: `Encounter.cs:1566`.
- Aliens keep firing during a comm link: `Encounter.cs:1123`.
- JustLaunched is treated as hyperspace: `SpaceflightController.cs:417-425` (INFERRED trigger).
- Wrong approach direction in star systems: `Encounter.cs:640`.
- No `break` after switching to an encounter: `SpaceflightController.cs:443-456` (INFERRED).
- In-orbit encounters can never start: `SpaceflightController.cs:401-405`.
- No guard against dying more than once, and missiles keep moving while paused (`MissileProjectile.Update` has no pause check, so a missile can hit while the save panel is open): `CombatController.ApplyDamageToPlayer`.
- Pooled explosion scale leaks, and a missile launch uses its fuel before the pool check, so a launch with no free missile object costs fuel and does nothing: `CombatController.FirePlayerMissile`.
- After the player has fired on Mechans, five correct answers still set `m_mechan9Unlocked`, although the stance is put back to Hostile: `Encounter.UpdateMechanEncounter` (found 2026-10-03, INFERRED, not run).
- Nothing in the game lowers crew vitality. The only writes to `m_vitality` are crew creation and Treat, so Examine always reports 100% and Treat never has a patient (found 2026-10-03 by searching the scripts).
- A missile that times out never reports a miss: `MissileProjectile.cs:264-271`.
- Radar detection fails across the ±180° wrap: `Radar.cs:112`.
- A button activation can run on a different button set: `ButtonController.cs:129-139` (INFERRED).

**Terrain and planets**
- `AddToSpawnList` can index 32 when `Random.Range` returns its maximum: `TerrainGridPopulator.cs:197-200` (INFERRED).
- Disembarking reseeds the global `Random`: `TerrainGridPopulator.cs:52`.
- Scan labels survive leaving Disembarked: `TerrainObjectLabel.cs:66-70`.
- A Scan during the 1.5 s of a deposit's transporter effect still counts and labels the deposit that was just picked up: `ScanButton.ScanNearbyObjects` (found 2026-10-04 by reading).
- The Escape key opens the save panel during the landing and launch animations, and the animation and its events carry on behind the panel: `SpaceflightController.Update` (found 2026-10-04 by reading, not run).
- `PG_Craters` re-initializes on every Spaceflight start, about 6M `GetPixel` calls: `PG_Craters.cs:11-32`.
- Abort leaves a null elevation map that landing then dereferences: `Planet.cs:179-181` (INFERRED).

**Leaks and per-frame cost**
- `TransporterEffect` materials are never destroyed.
- `DockingBayPanel.UpdateOpacity` reads `.materials` every frame.
- `StatusDisplay` and `TerrainVehicleDisplay` rebuild strings every frame.
- `ShipsLog` row loops have no iteration cap (HYPOTHESIZED hang).

**Assets**
- 80 asset references in scenes, prefabs and materials point at assets that are not in the repository (found by cross-referencing GUIDs, 2026-10-03). Examples: the Debris mask in `SensorsDisplay.m_maskTextures` (index 24) in `Spaceflight.unity`, textures on several ship and planet materials, and objects in `Test.unity` and `Ecosystem.unity`. Most are harmless empty slots; none has been checked one by one.

**Visual**
- `Float.cs` wraps at 360 instead of 2π, so the object pops every few minutes.
- Explosion particles are cut off at 1.5 s.
- `TerrainMapDisplay` crosshair speed is unbounded and frame-rate dependent.

**Project-rule violations**
- `??` on a UnityEngine.Object: `TargetButton.cs:46`; `SpaceflightController.cs:210` (editor-only).
- `?.` on a UnityEngine.Object: `TerrainElement.cs:42`.
- `tag ==` instead of `CompareTag`, and `GetContact(0)` unguarded: `TerrainVehicleCollider.cs:10-12`.

**Test.unity only**
- `PlanetManager` colours are overwritten, its mesh leaks, the 16-bit index limit is hit above resolution 105, and `transform.parent` is used without a null check.
- `ProceduralAdapter` dereferences a null controller.

**Editor tools**
- Texture2D leaks in `PG_Tools` and `SaveTextureToFile`.
- Cancelling `SaveFilePanel` breaks the window.
- `isReadable` isn't checked before `EncodeToPNG`.
- `MassCreateGameObjects` can divide by 0.
- `SFShaderGUI` throws a null reference with non-2D textures and ignores multi-select.
- Seam in the perlin noise texture.
- `PG_ContractResolver` logic bug (unused class).

**Dead or latent**
- `InfiniteStarfield.cs:138` y-wrap.
- `Messages.cs:164` passes seconds to `SmoothStep`.
- `TerrainVehicle.cs:344` `if ( false && ... )`.
- `Encounter.LeaveEncounterAfterVictory` has no callers.
- `TerrainVehicleCargoDisplay`, `TerrainRuins` and `TerrainArtifact` aren't wired into any scene.

## Status of `PROJECT_ANALYSIS_REPORT.md` items

| Report item | Status now |
|---|---|
| TradeDepotPanel `while(true)` (critical) | Capped by 70445da. It could only hang with a viewport of 3 rows or fewer; the cap hides that case rather than fixing it. |
| TradeDepotPanel `amountParts[1]` with "5." | Was never a real issue. The real parsing bugs are M3. |
| TradeDepotPanel `renderedHeight / m_rowCount` | Was never a real issue: float divided by int, and the row count is always at least 3. |
| AnalysisButton bounds (2 items) | Fixed. |
| Encounter `garbledWord[0]` | Was never reachable. The "fix" caused M1. |
| Encounter "~1693" | Actually `commWord[ Length - 1 ]`. Still unguarded, but unreachable with the shipped data. |
| SystemDisplay orbit position | Fixed in `Update`. `ChangeSystem:154` is unguarded, but every planet's orbit is 1-8. |
| StarSystem orbit position | Fixed; was never reachable. |
| Planetside `GetComponent<Collider>` | Never existed in `Planetside.cs` (checked with `git log -S`). |
| BridgeController `UIDocument` | Never a real issue: the class has `RequireComponent`, and it's only used in a test scene. |
| Encounter `if (false && ...)` | Actually in `TerrainVehicle.cs:344`, and still present. |
| "m_alienComms NRE: FIXED" | Only the write path was guarded at the time; fully fixed by H2 in PR #3. |
| `PD_Bank` "make the player rich" hack | Still present (`PD_Bank.cs:34`, 1,000,000 MU). |

The report's ✅ claims for "Save/Load complete", "alien comm history" and "Personnel delete" were contradicted by H2, H3, M8 and M9; all four are fixed now.
