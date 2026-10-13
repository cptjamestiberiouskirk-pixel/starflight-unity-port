# Screen comparison

Roadmap Phase 7, "compare every screen with `Research/Screenshots/`". The comparison was made on 2026-10-11 (commit 68d89f61), by the session that then fixed most of what it found. Every item was checked again against `origin/master` at 64bf172d on 2026-10-12, with new probe pictures of the port's screens. This document records each item's status today and the work that is left. Nothing in it changes the game.

The messages the player sees are covered by `Research/STRINFO message audit.md`. This document covers what is on the screen: displays, labels, layout and the flow of a screen.

## Sources

- **The original:** `Research/Screenshots/` (259 EGA pictures in 19 folders). Where the screenshots stop, the decompiled original ([s-macke/starflight-reverse](https://github.com/s-macke/starflight-reverse), `starflt1-out/overlays/`) says what a screen does: `ENGINEER.c` (Damage and Repair), `NAVIGATR.c` (shields and weapons), `ANALYZE-OV.c` (Analysis), `HYPERMSG-OV.c` (hyperspace messages), `OP-OV.c` (Operations), `DAMAGE-OV.c` (combat damage), `LAUNCH-OV.c` (launch), `CAPTAIN.c` (the Captain's menu) and `data.h` (the bank's transaction names).
- **The port:** pictures rendered by the headless probe with a graphics device (`DevTools/HeadlessProbe/probe.ps1 -Scenario screenpictures -Graphics` and `starport-screenpictures`), 12 of the flight screens and 8 of the Starport's rooms, into `Logs/screen-pictures/` (not committed). Since 2026-10-13 also `surfacepictures` (the landed ship and the terrain vehicle's screens) and `bridgepictures` (the flight scene's docking bay, Medical, Communications and the launch), and `starport-screenpictures` goes on into the rooms' other screens (29 pictures). Where a screen has no picture, the port side comes from the code.

Not reported: art style, resolution, colours, fonts and 3D models.

## Labels

- **Status:** FIXED (by the PR named), OPEN, PARTLY (what is left is said), NOT A DEFECT, OWNER'S CALL, PROBE (a gap or an artifact of the probe's pictures, not of the game).
- **Owner:** GAMEPLAY (what the game logic does or writes) or UI KIT (the labels, layout and look of the console and the panels: the UI kit's work). An item marked UI KIT is left to that work and only recorded here.
- **Evidence:** CONFIRMED (seen in a picture or in the code on 2026-10-12), INFERRED (one link is not verified; it is named), HYPOTHESIZED.

## Summary

| | Items | FIXED | OPEN or PARTLY | Not a defect or owner's call |
|---|---|---|---|---|
| GAMEPLAY (with the items shared with the UI kit) | 52 | 43 | 5 | 4 |
| UI KIT | 41 | 0 | 40 | 1 |
| The probe's pictures | 17 | 3 (PR 234) | 14 gaps | |

Since 2026-10-11 the gameplay side has been fixed in PRs 215, 216, 218, 219, 220, 221, 222, 223, 224, 225, 226, 231, 242, 254 and 263, and since the comparison was checked again on 2026-10-12 in PRs 281 (the DAMAGE display), 282 (the interest), 284 (combat messages and wounds), 285 (the shields and weapons messages), 286 (Repair's choice), 287 (the orbit lines), 288 (the notices screen), 289 (the ledger's names), 290 (the analysis heading and the temperature), 291 (Look), 292 (the landing site), 294 (the weather line), 296 and 297 (the encounter pictures), 299 (the typing of a notice), 300 (the vehicle's distance) and 301 (the training sessions); the probe pictures in PR 234. The UI kit items are all still open: the UI kit's PRs so far (213, 260, 268) redid the docking bay, the in-orbit view and the intro, and the owner ruled on 2026-10-12 that the kit's own words stay (the bridge entries, the officer label, the button and bar labels).

The open gameplay items:

1. **The bank books a recommendation's bonus or fine at once in the original** (section 21), where the port pays it on docking: it touches the colony rulings of roadmap 1.3, so it waits for the owner.
2. **"MOTION DETECTED AT X: Y:"** (section 11): parked, since the numbers are the steps the original's alien groups take each star hour, and the port moves its aliens otherwise.
3. Low priority: when a supplemental evaluation's fine is booked (18), the original's "NEW" in "YOU HAVE NOT FILED ANY NEW COLONY WORLD RECOMMENDATIONS" (18), and three pre-flight lines of the port's that no screenshot has (24).

---

## 1. Ship console (all flight screens)

| Item | Status | Evidence | Owner |
|---|---|---|---|
| Bridge entries read COMMAND, SCIENCE, NAVIGATION, ENGINEERING, COMMUNICATIONS, MEDICAL; the original's are CAPTAIN, SCIENCE OFF., NAVIGATOR, ENGINEER, COMM. OFF., DOCTOR | OPEN | `CommandButton.cs:6` and the other bridge buttons' `GetLabel`; every flight picture (CONFIRMED) | UI KIT |
| The officer label under the ship icon is "Officer <name>" for four roles; the original shows the role over the name ("SCIENCE OFF. / DATA") | OPEN | `ScienceButton.cs:21`, `EngineeringButton.cs:21`, `CommunicationsButton.cs:29`, `ButtonController.cs:338` (CONFIRMED, code) | UI KIT |
| The terrain vehicle shows a "Terrain Vehicle" officer label; the original has none | OPEN | `Disembarked.cs:152`, the terrain vehicle picture (CONFIRMED) | UI KIT |
| The text window sits in the right-hand column under the console; the original's is a band across the whole bottom of the screen. In an encounter it still widens over the main view | OPEN | every flight picture (CONFIRMED) | UI KIT |
| SYSTEM MAP and TERRAIN MAP headings over the auxiliary view, which the original does not have | OPEN, low priority | `SystemDisplay.cs:193`, `TerrainMapDisplay.cs:53` (CONFIRMED) | UI KIT |
| Yes and No questions replace the console's menu with Yes and No buttons; the original asks them in the text window (`<-N Y->`) and keeps its menu | PARTLY | the words and the No-then-Yes order are the original's since PRs 222 and 223; the buttons on the console are a design choice (CONFIRMED, code) | UI KIT |

## 2. Navigator menu

| Item | Status | Evidence | Owner |
|---|---|---|---|
| "Raise Shields" where the original has RAISE SHIELD | OPEN | `RaiseShieldsButton.cs:6` (CONFIRMED) | UI KIT |
| "Drop Shields" and "Disarm Weapon" where the original has DROP SHIELD and DISARM | OPEN | `DropShieldsButton.cs:6`, `DisarmWeaponButton.cs:6`; `NAVIGATR.c` (CONFIRMED, code on both sides) | UI KIT |
| The messages of the shields and weapons. Original: SHIELDS ARE NOW UP, SHIELDS ARE DOWN, ARMING MISSILES or ARMING LASERS, WEAPONS ARE DISARMED, and SHIELDS CAN'T BE RAISED with a beep; in a nebula MAGNETIC DISTURBANCES IN NEBULA / CAUSING ENERGY FLUCTUATIONS. Port: "Shields raised.", "Shields dropped.", "Weapons armed.", nothing at all when disarming (the button sets the flag and does not call `DisarmWeapons`), and its own lines for a nebula and for no fuel | FIXED (the shields' and weapons' messages PR) | `PD_PlayerShip.cs:626, 637, 964, 974`, `DisarmWeaponButton.cs:13`, `RaiseShieldsButton.cs:30, 41`; `NAVIGATR.c` 190 to 323, 374 (CONFIRMED, code) | GAMEPLAY |

## 3. Star system

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The messages of travel in a system ("...COMMENCING SYSTEM TRAVEL", "OUTSIDE ORBITAL RANGE", "STANDING BY TO ORBIT") | FIXED | PR 219 (CONFIRMED, code) | GAMEPLAY |
| "Press Enter to orbit." where the original says spacebar | NOT A DEFECT | the port's key is Enter (`StarSystem.cs:140`) | GAMEPLAY |

The stellar parameters and the system map match the original (CONFIRMED, picture).

## 4. Status display

| Item | Status | Evidence | Owner |
|---|---|---|---|
| SHIELDS and WEAPONS read None on a ship without them; the original reads DOWN and UNARMED | FIXED | PR 216; picture: "Down", "Unarmed" (CONFIRMED) | GAMEPLAY |
| DAMAGE as a percentage; the original gives a word | FIXED | PR 216 (CONFIRMED, code) | GAMEPLAY |
| ENERGY "20.0M3", CARGO "40.0% Full"; the original's "20.00" and "40 %" | FIXED | PR 216; picture (CONFIRMED) | GAMEPLAY |
| The label WEAPONS where the original has WEAP | OPEN, low priority | `Spaceflight.unity:7087` (CONFIRMED) | UI KIT |
| The bars are labelled SHIELDS and ARMOR, written upright; the original has S and A | OPEN, low priority | picture (CONFIRMED) | UI KIT |

## 5. Orbit

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The lead-up to orbit (STANDING BY TO ORBIT, PRESS SPACEBAR TO ORBIT) | FIXED | PR 219 (CONFIRMED, code) | GAMEPLAY |
| The port clears the text window at each step; the original lets the lines scroll on, from "...COMMENCING SYSTEM TRAVEL" to "...ORBIT ESTABLISHED" | FIXED (the orbit lines PR) | `StarSystem.cs:142`, `ManeuverButton.cs:194`, `InOrbit.cs:231`; starflt_141, starflt_143 (CONFIRMED) | GAMEPLAY |
| "Orbit established." where the original has "...ORBIT ESTABLISHED" | FIXED (the orbit lines PR) | `InOrbit.cs:232`, starflt_143 (CONFIRMED) | GAMEPLAY |
| A frame in the main view ("IN ORBIT", "ORBIT VII · FROZEN", "ENCOUNTER", corner brackets) that the original does not have | OPEN | `ViewportFrame.cs:117, 129` (PR 260); pictures (CONFIRMED) | UI KIT |
| The auxiliary view in orbit | NOT A DEFECT | the game keeps the display Maneuver leaves on, the system map, as the original's starflt_143 (Orbit); the probe had left Status on and now shows the system display (`screenpictures` `03_orbit.png`) (CONFIRMED) | |

## 6. Sensors

| Item | Status | Evidence | Owner |
|---|---|---|---|
| No picture of a scan | FIXED | PR 234: a planet and a Spemin scout scanned (CONFIRMED) | |
| Each value sits on the line under its heading; the original writes it on the heading's line, in a five-line window | OPEN | `SensorsDisplay.cs:272` (CONFIRMED) | UI KIT |
| The lithosphere is one line ("Tin, Lead, Gold"); the original lists one element a line under the values | OPEN | `SensorsDisplay.cs:272`, starflt_144 (CONFIRMED) | UI KIT |
| Mass above the scan picture and Bio and Min below it; the original stacks them in a column on its left | OPEN, low priority | pictures against starflt_144 (CONFIRMED) | UI KIT |

A vessel's reading matches the original's (mass, bio, energy, object constituents; CONFIRMED, picture). The probe's sensor pictures show the bridge's menu because the probe runs the Sensors button directly (PROBE).

## 7. Analysis

| Item | Status | Evidence | Owner |
|---|---|---|---|
| No picture of an analysis | FIXED | PR 234 (CONFIRMED) | |
| An alien ship's shields and weapons were always "Not certain" | FIXED | PR 220; picture: "Shields: None apparent", "Weapon Status: Armed" (CONFIRMED) | GAMEPLAY |
| "Size:" label before the ship's size | FIXED | PR 220 (CONFIRMED) | GAMEPLAY |
| The heading has no colon: "Analysis of Last Sensor Reading"; the original's "ANALYSIS OF LAST SENSOR READING:" | FIXED (the analysis heading PR) | `AnalysisButton.cs:144`; `ANALYZE-OV.c:493`, starflt_145 (CONFIRMED) | GAMEPLAY |
| A temperature range with the same word at both ends: "Sub-Arctic to Sub-Arctic"; the original prints the one word (it drops the second when they are equal) | FIXED (the analysis heading PR) | `GD_Planet.cs:346`; `ANALYZE-OV.c`, word WF068; the orbit analysis picture (CONFIRMED) | GAMEPLAY |

## 8. Landing map and descent

| Item | Status | Evidence | Owner |
|---|---|---|---|
| LAT and LONG swapped | FIXED | PR 215 (CONFIRMED, code) | GAMEPLAY |
| The extra "Retro rockets firing..." and the order of the descent's lines | FIXED | PR 226 (CONFIRMED, code) | GAMEPLAY |
| Where the crosshair starts. Original: LAT 0, LONG 0 after Select Site (starflt_149): SITE-OV's WF014 puts it in the middle when PLHI is -1, which DEPART sets on leaving orbit, and keeps the last site within one orbit. Port: the last site chosen, reset only in a new game | FIXED (the landing site PR): reset on entering the star system or hyperspace | `SpaceflightController.SwitchLocation`; `SITE-OV.c` WF014, `DPART-OV.c` DEPART; the `landingsite` probe scenario (CONFIRMED); that DEPART runs on leaving orbit is INFERRED from what it does (the Black Egg's countdown, the planet obliterated) | GAMEPLAY |
| The original shows the LAT and LONG labels, empty, before Select Site (starflt_148); the port shows them only once Select Site is pressed (they are children of the Select Site object, which is hidden) | OPEN | the scene's hierarchy (INFERRED: not seen in a picture) | UI KIT |
| A SELECT SITE heading inside the auxiliary view | OPEN, low priority | picture (CONFIRMED) | UI KIT |
| The landing map with the crosshair moved to 15 north, 30 west reads LAT 15N LONG 30W, the crosshair west of and above the middle of the map, as the original's | NOT A DEFECT | `screenpictures` `06_landing_map.png` against starflt_150 (Ship UI/Land) (CONFIRMED). The menu in that picture is the bridge's, since the probe opens Select Site directly (PROBE) | GAMEPLAY |

## 9. Planetside (landed)

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The "HULL CHECK COMPLETE" line was missing | FIXED | PR 231 (CONFIRMED) | GAMEPLAY |
| "Stand by, scanning planet..." for "STANDBY...SCANNING PLANET." | FIXED | PR 231 (CONFIRMED) | GAMEPLAY |
| The landed ship: the coordinates (125 100) and the bridge are the original's, but the auxiliary view shows Status where the original keeps the terrain map with the site's LAT and LONG (starflt_178, Landing) | FIXED (the landed terrain map PR) | `Planetside.Show` now leaves the terrain map in the auxiliary view; the `landinglines` probe scenario checks it after a real landing (CONFIRMED). The text window still has the orbit's lines in the picture because the probe skips the descent (PROBE) | GAMEPLAY |

## 10. Terrain vehicle

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The date carried the hour | FIXED | PR 218; picture "01-01-4620" (CONFIRMED) | GAMEPLAY |
| The coordinates' order, with no "x" | FIXED | PR 215; picture "1S x 0E" (CONFIRMED) | GAMEPLAY |
| The bearing to the ship abbreviated | FIXED | PR 218; picture "North" (CONFIRMED) | GAMEPLAY |
| "SHIP 14 KM. North" with the ship's model just above the vehicle; the original shows "1KM." with the ship beside it (starflt_179) | FIXED (the terrain vehicle distance PR) | The port counted the height of the ship's place (the vehicle starts 143.6 units below it) and used a kilometer of about 9 world units. The original's distance is along the surface in its own units, a kilometer each, 6.4 to a degree of longitude and 5.3 to a degree of latitude (`MOVE-OV.c` WF05A, `SITE-OV.c` .LON and WEFA0), now `Tools.SurfaceDistanceInKm`: the vehicle reads 4 KM. as it comes out, since the port puts it 16 units south of the ship's place so that the models do not overlap (CONFIRMED, the `tvdistance` probe scenario) | GAMEPLAY |
| The weather is the first line on the surface in the original ("IT IS CLEAR.", alone in the window); the port wrote it as a sixth line under the disembark lines | FIXED (the weather line PR): the text window is cleared before the first sky | `PlanetWeather.Begin`; starflt_179; the `disembarklines` probe check (CONFIRMED) | GAMEPLAY |
| Look: the port writes Surface, Weather, Sky, Atmosphere and Gravity; the original writes SKY, TEMPERATURE, GRAVITY, ATMO, HYDRO and TERRAIN (starflt_180): no temperature and no hydrosphere in the port | FIXED (the Look fields PR; the temperature is the planet's, the port has no regions) | `LookButton.cs:40` to `46` (INFERRED: code, no picture) | GAMEPLAY |
| No CREW VITALITY box with dot leaders | OPEN | picture (CONFIRMED) | UI KIT |
| MAP for MAPS, EFFICIENCY for EFF. | OPEN | `MapButton.cs:10`, `Spaceflight.unity:65137` (CONFIRMED) | UI KIT |
| A menu of six on the console; the original's is a bar of seven across the bottom with ICONS, which opens two ICON KEY screens | OPEN | `ButtonController.cs:127` (CONFIRMED) | UI KIT |
| Weapon: Fire, Switch, Back; the original's STUNNER / LASER / CHOOSE WEAPON / RETURN= AIM | OPEN | `ButtonController.cs:129`; `surfacepictures` `06_weapon.png` against starflt_185 (CONFIRMED) | UI KIT |
| A lifeform on screen: the 3D model of its body plan beside the vehicle, where the original draws an icon on its map | NOT A DEFECT (art) | `surfacepictures` `02_lifeform.png` against starflt_055 (CONFIRMED) | |
| Look describes at once, under a heading of the port's ("You observe your surroundings:") and with the nearest object's description; the original moves an eyeball cursor over the map and describes the icon under it (planet.txt LOOK). The text window shows its last five lines only, so the heading, SKY and TEMPERATURE have scrolled out of the picture | OPEN | `LookButton.cs`; `surfacepictures` `03_look.png` against starflt_180, starflt_183 (CONFIRMED) | GAMEPLAY (the cursor), UI KIT (the heading and the window) |
| Scan reports at once ("Local Scan Results:", mineral and bio density, the elements, the nearby objects with a LIFEFORM label in the main view); the original scans what is under a cursor ("YOUR SHIP") | OPEN | `ScanButton.cs`; `surfacepictures` `04_scan.png` against starflt_184 (CONFIRMED) | GAMEPLAY |
| Cargo. Original: a list in the text window, PLANET SURFACE (what is in reach) and TERRAIN VEHICLE (what it carries), each item with its volume and value, SCROLL ITEMS and QUIT, and the item to pick up chosen from it. Port: Cargo acts at once (beside a lifeform it offers Capture, Record and Ignore), and with nothing in reach lists the vehicle's hold only, in a CARGO display that replaces Status, with no values, and Next, Drop and Back | FIXED (the cargo values PR) for the values; the list to pick from, Claude's call by the owner's leave (2026-10-13) | The cargo display now gives each item's value beside its volume, as the original's list (an element at what the Starport pays for a cubic meter, an artifact and a specimen at their value; `TerrainVehicleCargoDisplay`, the `cargodisplay` probe scenario, and the picture `08_cargo_list.png`: ZINC 3.0 m3 140). The PLANET SURFACE part and picking an item from the list stay the port's: in its 3D view Cargo takes what is beside the vehicle, the item the original's list would offer; `TVCargoButton.cs`; `surfacepictures` `05_cargo_lifeform.png` against starflt_182, starflt_189 to 191 (CONFIRMED) | GAMEPLAY (no surface list, no values), UI KIT (the layout) |
| Fire hits the nearest living lifeform in range ("The laser hits the lifeform."); the original aims with a cursor (RETURN= AIM) | OPEN, design choice | `TVFireButton.cs`; `surfacepictures` `07_weapon_fire.png` against starflt_185 (CONFIRMED) | GAMEPLAY |
| Map raises the camera high over the vehicle; the original's MAPS shows a wider map of the region with a box around the vehicle's part of it | OPEN | `MapButton.cs`; `surfacepictures` `09_maps.png` against starflt_181 (CONFIRMED) | UI KIT |

In the surface pictures the 3D view stops at about x 1265 of 1920 with the sky beyond it, and in these and the other flight pictures the left part of the view, up to about x 968, is darker than the rest; the frame's corner marks end at the same x 960. They are taken for PROBE artifacts of rendering every camera into one 1920 by 1080 picture, not seen in the game (HYPOTHESIZED: not checked in the Editor).

## 11. Hyperspace

| Item | Status | Evidence | Owner |
|---|---|---|---|
| No picture of hyperspace | FIXED | PR 234 (CONFIRMED) | |
| The coordinates were not written again on entering hyperspace | FIXED | PR 221 (CONFIRMED) | GAMEPLAY |
| "MOTION DETECTED AT / X: Y:". The port clears the window and writes "Motion detected." with no numbers. The original prints MOTION DETECTED AT once a pass and then each moving group's step toward the ship (X and Y from -1 to 1), when the science officer's skill is above 150, and keeps the earlier lines | FIXED (the motion report PR) for the gate; the numbers left out, Claude's call by the owner's leave (2026-10-13) | The skill is the science officer's science (CONFIRMED: the ASSIGN-CREW slot at 0x14 is the science officer's, as 0x17 with the navigation byte 0x1b is the navigator's and 0x1a with 0x1c the engineer's, and the CREWMEMBER byte 0x1a is science, the first of the five skills, 0x1d being COMM-SKILL); the port now says "Motion detected." only through a science officer of science above 150 (`Radar.ScienceOfficerReportsMotion`). The numbers are each group's jump of that hour, 3/4 of the way to the ship in hyperspace and 1/2 in a system, in 8-unit cells (WF333, WF31B): the port's groups move smoothly at 32 world units a second (about 2.6 game units an hour), so the original's numbers would always be 0 and are not printed. The `motiondetected` probe scenario (CONFIRMED) | GAMEPLAY |

## 12. Encounter and comm link

| Item | Status | Evidence | Owner |
|---|---|---|---|
| "LEAVING ENCOUNTER... / ...ENCOUNTER TERMINATED" | FIXED | PR 242 (CONFIRMED, code) | GAMEPLAY |
| "THEY'VE ARMED THEIR WEAPONS" and the other shield and weapon news | FIXED | PR 224 (CONFIRMED, code) | GAMEPLAY |
| Combat messages. Original: MISSILE CLASS n or PLASMA BOLT, and "<name> IS SLIGHTLY / MODERATELY / HEAVILY / CRITICALLY WOUNDED" or KILLED. Port: "Incoming torpedo!", "Plasma bolt incoming!", "Hull takes N damage!", and combat never wounds the crew (`InjureCrewMember` is called only by lifeforms and the weather) | FIXED (the combat wounds PR: the original's words, and a hit on the crew wounds them) | `CombatController.cs:642, 878` to `935`; `DAMAGE-OV.c:1160` to `1243`, `1872` (CONFIRMED, code) | GAMEPLAY |
| No alien ship and no scan bracket in the encounter picture, and the player's ship is a speck | FIXED (the encounter view PR) | Not the probe: its `EnterEncounter` does what `SpaceflightController.BeginEncounter` does. Measured with a graphics run on 2026-10-12: the encounter camera climbed to 7,000 to 11,000 units above the ship (perspective, 30 degrees) to frame alien ships that start about 3,700 units away, off the left edge at 4 s and in view from about 6 s; at that height a 90-unit Spemin scout covered under 2% of the screen and the player's ship was a speck. The original draws the ships as icons at a fixed scale, about a sixteenth of the view's height. Claude's call by the owner's leave (2026-10-12), both parts: the camera frames the ships out to the aliens' fire range and no further (at most about 4,450 units), and the ships are drawn twice as large in an encounter, about a twentieth of the view at the camera's highest (CONFIRMED, `encounterviewscale` probe scenario and its pictures) | GAMEPLAY or UI KIT |
| The comm link's main view shows coloured static where the alien's portrait should be | FIXED (the comm picture PR) | Not a capture artifact: the portrait is there (seen at 8 s, the Spemin under the static), and the static was the port's own `CommScreen` effect (coloured rays, waves, low resolution), stronger the lower the communications officer's skill. The original shows the portrait clean and garbles only the words. Claude's call by the owner's leave (2026-10-12): the interference is now a tuning in that clears in 5 s and does not come back (CONFIRMED, `commpicture` probe scenario) | GAMEPLAY or art |
| The coordinates above an encounter in a star system are the system's | NOT A DEFECT | the 118 96 was the probe's: it put the ship in hyperspace away from Arth for the hyperspace picture and entered Arth's system from there. It now puts the ship back at Arth's place first, and the encounter reads 125 100 (`screenpictures` `09_encounter.png`, `bridgepictures` `12_hail_postures.png`), as the original's encounter shows its system's (starflt_044: 98 81) (CONFIRMED) | |

## 13. Engineering (Damage and Repair)

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The DAMAGE display. Original (`ENGINEER.c`): Damage clears the auxiliary view and draws DAMAGE with a 40-pixel bar for each of HULL, ENGINES, SENSORS, COMM., SHIELD, MISSILE, LASER, green for what works and red for the rest, and writes no text. Port: there was no damage display; Damage wrote a "Damage Report:" text | FIXED (the DAMAGE display PR) | `DamageDisplay`, shown by Damage and at the start of Repair; the picture `13_damage.png` of `screenpictures` (CONFIRMED) | GAMEPLAY and UI KIT |
| Repair. Original: shows the same display and lets the player scroll the seven rows; the text window gives each one ("ENGINES: SLIGHTLY DAMAGED", EFFICIENCY, CURRENT FUNCTIONALITY, ESTIMATED TIME FOR COMPLETE REPAIRS, CURRENTLY UNDER REPAIR), and the fire button chooses what to repair, with NO REPAIRS NEEDED, DAMAGED BEYOND REPAIR, "CEASE REPAIRS ON X AND BEGIN REPAIRS ON Y? [N Y]" and BEGINNING, CONTINUING or CEASING REPAIRS ON X. Port: Repair picked the system itself, the worst first, by the owner's ruling of 2026-10-11 for roadmap 1.2, made when no shipped source had the mechanics; on 2026-10-12 the owner ruled that it follows the original | FIXED (the Repair choice PR) | `RepairButton.cs`, the DAMAGE display's cursor; the `repairchoice` probe scenario and the picture `14_repair.png` (CONFIRMED) | GAMEPLAY |
| Pictures of Damage and Repair | NOT A DEFECT | `screenpictures` `13_damage.png` and `14_repair.png` against starflt_065, starflt_066 and the docking bay's starflt_119 to 126: the seven rows, bars only for what is installed, and Repair's "Engines: Slightly damaged / Efficiency: 70% / Current functionality: Operative / Estimated time for complete repairs" lines as the original's (CONFIRMED). The menu in `13_damage.png` is the comm link's, since the probe presses Damage directly (PROBE) | |

## 14. Medical (Examine and Treat)

| Item | Status | Evidence | Owner |
|---|---|---|---|
| No choice of crew member and no durability | FIXED | PR 225: "Who should I examine?", "...is not wounded, and has a vitality of N and a durability of N." (CONFIRMED, code) | GAMEPLAY |
| Examine and Treat: "Who should I examine?" and "Who should I treat?" with the name and "Choose crewmember <-> and press Enter.", "<name> is not wounded, and has a vitality of 100 and a durability of 6.", "<name> doesn't need medical treatment.", as the original's (its SPACEBAR is the port's Enter, as in section 3) | NOT A DEFECT | `bridgepictures` `02_examine_question.png` to `05_treat.png` against starflt_129 to 132 (Docking Bay) (CONFIRMED) | GAMEPLAY |

## 15. Communications (Hail and Distress)

| Item | Status | Evidence | Owner |
|---|---|---|---|
| Distress refused in the docking bay | FIXED | PR 222: "Transmit emergency distress call?" with No and Yes, "We're already home!" (CONFIRMED, code) | GAMEPLAY |
| Hail with no one to hail: "There's no one to hail." | NOT A DEFECT | `bridgepictures` `06_hail_nobody.png` against starflt_127 (CONFIRMED) | GAMEPLAY |
| Distress asked "Transmit emergency distress call?" with no CONFIRM; the original's is TRANSMIT EMERGENCY DISTRESS CALL / CONFIRM: <-N Y->, and the port's own Launch asks "Ready to launch. Confirm?" | FIXED (the distress confirm PR) | the question is now "Transmit emergency distress call" / "Confirm?" on two lines, as the original's (COMM-OV TOW-US: the line, CTCR, CONFIRM: [N Y]); the `distressconfirm` probe scenario checks for it (CONFIRMED) | GAMEPLAY |
| Hail in an encounter: the postures to hail with (Friendly, Hostile, Obsequious), then "Transmitting:" and the friendly greeting "Greetings. We come in peace and wish to communicate, please identify yourselves." as the original's | NOT A DEFECT | `bridgepictures` `12_hail_postures.png`, `13_hail_friendly.png` against starflt_044 (Encounters/Hail) (CONFIRMED for the words; the choice of posture INFERRED, code) | GAMEPLAY |
| After the hail the console goes back to the bridge's menu; the original keeps HAIL, DISTRESS, BRIDGE | OPEN, low priority | `bridgepictures` `13_hail_friendly.png` against starflt_044, starflt_047 (CONFIRMED) | UI KIT |

## 16. Docking bay and the Captain's menu

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The CAUTION / PRESSURIZED AREA sign | NOT A DEFECT | it is on the docking bay doors' texture (PR 213) (CONFIRMED) | UI KIT |
| CLOSING DOCKING BAY DOORS on arrival | FIXED | PR 219 (CONFIRMED, code) | GAMEPLAY |
| "Opening docking bay doors..." | NOT A DEFECT | the original's own line (`LAUNCH-OV.c`) (CONFIRMED) | GAMEPLAY |
| "Confirm launch?" for READY TO LAUNCH / CONFIRM | FIXED | PR 223 (CONFIRMED, code) | GAMEPLAY |
| The launch's security code | OWNER'S CALL | the copy protection, decision D4 in `ROADMAP.md` | GAMEPLAY |
| The Ship's Log is a menu (notices, comms, messages); the original's is a free-text notepad with an EXIT tab | OWNER'S CALL | the port's Messages log is roadmap item 3.3 (INFERRED: a deliberate design) | GAMEPLAY |
| The docking bay as the crew comes aboard: "Ship computer activated. / Pre-launch procedures complete. / Standing by to initiate launch.", the Status display and the bridge, as the original's | NOT A DEFECT | `bridgepictures` `01_docking_bay.png` against starflt_103, starflt_192 (CONFIRMED) | GAMEPLAY |
| The coordinates 125 100 above the docking bay; the original's docking bay shows none at the start of a game (starflt_103, 192), and shows them on the way back in (starflt_075) | OPEN, low priority | `Viewport.UpdateCoordinates`; `bridgepictures` `01_docking_bay.png` (CONFIRMED) | UI KIT |
| The launch from the Starport. Original (`LAUNCH-OV.c`, &LAUNCH and LAUNCH): OPENING DOCKING BAY DOORS, then the window cleared and COMMENCING LAUNCH SEQUENCE... with a countdown of 5 to 1 in the main view (COUNTDOWN), then LEAVING STARPORT..., ...STARPORT CLEAR and STANDING BY TO MANEUVER kept together (starflt_136 to 139). Port: "Opening docking bay doors..." during the whole launch, no COMMENCING LAUNCH SEQUENCE and no countdown, and at the end the window cleared to "Starport clear. / Standing by to maneuver." (no LEAVING STARPORT... left, no leading dots) | FIXED (the launch lines PR) | The countdown was there (the port's `Countdown` animation from `PlayerCamera.BeginCountdown`, 15 s into the launch, after the pictures' moment) but under "Opening docking bay doors..."; the window now clears to "Commencing launch sequence..." as it begins, and out of the Starport "...Starport clear" and "Standing by to maneuver." are added under "Leaving starport..." instead of the window being cleared twice (`PlayerCamera.BeginCountdown`, `SpaceflightController.SwitchLocation`; the bridge buttons no longer clear it); the `launchlines` probe scenario (CONFIRMED) | GAMEPLAY |
| After the launch the console shows the bridge's menu; the original keeps the Captain's (LAND, DISEMBARK, ...) | OPEN, low priority | `bridgepictures` `11_launched.png` against starflt_139 (CONFIRMED) | UI KIT |
| Launch's question "Ready to launch. Confirm?" with No and Yes | NOT A DEFECT | `bridgepictures` `09_launch_question.png` against starflt_133 (CONFIRMED); the security code of starflt_134 and 135 is D4 | GAMEPLAY |

## 17. Starport concourse

The doors are where the original's are (Operations upper left, Personnel top, Crew Assignment upper right, Bank lower right, Ship Configuration bottom, Trade Depot lower left, the Docking Bay in the middle; CONFIRMED, scene and picture). No difference.

## 18. Operations

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The evaluations one to a page, the supplemental ones once, END OF EVALUATIONS, and each evaluation's heading | FIXED | PRs 254, 263 (CONFIRMED, code) | GAMEPLAY |
| The notices' header. Original: "NOTICES 01-01-4620". Port: "Today is" and the date | FIXED (the notices screen PR): the date beside the scene's NOTICES heading | `OperationsPanel.ShowNotices`; `OP-OV.c` WE735; the `starport-notices` probe scenario (CONFIRMED) | GAMEPLAY |
| "***END OF NOTICES***" after the last notice | FIXED (the notices screen PR) | after the newest notice that is out; `OP-OV.c:902`; `starport-notices` (CONFIRMED) | GAMEPLAY |
| With no unread notice the original says NO NEW NOTICES and asks REPEAT LAST NOTICE? [N Y], and on Y "***REPEATING LAST NOTICE***"; the port showed the last notice read again without asking | FIXED (the notices screen PR): More and Quit answer Yes and No meanwhile | `OperationsPanel.AskToRepeat`; `OP-OV.c:1001` to `1020`; `starport-notices` (CONFIRMED) | GAMEPLAY |
| The original types a notice line by line with a pause (the spacebar skips it), with PRESS SPACEBAR TO CONTINUE / PRESS [] TO ABORT CURRENT NOTICE and ***NOTICE ABORTED***; the port showed one paragraph a press of More, with no abort | FIXED (the notice typing PR) for the flow: a paragraph every 0.3 s (the original's 300 ms), More shows the next at once, Quit while a notice is typed ends it with ***NOTICE ABORTED*** (and END OF NOTICES after the newest), a second Quit leaves; the two prompts stay the UI kit's More and Quit buttons | `OperationsPanel.Tick`, `AbortNotice`; `OP-OV.c:536, 543, 862, 879`; the `starport-noticetyping` probe scenario (CONFIRMED) | GAMEPLAY (the flow), UI KIT (the prompts) |
| After a notice the original shows "<- NEXT MESSAGE  PREVIOUS MESSAGE ->" and "PRESS SPACEBAR TO REPLAY OR [ QUIT ]"; the port has More, Previous, Next and Quit buttons and no Replay | OPEN | `OperationsPanel.cs:20` to `23`; `OP-OV.c:512, 519` (CONFIRMED) | UI KIT |
| The original keeps NOTICES, EVALUATION and EXIT on screen inside both screens; the port hides them | OPEN | `OperationsPanel.cs:117` to `125` (CONFIRMED) | UI KIT |
| The evaluation screen's prompts: the original's PRESS SPACEBAR FOR NEXT EVALUATION, [ QUIT ] and PRESS SPACEBAR TO CONTINUE; the port's More and Quit buttons | OPEN, design choice (PR 263) | `OperationsPanel.cs:178` to `188` (CONFIRMED) | UI KIT |
| The original books a supplemental evaluation's fine in the ledger only when it is shown in Operations; the port booked it at once | FIXED (the supplemental fines PR) | CONFIRMED: the evaluation screen's WE78B calls WF045 (the lost terrain vehicle) and WF12C (the towing charges), which book the fine through WF02B as they show it; the port keeps the fine with the evaluation (`PD_General.AddSupplementalFine`) and books it when Operations shows it (`PlayerData.BookSupplementalFines`); the `starport-supplementalfine` and `distress` probe scenarios | GAMEPLAY |
| "YOU HAVE NOT FILED ANY NEW COLONY WORLD RECOMMENDATIONS", the original's variant with NEW | FIXED (the preflight check PR) | The count it tests is the number of recommendations filed (CAPTAIN WF37A adds one to it), and the line shows when no evaluation is left: the original deletes the colony evaluations as the ship leaves the Starport with them read (CHKFLIGHT-OV WF40A), where the port kept them for good. The port now deletes them then (`DockingBayPanel.Open`) and says NEW once a recommendation has been filed (`OperationsPanel.MakeEvaluationPages`); the `starport-preflight` probe scenario (CONFIRMED) | GAMEPLAY |
| The notices screen: NOTICES and the date in the header, the notice's date, then the text typed out a paragraph at a time, as the original's; the evaluation with none filed: "You have not filed any colony world recommendations" | NOT A DEFECT | `starport-screenpictures` `09_operations_notice.png`, `10_operations_notices_end.png`, `11_operations_evaluation.png` against starflt_009 to 011 (CONFIRMED). Typed out without a press, the notice ends with ***End of notices*** at the bottom of the window, as starflt_010 | GAMEPLAY |

## 19. Personnel

| Item | Status | Evidence | Owner |
|---|---|---|---|
| CREATE, PREVIOUS, NEXT, EXIT with no files; the original's CREATE, TRAIN, DELETE, EXIT always, with the files paged by the arrow keys | OPEN | `PersonnelPanel.cs:521` to `531`, picture (CONFIRMED) | UI KIT |
| DURABILITY and LRN RATE in the right column beside RACE in the original | OPEN | picture (CONFIRMED) | UI KIT |
| The empty file: the original shows FILE #, VITALITY %, RACE, DURABILITY and LRN RATE with no values; the port's "No Personnel Files Found" | OPEN | `PersonnelPanel.cs:487`, picture (CONFIRMED) | UI KIT |
| AVG. HT., AVG. WT., LRN RATE as Average Height, Average Weight, Learn Rate | OPEN | picture (CONFIRMED) | UI KIT |
| Training. Original: "BALANCE: 11700 COST: 300 MU/SESSION / HOW MANY TRAINING SESSIONS (0-23)?" (starflt_040). Port: one session a press | FIXED (the training sessions PR) for the count: the fire button on a skill asks "How many training sessions (0-N)?", N the fewer of the sessions left to the skill's maximum and those the balance pays for (up to 100,000 M.U.), as `PERSONNEL-OV.c` WEE65; the stick sets the number (right and left one more and one fewer, up the most, down none) where the original takes typed digits, and the fire button trains them all at once (WF2CD); the balance and cost lines stay the UI kit's | `PersonnelPanel.SetSessionCount`, `TrainSessions`; the `starport-training` probe scenario (CONFIRMED) | GAMEPLAY (the count), UI KIT (the text) |
| Choosing a race: the race's skills, durability, learning rate, type, height and weight as the original's (a human: 50, 30, 30, 30, 30, 6, 9, Mammalian, 2.0 M, 80 KG) | NOT A DEFECT | `starport-screenpictures` `12_personnel_race.png` against starflt_026 to 030 (CONFIRMED) | GAMEPLAY |
| The prompts of a new file: the original keeps FILE # and VITALITY over the race and writes SELECT RACE <-> AND PRESS SPACEBAR and TYPE THIS CREWMEMBER'S NAME at the file's name; the port shows a NEW PERSONNEL FILE heading, arrows with Select and Cancel, and the name in a dialog over the file with the original's words | OPEN | `starport-screenpictures` `12_personnel_race.png`, `13_personnel_name.png`, `14_personnel_file.png` against starflt_026, starflt_031, starflt_032 (CONFIRMED) | UI KIT |
| Training: "How many training sessions (0-23)?" for a new human, the original's (0-23) for the same file; the balance line reads "Bank balance: 1,000,000 M.U. / Cost: 300 M.U. per session" for the original's BALANCE: 12000 COST: 300 MU/SESSION, and the original's SELECT SKILL <-> AND PRESS SPACEBAR is not written | NOT A DEFECT (the numbers), OPEN (the text) | `starport-screenpictures` `15_personnel_train.png`, `16_personnel_sessions.png` against starflt_034 to 036 (CONFIRMED) | GAMEPLAY (the numbers), UI KIT (the text) |
| Deleting: "Delete this crewmember?" with Yes before No; the original asks DELETE THIS CREWMEMBER? <-N Y->, No first, as the port's flight console does | OPEN, low priority | `starport-screenpictures` `17_personnel_delete.png` against starflt_033, starflt_038 (CONFIRMED) | UI KIT |

## 20. Crew Assignment

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The original always shows the personnel file under the list (FILE #, VITALITY, SKILLS, RACE, DURABILITY, LRN RATE); the port hides it without a crew, and with one shows the name, the vitality and the skills only | OPEN | `CrewAssignmentPanel.cs:236`, `405`, `413` to `423` (CONFIRMED with no crew, and with one in `starport-screenpictures` `18_crew_assignment_file.png` and `19_crew_assignment_assign.png` against starflt_041) | UI KIT |
| A [Not Assigned] column the original does not have | OPEN | `CrewAssignmentPanel.cs:313` (CONFIRMED) | UI KIT |
| ASSIGN and EXIT buttons for the original's PAGE THROUGH FILES / PRESS SPACEBAR TO ASSIGN CREWMEMBER | OPEN | picture (CONFIRMED) | UI KIT |
| With a crew member on file the menu shows the roles and [Not Assigned] only, and the file appears once Assign is pressed | NOT A DEFECT beyond the first item of this section | `starport-screenpictures` `18_crew_assignment_file.png`, `19_crew_assignment_assign.png` against starflt_041 (CONFIRMED) | UI KIT |

## 21. Bank

| Item | Status | Evidence | Owner |
|---|---|---|---|
| **The bank paid no interest.** The screen says INTEREST RATE: 12%, and the original's ledger has INTEREST rows (starflt_060). The original (`BANK-OV.c`, INT%) pays 12% a year of the balance for the days since it last paid (the balance times 12 over 100, then times the days over 300, each rounded down), when the crew goes aboard at the Starport (`PORTMENU-OV.c`, WEBA2) and on a new colony recommendation (`CAPTAIN.c`, which then starts the count again) | FIXED (the bank interest PR) | `PlayerData.PayInterest`, `PD_Bank.GetInterestDue`; the `starport-interest` probe scenario (CONFIRMED) | GAMEPLAY |
| A colony recommendation's bonus or fine. The original books it in the ledger when the planet is recommended (`CAPTAIN.c`, WF292: T+BALANCE, then TRANSACT as BONUS or FINE, after the interest); the port paid it when the ship docked, with Interstel's evaluation (roadmap 1.3, PR 142) | FIXED (the colony booking PR): the bonus or the fine is booked when the planet is recommended, after the interest, as the original's WF37A; Interstel's evaluation is still read at the Starport. The amount (WE096 times 1000, at least 100) is the fine of WF348 for an unsuitable planet, as the port already had it, and the bonus of Interstel's list for a suitable one (the owner's ruling of 2026-10-13: "Keep the original's timing for the colony bonus") | `PlayerData.BookColonyRecommendation`; the `colonies` probe scenario (CONFIRMED) | GAMEPLAY |
| The ledger's descriptions. The original has seven: FINE, SHIP CONFIGURATION, TRADE DEPOT, BONUS, PERSONNEL, INTEREST, GAME PURCHASE. The port wrote "Lifeform data", "Mission completed" and "Colony bonus" (the original's BONUS) and "Colony fine", "Towing charges" and "Terrain vehicle replaced" (the original's FINE) | FIXED (the ledger names PR): Bonus and Fine as the original books them (the win in DPART-OV, a colony evaluation in CAPTAIN, the supplemental evaluations in OP-OV), and "Ship configuration" in sentence case; "Lifeform data" keeps its name, since the shipped game has no such payment (the 1984 notes, ruling D3) | `PlayerData.cs`, `DistressYesButton.cs`, `DockingBay.cs`; `data.h` BANKTRANS_STRINGS; the `colonies`, `distress`, `win` and `lava` probe checks (CONFIRMED) | GAMEPLAY |
| AMT. as AMOUNT; dot leaders after the description ("GAME PURCHASE......") | OPEN, low priority | picture (CONFIRMED) | UI KIT |
| The balance line: the original's "BALANCE......12000" and "( INTEREST RATE: 12% )", the port's "Your current balance is 1,000,000 M.U." | OPEN | `BankPanel.cs:56` (CONFIRMED); the 1,000,000 itself is the Editor's starting balance by design (`PD_Bank.GetStartingBalance`) | UI KIT |
| PRESS SPACEBAR TO EXIT as an EXIT button | OPEN, low priority | picture (CONFIRMED) | UI KIT |
| A ledger longer than its window shows its latest rows, the newest just above the balance, as the original's (starflt_046, starflt_053, starflt_060), but the top row is cut in half under the headings | OPEN, low priority | `BankPanel.cs:68` to `73`; `starport-screenpictures` `20_bank_ledger.png` (CONFIRMED) | UI KIT |

## 22. Ship Configuration

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The balance has no thousands separators here, while the Bank and Personnel have them; the Trade Depot has none either | OPEN | `ShipConfigurationPanel.cs:550`, `BankPanel.cs:56`, `PersonnelPanel.cs:707`, `TradeDepotPanel.cs:844` (CONFIRMED) | UI KIT |
| Naming: "Type ship name" for the original's "SHIP IS NAMED: ISS ... / TYPE NEW SHIP NAME: ISS" (starflt_077), in a dialog with an empty field and no ISS | OPEN | `starport-screenpictures` `25_ship_name.png` (CONFIRMED) | UI KIT |
| An equipped ship (16 cargo pods, engines, shields, missiles and lasers class 5, armor class 3) weighs 356 tons, as the original's starflt_076, but accelerated at 6 G where the original reads 7 G | FIXED (the acceleration PR) | The port had its own curve; the original's acceleration is the engines' class times 500 over the mass, in whole G (`SHIPGRPH-OV.c` (.ACC)), whose mass formula (.MASS) is the port's; now `PD_PlayerShip.RecalculateAcceleration`: 7 G for this ship; the `acceleration` probe scenario (CONFIRMED) | GAMEPLAY |
| The buy and sell lists: the parts with the cargo pod's price (500), the classes with their prices, the installed equipment beside them; the original has dot leaders after the parts and classes | OPEN, low priority | `starport-screenpictures` `22_ship_buy.png`, `23_ship_buy_class.png`, `24_ship_sell.png` against starflt_019, starflt_076 (CONFIRMED) | UI KIT |

## 23. Trade Depot

| Item | Status | Evidence | Owner |
|---|---|---|---|
| "Transfer how many cubic meters? (0.0 to 20.5)" for the original's "TRANSFER HOW MANY CUBIC METERS? / ENTER VOLUME (0-20.5)" | OPEN | `TradeDepotPanel.cs:584`; `starport-screenpictures` `27_trade_buy_amount.png` against starflt_052 (CONFIRMED) | UI KIT |
| The lists: one "Unit Value" heading where the buy list says UNIT COST, no FOR SALE or CARGO title, and "Starship hold is empty" as an error message where the original shows it inside the CARGO list | OPEN | the scene, `TradeDepotPanel.cs:763, 885`; `starport-screenpictures` `26_trade_buy.png`, `28_trade_sell.png` against starflt_097, starflt_098 (CONFIRMED for the headings and titles; the empty hold INFERRED, no picture) | UI KIT |
| The buy list's elements and artifacts, their prices and the hold's volumes are the original's (Endurium 1000, Flat Device 30000, Whining Orb 6000), and the sell list's (Endurium 20.0 at 1000) | NOT A DEFECT | `starport-screenpictures` `26_trade_buy.png`, `28_trade_sell.png` against starflt_097, starflt_098 (CONFIRMED) | GAMEPLAY |
| The buy list's elements were in the order of the game data (Aluminum, Cobalt, Endurium, Molybdenum, Promethium, Titanium); the original's order is Titanium, Promethium, Molybdenum, Cobalt, Aluminum, Endurium | FIXED (the buy list order PR) | `TradeDepotPanel.GetStarportElementOrder`; the `starport-buyorder` probe scenario (CONFIRMED) | GAMEPLAY |
| Analyze listed the Starport's artifacts for sale at their price (Flat Device 30000, Whining Orb 6000) under one ARTIFACTS heading and the ship's under a second; the original lists the ship's artifacts only, at what the Starport pays, under one heading | FIXED (the analysis list PR) | the Starport's own artifacts are on the Buy list only (`TradeDepotPanel.UpdateScreen`); the `starport-analyzelist` probe scenario (CONFIRMED) | GAMEPLAY |
| "Welcome to the Trade Depot!" with an exclamation mark the original does not have | OPEN, low priority | picture (CONFIRMED) | UI KIT |
| The typo "Insufficent funds" | OPEN | `TradeDepotPanel.cs:1020` (CONFIRMED) | UI KIT |

## 24. Pre-flight check (the Docking Bay panel)

| Item | Status | Evidence | Owner |
|---|---|---|---|
| "• Report to Ship Configuration to christen ship." with a bullet and a full stop, and an EXIT button, for the original's REPORT TO SHIP-CONFIGURATION TO CHRISTEN SHIP and PRESS SPACEBAR TO EXIT | OPEN, low priority | `DockingBayPanel.cs:59, 77` (CONFIRMED) | UI KIT |
| Three lines of the port's that no screenshot has ("At least one of your crew members is dead.", "...to purchase engines.", "Report to Trade Depot to purchase fuel.") | FIXED (the preflight check PR) | The original's check is CHKFLIGHT-OV ?CAN-LEAVE (CONFIRMED, code): REPORT TO OPERATIONS FOR EVALUATION while an evaluation waits unread (a colony evaluation, the lost terrain vehicle, the towing charges, the completion of the mission), REPORT TO CREW ASSIGNMENT for a missing or dead crew member (no line of its own for the dead), REPORT TO SHIP-CONFIGURATION TO CHRISTEN SHIP AND PURCHASE ENGINES as one line, and REPORT TO TRADE DEPOT TO PURCHASE FUEL. The port now says these (`DockingBayPanel.Open`, `PD_General.HasUnreadEvaluation`); the `starport-preflight` probe scenario | GAMEPLAY |

## 25. Screens the probe does not render yet

| Screen | Status |
|---|---|
| Intro (credits, title, Interstel Police warning) | no picture, though PR 268 redid the intro |
| Load and save | no picture of `SaveGamePanel` |
| Each department's menu with its officer label | the bridge, the comm menu, Medical, Communications and the Captain's in the docking bay (`bridgepictures`), Engineering's in `screenpictures`; not Science's and Navigation's |
| The flight scene's docking bay and the launch | `bridgepictures` since 2026-10-13 |
| Sensors and Analysis of a planet and of an alien ship | rendered since PR 234; Log Planet and its question are not |
| Hyperspace, the Starmap, an encounter, the comm link, combat, debris | hyperspace (placed, not jumped), the encounter and the comm menu; no Starmap, no combat on a whole screen |
| The descent, the landed ship, the terrain vehicle's sub-screens, a lifeform | the landing map and the terrain vehicle; the landed ship, Look, Scan, Cargo, Weapon, Map and a lifeform beside the vehicle in `surfacepictures`; the lifeform models in `lifeformpictures`; not the descent |
| The Black Egg's countdown, and the Starport rooms' sub-screens | the rooms' sub-screens in `starport-screenpictures` since 2026-10-13; not the Black Egg's countdown |

---

## Step 2: the work that is left

One small PR per item, gameplay first. Each documented in the screenshots or the decompiled original, so D1 applies; a probe check that fails on the code before goes with each.

1. **The DAMAGE display and Repair's choice of system** (section 13), from `ENGINEER.c`: done (PRs 281, 286; the choice by the owner's ruling of 2026-10-12, which replaced the worst first).
2. **The bank's interest** (section 21): done (PR 282). Left from it: the original books a recommendation's bonus or fine at once, which touches the colony rulings of roadmap 1.3.
3. **The notices screen** (section 18): done (PRs 288, 299).
4. **Combat messages and wounds** (section 12), from `DAMAGE-OV.c`: done (PR 284).
5. **MOTION DETECTED AT X: Y:** (section 11), from `HYPERMSG-OV.c`: parked.
6. **The shields and weapons messages** (section 2), from `NAVIGATR.c`: done (PR 285).
7. Smaller: all done (the temperature range and the analysis heading's colon, PR 290; the orbit lines, PR 287; the crosshair's start, PR 292; Look, PR 291; the weather line, PR 294; the ledger's names, PR 289; the training sessions, PR 301; the vehicle's distance, PR 300).
8. **The probe's pictures**: the missing screens of section 25, the landing map after the crosshair is placed, the encounter with the aliens in view, so that the next comparison can see them.

The UI kit items are left to the UI kit's work. Two items are the owner's call: the security code (D4) and the Ship's Log as a menu.
