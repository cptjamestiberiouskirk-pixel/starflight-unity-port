# Code review, 2026-10-03

Review of `master` at 4648ce3. Items fixed since then are listed under "Fixed since the review"; everything else in this file is still open as of the Unity 6000.3.13f1 upgrade (PR #4).

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
| (PR #2) | `PD_General.m_lastCommIds` (`int[,]`) was never saved; three compile errors from 70445da |
| (PR #4) | `com.unity.ai.generators` (deprecated) and `com.unity.2d.enhancers` removed |

## Regressions from earlier "fix" commits

| Commit | What it broke | Finding |
|---|---|---|
| b9727c5 | Replaced the lookup by `m_encounterId` with indexing by array position. Its comment says the id "is not serialized", but it's a plain `public int` and is saved. | H1 |
| 70445da | Deleted the body of the garble capitalization `if`. The bare `if` now guards `garbledCommText.Add( garbledWord )`. | M1 |
| 70445da | Introduced the three compile errors fixed in PR #2. | (fixed) |

## High

| # | Where | Problem | Failure | Root-cause fix | Label | Checked |
|---|---|---|---|---|---|---|
| H5 | `CombatController.cs:398` | Alien damage is `vessel.m_armorClass - damage / 10` and is never stored. `PD_AlienShip` has no hit points. | Each hit either kills outright or does nothing. Armor-class-5 ships are immortal against class 1-2 lasers; a class-3 laser one-shots almost everything. | Add a saved remaining-armor field to `PD_AlienShip` and subtract per hit. Treat 0 on a living ship (old saves) as "not yet set". | CONFIRMED | yes |
| H6 | `CombatController.cs:337, 427`; `Encounter.cs:91-114, 222-230` | The combat target is a position in the ship list, but it's used as a model-slot index. `ResetAlienShipModels` packs only living ships into slots. | In a 6-ship, 3-at-a-time encounter, leave and come back after wave 1, then kill ship 3. Its debris activates an empty slot and `Encounter.Update` throws every frame, so the player can't leave until everything is dead. | Map ship index to slot in `ResetAlienShipModels`, and use that map in combat and debris spawning. | CONFIRMED (reviewer) | part |
| H7 | `SpaceflightController.cs:362-372` | `RestartGame()` only loads Intro. The in-memory player data still has 0 armor, the Encounter location and a hostile stance. | Game over, then title screen, puts you back in the same fight with 0 armor. | Reload the active slot from disk before going to Intro, and block saves while `m_gameOver` is set. | CONFIRMED (reviewer) | part |
| H8 | `PD_PlayerShip.cs:32` | `m_missilesRemaining` is never set above 0. The only write is the decrement. | A bought missile launcher always reports "Out of missiles!". | Stock missiles on purchase and refill at Starport. Needs a capacity value; `GD_MissileLauncher` has none, so this is a design call. | CONFIRMED | yes |

## Medium

| # | Where | Problem and failure | Fix | Label | Checked |
|---|---|---|---|---|---|
| M5 | `ShipConfigurationPanel.cs:817-832` | Selling armor resets the armor class but keeps `m_armorPoints`, so you can buy class 5 and sell it back for full armor at a 1,200 MU loss. | Reset the points when armor is removed. | CONFIRMED | no |
| M7 | `SensorsDisplay.cs:361-373` | `m_scanType` is overwritten with `Unknown` when the texture slot is empty. Per the reviewer, the scene leaves slots 0, 5-17, 21 and 23 empty. Scanning Elowan, Thrynn, Veloxi, Gazurtoid or Uhlek ships (447 of 597 vessel slots) shows "Unknown", and Analysis shows "Insufficient data". | Keep the real scan type and use a separate texture index for the fallback. | CONFIRMED (reviewer) | part |
| M8 | `JsonSaveSystem.cs:15`, `DataController.cs:148-163` | Saves overwrite the file in place, and on load any exception silently becomes a new game. A crash or full disk during a save (about 2.3 MB, written on every location change) wipes the slot. | Write to `.tmp` then replace, keep a `.bak`, and rename a corrupt file instead of resetting. | CONFIRMED (code) | no |
| M10 | `TerrainElement.cs:70-86`, `TVCargoButton.cs:83-91` | A deposit stays pickable for 1.5 s during the transporter effect, so double-pressing Cargo adds it twice. | Add a picked-up flag. | CONFIRMED (2 reviewers) | no |
| M11 | `StatusDisplay.cs:57-60, 138-141`; `DamageButton`, `RepairButton` | The gauges use hard-coded maxima of 1500/2500. A new ship shows "83% Hull Damage", and class-0 armor (250 points) shows "None installed" and can't be repaired. | One source of truth for max armor and shields. | CONFIRMED | no |
| M12 | `Encounter.cs:165-216, 504-555` | Only Mechans (and the editor F9 key) ever turn hostile, and attacking doesn't change stance. Uhlek, Enterprise and Noah have no update case at all, so 67 Uhlek encounters never act. | Set Hostile on attack and add an Uhlek update (design call). | CONFIRMED | no |
| M13 | `SpaceflightController.cs:393-456`; `Encounter.cs:287-304` | A fully destroyed encounter keeps moving toward the player and re-triggers as an empty encounter. | Skip encounters with no living ships. | CONFIRMED | no |
| M14 | `Encounter.cs:222-243` | Debris from a destroyed ship keeps chasing the player. | Skip movement for dead ships. | CONFIRMED | no |
| M15 | `CombatController.cs:142-145, 303-309` | The target index carries over between encounters, and firing doesn't check that the target is added and alive. It can kill an invisible ship, which then leads to H6. | Reset the target on encounter entry and validate it when firing. | CONFIRMED (2 reviewers) | no |
| M16 | `Planet.cs:93`, `PlanetGenerator.cs:517-616` | The old generator's runtime textures are never destroyed: roughly 40 MB or more leaked per star system until docking. | `PlanetGenerator.Release()` plus `Resources.UnloadAsset`. | CONFIRMED (2 reviewers) | no |
| M17 | `PlanetGenerator.cs:139-223` | No try/catch in the async task, and the version-mismatch path falls through. A bad or corrupt planet file soft-locks the game on the penetration popup, and the auto-save makes it permanent. Latent: all 811 shipped files are valid. | Catch, then abort and return; poll `IsCompleted`. | CONFIRMED path / INFERRED trigger | no |
| M18 | `PlanetGenerator.cs:146` | `Task.Wait()` runs on the main thread the next frame, so all planet processing blocks it and the progress bar freezes. | Poll `IsCompleted`. | CONFIRMED | no |
| M19 | `DropShieldsButton`, `RaiseShieldsButton` | Dropping and raising shields refills them to full instantly, mid-combat. | Keep shield points across drop/raise (design call). | CONFIRMED | no |
| M20 | `RepairButton.cs:41-59`, `TreatButton.cs:44-55` | Repair and Treat have no cost or cooldown. Treat also heals and lists every hired person, not just the crew on board. | Iterate the assigned roles; cost or cooldown is a design call. | CONFIRMED | no |
| M21 | `ShipConfigurationPanel.cs:1032-1055` | A cargo pod can be sold when the remaining capacity is below the cargo already loaded. The negative remaining volume then breaks the trade depot. | Refuse the sale; check `<= 0` at `TradeDepotPanel.cs:559`. | CONFIRMED | no |
| M22 | `Panel.cs:35-42` | No closing guard, so a double-click on Exit logs bank transactions twice, and Buy still works during the slide-out. | Add an `m_isClosing` flag. | CONFIRMED | no |
| M23 | `DescendButton.cs:60` | `Update()` returns false, so input stays live during the roughly 12 s landing animation (Abort, Descend again, Select Site). | Return true. | CONFIRMED input / INFERRED effects | no |
| M24 | `GD_Planet.cs:226` | Gravity drops the leading zero of the hundredths: 105 shows "1.5 G" instead of "1.05 G". 105 of 811 planets are affected. | `ToString( "D2" )`. | CONFIRMED | no |
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
- No guard against dying more than once, and missiles keep moving while paused: `CombatController.cs:439-516`.
- Pooled explosion scale leaks, and the missile is counted before the pool check: `CombatController.cs:323, 352`.
- A missile that times out never reports a miss: `MissileProjectile.cs:264-271`.
- Radar detection fails across the ±180° wrap: `Radar.cs:112`.
- A button activation can run on a different button set: `ButtonController.cs:129-139` (INFERRED).

**Terrain and planets**
- `AddToSpawnList` can index 32 when `Random.Range` returns its maximum: `TerrainGridPopulator.cs:197-200` (INFERRED).
- Disembarking reseeds the global `Random`: `TerrainGridPopulator.cs:52`.
- Scan labels survive leaving Disembarked: `TerrainObjectLabel.cs:66-70`.
- `PG_Craters` re-initializes on every Spaceflight start, about 6M `GetPixel` calls: `PG_Craters.cs:11-32`.
- Abort leaves a null elevation map that landing then dereferences: `Planet.cs:179-181` (INFERRED).

**Leaks and per-frame cost**
- `TransporterEffect` materials are never destroyed.
- `Viewport` fade mutates the shared material asset in the Editor.
- `DockingBayPanel.UpdateOpacity` reads `.materials` every frame.
- `StatusDisplay` and `TerrainVehicleDisplay` rebuild strings every frame.
- `ShipsLog` row loops have no iteration cap (HYPOTHESIZED hang).

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

The report's ✅ claims for "Save/Load complete", "alien comm history" and "Personnel delete" were contradicted by H2, H3, M8 and M9; H2, H3 and M9 are now fixed, M8 is still open.
