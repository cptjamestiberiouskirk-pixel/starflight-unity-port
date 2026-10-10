# Roadmap to a complete Starflight

Written 2026-10-05 against `master` at `bf6a2910`, and agreed with the project owner on 2026-10-09. The goal is a port that does everything the 1986 game does, before anything new or different is added.

This is the plan of record. Pick work from it, and when a PR for an item merges, put the PR number into the item's row in the same docs change that updates `CODE_REVIEW_2026-10-03.md` or `CHANGELOG.md`. Change an item's description only when the sources or the owner's rulings change it.

## How to read this

- **Status** of a feature in the port: DONE, PARTIAL (says what is missing), STUB (a button that only prints text or plays a sound), MISSING.
- **Source** is where the original behaviour is written down:
  - STRINFO: `Research/Data/STRINFO.DOC`
  - xls: `Research/Data/Starflight 1 Data.xls`
  - Survey: `Research/Data/Starflight_1_Survey.xls`
  - notes: `Research/Notes/`
  - SS: `Research/Screenshots/`
  - Files under notes marked DRAFT are 1984 to 1985 design notes. They are not proof of what the game shipped with.
- Each numbered item is meant to be one PR with its own probe scenario, as `CLAUDE.md` requires.
- **Ask** marks an item where the sources do not settle what the original did, so the project owner has to decide.
- **Evidence.** The port's status comes from reading the code on 2026-10-05; nothing was run. Spot-checked by hand: the Starport repair, Distress, the terrain vehicle weapon, the Messages log, crew vitality and the game clock. Re-verify line numbers before working on an item.

## Where the port stands

| Area | Status | Main gaps |
|---|---|---|
| Starport: Personnel, Crew Assignment, Bank, Docking Bay | DONE | |
| Starport: Trade Depot | PARTIAL | no lifeform sales; Endurium price never changes |
| Starport: Ship Configuration | PARTIAL | Repair is a STUB (`ShipConfigurationPanel.cs:692`) |
| Starport: Operations | PARTIAL | Evaluation is fixed scene text; no colony recommendations |
| Flight, hyperspace, fluxes, fuel | DONE | |
| Nebulae | PARTIAL | drawn, no effect (`PlayerShip.cs:218` TODO) |
| Stellar flares | PARTIAL | checked once on entering a system; Arth's flare does nothing |
| Starmap | PARTIAL | no fuel estimate |
| Game clock | PARTIAL | runs only in the star system and hyperspace (`SpaceflightController.cs:181`) |
| Ship console: Captain | PARTIAL | Log Planet is stored and never used; Messages log always empty |
| Ship console: Science | PARTIAL | science skill has no effect; salvage not implemented |
| Ship console: Engineer, Doctor | PARTIAL | repair needs no minerals; the Doctor never has a patient |
| Ship console: Communications | PARTIAL | Distress is a STUB |
| Combat | DONE | alien vessel armor, shields and speed are not taken from the data |
| Comm system | PARTIAL | posture never changes the aliens' mood; homeworld and surrender lines are never shown |
| Alien trade, tribute, surrender | MISSING | |
| Terrain vehicle: drive, map, mine, return | DONE | |
| Terrain vehicle: Look, Scan | PARTIAL | no lifeforms to report |
| Ruins, artifacts, planet messages | MISSING | code exists but is not wired into any scene |
| Weather and terrain hazards | MISSING | dust storm is cosmetic |
| Lifeforms (stun, capture, sell, attack) | MISSING | |
| Crew injury and death | MISSING | nothing lowers vitality |
| Artifact effects (15 special artifacts) | MISSING | effects exist as analysis text only |
| Main story and endgame | MISSING | no Crystal Planet, no win, no flare deadline |

The README's "Completion: ~95%" is far too high for the game as a whole. It is closer to an accurate figure for the engine and the Starport.

## Decisions

These decide how much of the roadmap can go ahead without asking item by item.

- **D1. Fidelity rule. Ruled 2026-10-09: yes.** Where the sources document the original's behaviour, implement it as documented without asking per item. Ask only where the sources are silent or contradict each other (the items marked Ask). Behaviour found only in the DRAFT notes counts as Ask.
- **D2. Where recovered data goes. Ruled 2026-10-09: a second data file**, `Assets/Resources/Starflight Recovered Data.json`, merged into the game data at load (first used by item 0.2). The options were:
  - (a) Add them to `Starflight Game Data.json`. `.claude/settings.json` asks before every edit of that file.
  - (b) Put them in a second file next to it (for example `Starflight Story Data.json`). The original values stay untouched in one file and the added records live in the other.

  Either way no existing value changes.

Still open:

- **D3. Lifeforms.** The original did not store lifeforms as a table. The STARB.COM map in the xls shows overlays that created them per planet: VITA-OV (ecosystem), HP-OV and LP-OV (lifeform classes and species), SEED-OV (minerals, lifeforms and ruins) and BEHAV-OV (behaviour), plus text tables of lifeform descriptions. None of that code or text is in the repository. The sources available are the 1984 notes (`planet.txt`, `lf-behav.txt`, `lf-words.txt`, 874 lines in total). Options:
  - (a) Rebuild the lifeforms from the notes. This is a reconstruction, not a copy.
  - (b) Work from a copy of the original game files, if you own one.
  - (c) Leave lifeforms out.
- **D4. Interstel Police** (the copy protection arrest). Recommendation: leave it out.
- **D5. Conflicts in the sources.**
  - The Rod Device site: STRINFO gives two different coordinates. Ruled 2026-10-09: 54N x 13E, Harrison's base 2, as New Scotland's own entry says.
  - The Red Cylinder and Koann 3: STRINFO prints planet 4 of 112,200 three times (1.2, 4.1, 5.1), but the name Koann 3, the orbit number 5 of its 5.1 entry (the third planet from that sun) and the Elowan message (3.1, "planet 3") all point to orbit 5. Ruled 2026-10-09: the third planet (orbit 5).
  - The real effects of the Black Box, the Hypercube and the Ellipsoid.
  - The flare deadline: resolved 2026-10-09. The data does not disagree with itself: the year, month and day fields of every star still to flare are its flare day in the original's calendar of 10 months of 30 days (234 of 234), so Arth's day 300 is 01-01-4621. The original looks for a flare as a day ends, so Arth's comes as 30-10-4620, the last day of the year, ends: the Elowan's "final week of your Ten-month". See item 0.7.

Reading STRINFO: its "PLANET N OF SYSTEM X, Y" is the N-th planet from the sun, not the orbit number. This holds in 15 of the 16 entries that also print an orbit number; the 16th is Koann, see D5. To find the JSON's `m_orbitPosition`, sort the star's planets by orbit.

## Phase 0: Foundations

| # | Item | Original (source) | Port today |
|---|---|---|---|
| 0.1 | Correct the orbit positions of encounters 139, 165, 301, 302, 305, and put encounters 144 to 146 back in hyperspace, with a load repair for old saves | xls; STRINFO agrees for 4 of the 5 orbits | PR 80 |
| 0.2 | Land past an orbit guardian | Homeworlds cannot be landed on at all: "the homeworlds of all races are well guarded and thither thou mayest not descend" (Elowan lore, STRINFO 2.4), so the home fleets keep blocking. Veloxi drones grant orbit if you answer yes to multiples of six; the Mechans help only "Group 9" (STRINFO 2.1 to 2.3) | drones: PR 82 (three numbers, permission until the ship leaves the system: the owner's choices of 2026-10-09). Mechans at Heaven (encounter 77): Ask, the sources do not say that Mechan 9 lets a ship land |
| 0.3 | Game clock runs on the planet surface, and (Ask) in orbit and in encounters | the terrain vehicle panel shows the date (SS Terrain Vehicle) | PR 83: the clock runs in every Spaceflight location except the docking bay (kernel `PARALLEL-TASKS`, manual pages 7 and 24); the Starport is left as it is |
| 0.4 | Recover the missing data per D2: 36 ruin messages with sites, 15 artifact sites, colony evaluation list with bonuses and fines, the game's messages for the endgame and flares | STRINFO 1.2, 2.12, 2.14, 3.1, 4.1; Survey sheets "Habitable Planets" and "Optimal Planets" | PR 85: 38 planet messages, 14 artifact sites, 51 colony evaluations and 10 story texts in the recovered data file, every one resolved to its planet; no behaviour yet |
| 0.5 | Flare data: the 36 stars whose day count wrapped below zero are marked as already flared | `m_daysToNextFlare` equals 65536 plus `m_daysSincePreviousFlare` for all 36 | PR 89: the 36 have a negative flare day (Earth's sun -60), and their suns shine as stable suns |
| 0.6 | Play the visual results of PRs 66 to 72 once in the Editor (owner) | | nothing visual has been seen |
| 0.7 | Show and keep stardates in the original's calendar: 10 months of 30 days, day 0 = 01-01-4620 | the flare date fields of all 234 stars still to flare (234 of 234; 18 match the real calendar); starship.txt (DRAFT: 10 months of 30 days); the manual ("the sixteenth day of ten-month"); Arth flares on day 300 = 01-01-4621, as 30-10-4620 ends, the Elowan's "final week of your Ten-month" | PR 94: `PD_General.GetStardateYMD` and `GetStardateDHMY` make every stardate in the original's calendar; a save in the real-world calendar has its bank, ship's log and current dates moved on load (`PlayerData.ValidateStardateCalendar`) |

## Phase 1: Close the loops in systems that already exist

Small items, each documented in the sources.

| # | Item | Original (source) | Port today |
|---|---|---|---|
| 1.1 | Repair at the Starport, at a cost | Ship Configuration has Repair (SS Ship Configuration); price: Ask | STUB, plays a sound |
| 1.2 | Engineer's repair uses minerals from the cargo hold | "We need 2 cubic meters of Molybdenum for repairs" (SS Repair) | timed and free |
| 1.3 | Colony recommendation: Log Planet asks "Recommend this planet for colonization?"; Operations Evaluation lists the results; bonus of 30k to 55k MU, or a fine that grows with each unsuitable recommendation | STRINFO 1.2, Survey; `m_habitable` on five planet tables is unused | Log Planet stores an entry nobody reads |
| 1.4 | Distress: towed home and fined 15k to 80k MU; "There's no response" once the Starport is gone | STRINFO 1.2, 2.14 | STUB, prints text |
| 1.5 | Endurium price rises to 1500 and then 2000 MU with the notices of 20-02 and 15-05 | STRINFO 1.1, 1.4 | fixed price |
| 1.6 | Losing the terrain vehicle costs 10,000 MU and a replacement | STRINFO 1.2 | depends on 5.x hazards |
| 1.7 | Science skill decides whether an analysis succeeds | starship.txt (DRAFT formula: skill / 2); Ask on the formula | science skill unused |
| 1.8 | Flares: checked every day while the ship is in a system; a flare with the ship there ends the game ("incinerated"); Analysis shows "UNSTABLE, est. time to flare" under 1000 days | disys.txt `?FLARE`, STRINFO 2.14, notes | PR 122: every star flares at a ship in its system on its day; the Analysis text still to do |
| 1.9 | Nebulae act on the shields | priority.txt; how strongly: Ask | TODO, no effect |
| 1.10 | Starmap shows a fuel estimate | alpha.txt | no estimate |
| 1.11 | Alien vessels use their own armor, shields and speed from the data | xls Vessels; `GD_Vessel.m_armor`, `m_shields`, `m_moveDelay` unused | 100 points per class, one speed for all |
| 1.12 | Salvage from debris | wrecks leave debris (communic.txt); what can be taken: Ask | "Salvage collection not yet implemented" |

## Phase 2: Encounters that matter

| # | Item | Original (source) | Port today |
|---|---|---|---|
| 2.1 | Posture moves the aliens' mood (0 to 100, with friendly, diplomatic and hostile or obsequious bands; your strength decides between hostile and grovelling) | compart.txt, Notes.txt | posture only picks the lines (`Encounter.cs:1543`) |
| 2.2 | Homeworld warnings and surrender (22 and 8 lines in the data); the Spemin surrender after losses | STRINFO 2.x | lines skipped (`Encounter.cs:1561`, `1577`) |
| 2.3 | Trade and tribute: the Thrynn buy artifacts and plutonium for Endurium and sell the Black Box for 30; the Veloxi demand 3 Endurium; the Elowan give 15 if you carry under 20 | STRINFO 2.x; `GD_Artifact.m_thrynnPrice` unused | MISSING |
| 2.4 | Race rules: a crew member of the race removes the garbling; the Elowan refuse a ship with a Thrynn aboard; the Mechans will not deal without humans; Gazurtoid ships are immune to missiles; a comm link that is ended cannot reopen in the same encounter | compart.txt, alpha.txt, STRINFO 2.x, 7.x | garbling by comm skill only; the rest MISSING |
| 2.5 | The Noah 9 derelict (SOS) and the other races that now fall to the `default` case | STRINFO 2.x | they only fight back |

## Phase 3: Ruins, artifacts and messages on the ground

This is on the critical path to the endgame.

Rulings of 2026-10-09: place only what the sources document now (the 14 artifact sites and the 38 messages of the recovered data); random ruins come later (item 3.5); a message STRINFO puts at "random locations" lies in one ruin, at a place picked from the planet's seed; every ruin uses the five Ancient Ruins models for now, and an artifact a small marker; what has been taken from a planet is saved, deposits too. The manual (page 21): the terrain vehicle's Cargo picks up any item beside it and records the messages found in ruins, and "any messages you find are identified by the date found".

| # | Item | Original (source) | Port today |
|---|---|---|---|
| 3.0 | What has been taken from a planet stays taken: deposits now, the artifacts and messages with 3.2 and 3.3 | the manual (page 21) | PR 100: every disembark placed the planet again from its seed, so a deposit that had been picked up was back |
| 3.1 | Ruins at the sites of the recovered data: a ruin at each artifact site and at each message, the two formations (the Most Magnificent Hexagon of 6 ancient ruins on Sphexi, the City of the Ancients of 15) | planet.txt, dir.txt SEED-OV, data from 0.4 | the messages: PR 102, 12 ruins on Earth; the artifact sites: PR 108; the two formations: PR 109 |
| 3.2 | The 12 special artifacts at their 14 sites, taken with the Cargo button | STRINFO 4.1 | PR 108: in the ruins of their sites, taken once and saved, carried to the ship; the Black Box, the Flat Device and the Whining Orb are bought, not found |
| 3.3 | The 38 messages go to the ship's log under Messages, recorded with the Cargo button beside their ruin, dated the day they were found | STRINFO 3.1; the manual (page 21) | PR 102 |
| 3.4 | Dropping cargo, and the terrain vehicle's cargo display (ruled 2026-10-09: both, dropping first) | the manual (page 21): Cargo lists what the vehicle carries "and gives you the option of dropping anything", and a dropped object can be picked up again; the display is not in the sources | dropping: PR 110; the display: PR 112, shown while the cargo list is open |
| 3.5 | Random ruins: ancient crystal ruins with Endurium lumps or 1 to 4 "truly amazing" artifacts, Old Empire ruins with artifacts or writing | planet.txt (DRAFT only) | Ask: deferred by the owner on 2026-10-09 |

## Phase 4: Artifact effects

Only the effects that matter for winning have to come before Phase 5. The others can follow at any time.

| Artifact | Effect (source: Starport analysis, STRINFO) | How it is obtained | Needed to win |
|---|---|---|---|
| Crystal Orb | cancels the Crystal Planet's field: PR 115, with item 5.2 | Sphexi, 46N 14E | yes |
| Black Egg | planet bomb, armed by dropping it: PR 117 (countdown in orbit, the planet destroyed and saved) | three Old Empire sites | yes |
| Crystal Cone | finds the Crystal Planet's nexus from orbit: PR 118 (the messages report it on entering orbit and on Land; how it is shown is the port's choice) | Uhlek space | INFERRED yes |
| Ring Device | shows nearby fluxes (alpha.txt: fluxes are hidden without it and navigation skill) | Mars, 90N 0 | no; ruled 2026-10-10: fluxes are hidden without it. PR 132 |
| Whining Orb | translates Spemin: PR 126 | Starport, 6000 MU | no |
| Flat Device | shields the terrain vehicle from lifeforms | Starport, 30,000 MU | no, needs Phase 6 |
| Shimmering Ball | automatic cloak in combat; ruled 2026-10-10: the aliens cannot fire at the ship until it fires. PR 130 (3 s uncloaked after each shot) | 68,66 planet 1 | no |
| Rod Device | stronger laser shield: PR 127 (half of every alien laser hit; the amount is the port's choice) | New Scotland (site: D5) | no |
| Tesseract | doubles engine efficiency: PR 124 (half the engines' fuel in hyperspace) | 18,50 planet 5 | no |
| Crystal Pearl | warps a badly damaged ship away: PR 128 (out of an encounter below a tenth of the armor; the tenth is the port's choice) | City of the Ancients | no |
| Red Cylinder | finds ancient ruins from orbit | Koann (planet: D5) | no |
| Dodecahedron | attracts every ship nearby: PR 129 (twice the alien radar distance; the factor is the port's choice) | 118,146 planet 4 | no |
| Black Box, Hypercube, Ellipsoid | unclear (D5) | Thrynn trade, Earth, 81,98 | Ask |

## Phase 5: Main story and endgame

After this phase the game can be played from start to win.

| # | Item | Original (source) |
|---|---|---|
| 5.1 | Arth's sun flares on its date: the Starport is destroyed and the game goes on (distress gets no answer; the win message can still arrive). PR 121: day 300 unless won; a ship in Arth's system incinerated; no Starport model, no docking, no answer to distress | STRINFO 2.12, 2.14, Elowan comm, data (D5); disys.txt `?FLARE` |
| 5.2 | The Crystal Planet (192,152 planet 1): its field damages a ship without the Orb. PR 115: 5 points a second in orbit without the Orb (the rate is the port's choice) | STRINFO 5.1; disys.txt `'HEAT` (crystal planet heating routine) |
| 5.3 | Drop the armed Black Egg at 47N 45E to win. Anywhere else on the Crystal Planet: "damaged but not destroyed". On the Uhlek brain world (55,32 planet 2) the Uhlek fall silent. On Elan the Elowan young die. PR 117: the egg, the nexus, the damaged planet and the crew's two reports; PR 123: the Uhlek harmless, the Elowan mortal enemies | STRINFO 2.12, 5.1, SS Story; disys.txt `?BOMB` |
| 5.4 | Win sequence: the message, 500,000 MU, the Interstel medal, a supplemental evaluation; no more flares after the win. PR 120: the win saved, no flares, the bonus paid on docking, the evaluation in Operations | STRINFO 1.2, 2.12; disys.txt `?WIN`, `?FLARE`, `WMSG` |
| 5.5 | Every way to lose (flare, ship destroyed, all crew dead) ends through the same game over. PR 122: one destruction path, the game over says the cause; all crew dead waits for Phase 6 (crew death is only in the 1984 drafts) | STRINFO, disys.txt |

## Phase 6: Lifeforms and crew injury

Depends on D3. This is the largest piece and is not on the path to winning, which is why it comes after Phase 5.

| # | Item | Original (source) |
|---|---|---|
| 6.1 | Lifeforms created per planet from its seed, the same every visit | dir.txt VITA-OV, HP-OV, LP-OV, SEED-OV; planet.txt |
| 6.2 | Scan reports them (volume, aggression, intelligence, niche); Look describes them | planet.txt, lf-words.txt |
| 6.3 | Behaviour: approach, attack, flee, predator and prey, flying | lf-behav.txt |
| 6.4 | Stunner and laser; capture into stasis; sale at the Trade Depot; payment for recorded data | planet.txt (DRAFT values) |
| 6.5 | Attacks hurt the crew, scaled by durability; the Doctor treats them; death, the chain of command, DEAD on the personnel file | lf-words.txt, starship.txt, disys.txt |
| 6.6 | Weather and terrain hazards: storms, lava destroys the vehicle, uphill is slow | planet.txt, alpha.txt, dir.txt STORM-OV |

## Phase 7: Fidelity pass

- Compare every screen with `Research/Screenshots/` and every message with STRINFO.
- Correct the README's completion figure.
- Draw the planet in encounters that begin in orbit (ruling 5, later step).
- Alien captain and ship names (`%` and `+` in 15 greetings): Ask, since the data has no names.
- Crew learn by using their skills: DRAFT only, so Ask.

## Order

The critical path to a game that can be won is 0.1, 0.2 and 0.4, then Phase 3, the winning artifacts of Phase 4, and Phase 5. Phases 1 and 2 are independent small PRs that can run in between. Lifeforms (Phase 6) come last because they are the biggest item, depend on D3, and only the Flat Device needs them.

Rough size, counting one PR per item: Phase 0 about 5, Phase 1 about 12, Phase 2 about 5, Phase 3 about 4, Phase 4 about 12, Phase 5 about 5, Phase 6 about 6 to 10. That is about 50 PRs in all.
