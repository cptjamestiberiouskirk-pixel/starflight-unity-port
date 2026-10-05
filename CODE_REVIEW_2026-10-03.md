# Code review, 2026-10-03

Review of `master` at 4648ce3. Items fixed since then are listed under "Fixed since the review"; everything else in this file is still open as of PR #73 (2026-10-05).

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
| M11 (PR #33) | The status display measured the armor against 1500 points and the shields against 2500, so an undamaged new ship read "83% Hull Damage". The Damage report and the Repair button took the maximum from the armor plating, which is 0 with none installed, so the bare hull's 250 points could not be repaired. `PD_PlayerShip.GetMaximumArmorPoints()` and `GetMaximumShieldPoints()` are the one source now, the engineer repairs a bare hull, and the hull breach warning comes below a quarter of the maximum instead of below a fixed 250 points |
| M18 (PR #36) | `PlanetGenerator.Process` called `Task.Wait()` on the main thread, so the game stood still while each planet was processed and the progress bar moved once per planet; it looks at `IsCompleted` each frame now |
| M17 (PR #37) | A planet file that could not be read made `Process` throw on every frame, which left the game paused for good in the middle of generating the star system, and a file cut off inside its difference buffer was accepted with no error. A failed task now aborts that planet with an error in the log, and `ReadPlanetData` checks the version, the map size, that all of the data is there and nothing more, and the checksum. The review had the trigger as INFERRED; it was reproduced with bad files made in memory |
| M16 (PR #39) | The textures of every planet of every star system visited stayed in memory, with the planet files, and the maps of the last system outlived even the scene change. `PlanetGenerator.Release()` destroys them: `Planet` calls it once the maps of the next planet are on the material, at once for an orbit that is empty in the new system, and in `OnDestroy`. The planet file is unloaded as soon as its bytes are copied |
| M25 (PR #41) | The blur of a planet's colour map took a quarter from the right neighbour and nothing from the left (`x0` was `x1`), and a second blur from row to row built a 32 MB map that was thrown away. The x blur is symmetric now and the y blur is gone. This changes the look of planets slightly: on planet 90, 7% of the pixels, by 1.4 of 255 on average. Not looked at in the GUI Editor |
| M26 (PR #42) | The planet generator tool padded the south pole with heights taken from the top row. The tool reads the bottom row now. **The planet files were not generated again**, so the 811 files in the project still carry the old padding (see "Assets" in the Low list) |
| Low (PR #40) | A planet whose maps could not be generated has no elevation data, and Descend ran into that with a `NullReferenceException` after "Computing descent profile..." (INFERRED in the review, reproduced). Land and Disembark now refuse such a planet with a message (`Planet.HasMaps()`), and the two `UpdateTerrainGridNow` methods do nothing for it |
| M27 (PR #44) | The planet generator tool left its progress bar up when it threw, a rain drop of the hydraulic erosion had no limit on its steps (34,346 with the evaporation at 0, and still going after 2,000,000 with the friction at 0 as well; the review had the loop as INFERRED), and settings of 0 gave NaN. `MakeSomeMagic` checks the settings first (`GetSettingsProblem`) and clears the progress bar in a `finally`, a drop takes a limited number of steps, and a blur radius of 0 means no blur. The write race between the erosion threads is still there (see the Low list) |
| Low (PR #45) | Economy and UI text. The bank ledger wrote income as "-1400+", the Trade Depot's buy maximum overflowed above 214 million MU, the sensor analysis dropped the decimals of a vessel's size, cargo volumes were printed ten times too large (tenths of a cubic meter shown as cubic meters), and a deposit that did not fit into the hold vanished whole |
| Low (PR #46) | Save and data. A game saved in the terrain vehicle loaded into Starport (no `Disembarked` case in `GetCurrentSceneName` and in the save panel), a new game never reset its terrain vehicle, and the stardate was made with the calendar of the computer's region (INFERRED in the review, reproduced: the year read 5163 with the Thai calendar, and the Saudi Arabian one threw on every frame) |
| Low (PR #47) | Project-rule violations: `??` and `?.` on Unity objects (`TargetButton`, the F10 debug key, `TerrainElement`, `TerrainArtifact`), and `tag ==` and an unguarded `GetContact( 0 )` in `TerrainVehicleCollider`. Checked by reading: the two versions only differ for an object that has been destroyed or for a collision with no contact point, and the game reaches neither |
| Low (PR #48) | Combat. The ship could be destroyed more than once (a hit during the 1.5 s of its explosion; the aliens' own fire did it in the test), missiles flew on while the game was paused, a pooled explosion kept the half size a missile hit had given it, and a launch with all eight missile objects in the air cost fuel and made the aliens hostile. `CombatController.PlayerIsDestroyed()` is the one state now: nothing hits a destroyed ship, the aliens stop firing at it and its weapons do not fire |
| Low (PR #49) | How encounters start. A ship that had just launched was taken to be in hyperspace, so hyperspace encounters were measured against its position inside the Arth system and came after it (INFERRED in the review, reproduced: nine of them at the start of a new game, the nearest about 50 s away). Aliens in a star system appeared on the side given by the ship's last hyperspace position. Two encounters could begin in one frame. The radar never saw an encounter dead astern |
| Low (PR #50) | Comms and console. Alien messages showed the `*` of the captain's name (the incoming branch of `AddComm` started again from the unfilled text). Five right answers unlocked Mechan 9 even in an encounter in which the player had fired on the Mechans (INFERRED, reproduced). A press of the fire button activated whatever button was first when the buttons changed during the 0.35 s before it was carried out (INFERRED, reproduced: a press on Statement answered Yes to a question that came after it). The unguarded `commWord[ Length - 1 ]` got its guard here, because filling in a name that has two spaces in a row would have made it reachable |
| Low (PR #51) | Terrain. Placing the objects of a planet left the global `Random` seeded with the planet's seed, a map position of exactly the map width indexed spawn list 32 of 32 (INFERRED: the function throws for that position; whether any planet draws it is not known), a scan counted a deposit during its transporter effect, scan labels stayed in the air after leaving the terrain vehicle, and the crater maps were read again on every start of the scene (0.45 s) |
| Low (PR #52) | The Escape key opened the save panel during launches and landings, where the camera animation and its events carried on behind it (found by reading, reproduced), and during the ship's explosion, where closing the panel set the lost game running again (found while working on PR #48) |
| Low (PR #53) | Visual. `Float` wrapped its timer at 360 where the sine needs 2 pi, explosions were switched off at 1.5 s with particles that live 2 s, and the landing site crosshair moved by its speed once per frame with no top speed. **Not seen**: the probe has no graphics. The crosshair now has the top speed that was set in the scene and never read (60 degrees a second) |
| Low (PR #54) | Per-frame cost. `StatusDisplay` and `TerrainVehicleDisplay` built their text every frame (896 and 496 bytes of garbage a frame), and `DockingBayPanel.UpdateOpacity` read `Renderer.materials` nine times a frame (566 bytes) |
| Low (PR #55) | Leaks. The spaceflight scene stayed in memory after it was left. It had three roots, each shown to be enough by itself: the static `SpaceflightController.m_instance`, the save panel's callback object, and the static planet generator of `TerrainGridPopulator`. `TransporterEffect` never destroyed the copies of the materials it asked for. **The `ShipsLog` hang (HYPOTHESIZED) did not reproduce** and its loops are unchanged (see the Low list); a log that is opened after an empty log now starts at its first entry |
| Low (PR #56) | Latent and Test.unity. The message box slide passed seconds to `SmoothStep` (right only for the 1 s set in the scene), the starfield's y wrap tested the x coordinate (unused branch, by reading), `PlanetManager` kept 16 bit indices above 65535 vertices (the triangles then point at the wrong vertices, with no error), used `transform.parent` unchecked and never destroyed its mesh, and `ProceduralAdapter` used a null controller |
| Low (PR #57) | Editor tools. `PG_Tools` left behind a texture for every picture it saved, `SFShaderGUI` threw for a normal map that is not a 2D texture and only updated the first of several selected materials, a cancelled file dialog left `HighResolutionScreenshot` and `BuildAtmosphreNormalMap` with an empty file name that the next click could not use, `SaveTextureToFile` did not check `isReadable` and left its copy behind, and `MassCreateGameObjects` divided by zero. The first two were run by reflection; the rest is by reading |
| Low (PR #66) | Save and data, rulings 2, 3 and 17 of 2026-10-05. A save from before PR #17 could hold more armor points than its armor allows; they are cut back to the maximum when the save is loaded (`PD_PlayerShip.ValidateArmorPoints`, next to the other load repairs), and a save written with a destroyed ship still loads with 1 point. The dates of the bank, of the Operations notices and of their ship's log entries were printed in the date format of the computer's region; they are in-game stardates and are printed day-month-year now, as the original's screens show them (`PD_General.GetDisplayStardate`), and notice entries an older build dated are dated again on load. The review had named two places; `PD_ShipsLog.AddStarportNotice` was a third, and it saved its result. A build starts with the original 12,000 MU, the Editor keeps 1,000,000 (`PD_Bank.GetStartingBalance`) |
| Low (PR #67) | Combat, ruling 4, default A and ruling 15. Hostile aliens kept firing while the comm link was up; they hold their fire now (`Encounter.UpdateAlienCombat`, the one place all alien fire comes through), as every alien fire rule of the design notes requires. A missile that ran out of time was switched off without a word; it reports a miss to its callback now (an explosion where it was, no damage). `Encounter.LeaveEncounterAfterVictory`, which nothing called, is deleted |
| Low (PR #68) | Terrain, ruling 8. A mineral deposit holds 1 to 5 cubic meters instead of 0.1 to 0.5. The multiplication is in `TerrainElement.Initialize`, not in `TVCargoButton.PickupElement` as the review had suggested: `PickupElement` writes what is left of a deposit that did not fit back in tenths, and a conversion there would convert that remainder twice. Still one random number per deposit, so every planet's objects are where they were. `TerrainVehicleCargoDisplay` (not in any scene) printed tenths as cubic meters, the one cargo text PR #45 had not reached |
| Low (PR #69) | Planets, rulings 9 and 10. A planet whose maps could not be generated kept the maps of the planet that was in its orbit before; it gets four plain maps now (`PlanetGenerator.CreatePlaceholderTextures`), `Planet.CouldNotBeMapped()` says so, and the player is told within orbital range and in orbit. A game saved in the terrain vehicle and loaded after the planet's file was damaged threw on every frame (found by reading in the review, reproduced: 61 `NullReferenceException`s in the first second) and had no way out; it goes back into orbit on load, with the vehicle's cargo, and says why. **Not seen**: the probe has no graphics |
| Low (PR #70) | In-orbit encounters, ruling 5. The twelve encounters with location 2 could never begin. One begins when the ship goes into orbit around its planet (`SpaceflightController.BeginInOrbitEncounter`, one look per entry into orbit, after a launch only when the launch animation has ended), its ships come from the side of the planet, and it ends at the level of the star system, where the ship was (`Encounter.LeaveEncounter`, the one way out of every encounter now). The planet is not drawn in the encounter yet. Four of the twelve still cannot begin, and one begins at the wrong planet: see the Low list |
| Low (PR #71) | Tools and assets, rulings 13 and 12. The erosion pass of the planet generator tool rains on its eight parts of the map one after the other, on one thread, so the same map and settings give the same result every time (six runs gave six different results before); it takes five times as long on the map that was measured. No planet file was generated again. `LiberationSans SDF - Fallback.asset` is committed in the format Unity 6000.3 writes, and a new `.gitattributes` checks that one file out with LF, as Unity writes it: committing the file alone, which the review had named as the fix, still left it listed as modified (with an empty diff) on a checkout with CRLF |
| Low (PR #72) | Default B and the specified entries. The two scroll loops of `ShipsLog.UpdateDisplay` have a limit. `PD_General.UpdateGameTime` makes the stardate texts when the day or the hour changes (it made 112 bytes of garbage a frame). The Statement button transmits nothing in a posture the game data has no statement for (it transmitted "ERROR"). `SystemDisplay.ChangeSystem` checks its orbit index. `PG_ContractResolver.IncludeProperty` looked in the wrong list. The `if ( false && ... )` block of `TerrainVehicle.OnDrawGizmos` and `PlanetGenerator.m_legendTexture` are removed |
| (PR #73) | Probe only. The `h7` scenario gave a bare hull 400 armor points and expected them back after a reload; since PR #66 a loaded save is cut back to the 250 its hull can have, so three of its checks failed. Its ship gets class 1 armor plating now. No game code |
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
- **PR #33 to #42 (2026-10-04):** M11, M18, M17, M16, the landing on a planet without maps, M25 and M26 were each run before and after the change with a scenario of their own (`m11`, `m18`, `m17`, `m16`, `nomaps`, `m25`, `m26`), which are in the probe in the repository.
  - M25 was run on a black map with single white pixels, which shows what each neighbour takes. The change to a real planet was measured by comparing the albedo map of planet 90 pixel by pixel between the two runs.
  - M26 is editor code. Its scenario runs the tool's own methods on the source images of all 811 planets by reflection and compares the result with the planet files: the unchanged tool rebuilds every file's prepared height map exactly, the fixed one differs in the south pole rows of 273 planets and nowhere else.
  - M18 was measured from a coroutine while a planet was being processed: 5 frames for 5 planets before, more than 1000 after.
  - M17 was run with eight kinds of bad file made in memory from a real one, and with one of them given to a planet of the real star system. All 811 planet files of the project pass the stricter reader.
  - M16 was flown through four changes of star system: the textures made at runtime went from 22 MB to 69 MB before and stayed at 22 MB after. The probe has no graphics device, so graphics memory is not in those numbers.
  - The whole set of 24 scenarios was run on the final tree.
  - Since M18 the probe waits for the first star system's planets before it starts a scenario (PR #38). Before that, scenarios started while the game was still paused for the generation, and two of them reported smaller numbers than they should have.
- **PR #44 to #57 (2026-10-04):** M27 and the Low list. Every PR has a scenario of its own in the probe, run on the old code and on the new code, except PR #47 (the two versions only differ for an object that has been destroyed or for a collision with no contact point, so it was checked by reading). Of PR #57 only `PG_Tools` and `SFShaderGUI` could be run; the other editor tools sit behind dialogs and `OnGUI`.
  - Entries the review had as INFERRED, HYPOTHESIZED or as found by reading were reproduced first. They reproduced, with three things to know. The `ShipsLog` hang (HYPOTHESIZED) did not, and its loops are left alone. The spawn list function fails for a map position of exactly the map width, but no planet is known to produce that position. The buy maximum overflows as arithmetic, but that a player gets to 214 million MU is still INFERRED.
  - Some defects needed a state that was set up by hand, not reached by playing: two encounters arriving in one frame, eight missiles in the air, an alien question arriving inside the 0.35 s of a button press, the Mechan answers. The PR bodies say which.
  - Two checks would have passed without proving anything, and were caught because the scenario was run on the old code first. An allocation meter (`GC.GetAllocatedBytesForCurrentThread`) returns 0 for everything in this Unity version; the scenarios now measure a known allocation before they trust a zero. A planet mesh with 72600 vertices was accepted without an error; reading the triangle indices back showed them cut off at 65535.
  - One change was taken back after a control check: reading the crater textures with `GetPixels` is faster, but 1633 of 180810 sampled values differed from what `GetPixel` gives, in the last bit.
  - A fix exposed a helper that relied on the defect: the `h8` scenario fired 50 missiles in one frame, 42 of them with no free missile object.
  - The scene leak (PR #55) was checked with weak references, and with each of its three fixes taken out in turn.
  - The whole set of 43 scenarios was run on the tree of PR #57, the last code change: 189 checks in 33 scenarios pass and none fails, and the 10 scenarios that have no checks give the same result lines as before (apart from the fields that depend on the random ship types).
  - **Not seen:** PR #53 changes what the player sees (floating objects, explosions, the landing site crosshair), and PR #49 the side on which alien ships appear in a star system. The probe has no graphics, so these are checked by numbers only.
- **PR #66 to #73 (2026-10-05):** the rulings of 2026-10-05. Every PR has a scenario of its own in the probe, run on the old code and on the new code; each PR body has both result lines and every check line.
  - Checks that pass, old code and new code: `starport-savedata` 4 of 11 and 11 of 11, `commlink` 3 of 8 and 8 of 8, `deposits` 1 of 5 and 5 of 5, `unmapped` 3 of 11 and 11 of 11, `orbit` 2 of 8 and 8 of 8, `erosion` 0 of 1 and 1 of 1, `smallfixes` 3 of 9 with a hang in its last step and 9 of 9. The checks that pass on the old code are the controls.
  - Two entries of the review were reproduced before they were fixed. The game loaded in the terrain vehicle on a damaged planet ("found by reading, not run") threw 61 exceptions in its first second. The race in the erosion pass gave six different results in six runs on the same map.
  - One claim of the review did not hold. Committing the fallback font asset in the current format does not keep the working tree clean by itself: on a checkout with CRLF line endings the file was still listed as modified after a run, with an empty diff. The `.gitattributes` line that checks it out with LF is what makes it clean (three runs: old file, new file, new file with the attribute).
  - One suggestion of the review was not followed as written. The deposit size is multiplied in `TerrainElement.Initialize`, not in `TVCargoButton.PickupElement`: the remainder of a deposit that did not fit is written back in tenths, and a conversion at pickup would convert it twice.
  - States that were set by hand, because play does not reach them: the comm flag on an Uhlek encounter, a neutral posture for the Statement button, a planet in orbit 9, the ship's log with rows of no height and a list that is scrolled down (the old loops never came back from that), missiles run out of time by setting their lifetime, and the load of a game in the terrain vehicle (done inside a running scene). The launch from a planet with an encounter in its orbit was the real one, with its animation of 30 seconds.
  - The probe always runs in the Editor, so the starting balance of a build was asked from `PD_Bank.GetStartingBalance( false )`. No build was made.
  - The font asset was compared field by field with a script that ignores the order of the fields: all 157 fields of the font asset object keep their values; the one value that is more than format is the size of its empty atlas texture (see the Low list).
  - No planet file was touched and the game data was not edited. The two data findings of this round (the location of encounters 144 to 146, five orbit positions) were worked out from `Research/Data/Starflight 1 Data.xls` with a reader written for the purpose; they are proposals in the Low list.
  - The whole set of 50 scenarios was run on the tree of PR #72, the last code change: 239 checks in 40 scenarios pass and 3 fail. All three are in `h7`, an older scenario, and the fault was in the scenario: it gave a bare hull 400 armor points and expected them back after it reloaded its save, and the load repair of PR #66 cuts such a ship back to its 250. PR #73 gives the scenario's ship armor plating that can hold the 400, and it passes 6 of 6. `h7` had not been run with PR #66, although that PR changes the loading it goes through; the scenarios run with each PR were picked by what the change was thought to touch, and that pick was wrong once. The 10 scenarios that have no checks give the same result lines as in their last run (apart from the fields that depend on the random ship types).
  - **Not seen:** PR #69 and #70 change what the player sees (a planet without maps, an encounter that begins in orbit). The probe has no graphics.
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

None open. M1 to M27 are all under "Fixed since the review".

## Low

What is still open after PR #73 (2026-10-05). Everything else that was on this list is under "Fixed since the review". On 2026-10-05 the project owner ruled on the seventeen design questions this list had; the rulings that changed code are PR #66 to #72, and the rulings that change nothing are recorded here with their date. All entries are CONFIRMED unless marked otherwise.

**Game data: two proposals that wait for the owner's go-ahead**

Neither was applied: original game data is not changed on a finding, and `.claude/settings.json` asks before any edit of `Starflight Game Data.json`. Both were researched on 2026-10-05 against `Research/Data/Starflight 1 Data.xls`, an extract of the original game's STARA.COM and STARB.COM (third-party work, last updated in 2006; that it is a faithful extract is INFERRED, and so is that the port's data was made from it: the values agree, the order of the records does not).

- **Encounters 144 to 146 (Thrynn) are hyperspace encounters, and only their location is wrong.** Ruling 1 asked which star their coordinates were meant for. Finding: none. In the original encounter table they are records 68, 69 and 72 of a run of seven single Thrynn scouts (records 67 to 73). Their bytes are the same as those of the other four, apart from the coordinates and the pointers; they sit inside the first 176 records of the table, which are the hyperspace encounters (every one of the other 173 has `m_location` 0 in the game data and none is on a star, while records 177 to 317 are all on a star); and each is linked to its table neighbours as a sibling, in one unbroken list from record 62 to 95. Their coordinates (144, 49), (149, 37) and (202, 10) are no star in the game data and none in the draft star list of `Research/Notes/starmap.txt`; the first two are 18 and 17 from the middle of the Thrynn territory, like three of the other four scouts. The original record has no location field at all: where a record hangs decides it. **Proposal:** set `m_location` of encounters 144, 145 and 146 from 1 to 0. No original value changes. Today `PD_Encounter.Reset` puts all three into star 0 (the star at 215, 86). A save holds the location and the place of every encounter, so the three would have to be reset when an older save is loaded.
- **Five of the twelve in-orbit encounters name the wrong orbit.** Found with PR #70. `Notes.txt` already lists the orbit positions of the Veloxi drones under "Questionable Data". In the original, a planet record points to its orbit encounter record; all twelve are found that way, each at the star with its own coordinates, and planet number n of the extract is the n-th planet from the sun (the planet types agree with the game data for all 270 stars). Seven agree with the game data: 33 (orbit 1), 65 (4), 77 (4), 233 (3), 303 (5), 304 (4), 316 (3). Five do not:

  | Encounter | Race | Star | Original | Game data | Effect today |
  |---|---|---|---|---|---|
  | 139 | Spemin home fleet | 32 | orbit 6 (planet 120) | orbit 2 | no planet in orbit 2: cannot begin |
  | 165 | Thrynn home fleet | 13 | orbit 4 (planet 46) | orbit 6 | no planet in orbit 6: cannot begin |
  | 301 | Veloxi drone | 137 | orbit 1 (planet 431) | orbit 2 | begins at planet 430 |
  | 302 | Veloxi drone | 29 | orbit 5 (planet 112) | orbit 2 | no planet in orbit 2: cannot begin |
  | 305 | Veloxi drones | 35 | orbit 1 (planet 130) | orbit 2 | no planet in orbit 2: cannot begin |

  **Proposal:** set `m_orbitPosition` of encounters 139, 165, 301, 302 and 305 to 6, 4, 1, 5 and 1. INFERRED: that the planet's pointer means "its orbit encounter" (it leads to the record with the same star coordinates in 12 of 12 cases).

**Encounters and combat**
- A planet with a living encounter in its orbit cannot be landed on (new with PR #70, follows from ruling 5): the encounter begins every time the ship goes into orbit, before the player can press Land. The drones and the derelict are single ships; the home fleets have 255 ships, 8 at a time. For the Mechans the encounter begins friendly once Mechan 9 is unlocked, but it still begins. Needs a decision if such planets are meant to be reachable (after a peaceful end, once per visit to the star system, with Mechan 9 unlocked).
- The planet is not drawn in an encounter that began in orbit (the later step of ruling 5). The design notes describe a disc of one of three sizes and six colours that ships fly over (`Research/Notes/gameflow.txt`).
- Whether hostile aliens answer a hail was not looked at. Where they do, a hail pauses the fight since PR #67 (INFERRED from ruling 4, not run through the console).
- Nothing in the game lowers crew vitality, so Examine always reports 100% and Treat never has a patient. **Ruled 2026-10-05: no change now; wait until planet lifeforms exist.** The design notes name lifeforms attacking the terrain vehicle as the source (`Research/Notes/planet.txt` lines 245 to 248).
- The names of the alien captains and their ships are never filled in: 15 alien greetings in the game data carry `%`, and 8 of them also `+`, for example "This is captain % of the Spemin ship +." They are shown as they are. The game data has no names for them (found 2026-10-04 with PR #50). Not ruled on.
- Three Spemin lines (267, 269, 270) belong to the subject "waiting for response", which the code never uses. **Ruled 2026-10-05: leave them as unused data.**
- The stand-in comm with the text "ERROR" is still what the aliens "say" when the game data has no line for a race, subject and stance; only the player's Statement was guarded (PR #72). No such case is known for the races that talk.

**Terrain and planets**
- A game that is loaded while the ship stands on the surface of a planet whose maps can no longer be generated is left where it is: Launch works from there and Disembark refuses (PR #40). Ruling 10 covered the terrain vehicle only.
- A new ship has room for 30.0 cubic meters (20.0 of its 50.0 are Endurium), so of a full terrain vehicle hold 20.0 stay in the vehicle until there is room. Seen in the `deposits` scenario; with deposits of 1 to 5 cubic meters a player gets there after about ten pickups. Nothing is lost. Not a defect; noted because PR #68 made it ten times as near.
- Ten times the minerals at unchanged Trade Depot prices is ten times the income from mining (PR #68). Planet 90 alone carries 2,218 cubic meters. The prices were not looked at.

**Leaks and per-frame cost**
- Generating the maps of one planet allocates roughly 170 MB of arrays on its background thread (the sum of the array sizes, not measured), and the managed heap was about 1.5 GB in the Editor while a star system was generated. With M18 fixed, two to four frames of 0.05 to 0.35 s are left per star system. In every run they coincided with a garbage collection, with the heap growing, or with a 32 MB allocation of the task (found 2026-10-04 by measuring with the `m18` scenario; that the collector is what stops the main thread is INFERRED, no profiler sample shows it). Not ruled on.

**Assets**
- The 811 planet files still carry the south pole padding of the code before PR #42 (M26). In all of them the south pole row is exactly what the old code computes. Generated again, 273 planets would get a different south pole height: by 0.21 of the height range at the median, by more than 0.1 for 203 of them, and by 0.86 at the most (planet 349). **Ruled 2026-10-05: leave the files; patch only the south pole rows of the 273 if the pole looks wrong in play; never regenerate all of them while the erosion pass is not reproducible.** Since PR #71 the erosion pass is reproducible. That does not make a regeneration neutral: the files in the project were made by the old pass, whose result differed from run to run, so every planet with an atmosphere would still come out different from its file. A regeneration stays the owner's call.
- 80 asset references in scenes, prefabs and materials point at assets that are not in the repository (found by cross-referencing GUIDs, 2026-10-03). Examples: the Debris mask in `SensorsDisplay.m_maskTextures` (index 24) in `Spaceflight.unity`, textures on several ship and planet materials, and objects in `Test.unity` and `Ecosystem.unity`. Most are harmless empty slots; none has been checked one by one. Not ruled on.
- The seam in the perlin noise texture (from the review's editor tools list). An image; not looked at. Not ruled on.
- `LiberationSans SDF - Fallback.asset` in the current format stores its empty atlas as a texture of 1 by 1 pixel where the old file had 0 by 0 (PR #71). That is how this version of TextMesh Pro stores a cleared atlas; no glyph, character or metric changed. Listed because the ruling said the content must not change, and this one value did.

**Test.unity only**

The experimental planet path (`PlanetManager`, `TerrainJob`, `PlanetData`, bridged by `ProceduralAdapter`) is wired only into `Test.unity`.

- `PlanetManager.GeneratePlanet` copies the three colours from the `PlanetData` asset every time, which throws away the colours `ProceduralAdapter.ApplyBiomeData` has just set. **Ruled 2026-10-05: leave it until the experimental path is used.**

**Dead or latent**
- `TerrainVehicleCargoDisplay`, `TerrainRuins` and `TerrainArtifact` aren't wired into any scene. **Ruled 2026-10-05: keep them as groundwork.** (The cargo display's volume texts were corrected with PR #68.)

**Not seen by anyone**

The probe has no graphics, and nothing was played by hand in the GUI Editor. From this round: the grey ball of a planet without maps and its clouds (PR #69), where the alien ships appear in an encounter from orbit and the missing planet there (PR #70), the explosion of a missile that runs out of time (PR #67), the width of the stardates in the bank's date column and in the Operations header (PR #66), and that the "Starport clear" arrow still draws with the font asset in its new format (PR #71).

## Status of `PROJECT_ANALYSIS_REPORT.md` items

| Report item | Status now |
|---|---|
| TradeDepotPanel `while(true)` (critical) | Capped by 70445da. It could only hang with a viewport of 3 rows or fewer; the cap hides that case rather than fixing it. |
| TradeDepotPanel `amountParts[1]` with "5." | Was never a real issue. The real parsing bugs are M3. |
| TradeDepotPanel `renderedHeight / m_rowCount` | Was never a real issue: float divided by int, and the row count is always at least 3. |
| AnalysisButton bounds (2 items) | Fixed. |
| Encounter `garbledWord[0]` | Was never reachable. The "fix" caused M1. |
| Encounter "~1693" | Actually `commWord[ Length - 1 ]`. Guarded since PR #50, which skips an empty word. It was unreachable with the shipped data until that PR filled the captain's name into alien messages: a name with two spaces in a row leaves an empty word. |
| SystemDisplay orbit position | Fixed in `Update`, and in `ChangeSystem` since PR #72. Every planet's orbit is 1-8. |
| StarSystem orbit position | Fixed; was never reachable. |
| Planetside `GetComponent<Collider>` | Never existed in `Planetside.cs` (checked with `git log -S`). |
| BridgeController `UIDocument` | Never a real issue: the class has `RequireComponent`, and it's only used in a test scene. |
| Encounter `if (false && ...)` | Was in `TerrainVehicle.cs:344`. Removed in PR #72. |
| "m_alienComms NRE: FIXED" | Only the write path was guarded at the time; fully fixed by H2 in PR #3. |
| `PD_Bank` "make the player rich" hack | Editor only since PR #66. A build starts with the original 12,000 MU. |

The report's ✅ claims for "Save/Load complete", "alien comm history" and "Personnel delete" were contradicted by H2, H3, M8 and M9; all four are fixed now.
