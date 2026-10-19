# Roadmap to a complete Starflight

> **Spoiler warning:** this is a development document. It describes the game's story, its artifacts, where they are and how the game is won.

This roadmap took the Unity port of *Starflight* to parity with the 1986 original, and every item on it is finished: all 59 items of Phases 0 to 7 are done or decided, and version 1.0.0 is built on them. The goal from the start was a port that does everything the original game does before anything new or different is added. The port now does exactly that, from the Starport, flight and combat through the comm system and its moods, the ruins and artifacts and the lifeforms, to the full story and the win.

A second reading of the shipped game's own code (the decompiled overlays of s-macke/starflight-reverse) on 2026-10-18 found 39 more differences that the research sources of 2026-10-05 could not show. They are Phase 8, decided the same day (D9), and under way.

This document is that plan as it was carried out. Each item gives the original behaviour, the research source that documents it and how the port delivers it, usually with the PR that did the work. The decisions taken along the way are collected under [Decisions](#decisions), and the few points still open are listed under [Where the port stands](#where-the-port-stands).

The plan was first written on 2026-10-05 against `master` at `bf6a2910` and adopted as the plan of record on 2026-10-09. It remains the plan of record: pick parity work from it, and when a PR for an item merges, record its number in the item's row, in the same docs change that updates `CHANGELOG.md`. An item's description changes only when the sources or a decision change it.

## How to read this

- **Status** of a feature in the port: DONE, PARTIAL (with what is missing), STUB (a button that only prints text or plays a sound), MISSING.
- **Source** is where the original behaviour is documented:
  - STRINFO: `Research/Data/STRINFO.DOC`
  - xls: `Research/Data/Starflight 1 Data.xls`
  - Survey: `Research/Data/Starflight_1_Survey.xls`
  - notes: `Research/Notes/`
  - SS: `Research/Screenshots/`
  - Files under notes marked DRAFT are design notes from 1984 and 1985. They are not proof of what the game shipped with.
- Each numbered item is meant to be one PR with its own headless probe scenario, as the project's contribution rules require.
- **Decision needed** marks an item where the sources do not settle what the original did, so a design decision is required. Once it is made, the item says **Decided**, with the date.
- **Evidence.** The starting status came from reading the code on 2026-10-05, without running anything. Spot-checked by hand: the Starport repair, Distress, the terrain vehicle weapon, the Messages log, crew vitality and the game clock. Line numbers drift, so re-verify them before working on an item.

## Where the port stands

| Area | Status | Gaps closed |
|---|---|---|
| Starport: Personnel, Crew Assignment, Bank, Docking Bay | DONE | |
| Starport: Trade Depot | DONE | Endurium price changes (#134), specimens sold (#177) |
| Starport: Ship Configuration | DONE | Repair at a cost (#137) |
| Starport: Operations | DONE | colony evaluations, the win evaluation, fines (#120, #142) |
| Flight, hyperspace, fluxes, fuel | DONE | |
| Nebulae | DONE | the shields stay down inside one (#136) |
| Stellar flares | DONE | every star on its day, Arth's flare (#121, #122) |
| Starmap | DONE | fuel estimate (#141) |
| Game clock | DONE | runs in every Spaceflight location except the docking bay (#83) |
| Ship console: Captain | DONE | colony recommendations, the Messages log (#102, #142) |
| Ship console: Science | DONE | science skill (#161), salvage (#162), star stability (#160) |
| Ship console: Engineer, Doctor | DONE | repairs with minerals, damage per system (#145 to #147); the Doctor treats lifeform injuries |
| Ship console: Communications | DONE | Distress (#138) |
| Combat | DONE | vessel armor, shields and speed from the data (#140) |
| Comm system | DONE | moods, rivals, translation, homeworld warnings, surrender, transmissions (#150 to #159) |
| Alien trade, tribute, surrender | DONE | #152, #154 to #156 |
| Terrain vehicle: drive, map, mine, return | DONE | fuel by the ground under the vehicle, as the released game's MOVE-OV (replaced the slope rule of #187 on 2026-10-15) |
| Terrain vehicle: Look, Scan | DONE | lifeforms, minerals, ruins (#167, #181) |
| Ruins, artifacts, planet messages | DONE | Phase 3; random ruins (#164) |
| Weather and terrain hazards | DONE | lava (#165), slopes (#187), storms (the released game's rules, BEHAV-OV `?STORM`) |
| Lifeforms (stun, capture, sell, attack) | DONE | Phase 6, with Blender models (#167 to #185) |
| Crew injury and death | DONE | #170 |
| Artifact effects (15 special artifacts) | DONE | the Black Box, Hypercube and Ellipsoid have none beyond their roles (D5) |
| Main story and endgame | DONE | Phase 5 |

All 59 items of Phases 0 to 7 below are done or decided (2026-10-13). The play test of item 0.6 was closed on 2026-10-12, and the comparison of every screen with the original game was completed on 2026-10-13, when the UI kit items it raised were also fixed or decided (`Research/Screen comparison.md`). The headless probe plays all of its scenarios, though the port has not yet been played through to the win by hand. Beyond that, only a few details remain open, each noted at its item: the writing in Old Empire ruins, for which the sources have no texts (3.5), typing a notice on the notices screen line by line (Phase 7), and the pace of the aliens' talk, which has no single value to port (2.1).

## Decisions

These set how much of the roadmap could go ahead without a separate decision for each item.

- **D1. Fidelity rule. Decided 2026-10-09: yes.** Where the research sources document the original game's behaviour, the port follows it as documented, with no separate decision per item. A decision is needed only where the sources are silent or contradict each other (the items marked Decision needed). Behaviour found only in the DRAFT notes also needs a decision.
- **D2. Where recovered data goes. Decided 2026-10-09: a second data file**, `Assets/Resources/Starflight Recovered Data.json`, merged into the game data at load (first used by item 0.2). The options were:
  - (a) Add the records to `Starflight Game Data.json`, a file where every edit needs explicit confirmation.
  - (b) Put them in a second file next to it (for example `Starflight Story Data.json`), so the original values stay untouched in one file and the added records live in the other.

  Either way, no existing value changes.

Decided later:

- **D3. Lifeforms. Decided 2026-10-11: (a), rebuild from the notes, with procedural stand-in models made by script (as the ship models are) for the body plans in the notes.** The original game did not store lifeforms as a table. The STARB.COM map in the xls shows the overlays that created them per planet: VITA-OV (ecosystem), HP-OV and LP-OV (lifeform classes and species), SEED-OV (minerals, lifeforms and ruins) and BEHAV-OV (behaviour), plus text tables of lifeform descriptions. None of that code or text is in the repository. The sources available are the 1984 notes (`planet.txt`, `lf-behav.txt`, `lf-words.txt`, 874 lines in total). The options were: Reopened 2026-10-18 by D9: the shipped game's lifeform code (HP-OV, VITA-OV, ITEMS-OV) can now be read, and item 8.18 builds the lifeforms from it.
  - (a) Rebuild the lifeforms from the notes. This is a reconstruction, not a copy.
  - (b) Work from a copy of the original game files, if you own one.
  - (c) Leave lifeforms out.
- **D4. Interstel Police** (the copy protection arrest). Decided 2026-10-12: left out. The arrest was the original game's copy protection (a wrong security code at launch, read from the code wheel), not part of the game itself, and the port has no code wheel. The launch asks for no code, and the police's lines are not used.
- **D5. Conflicts in the sources.**
  - The Rod Device site: STRINFO gives two different places for it. Decided 2026-10-09: the one its planet's own entry gives.
  - The Red Cylinder's planet: STRINFO prints it as planet 4 of its star three times (1.2, 4.1, 5.1), but the planet's name, the orbit number 5 in its 5.1 entry (the third planet from that sun) and the Elowan message (3.1, "planet 3") all point to orbit 5. Decided 2026-10-09: the third planet (orbit 5).
  - The real effects of the Black Box, the Hypercube and the Ellipsoid. Decided 2026-10-11: none beyond their documented roles. STRINFO 4.1 gives no effect, and their analyses read as curios ("It slices, it dices", "decorative", "a 4-dimensional puzzle"); the Black Box is the Thrynn's sale (#156) and the Ellipsoid a Veloxi demand (#155).
  - The flare deadline. Decided 2026-10-09: the data does not disagree with itself. The year, month and day fields of every star still to flare give its flare day in the original calendar of 10 months of 30 days (234 of 234), so Arth's day 300 is 01-01-4621. The original checks for a flare as each day ends, so Arth's flare comes as 30-10-4620, the last day of the year, ends: the Elowan's "final week of your Ten-month". See item 0.7.

- **D6. A survey from orbit. Decided 2026-10-17: yes, for minerals and life, with the ruins left to the Red Cylinder, and no setting to turn it off.** The shipped game's sensors give a planet's mineral and bio densities only, and its landing map has no marks (`starflt_150`). The 1984 notes have more: life readings as dots on the sensor picture (`starship.txt`) and minerals and ruins as marks on the surface map (`planet.txt`). The science officer's analysis of a planet in orbit now surveys it: where its mineral fields (the concentrations of its deposits) and its lifeforms are, worked out from the planet's own seeds exactly as the surface places them, and found as well as the officer's science skill and the sensors' health allow. Ancient ruins are marked only with the Red Cylinder aboard, since locating them from orbit is that device's purpose in the original.
- **D7. The ground game beyond the original. Decided 2026-10-17: these departures, each its own PR.** The terrain vehicle was hard to use where the port's smooth driving meets rules made for the original's moves of a map cell at a time, and the ground had little to do and little to see up close. The original's numbers (deposit sizes, hold volumes, values, energy) stay.
  - Getting back to the ship: a vehicle coming back goes in anywhere within 3 km of the ship (the original's ship's own cell, half a kilometer, was hard to stop on); a Return order drives it back by itself; and Send beams the vehicle's hold to the ship from within about 20 km.
  - Mining: a mining laser replaces the Cargo button's one-press pickup of a deposit. The Weapon button becomes Tools, with Mine (a beam cuts the deposit into the hold in tenths of a cubic meter while it runs, on the vehicle's energy), Capture (only a stunned lifeform), the weapons and Back. Cargo keeps ruins, artifacts, dropped cargo and the cargo list.
  - Ground graphics: grass and small clutter, sharper close-up ground, a denser terrain mesh near the vehicle, and shadows and depth, under a detail setting picked by the graphics card (Auto, with Low, Medium and High to override).
- **D8. Ship speed by the engines. Decided 2026-10-17: the top speed in hyperspace and in a star system follows the engines' class, in both.** The original's ship flies at one speed whatever its engines (HYPER-OV `CSPEED`: 12 units a step in a star system, 1 in hyperspace, the step growing by one each pass; the engines and the ship's mass count only in an encounter's acceleration, `!S/A`). At the original's pace in the GOG release a new ship was too fast to stop at a planet in the port's 3D view. Class 5 engines now have the original's top speeds (2998 units a second in a star system, 371 in hyperspace), class 1 a third of them, each class a sixth more; and the ship reaches its top speed from a twelfth of it in 5 seconds over its acceleration in G (engines against mass), so a new ship of 5 G takes a second, as the original's 12 steps. Fuel still goes by the distance flown, and an encounter keeps the original's rules. A slower ship takes longer on a trip, and the game's clock runs in real time, so a trip with class 1 engines takes more game days than with class 5.
- **D9. The gap list. Decided 2026-10-18: build the shipped game's behaviour for 37 of the 39 differences of Phase 8, and leave two as they are.** Four readings of the decompiled overlays against the port (flight and fluxes, encounters and comms, planets and the surface, the Starport, crew and ship) listed every behaviour of the shipped game the port lacks, each with the overlay words that do it. Built as the shipped game does: every item of Phase 8. This reopens and replaces earlier calls that were made before the shipped code could be read: D3 (lifeforms come from the shipped game's generator and values, item 8.18), item 3.0 (a planet's minerals, lifeforms and curios come back after the ship leaves, as the shipped game seeds them afresh, item 8.19), item 3.5 (random ruins by the shipped game's rule, item 8.32), the translation rule of 2026-10-11 (item 8.9), the Veloxi drone's three numbers of item 0.2 (six, item 8.36), the Shimmering Ball of 2026-10-10 (the aliens' aim jitters, item 8.37), the Crystal Pearl's trigger of #128 (item 8.10), the Crystal Planet's damage rate of item 5.2 (item 8.1), and the artifact that draws the aliens (the Crystal Pearl, as the shipped code has it, item 8.11). Left as they are: the player's aiming (target cycling and separate laser and missile buttons) and the port's earlier departures (the Ship's Log menu, the Messages log, five automatic save slots, D4, music, salvage by flying over a wreck, D8).

A note on reading STRINFO: its "PLANET N OF SYSTEM X, Y" is the N-th planet from the sun, not the orbit number. This holds in 17 of the 18 entries that also print an orbit number; the 18th is the one in D5. To find the JSON's `m_orbitPosition`, sort the star's planets by orbit.

## Phase 0: Foundations

| # | Item | Original (source) | Port today |
|---|---|---|---|
| 0.1 | Correct the orbit positions of encounters 139, 165, 301, 302 and 305, put encounters 144 to 146 back in hyperspace, and repair old saves on load | xls; STRINFO agrees for 4 of the 5 orbits | #80 |
| 0.2 | Land past an orbit guardian | Homeworlds cannot be landed on at all: "the homeworlds of all races are well guarded and thither thou mayest not descend" (Elowan lore, STRINFO 2.4), so the home fleets keep blocking. Veloxi drones grant orbit if you answer yes to multiples of six; the Mechans help only "Group 9" (STRINFO 2.1 to 2.3) | Drones: #82 (three numbers, and permission until the ship leaves the system, decided 2026-10-09). Mechans at Heaven (encounter 77): the decompiled original makes the Heaven encounter inactive once the Mechans are friendly (COMM-OV +!EDL), so the ship goes back into orbit and can land; done with the Mechans' shipped mood (#279) |
| 0.3 | The game clock runs on the planet surface, and (Decided) in orbit and in encounters | the terrain vehicle panel shows the date (SS Terrain Vehicle) | #83: the clock runs in every Spaceflight location except the docking bay (kernel `PARALLEL-TASKS`, manual pages 7 and 24); the Starport is left as it is |
| 0.4 | Recover the missing data per D2: 36 ruin messages with sites, 15 artifact sites, the colony evaluation list with bonuses and fines, and the game's messages for the endgame and flares | STRINFO 1.2, 2.12, 2.14, 3.1, 4.1; Survey sheets "Habitable Planets" and "Optimal Planets" | #85: 38 planet messages, 14 artifact sites, 51 colony evaluations and 10 story texts in the recovered data file, each one resolved to its planet; data only, no behaviour yet |
| 0.5 | Flare data: mark the 36 stars whose day count wrapped below zero as already flared | `m_daysToNextFlare` equals 65536 plus `m_daysSincePreviousFlare` for all 36 | #89: the 36 have a negative flare day (Earth's sun -60), and their suns shine as stable suns |
| 0.6 | Play the visual results of #66 to #72 once in the Editor | | Closed 2026-10-12: the port has been played in the Editor many times since (the hand tests of the launch question, Examine and Treat, the mood stack, the floating lifeforms and the combat wounds), with nothing reported from #66 to #72. From then on, visual changes are tested with the probe's pictures, and the look decisions are made as part of the work (decided 2026-10-12) |
| 0.7 | Show and keep stardates in the original calendar: 10 months of 30 days, day 0 = 01-01-4620 | the flare date fields of all 234 stars still to flare (234 of 234; 18 match the real calendar); starship.txt (DRAFT: 10 months of 30 days); the manual ("the sixteenth day of ten-month"); Arth flares on day 300 = 01-01-4621, as 30-10-4620 ends, the Elowan's "final week of your Ten-month" | #94: `PD_General.GetStardateYMD` and `GetStardateDHMY` express every stardate in the original calendar; a save in the real-world calendar has its bank, ship's log and current dates moved on load (`PlayerData.ValidateStardateCalendar`) |

## Phase 1: Close the loops in systems that already exist

Small items, each documented in the sources.

| # | Item | Original (source) | Port today |
|---|---|---|---|
| 1.1 | Repair at the Starport, at a cost | Ship Configuration has Repair (SS Ship Configuration). Price decided 2026-10-10: the value of the missing armor points at the price of the ship's armor class, shields free. Since 2026-10-14, the original game's price from the decompiled CONFIG-OV (`WF226`, D1): 4 M.U. per armor point, the damaged engines, shields, missile launcher and laser cannon at their price, the sensors and comm at 100 M.U. per point, and a part sells for what it is worth in its condition (`WECB0`) | #137, the repair prices PR |
| 1.2 | The Engineer's repair uses minerals from the cargo hold | "We need 2 cubic meters of Molybdenum for repairs" (SS Repair). Decided 2026-10-10: the original's damage per system (hull, engines, sensors, comm, shields, missiles, lasers, SS Damage) with its repair minerals, a larger item of several PRs. Spread, effects, minerals and repair order decided 2026-10-11; the repair order decided again 2026-10-12: the player chooses, as in the decompiled original (ENGINEER) | the damage: #145; the effects: #146; the repairs with minerals: #147; the DAMAGE display: #281; Repair's choice of system: the Repair choice PR |
| 1.3 | Colony recommendation: Log Planet asks "Recommend this planet for colonization?"; Operations Evaluation lists the results; a bonus of 30k to 55k MU, or a fine that grows with each unsuitable recommendation | STRINFO 1.2, Survey; `m_habitable` on five planet tables was unused | #142 (the 2 G gravity limit is the port's choice). The later fines are the shipped game's: 10,000 MU for each unsuitable planet recommended before, up to 80,000 (the decompiled original, 2026-10-12). They are booked in the ledger when the planet is recommended, as the original's CAPTAIN WF37A does, and the evaluation is still read at the Starport (2026-10-13) |
| 1.4 | Distress: towed home and fined 15k to 80k MU; "There's no response" once the Starport is gone | STRINFO 1.2, 2.14 | #138 (no response: #121) |
| 1.5 | The Endurium price rises to 1500 and then 2000 MU with the notices of 20-02 and 15-05 | STRINFO 1.1, 1.4 (bought back at the same price: decided 2026-10-10 over the game data's 500) | #134 |
| 1.6 | Losing the terrain vehicle costs 10,000 MU and a replacement | STRINFO 1.2 | lava destroys a vehicle that enters it (planet.txt, the 6.6 hazard), and the Starport installs a new one for the fine, with the evaluation; the crew is unharmed (the port's choice) |
| 1.7 | Science skill decides whether an analysis succeeds | starship.txt (DRAFT formula: skill / 2); the formula was a decision | #161: the draft's rule (Decided 2026-10-11); learning by use is not done (Phase 7) |
| 1.8 | Flares: checked every day while the ship is in a system; a flare with the ship there ends the game ("incinerated"); Analysis shows "UNSTABLE, est. time to flare" under 1000 days | disys.txt `?FLARE`, STRINFO 2.14, notes | #122: every star flares at a ship in its system on its day. The stability text: #160, in the star system scan (the port takes no reading of a star; the text is starship.txt's, decided 2026-10-11) |
| 1.9 | Nebulae act on the shields | priority.txt; decided 2026-10-10: the shields cannot be up inside a nebula, and there is no damage | #136 |
| 1.10 | The Starmap shows a fuel estimate | alpha.txt | #141 |
| 1.11 | Alien vessels use their own armor, shields and speed from the data | xls Vessels; `GD_Vessel.m_armor`, `m_shields`, `m_moveDelay` | #140 (a move delay of 2200 at the old speed: the port's choice) |
| 1.12 | Salvage from debris | wrecks leave debris (communic.txt); what can be taken: the original's vessel records ("Endurium Debris", "Debris 1" to "3"), and a Thrynn line about salvaging wrecked Elowan ships | the debris of the records, taken aboard by flying over the wreck (how it is collected is the port's choice) |

## Phase 2: Encounters that matter

| # | Item | Original (source) | Port today |
|---|---|---|---|
| 2.1 | Posture moves the aliens' mood (0 to 100, with friendly, diplomatic and hostile or obsequious bands; the player's strength decides between hostile and grovelling) | compart.txt, Notes.txt | #157: the draft's model (compart.txt) rebuilt for the Elowan, Thrynn, Spemin and Veloxi with the port's numbers (decided 2026-10-11), then switched to the shipped game's numbers from STARB.COM (decided 2026-10-12): the lines' posture masks (#259); the races' thresholds, effects and starting moods, with the shields and weapons added at the meeting (#265); the lines' actions and the answers' mood changes (#269); the shipped strength rule, with the vessels' strengths recovered from the data extract (#272); the shipped events that move the mood: shields, weapons, hails, phrases of the link (#275); the shipped phrase limits (#276); how the aliens wait for a reply and how often they ask: hails, reminders, question rate (#278); the Mechans' mood, with Heaven letting a friend in (#279, and item 0.2); the Gazurtoid's mood (#280); and the homeworld context (#270). Not ported, by its nature: the pace of the aliens' talk. The original rolls each race's talkativeness (T%, scaled by the posture) once per pass of its rule engine, so the pace follows the speed of the machine it runs on and has no single value; the port keeps its conversation timer |
| 2.2 | Homeworld warnings and surrender (22 and 8 lines in the data); the Spemin surrender after losses | STRINFO 2.x | the homeworld warnings: #150 (the attack after the last warning is read from the lines); the surrender after losses: #152 (the Thrynn and Veloxi as the Spemin, decided 2026-10-11; half the ships is the port's choice) |
| 2.3 | Trade and tribute: the Thrynn buy artifacts and plutonium for Endurium and sell the Black Box for 30; the Veloxi demand 3 Endurium; the Elowan give 15 if you carry under 20 | STRINFO 2.x; `GD_Artifact.m_thrynnPrice` was unused | the Elowan's gift: #154 (asked as their first question: the port's choice); the Veloxi demands: #155; the Thrynn trades: #156. Decided 2026-10-11: a refused tribute (or a yes with too little) brings an attack, as the Crystal Orb and Ellipsoid demands do; the Thrynn pay 1 cubic meter of Endurium for each of plutonium |
| 2.4 | Race rules: a crew member of the race removes the garbling; the Elowan refuse a ship with a Thrynn aboard; the Mechans will not deal without humans; Gazurtoid ships are immune to missiles; a comm link that has ended cannot reopen in the same encounter | compart.txt, alpha.txt, STRINFO 2.x, 7.x | garbling depended on comm skill only. Already in place: Gazurtoid ships are immune to missiles (`GD_Vessel.m_immuneToMissiles` in the original data), an ended comm link gets no answer to a new hail (`DefaultEncounterUpdate`), and the Mechans' no-humans warning. Translation by a crew member of the race, the Elowan's refusal with a Thrynn aboard and the rivals' first contact: #158 (decided 2026-10-11) |
| 2.5 | The Noah 9 derelict (SOS) and the other races that fell to the `default` case | STRINFO 2.x | #159: the Minstrels, Nomad probes, Noah 9 derelict and Mysterions transmit their lines in order once the comm link is up |

## Phase 3: Ruins, artifacts and messages on the ground

This phase was on the critical path to the endgame.

Decided 2026-10-09: place only what the sources document (the 14 artifact sites and the 38 messages of the recovered data) and leave random ruins for later (item 3.5); a message that STRINFO puts at "random locations" lies in one ruin, at a place picked from the planet's seed; every ruin uses the five Ancient Ruins models for now, and an artifact uses a small marker; what has been taken from a planet is saved, deposits included. The manual (page 21) describes the rest: the terrain vehicle's Cargo picks up any item beside it and records the messages found in ruins, and "any messages you find are identified by the date found".

| # | Item | Original (source) | Port today |
|---|---|---|---|
| 3.0 | What has been taken from a planet stays taken: deposits now, the artifacts and messages with 3.2 and 3.3 | the manual (page 21) | #100: every disembark had placed the planet again from its seed, so a deposit that had been picked up was back. Replaced 2026-10-18 (D9, item 8.19): the shipped game deletes a planet's minerals, lifeforms and curios when the ship leaves and seeds them afresh |
| 3.1 | Ruins at the sites of the recovered data: a ruin at each artifact site and at each message, and the two formations (the Most Magnificent Hexagon of 6 ancient ruins, the City of the Ancients of 15) | planet.txt, dir.txt SEED-OV, data from 0.4 | the messages: #102; the artifact sites: #108; the two formations: #109 |
| 3.2 | The 12 special artifacts at their 14 sites, taken with the Cargo button | STRINFO 4.1 | #108: in the ruins of their sites, taken once and saved, carried to the ship; the Black Box, the Flat Device and the Whining Orb are bought, not found |
| 3.3 | The 38 messages go to the ship's log under Messages, recorded with the Cargo button beside their ruin and dated the day they were found | STRINFO 3.1; the manual (page 21) | #102 |
| 3.4 | Dropping cargo, and the terrain vehicle's cargo display (decided 2026-10-09: both, dropping first) | the manual (page 21): Cargo lists what the vehicle carries "and gives you the option of dropping anything", and a dropped object can be picked up again; the display is not in the sources | dropping: #110; the display: #112, shown while the cargo list is open |
| 3.5 | Random ruins: ancient crystal ruins with Endurium lumps or 1 to 4 "truly amazing" artifacts, Old Empire ruins with artifacts or writing | planet.txt (DRAFT only); the kernel has a class of ruins and dir.txt's SEED-OV seeds them | the notes' ruins, with the port's counts (a quarter of the planets; decided 2026-10-11, after being deferred on 2026-10-09); the writing in Old Empire ruins is not placed (no texts) |

## Phase 4: Artifact effects

Only the effects that matter for winning had to come before Phase 5. The others could follow at any time. Where each artifact lies is left out, so that the roadmap gives nothing away to players.

| Artifact | Effect (source: Starport analysis, STRINFO) | How it is obtained | Needed to win |
|---|---|---|---|
| Crystal Orb | cancels the Crystal Planet's field: #115, with item 5.2 | found | yes |
| Black Egg | a planet bomb, armed by dropping it: #117 (countdown in orbit, the planet destroyed and saved) | found | yes |
| Crystal Cone | finds the Crystal Planet's nexus from orbit: #118 (the messages report it on entering orbit and on Land; how it is shown is the port's choice) | found | INFERRED yes |
| Ring Device | shows nearby fluxes (alpha.txt: fluxes are hidden without it and navigation skill) | found | no; decided 2026-10-10: fluxes are hidden without it (#132); since 2026-10-15 also shown to a navigator of skill above 150, as the released game's HYPER-OV .FLUX-ICON |
| Whining Orb | translates Spemin: #126 | Starport, 6000 MU | no |
| Flat Device | shields the terrain vehicle from lifeforms: a lifeform hits half as often with the device in the ship or the terrain vehicle (the half is the port's choice) | Starport, 30,000 MU | no |
| Shimmering Ball | automatic cloak in combat; decided 2026-10-10: the aliens cannot fire at the ship until it fires (replaced 2026-10-18 by item 8.37: the aliens' aim jitters, as the shipped game's). #130 (3 s uncloaked after each shot) | found | no |
| Rod Device | stronger laser shield: #127 (half of every alien laser hit; the amount is the port's choice) | found | no |
| Tesseract | doubles engine efficiency: #124 (half the engines' fuel in hyperspace) | found | no |
| Crystal Pearl | warps a badly damaged ship away: #128 (out of an encounter below a tenth of the armor; the tenth is the port's choice; replaced 2026-10-18 by item 8.10, the shipped game's trigger and destination) | found | no |
| Red Cylinder | finds ancient ruins from orbit: flashes on the landing map near an ancient ruin (the shipped game's "THE RED CYLINDER IS FLASHING" is in its landing map code; how near is the port's choice) | found | no |
| Dodecahedron | attracts every ship nearby: #129 (twice the alien radar distance), replaced on 2026-10-15 by the released game's rule (HYPERMSG-OV TRAK-E: groups come four fifths of the way at each move instead of half, and are not held near home), which on 2026-10-18 turned out to be the Crystal Pearl's (item 8.11): no word of the shipped game looks for the Dodecahedron | found | no |
| Black Box, Hypercube, Ellipsoid | none beyond their documented roles (D5, decided 2026-10-11) | found or traded | no |

## Phase 5: Main story and endgame

After this phase the game can be played from start to win.

| # | Item | Original (source) |
|---|---|---|
| 5.1 | Arth's sun flares on its date: the Starport is destroyed and the game goes on (distress gets no answer; the win message can still arrive). #121: day 300 unless the game is won; a ship in Arth's system is incinerated; no Starport model, no docking, no answer to distress | STRINFO 2.12, 2.14, Elowan comm, data (D5); disys.txt `?FLARE` |
| 5.2 | The Crystal Planet: its field damages a ship without the Orb. #115: 5 points a second in orbit without the Orb (the rate is the port's choice; replaced 2026-10-18 by item 8.1, the shipped game's heat sequence and landing lock) | STRINFO 5.1; disys.txt `'HEAT` (crystal planet heating routine) |
| 5.3 | Drop the armed Black Egg at the Crystal Planet's control nexus to win. Anywhere else on the Crystal Planet: "damaged but not destroyed". On the Uhlek brain world the Uhlek fall silent. On Elan the Elowan young die. #117: the egg, the nexus, the damaged planet and the crew's two reports; #123: the Uhlek harmless, the Elowan mortal enemies | STRINFO 2.12, 5.1, SS Story; disys.txt `?BOMB` |
| 5.4 | Win sequence: the message, 500,000 MU, the Interstel medal, a supplemental evaluation; no more flares after the win. #120: the win saved, no flares, the bonus paid on docking, the evaluation in Operations | STRINFO 1.2, 2.12; disys.txt `?WIN`, `?FLARE`, `WMSG` |
| 5.5 | Every way to lose (flare, ship destroyed, all crew dead) ends through the same game over. #122: one destruction path, and the game over names the cause; all crew dead waited for Phase 6 (crew death is only in the 1984 drafts). With Phase 6's crew death, all crew dead ends through the same game over (`CombatController.EndGame`) | STRINFO, disys.txt |

## Phase 6: Lifeforms and crew injury

Depended on D3. This was the largest piece of the roadmap and not on the path to winning, which is why it came after Phase 5.

| # | Item | Original (source) |
|---|---|---|
| 6.1 | Lifeforms created per planet from its seed, the same on every visit | dir.txt VITA-OV, HP-OV, LP-OV, SEED-OV; planet.txt |
| 6.2 | Scan reports them (volume, aggression, intelligence, niche); Look describes them. Done: Scan (#167); Look gives the nearest lifeform's body plan, size, colour, movement and status, and the notes' lines for minerals, ruins and dropped cargo | planet.txt, lf-words.txt |
| 6.3 | Behaviour: approach, attack, flee, predator and prey, flying. Done: the response to the player (approach, attack, flee, wander), the laser's effect on aggression, the mates (hunt, flee, bite, eat), flyers over water, the air-ground transitions FLY and LAND (decided 2026-10-12: 75% to fly and 20% to land per step; attacking the mate or the vehicle lands; a dead or stunned flyer comes down onto a liquid too), and FLOAT (floating plants drift, a tenth of the animals that do not fly float, and nothing in the air can be dealt with: planet.txt) | lf-behav.txt |
| 6.4 | Stunner and laser; capture into stasis; sale at the Trade Depot; payment for recorded data. Done: the stunner and the laser; recording and its payment at the Starport (the manual, page 21: Cargo makes "holographic recordings of lifeforms"; the kernel's BIO-DATA); capture into stasis and the sale of specimens at the Trade Depot (the manual, page 21: Cargo picks up "any item (including lifeforms)"; the kernel's specimen objects) | planet.txt (DRAFT values), the manual page 21, disys.txt (specimens and BIO-DATA objects) |
| 6.5 | Attacks hurt the crew, scaled by durability; the Doctor treats them; death, the chain of command, DEAD on the personnel file. Done: the lifeforms' attacks by the notes' formula, death at 0 vitality, DEAD on the file, and the game lost with the whole crew; since 2026-10-16 a dead member's posts go as the shipped game's do (HEAL-OV OBITS: to the crew member best at each, a dead captain's command to the science officer), in place of the notes' chain of command | lf-words.txt, starship.txt, disys.txt |
| 6.6 | Weather and terrain hazards: storms, lava destroys the vehicle, uphill is slow. Done: lava (#165), uphill (the slope ahead slowed the vehicle; replaced on 2026-10-15 by the released game's rule, MOVE-OV `SET-SPEED`: no slope, fuel by the contour of the ground), storms (observed in a GOG copy of the game: the hourly sky, a storm's half efficiency, lost bearing and injuries; redone on 2026-10-15 from the released game's code, BEHAV-OV `?STORM` and STORM-OV `(DO.STORM`: spells of weather by the planet's weather activity, storms by its surface, the injury and efficiency rolls, and the navigator's skill for the bearing) | planet.txt, alpha.txt, dir.txt STORM-OV |

## Phase 7: Fidelity pass

- Compare every screen with `Research/Screenshots/` and every message with STRINFO.
  - Screens: compared on 2026-10-11 against the probe's pictures of the port's screens (#214), with 77 differences found. The game's own differences (texts and behaviour) are fixed, most of them from the decompiled original (s-macke/starflight-reverse) where the screenshots stop: the coordinates (#215, #221), the Status display (#216), the terrain vehicle's display (#218), the messages of travel in a system (#219), the Analysis of an alien ship (#220) and the aliens raising their shields (#224), Distress (#222), and the landing's lines (#226). Launch's question (#223) and Examine and Treat (#225) were then hand tested (see 0.6). Since then: the DAMAGE display (#281), the notices screen (#288; still open there: typing a notice line by line, which can be aborted), where the landing crosshair starts (#292), the encounter view's scale (#296) and the comm link's picture (#297). The launch's security code is left out with D4, and the console's labels and layout stay the UI kit's (decided 2026-10-12). Done on 2026-10-13: the probe took pictures of every screen it had not yet seen (#307, #319), and every gameplay difference they showed was fixed or decided (#305, #306, #308 to #313, #315 to #317, #320 to #325, and the decisions of #318 and the final recount). `Research/Screen comparison.md` has no open gameplay item; its UI kit items went to the UI kit work.
  - Messages: the recovered texts were audited against STRINFO on 2026-10-12: 163 texts, 156 of them alike. The two documented ancient ruin sites were missing (#227) and two notes were wrong (#228); the Interstel Police's lines are left out with D4. Every message the player sees was then audited against STRINFO and the shipped game's own texts (the decompiled original's `instance.txt`, `data.h` and overlays): `Research/STRINFO message audit.md`. The game data's comm lines are the shipped game's; in all 47 where STRINFO's words differ, STRINFO is the one that slips ("Smelenuf", "share the tale" and the Thrynn sentences are the original's). The game data's notices and analyses follow STRINFO where it departs from the shipped game (four notice words, "ataraxia"; "laer" matches neither). Decided 2026-10-12: the shipped game's words, done in the game data PR of that day. Done per area since: the ruin sign's number (#239), the Thrynn offer's "unitxx" (#243), the evaluations' two words (#251) and headings (#254), the Noah 9 beacon as the officer's report (#264) and the homeworld fleets' order of lines (#270).
- Correct the README's completion figure. Done: it counts the roadmap's items, with what is left.
- Draw the planet in encounters that begin in orbit (decision 5, a later step). Done: the planet's own surface lies under the encounter, below the ships.
- Alien captain and ship names (`%` and `+` in 15 greetings): made of the race's syllables (the garble words), as STRINFO's own names are (Fi-noc-tzi, To-chu-pu, Yrh-ey). Done 2026-10-11.
- Crew learn by using their skills: DRAFT only. Left out by decision (2026-10-11): the shipped game trains crew at the Starport (the Personnel office, which uses the race's learning rate).

## Phase 8: The shipped game's code

Found on 2026-10-18 by reading the shipped game's decompiled overlays against the port, and decided the same day (D9). The items are in the order they are built: what the story and the win depend on, then the aliens, then the rest by how much they matter. Each is one PR with its own probe scenario. The damaged equipment malfunctions (MISC-OV `(?.EQ`), found in the same reading, were built first (#486).

| # | Item | Original (decompiled overlays) | Port today |
|---|---|---|---|
| 8.1 | The Crystal Planet without the Crystal Orb: the ship shakes, the armor drops to 0, a random system loses 3 points a tick, the hull warns three times and melts at the 280th tick; Land answers "COMPUTER NOT RESPONDING!", a distress call is jammed; with the Orb aboard it glows and none of this happens | HYPERMSG-OV `OSET`, `WED4B`, `HEAT`, `HCASE`; LAND-OV `?LAND`; COMM-OV `WEFC3` | The heat as the original's: the ship vibrates and its armor is gone, a random system loses 3 points a tick, the hull is halved at the 70th, 140th and 210th tick and melts at the 280th, Land and a distress call refused; a tick is the port's measured pass of 0.25 s (`InOrbit.UpdateCrystalPlanetHeat`; probe `crystalfield`; #496) |
| 8.2 | The Crystal Orb goes back to its site when the Veloxi take it, unless its planet has been destroyed | COMMSPEC-OV `CRYSTAL-ORB`, `-ARTIFACT`, `WEB3F`; SEED-OV `WF143`; DPART-OV `?DELETEPLANET` | The orb's site is free again once the Veloxi take it, unless its planet has been destroyed (`Encounter.ReturnCrystalOrbToItsSite`, `PD_PlanetSurfaces.ReturnArtifactSite`; probe `orbreturns`) |
| 8.3 | Alien ships move by their posture: hostile ones close to 11 cells (3 with lasers only), others leave once talking is over, grovelling ones flee, an idle ship leaves after 2 minutes; the encounter ends when the nearest alien is over 22 cells away | COMBAT-OV `<COMBAT>`, `WED86`, `WED72`, `WEDCE`; HYPER-OV `?UNNEST` | MISSING |
| 8.4 | Armed aliens in the hostile posture open fire (2% a ship's turn, then at their vessel's rate) | COMBAT-OV `<COMBAT>` rules 2 and 5, `WDF72` | MISSING |
| 8.5 | Each race remembers how it was treated: its base mood moves toward the mood an encounter ended in | HYPER-OV `ENCCLEANUP`, `BASE-EDL` | MISSING |
| 8.6 | The Veloxi demands have lasting effects (a refused Orb is war; the Ellipsoid given or refused) | COMMSPEC-OV `CRYSTAL-ORB`, `WEF36` | MISSING |
| 8.7 | Friendship with a rival race leads the Elowan or the Thrynn to end the link and fight | COMMSPEC-OV `WEDA7`, `WEE3F`, `WEDDB`, `WEE73` | MISSING |
| 8.8 | When races make their own offers: by posture, the tribute's size, lying to the Elowan, the Thrynn's offers in turn, plutonium at 84% | COMMSPEC-OV `WF174`, `WF1D2`, `NEED-ENERGY` | MISSING |
| 8.9 | The comm officer's translation strength: 16.7 x the square root of skill and the bonuses, at most 250 | COMM-OV `INIT-MCOMM` | MISSING |
| 8.10 | The Crystal Pearl fires when the ship is heavily damaged and the aliens are armed, and puts the ship at a random place in hyperspace | HYPERMSG-OV `?PRL`; SCIENCE `?HEAVY` | MISSING |
| 8.11 | The artifact that makes the aliens chase harder is the Crystal Pearl | HYPERMSG-OV `(!'EXT)`, COMMSPEC-OV `WEA50` (`?12`) | The aliens' pursuit with the Crystal Pearl aboard: four fifths of the way in hyperspace, six sevenths in a star system, not held near home; the Dodecahedron does nothing here (`SpaceflightController.UpdateEncounters`, `TrackTowards`; probe `pearlchase`) |
| 8.12 | Fluxes the ship has been through are remembered and drawn on the starmap | HYPER-OV `SET-FLUX-STATE`; MAP-OV `WEA70`, `WEA4C` | MISSING |
| 8.13 | Back into orbit after the Noah 9 derelict or a destroyed orbit fleet | HYPER-OV `?DOUBLE-UNNEST`, `SKIP2NEST`; HYPERMSG-OV `WE84B` | MISSING |
| 8.14 | A terrain vehicle that runs out of fuel is lost: the crew walk back, the vehicle stays as debris, the Starport fines its loss | MOVE-OV `WF16E`, `WEF48`; SHIPBUTTONS `WF095`; OP-OV `WF045` | the vehicle is lost and left as a wreck where it stopped (`MoveButton.LeaveTheWreck`, `TerrainRuins.PlaceVehicleWreck`, `DroppedCargo.m_isVehicleWreck`); the walk back and the fine were there (probe `outoffuel`) |
| 8.15 | The Red Cylinder flashes the planet's message and artifact sites when the landing map opens | SITE-OV `.MERCATOR`, `WF389`, `WF290` | MISSING |
| 8.16 | A deposit's mineral: the planet's first element half the time, the second a quarter, the third 15%, any other element 10% | SEED-OV `WF0F7`, `WF0C7` | the original's shares, drawn apart from the planet's seeded draws (`TerrainElement.ChooseDepositElement`, used by `TerrainElements` and `PlanetSurvey`; probe `depositminerals`) |
| 8.17 | Jettison from the ship's hold | CAPTAIN `(/ITEMS)`, `TOSS` | Jettison on the ship's cargo list: an artifact or a specimen at once, an element by a volume typed or stepped with the stick (`ShipCargoJettisonButton`; probe `jettison`) |
| 8.18 | Lifeforms as the shipped game makes and values them (replaces D3) | HP-OV, VITA-OV, LP-OV, ITEMS-OV `valuate` | MISSING |
| 8.19 | A planet's minerals, lifeforms and curios come back after the ship leaves (replaces 3.0) | DPART-OV `WF166`, `WF0D6`; MOVE-OV `?POPULA` | MISSING |
| 8.20 | A navigator above 150 sees a flux as 1 to 7 dots by skill, 16 with the Ring Device | HYPER-OV `.FLUX-ICON` | MISSING |
| 8.21 | The edge of hyperspace holds the ship | HYPER-OV `FLY`, `WEE53` | MISSING |
| 8.22 | Alien groups in a star system are placed afresh on each visit | HYPER-OV `SYSSETUP`, `SETRNDXY` | MISSING |
| 8.23 | The Mysterions, Nomad probes and Minstrels move on after an encounter | HYPER-OV `ENCCLEANUP`, `WDC19` | MISSING |
| 8.24 | "OUT OF FUEL! CRASH IMMINENT!" and the motion report's coordinates | ORBIT-OV `DESCEND`; HYPERMSG-OV `WF28B` | MISSING |
| 8.25 | Questions about the Old Empire and the Ancients can be shut off | COMM-OV `?LOCKED-OUT` | MISSING |
| 8.26 | Impatience, and a fighting race calming down | COMM-OV `>IMPATIENT`, `<EDL>` | MISSING |
| 8.27 | The terrain vehicle's icon key | LISTICONS `ICONS` | MISSING |
| 8.28 | The Flat Device: lifeform attacks hit a tenth as often | BEHAV-OV `WE8C2` | the hit roll times 10 with the Flat Device (`TerrainLifeform.AttackPlayer`; probe `flatdevice`) |
| 8.29 | A flare sterilises its system | ANALYZE-OV `WEC26` | MISSING |
| 8.30 | Look describes the land, its colours and the region's temperature | PLSET-OV `(TERRAIN`; MAPS-OV `.SURFACE`, `.CARPET` | the land in its colours and the region's temperature by latitude (`GD_Planet.GetLandText`, `GetRegionTemperatureText`; probe `lookland`); the carpet lifeform waits for 8.18 |
| 8.31 | Arming the Black Egg is asked on the drop, and picking it up disarms it | ITEMS-OV `WE9AA`, `WE9FA`; DPART-OV | the drop asks "Arm this device?" (`ArmEggYesButton`, `ArmEggNoButton`, `PD_PlanetSurfaces.DroppedCargo.m_leftUnarmed`), and picking up an armed egg says "Disarming device." (probe `eggarming`) |
| 8.32 | Random ruins by the shipped game's rule (replaces 3.5; needs two planet bytes from the original's data) | SEED-OV `WF492`, `WF468`, `WF26B` | MISSING |
| 8.33 | Describe an artifact or a specimen from the cargo list | CAPTAIN `WEA62`, `WEC73` | the ship's cargo list marks an item, with Next, Describe and Back (`ShipCargoButton`), and a specimen keeps its capture place and date (`PD_Specimen`; probe `cargodescribe`) |
| 8.34 | The Starport asks before repairing | CONFIG-OV `WF346`, `WF2C5` | MISSING |
| 8.35 | Disembark lowers the shields and disarms | SHIPBUTTONS `WEDEC` | Disembark lowers the shields and disarms, saying so (`DisembarkButton`; probe `disembarkshields`) |
| 8.36 | The Veloxi drone asks up to six numbers | `VPROBE#` | MISSING |
| 8.37 | The Shimmering Ball makes the aliens' aim jitter | `WE911` | MISSING |

## Order

The critical path to a game that can be won ran through 0.1, 0.2 and 0.4, then Phase 3, the winning artifacts of Phase 4, and Phase 5. Phases 1 and 2 were independent small PRs that could run in between. Lifeforms (Phase 6) came last because they were the biggest item, depended on D3, and only the Flat Device needs them.

The first estimate, counting one PR per item: Phase 0 about 5, Phase 1 about 12, Phase 2 about 5, Phase 3 about 4, Phase 4 about 12, Phase 5 about 5, Phase 6 about 6 to 10. That came to about 50 PRs in all.
