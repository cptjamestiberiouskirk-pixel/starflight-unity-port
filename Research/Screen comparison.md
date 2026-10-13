# Screen comparison

Roadmap Phase 7, "compare every screen with `Research/Screenshots/`". The comparison was made on 2026-10-11 (commit 68d89f61), by the session that then fixed most of what it found. Every item was checked again against `origin/master` at 64bf172d on 2026-10-12, with new probe pictures of the port's screens. This document records each item's status today and the work that is left. Nothing in it changes the game.

The messages the player sees are covered by `Research/STRINFO message audit.md`. This document covers what is on the screen: displays, labels, layout and the flow of a screen.

## Sources

- **The original:** `Research/Screenshots/` (259 EGA pictures in 19 folders). Where the screenshots stop, the decompiled original ([s-macke/starflight-reverse](https://github.com/s-macke/starflight-reverse), `starflt1-out/overlays/`) says what a screen does: `ENGINEER.c` (Damage and Repair), `NAVIGATR.c` (shields and weapons), `ANALYZE-OV.c` (Analysis), `HYPERMSG-OV.c` (hyperspace messages), `OP-OV.c` (Operations), `DAMAGE-OV.c` (combat damage), `LAUNCH-OV.c` (launch), `CAPTAIN.c` (the Captain's menu) and `data.h` (the bank's transaction names).
- **The port:** pictures rendered by the headless probe with a graphics device (`DevTools/HeadlessProbe/probe.ps1 -Scenario screenpictures -Graphics` and `starport-screenpictures`), 12 of the flight screens and 8 of the Starport's rooms, into `Logs/screen-pictures/` (not committed). Where a screen has no picture, the port side comes from the code.

Not reported: art style, resolution, colours, fonts and 3D models.

## Labels

- **Status:** FIXED (by the PR named), OPEN, PARTLY (what is left is said), NOT A DEFECT, OWNER'S CALL, PROBE (a gap or an artifact of the probe's pictures, not of the game).
- **Owner:** GAMEPLAY (what the game logic does or writes) or UI KIT (the labels, layout and look of the console and the panels: the UI kit's work). An item marked UI KIT is left to that work and only recorded here.
- **Evidence:** CONFIRMED (seen in a picture or in the code on 2026-10-12), INFERRED (one link is not verified; it is named), HYPOTHESIZED.

## Summary

| | Items | FIXED | OPEN or PARTLY | Not a defect or owner's call |
|---|---|---|---|---|
| GAMEPLAY (with the items shared with the UI kit) | 52 | 32 | 16 | 4 |
| UI KIT | 41 | 0 | 40 | 1 |
| The probe's pictures | 17 | 3 (PR 234) | 14 gaps | |

Since 2026-10-11 the gameplay side has been fixed in PRs 215, 216, 218, 219, 220, 221, 222, 223, 224, 225, 226, 231, 242, 254 and 263; the probe pictures in PR 234. The UI kit items are all still open: the UI kit's PRs so far (213, 260, 268) redid the docking bay, the in-orbit view and the intro.

The open gameplay items, most visible first:

1. **The typing of a notice** (section 18): the original types a notice line by line, with an abort (the notices screen's header, its end and "NO NEW NOTICES" are done, as are the DAMAGE display, Repair's choice of system and the interest).
2. **The bank books a recommendation's bonus or fine at once in the original** (section 21), where the port pays it on docking.
3. **Combat messages and wounds** (section 12): the original names the weapon that hits ("MISSILE CLASS 2") and says how the crew is wounded; in the port combat never wounds the crew.
4. **"MOTION DETECTED AT X: Y:"** (section 11), the shields and weapons messages (section 2), and several smaller ones.

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
| The auxiliary view shows Status in the probe's orbit picture | PROBE | the probe left it on | |

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
| The probe's landing map picture reads "LAT 180W LONG 90S" | PROBE | the probe opens Select Site without moving the crosshair, so the scene's placeholder texts show (which are still in the old swapped form) | |

## 9. Planetside (landed)

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The "HULL CHECK COMPLETE" line was missing | FIXED | PR 231 (CONFIRMED) | GAMEPLAY |
| "Stand by, scanning planet..." for "STANDBY...SCANNING PLANET." | FIXED | PR 231 (CONFIRMED) | GAMEPLAY |
| No picture of the landed ship | PROBE | the probe goes from orbit straight to the terrain vehicle | |

## 10. Terrain vehicle

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The date carried the hour | FIXED | PR 218; picture "01-01-4620" (CONFIRMED) | GAMEPLAY |
| The coordinates' order, with no "x" | FIXED | PR 215; picture "1S x 0E" (CONFIRMED) | GAMEPLAY |
| The bearing to the ship abbreviated | FIXED | PR 218; picture "North" (CONFIRMED) | GAMEPLAY |
| "SHIP 14 KM. North" with the ship's model just above the vehicle; the original shows "1KM." with the ship beside it (starflt_179) | OPEN | `TerrainVehicleDisplay.cs:124` (CONFIRMED, picture); that the scale is wrong is INFERRED | GAMEPLAY |
| The weather is the first line on the surface in the original ("IT IS CLEAR.", alone in the window); the port wrote it as a sixth line under the disembark lines | FIXED (the weather line PR): the text window is cleared before the first sky | `PlanetWeather.Begin`; starflt_179; the `disembarklines` probe check (CONFIRMED) | GAMEPLAY |
| Look: the port writes Surface, Weather, Sky, Atmosphere and Gravity; the original writes SKY, TEMPERATURE, GRAVITY, ATMO, HYDRO and TERRAIN (starflt_180): no temperature and no hydrosphere in the port | FIXED (the Look fields PR; the temperature is the planet's, the port has no regions) | `LookButton.cs:40` to `46` (INFERRED: code, no picture) | GAMEPLAY |
| No CREW VITALITY box with dot leaders | OPEN | picture (CONFIRMED) | UI KIT |
| MAP for MAPS, EFFICIENCY for EFF. | OPEN | `MapButton.cs:10`, `Spaceflight.unity:65137` (CONFIRMED) | UI KIT |
| A menu of six on the console; the original's is a bar of seven across the bottom with ICONS, which opens two ICON KEY screens | OPEN | `ButtonController.cs:127` (CONFIRMED) | UI KIT |
| Weapon: Fire, Switch, Back; the original's STUNNER / LASER / CHOOSE WEAPON / RETURN= AIM | OPEN | `ButtonController.cs:129` (INFERRED: code) | UI KIT |
| No pictures of Maps, Look, Scan, Cargo, Weapon or a lifeform on screen | PROBE | | |

## 11. Hyperspace

| Item | Status | Evidence | Owner |
|---|---|---|---|
| No picture of hyperspace | FIXED | PR 234 (CONFIRMED) | |
| The coordinates were not written again on entering hyperspace | FIXED | PR 221 (CONFIRMED) | GAMEPLAY |
| "MOTION DETECTED AT / X: Y:". The port clears the window and writes "Motion detected." with no numbers. The original prints MOTION DETECTED AT once a pass and then each moving group's step toward the ship (X and Y from -1 to 1), when the science officer's skill is above 150, and keeps the earlier lines | OPEN | `Radar.cs:200`, `201`; `HYPERMSG-OV.c`, TRAK-E and WF28B (CONFIRMED, code); that the skill is the science officer's is INFERRED from the record offsets | GAMEPLAY |

## 12. Encounter and comm link

| Item | Status | Evidence | Owner |
|---|---|---|---|
| "LEAVING ENCOUNTER... / ...ENCOUNTER TERMINATED" | FIXED | PR 242 (CONFIRMED, code) | GAMEPLAY |
| "THEY'VE ARMED THEIR WEAPONS" and the other shield and weapon news | FIXED | PR 224 (CONFIRMED, code) | GAMEPLAY |
| Combat messages. Original: MISSILE CLASS n or PLASMA BOLT, and "<name> IS SLIGHTLY / MODERATELY / HEAVILY / CRITICALLY WOUNDED" or KILLED. Port: "Incoming torpedo!", "Plasma bolt incoming!", "Hull takes N damage!", and combat never wounds the crew (`InjureCrewMember` is called only by lifeforms and the weather) | FIXED (the combat wounds PR: the original's words, and a hit on the crew wounds them) | `CombatController.cs:642, 878` to `935`; `DAMAGE-OV.c:1160` to `1243`, `1872` (CONFIRMED, code) | GAMEPLAY |
| No alien ship and no scan bracket in the encounter picture, and the player's ship is a speck | FIXED (the encounter view PR) | Not the probe: its `EnterEncounter` does what `SpaceflightController.BeginEncounter` does. Measured with a graphics run on 2026-10-12: the encounter camera climbed to 7,000 to 11,000 units above the ship (perspective, 30 degrees) to frame alien ships that start about 3,700 units away, off the left edge at 4 s and in view from about 6 s; at that height a 90-unit Spemin scout covered under 2% of the screen and the player's ship was a speck. The original draws the ships as icons at a fixed scale, about a sixteenth of the view's height. Claude's call by the owner's leave (2026-10-12), both parts: the camera frames the ships out to the aliens' fire range and no further (at most about 4,450 units), and the ships are drawn twice as large in an encounter, about a twentieth of the view at the camera's highest (CONFIRMED, `encounterviewscale` probe scenario and its pictures) | GAMEPLAY or UI KIT |
| The comm link's main view shows coloured static where the alien's portrait should be | OWNER'S CALL | Not a capture artifact: the portrait is there (seen at 8 s, the Spemin under the static), and the static is the port's own `CommScreen` effect (coloured rays, waves, low resolution), stronger the lower the communications officer's skill (the probe's crew has little). The original shows the portrait clean and garbles only the words. Whether the port keeps its reception effect is a look decision for the owner (CONFIRMED, pictures at 3 s and 8 s, `CommScreen.cs:170`) | GAMEPLAY or art |
| The encounter pictures show the coordinates 118 96 in Arth's system | PROBE | the probe enters Arth's system without moving the hyperspace coordinates (INFERRED) | |

## 13. Engineering (Damage and Repair)

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The DAMAGE display. Original (`ENGINEER.c`): Damage clears the auxiliary view and draws DAMAGE with a 40-pixel bar for each of HULL, ENGINES, SENSORS, COMM., SHIELD, MISSILE, LASER, green for what works and red for the rest, and writes no text. Port: there was no damage display; Damage wrote a "Damage Report:" text | FIXED (the DAMAGE display PR) | `DamageDisplay`, shown by Damage and at the start of Repair; the picture `13_damage.png` of `screenpictures` (CONFIRMED) | GAMEPLAY and UI KIT |
| Repair. Original: shows the same display and lets the player scroll the seven rows; the text window gives each one ("ENGINES: SLIGHTLY DAMAGED", EFFICIENCY, CURRENT FUNCTIONALITY, ESTIMATED TIME FOR COMPLETE REPAIRS, CURRENTLY UNDER REPAIR), and the fire button chooses what to repair, with NO REPAIRS NEEDED, DAMAGED BEYOND REPAIR, "CEASE REPAIRS ON X AND BEGIN REPAIRS ON Y? [N Y]" and BEGINNING, CONTINUING or CEASING REPAIRS ON X. Port: Repair picked the system itself, the worst first, by the owner's ruling of 2026-10-11 for roadmap 1.2, made when no shipped source had the mechanics; on 2026-10-12 the owner ruled that it follows the original | FIXED (the Repair choice PR) | `RepairButton.cs`, the DAMAGE display's cursor; the `repairchoice` probe scenario and the picture `14_repair.png` (CONFIRMED) | GAMEPLAY |
| No pictures | PROBE | | |

## 14. Medical (Examine and Treat)

| Item | Status | Evidence | Owner |
|---|---|---|---|
| No choice of crew member and no durability | FIXED | PR 225: "Who should I examine?", "...is not wounded, and has a vitality of N and a durability of N." (CONFIRMED, code) | GAMEPLAY |
| No pictures | PROBE | | |

## 15. Communications (Hail and Distress)

| Item | Status | Evidence | Owner |
|---|---|---|---|
| Distress refused in the docking bay | FIXED | PR 222: "Transmit emergency distress call?" with No and Yes, "We're already home!" (CONFIRMED, code) | GAMEPLAY |
| No pictures of Hail and Distress | PROBE | Hail's choice of posture matches the original (INFERRED, code) | |

## 16. Docking bay and the Captain's menu

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The CAUTION / PRESSURIZED AREA sign | NOT A DEFECT | it is on the docking bay doors' texture (PR 213) (CONFIRMED) | UI KIT |
| CLOSING DOCKING BAY DOORS on arrival | FIXED | PR 219 (CONFIRMED, code) | GAMEPLAY |
| "Opening docking bay doors..." | NOT A DEFECT | the original's own line (`LAUNCH-OV.c`) (CONFIRMED) | GAMEPLAY |
| "Confirm launch?" for READY TO LAUNCH / CONFIRM | FIXED | PR 223 (CONFIRMED, code) | GAMEPLAY |
| The launch's security code | OWNER'S CALL | the copy protection, decision D4 in `ROADMAP.md` | GAMEPLAY |
| The Ship's Log is a menu (notices, comms, messages); the original's is a free-text notepad with an EXIT tab | OWNER'S CALL | the port's Messages log is roadmap item 3.3 (INFERRED: a deliberate design) | GAMEPLAY |
| No picture of the flight scene's docking bay or of the launch | PROBE | | |

## 17. Starport concourse

The doors are where the original's are (Operations upper left, Personnel top, Crew Assignment upper right, Bank lower right, Ship Configuration bottom, Trade Depot lower left, the Docking Bay in the middle; CONFIRMED, scene and picture). No difference.

## 18. Operations

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The evaluations one to a page, the supplemental ones once, END OF EVALUATIONS, and each evaluation's heading | FIXED | PRs 254, 263 (CONFIRMED, code) | GAMEPLAY |
| The notices' header. Original: "NOTICES 01-01-4620". Port: "Today is" and the date | FIXED (the notices screen PR): the date beside the scene's NOTICES heading | `OperationsPanel.ShowNotices`; `OP-OV.c` WE735; the `starport-notices` probe scenario (CONFIRMED) | GAMEPLAY |
| "***END OF NOTICES***" after the last notice | FIXED (the notices screen PR) | after the newest notice that is out; `OP-OV.c:902`; `starport-notices` (CONFIRMED) | GAMEPLAY |
| With no unread notice the original says NO NEW NOTICES and asks REPEAT LAST NOTICE? [N Y], and on Y "***REPEATING LAST NOTICE***"; the port showed the last notice read again without asking | FIXED (the notices screen PR): More and Quit answer Yes and No meanwhile | `OperationsPanel.AskToRepeat`; `OP-OV.c:1001` to `1020`; `starport-notices` (CONFIRMED) | GAMEPLAY |
| The original types a notice line by line with a pause (the spacebar skips it), with PRESS SPACEBAR TO CONTINUE / PRESS [] TO ABORT CURRENT NOTICE and ***NOTICE ABORTED***; the port shows one paragraph a press of More, with no abort | OPEN | `OperationsPanel.cs:441` to `476`; `OP-OV.c:536, 543, 879` (CONFIRMED, code) | GAMEPLAY (the flow), UI KIT (the prompts) |
| After a notice the original shows "<- NEXT MESSAGE  PREVIOUS MESSAGE ->" and "PRESS SPACEBAR TO REPLAY OR [ QUIT ]"; the port has More, Previous, Next and Quit buttons and no Replay | OPEN | `OperationsPanel.cs:20` to `23`; `OP-OV.c:512, 519` (CONFIRMED) | UI KIT |
| The original keeps NOTICES, EVALUATION and EXIT on screen inside both screens; the port hides them | OPEN | `OperationsPanel.cs:117` to `125` (CONFIRMED) | UI KIT |
| The evaluation screen's prompts: the original's PRESS SPACEBAR FOR NEXT EVALUATION, [ QUIT ] and PRESS SPACEBAR TO CONTINUE; the port's More and Quit buttons | OPEN, design choice (PR 263) | `OperationsPanel.cs:178` to `188` (CONFIRMED) | UI KIT |
| The original books a supplemental evaluation's fine in the ledger only when it is shown in Operations; the port books it at once | OPEN, low priority | `DistressYesButton.cs:42`, `DockingBay.cs:82`; `OP-OV.c` WF02B (INFERRED: code) | GAMEPLAY |
| "YOU HAVE NOT FILED ANY NEW COLONY WORLD RECOMMENDATIONS", the original's variant with NEW | OPEN, low priority | `OP-OV.c:1119` to `1128`; what sets the count it tests is not known (HYPOTHESIZED) | GAMEPLAY |
| No pictures of the notices and evaluation screens | PROBE | `starport-screenpictures` takes each room's first screen only | |

## 19. Personnel

| Item | Status | Evidence | Owner |
|---|---|---|---|
| CREATE, PREVIOUS, NEXT, EXIT with no files; the original's CREATE, TRAIN, DELETE, EXIT always, with the files paged by the arrow keys | OPEN | `PersonnelPanel.cs:521` to `531`, picture (CONFIRMED) | UI KIT |
| DURABILITY and LRN RATE in the right column beside RACE in the original | OPEN | picture (CONFIRMED) | UI KIT |
| The empty file: the original shows FILE #, VITALITY %, RACE, DURABILITY and LRN RATE with no values; the port's "No Personnel Files Found" | OPEN | `PersonnelPanel.cs:487`, picture (CONFIRMED) | UI KIT |
| AVG. HT., AVG. WT., LRN RATE as Average Height, Average Weight, Learn Rate | OPEN | picture (CONFIRMED) | UI KIT |
| Training. Original: "BALANCE: 11700 COST: 300 MU/SESSION / HOW MANY TRAINING SESSIONS (0-23)?" (starflt_040). Port: one session a press | FIXED (the training sessions PR) for the count: the fire button on a skill asks "How many training sessions (0-N)?", N the fewer of the sessions left to the skill's maximum and those the balance pays for (up to 100,000 M.U.), as `PERSONNEL-OV.c` WEE65; the stick sets the number (right and left one more and one fewer, up the most, down none) where the original takes typed digits, and the fire button trains them all at once (WF2CD); the balance and cost lines stay the UI kit's | `PersonnelPanel.SetSessionCount`, `TrainSessions`; the `starport-training` probe scenario (CONFIRMED) | GAMEPLAY (the count), UI KIT (the text) |
| No pictures of choosing a race, naming, training or deleting | PROBE | | |

## 20. Crew Assignment

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The original always shows the personnel file under the list (FILE #, VITALITY, SKILLS, RACE, DURABILITY, LRN RATE); the port hides it without a crew, and with one shows the name, the vitality and the skills only | OPEN | `CrewAssignmentPanel.cs:236`, `405`, `413` to `423` (CONFIRMED with no crew; INFERRED with one) | UI KIT |
| A [Not Assigned] column the original does not have | OPEN | `CrewAssignmentPanel.cs:313` (CONFIRMED) | UI KIT |
| ASSIGN and EXIT buttons for the original's PAGE THROUGH FILES / PRESS SPACEBAR TO ASSIGN CREWMEMBER | OPEN | picture (CONFIRMED) | UI KIT |
| No picture with a crew on file | PROBE | | |

## 21. Bank

| Item | Status | Evidence | Owner |
|---|---|---|---|
| **The bank paid no interest.** The screen says INTEREST RATE: 12%, and the original's ledger has INTEREST rows (starflt_060). The original (`BANK-OV.c`, INT%) pays 12% a year of the balance for the days since it last paid (the balance times 12 over 100, then times the days over 300, each rounded down), when the crew goes aboard at the Starport (`PORTMENU-OV.c`, WEBA2) and on a new colony recommendation (`CAPTAIN.c`, which then starts the count again) | FIXED (the bank interest PR) | `PlayerData.PayInterest`, `PD_Bank.GetInterestDue`; the `starport-interest` probe scenario (CONFIRMED) | GAMEPLAY |
| A colony recommendation's bonus or fine. The original books it in the ledger when the planet is recommended (`CAPTAIN.c`, WF292: T+BALANCE, then TRANSACT as BONUS or FINE, after the interest); the port pays it when the ship docks, with Interstel's evaluation (roadmap 1.3, PR 142) | OPEN | `PlayerData.cs` (the colony evaluations at docking); which planet field gives the original's amount (WE096, times 1000, at least 100) is not worked out (INFERRED) | GAMEPLAY |
| The ledger's descriptions. The original has seven: FINE, SHIP CONFIGURATION, TRADE DEPOT, BONUS, PERSONNEL, INTEREST, GAME PURCHASE. The port wrote "Lifeform data", "Mission completed" and "Colony bonus" (the original's BONUS) and "Colony fine", "Towing charges" and "Terrain vehicle replaced" (the original's FINE) | FIXED (the ledger names PR): Bonus and Fine as the original books them (the win in DPART-OV, a colony evaluation in CAPTAIN, the supplemental evaluations in OP-OV), and "Ship configuration" in sentence case; "Lifeform data" keeps its name, since the shipped game has no such payment (the 1984 notes, ruling D3) | `PlayerData.cs`, `DistressYesButton.cs`, `DockingBay.cs`; `data.h` BANKTRANS_STRINGS; the `colonies`, `distress`, `win` and `lava` probe checks (CONFIRMED) | GAMEPLAY |
| AMT. as AMOUNT; dot leaders after the description ("GAME PURCHASE......") | OPEN, low priority | picture (CONFIRMED) | UI KIT |
| The balance line: the original's "BALANCE......12000" and "( INTEREST RATE: 12% )", the port's "Your current balance is 1,000,000 M.U." | OPEN | `BankPanel.cs:56` (CONFIRMED); the 1,000,000 itself is the Editor's starting balance by design (`PD_Bank.GetStartingBalance`) | UI KIT |
| PRESS SPACEBAR TO EXIT as an EXIT button | OPEN, low priority | picture (CONFIRMED) | UI KIT |
| No picture of a full ledger | PROBE | | |

## 22. Ship Configuration

| Item | Status | Evidence | Owner |
|---|---|---|---|
| The balance has no thousands separators here, while the Bank and Personnel have them; the Trade Depot has none either | OPEN | `ShipConfigurationPanel.cs:550`, `BankPanel.cs:56`, `PersonnelPanel.cs:707`, `TradeDepotPanel.cs:844` (CONFIRMED) | UI KIT |
| Naming: "Type ship name" for the original's "SHIP IS NAMED: ISS ... / TYPE NEW SHIP NAME: ISS" (starflt_077) | OPEN | scene (INFERRED: not seen in a picture) | UI KIT |
| No pictures of an equipped ship, naming, or the buy and sell lists | PROBE | | |

## 23. Trade Depot

| Item | Status | Evidence | Owner |
|---|---|---|---|
| "Transfer how many cubic meters? (0.0 to 20.5)" for the original's "TRANSFER HOW MANY CUBIC METERS? / ENTER VOLUME (0-20.5)" | OPEN | `TradeDepotPanel.cs:584` (INFERRED: code) | UI KIT |
| The lists: one "Unit Value" heading where the buy list says UNIT COST, no FOR SALE or CARGO title, and "Starship hold is empty" as an error message where the original shows it inside the CARGO list | OPEN | the scene, `TradeDepotPanel.cs:763, 885` (INFERRED: no picture) | UI KIT |
| "Welcome to the Trade Depot!" with an exclamation mark the original does not have | OPEN, low priority | picture (CONFIRMED) | UI KIT |
| The typo "Insufficent funds" | OPEN | `TradeDepotPanel.cs:1020` (CONFIRMED) | UI KIT |

## 24. Pre-flight check (the Docking Bay panel)

| Item | Status | Evidence | Owner |
|---|---|---|---|
| "• Report to Ship Configuration to christen ship." with a bullet and a full stop, and an EXIT button, for the original's REPORT TO SHIP-CONFIGURATION TO CHRISTEN SHIP and PRESS SPACEBAR TO EXIT | OPEN, low priority | `DockingBayPanel.cs:59, 77` (CONFIRMED) | UI KIT |
| Three lines of the port's that no screenshot has ("At least one of your crew members is dead.", "...to purchase engines.", "Report to Trade Depot to purchase fuel.") | OPEN, not checked | `DockingBayPanel.cs:68, 84, 91`; the original's code for this screen was not read (HYPOTHESIZED) | GAMEPLAY |

## 25. Screens the probe does not render yet

| Screen | Status |
|---|---|
| Intro (credits, title, Interstel Police warning) | no picture, though PR 268 redid the intro |
| Load and save | no picture of `SaveGamePanel` |
| Each department's menu with its officer label | the bridge and the comm menu only |
| The flight scene's docking bay and the launch | no picture |
| Sensors and Analysis of a planet and of an alien ship | rendered since PR 234; Log Planet and its question are not |
| Hyperspace, the Starmap, an encounter, the comm link, combat, debris | hyperspace (placed, not jumped), the encounter and the comm menu; no Starmap, no combat on a whole screen |
| The descent, the landed ship, the terrain vehicle's sub-screens, a lifeform | the landing map and the terrain vehicle; the lifeform models in `lifeformpictures`; the rest not |
| The Black Egg's countdown, and the Starport rooms' sub-screens | no picture: `starport-screenpictures` takes each room's first screen |

---

## Step 2: the work that is left

One small PR per item, gameplay first. Each documented in the screenshots or the decompiled original, so D1 applies; a probe check that fails on the code before goes with each.

1. **The DAMAGE display and Repair's choice of system** (section 13), from `ENGINEER.c`: done (the choice by the owner's ruling of 2026-10-12, which replaced the worst first).
2. **The bank's interest** (section 21): done. Left from it: the original books a recommendation's bonus or fine at once, which touches the colony rulings of roadmap 1.3.
3. **The notices screen** (section 18): the header, END OF NOTICES, NO NEW NOTICES and REPEAT LAST NOTICE are done; the typing and abort are left.
4. **Combat messages and wounds** (section 12), from `DAMAGE-OV.c`.
5. **MOTION DETECTED AT X: Y:** (section 11), from `HYPERMSG-OV.c`.
6. **The shields and weapons messages** (section 2), from `NAVIGATR.c`.
7. Smaller: the temperature range (7), the analysis heading's colon (7), the orbit lines scrolling on (5), the crosshair's start (8), Look's temperature and hydrosphere (10), the weather as the first line on the surface (10), Personnel's number of training sessions (19), the terrain vehicle's distance to the ship (10, once the scale is checked).
8. **The probe's pictures**: the missing screens of section 25, the landing map after the crosshair is placed, the encounter with the aliens in view, so that the next comparison can see them.

The UI kit items are left to the UI kit's work. Two items are the owner's call: the security code (D4) and the Ship's Log as a menu.
