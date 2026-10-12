# STRINFO message audit

Roadmap Phase 7, "compare every message with STRINFO", step 1: every place where a message the player sees differs from STRINFO. Audited 2026-10-12 against `origin/master` at 6bbbc6d7. Nothing in this document changes the game; the fixes come in later PRs, one per area (see "Step 2" at the end).

The screen half of the Phase 7 item (comparing every screen with `Research/Screenshots/`) is not part of this audit.

## Sources and how they were compared

- **STRINFO** (`Research/Data/STRINFO.DOC`, "Starflight Information" by William Cassidy): a hand transcription of the original's texts, in capitals. Its text was taken out with a regular expression over printable byte runs (cp1252) into a plain text file of 6275 lines; "line" below means a line of that extract, and section numbers are STRINFO's own. The extract drops table cells of fewer than 4 characters (the short prices and volumes of 1.3, 1.3.1 and 1.4), so those tables were read again from the `.DOC` itself.
- **The shipped game**, through the decompiled original (`s-macke/starflight-reverse`, `starflt1-out/`): `data/instance.txt` is the game's INSTANCE file (543 comm phrases, 50 notice paragraphs, the ruin messages), `data.h` holds the Starport's analyses (`ANALYZETEXT_STRINGS`), and the overlays hold the texts written in the game's code (`COMMSPEC-OV.c` the special comm lines, `OP-OV.c` the evaluations, `HYPERMSG-OV.c` the flare screen, `COMM-OV.c` the distress call). It is a third source the task did not name, added because it settles most of the disagreements below. Some of `instance.txt`'s message slots hold junk (`EKSDKSDK...`), so a few messages cannot be checked there.
- **The port**: `Assets/Resources/Starflight Game Data.json` (the original data; "GD" below), `Assets/Resources/Starflight Recovered Data.json` (records recovered from STRINFO; "RD" below), and the strings in `Assets/Scripts/`.

A script matched every STRINFO paragraph with its nearest port text and compared them word by word; three read-only passes over the C# code covered what the player actually sees after the code's replacements. These are not counted as differences:

- **Case.** STRINFO is in capitals; the game data is in sentence case.
- **The Thrynn slur.** STRINFO spells only the Thrynn's first line slurred and notes "ALL FURTHER COMMUNICATIONS WILL BE SPELLED DISTINCTLY" (line 1104). The game data and the shipped game keep the slur ("Thiss isss"). Thrynn lines were compared with doubled letters collapsed on both sides.
- **Placeholders.** STRINFO prints one example of a name or amount where the game fills one in: the alien captains and ships (FINOCTZI, TOCHUPU, YRHEY, NG'SENNIHSARYRH, IART'L, THESSRR, with alternatives in brackets) are `%` and `+` in the game data, filled by `Encounter.AddComm`; `[CAPTAIN'S NAME]` is `*`, `[VESSEL NAME]` is `&`; the Veloxi tribute "3" and the Thrynn offers' amounts are `X` and `Y`, as in the shipped game's own templates (`COMMSPEC-OV.c`: "TRIBUTE OF X ARTH ENERGY CRYSTALS", "LET USS SSAY XXX UNITSS", "SSHIP AX XXXXXXXX... FOR XXXX CUBIC ARTH UNITXX"). How the port fills them is in section 1.4.
- **STRINFO's own notes** in square brackets ("[IF A THRYNN IS ABOARD SHIP:]", "[NOTE: ...]").
- **Spacing and punctuation alone** ("82, 148" for "82,148", ". . ." for "...", a comma more or less). They are counted, not listed one by one.

Labels: CONFIRMED (seen in the files or the code), INFERRED (one link unverified, named), HYPOTHESIZED. Nothing here was run in the game.

## Summary

| Area | STRINFO texts | Alike | Differ in words | Who is right where they differ |
|---|---|---|---|---|
| Comms, game data (2.1 to 2.11) | 398 paragraphs | 226 identical, 43 punctuation only, 58 the Thrynn slur only, 9 placeholders only | 47 | the game data: the shipped game has the game data's words in all 47 (CONFIRMED by script) |
| Comms not in STRINFO | | | | 178 game data lines have no STRINFO paragraph (STRINFO silent) |
| Notices (1.1) | 20 notices | all 20 in words | 0 against STRINFO | the shipped game differs from both in 4 places (section 2) |
| Evaluations (1.2) | 6 texts | 3 identical | 3 | see section 3; the shipped game has two words STRINFO lacks |
| Starport analyses (1.3) | 16 texts | 13 | 3 | see section 4 |
| Ruin messages (3.1) | 38 messages | 38 in words (RD was made from STRINFO) | 0 against STRINFO | the shipped game differs in 5 (section 5); one shows a literal "[NUMBER]" |
| Story and reports (2.12, 2.14, 5.1) | 8 texts | all in words | 0 | layout points only (section 6) |
| Interstel Police (2.13) | 6 texts | | 6 missing | D4 (left out) |

The main finding (CONFIRMED by script, 512 of 556 game data comm lines identical to the shipped game's phrases and 15 more differing only in punctuation; the others are the special lines of `COMMSPEC-OV.c`, also alike, and a few of the port's own, section 1.5): **the game data's comm lines are the shipped game's text, and where STRINFO differs from them it is STRINFO's transcription that slips.** No comm line of the game data needs changing.

The notices and the analyses are the other way round (INFERRED from the shared slips: the game data's notice and analysis texts have STRINFO's MINIMIAL, ATARXIA and "penalized recommendations", where the shipped game has MINIMAL, ATARAXIA and "penalized for recommendations"): those two lists of the game data were evidently typed from STRINFO, not taken from the game.

## 1. Communications

### 1.1 Where STRINFO's words differ from the game data's

Every row is a line of the game data (`m_commList`, GD line = the line of its `m_text`). "GD / STRINFO" gives the words that differ. In every row the shipped game (`instance.txt`, or `COMMSPEC-OV.c` for the special lines) has the game data's words, so every row is the owner's call only in name: the recommendation is to leave the game data as it is.

| GD id | Race | STRINFO (line) | GD line | GD / STRINFO | Category |
|---|---|---|---|---|---|
| 171 | Mechan | 2.1.1 (655) | 31516 | "habitable" / "INHABITABLE" | different wording (STRINFO's) |
| 149 | Mechan | 2.1.2 (675) | 31296 | "preceeded" / "PROCEEDED" | typo on both sides; the game data is the shipped spelling |
| 120 | Mechan | 2.1.4 (729) | 31006 | "travelled" / "TRAVELED" | spelling (STRINFO's) |
| 412 | Veloxi | 2.2 (789) | 33926 | "tresspassers" / "TRESPASSERS" | typo in the shipped game, corrected by STRINFO |
| 398 | Veloxi | 2.2 (800) | 33786 | "you are have" / "YOU HAVE" | different wording (STRINFO's) |
| 458 | Veloxi | 2.2.2 (860) | 34386 | "starting year 3400" / "STARTING 3400" | missing word in STRINFO |
| 436 | Veloxi | 2.2.3 (872) | 34166 | ends "Is annoy Veloxi too much." / no such sentence | missing sentence in STRINFO |
| 423 | Veloxi | 2.2.4 (890) | 34036 | "obviously us" / "CERTAINLY WE" | different wording (STRINFO's) |
| 448 | Veloxi | 2.2.5 (926) | 34286 | "multiples" / "MULTIPLIES" | typo in STRINFO |
| 74 | Elowan | 2.4 (949) | 30546 | "thyself forthwith." / "THYSELF[. . .]" | missing word in STRINFO (its editor marked the gap) |
| 18 | Elowan | 2.4 (953) | 29986 | "Forsooth" / "FOR SOOTH" | spelling (STRINFO's) |
| 13 | Elowan | 2.4 (969) | 29936 | "hast thou" / "HAS THOU" | typo in STRINFO |
| 2 | Elowan | 2.4 (998) | 29826 | "friends of the evil Thrynn." / "FRIENDS OF THE THRYNN," | missing word in STRINFO |
| 3 | Elowan | 2.4 (1001) | 29836 | "Elan, the sacred planet of our young and" / "ELAN, AND" | missing words in STRINFO |
| 59 | Elowan | 2.4.1 (1023) | 30396 | "was" / "IS" | different wording (STRINFO's) |
| 37 | Elowan | 2.4.2 (1031) | 30176 | "should they be given" / "IF GIVEN" | different wording (STRINFO's) |
| 39 | Elowan | 2.4.2 (1035) | 30196 | "judge" / "CONTROL" | different wording (STRINFO's) |
| 40 | Elowan | 2.4.2 (1037) | 30206 | "has" / "HAST" | different wording (STRINFO's) |
| 46 | Elowan | 2.4.2 (1049) | 30266 | "subterranian" / "SUBTERRANEAN" | typo in the shipped game, corrected by STRINFO |
| 50 | Elowan | 2.4.2 (1057) | 30306 | "wandering creatures of space" / "WANDERING CREATURES" | missing words in STRINFO |
| 51 | Elowan | 2.4.2 (1059) | 30316 | "within" / "IN" | different wording (STRINFO's) |
| 32 | Elowan | 2.4.3 (1065) | 30126 | "to" / "TILL" | different wording (STRINFO's) |
| 23 | Elowan | 2.4.4 (1073) | 30036 | "measured not in years ... millenium" / "NOT MEASURED IN YEARS ... MILLENNIUM" | word order and spelling (STRINFO's) |
| 27 | Elowan | 2.4.4 (1081) | 30076 | "Ancient" / "ANCIENTS" | different wording (STRINFO's) |
| 382 | Thrynn | 2.5 (1105) | 33626 | "Thynynthyrnn" / "THYNYNTHRYNN" | spelling (STRINFO's) |
| 381 | Thrynn | 2.5 (1140) | 33616 | "How mayy we be of serrvice" / "HOW MAY WE OF SERVICE" | missing word in STRINFO |
| 379 | Thrynn | 2.5 (1173) | 33596 | "conquencesss" / "CONSEQUENCES" | typo in the shipped game, corrected by STRINFO |
| 368 | Thrynn | 2.5.1 (1197) | 33486 | "undissucsssed" / "UNDISCUSSED" | typo in the shipped game, corrected by STRINFO |
| 352 | Thrynn | 2.5.2 (1215) | 33326 | "it once was possessed" / "IT WAS ONCE POSSESSED" | word order (STRINFO's) |
| 354 | Thrynn | 2.5.2 (1219) | 33346 | "arre are a race" / "ARE A RACE" | doubled word in the shipped game, corrected by STRINFO |
| 334 | Thrynn | 2.5.3 (1231) | 33146 | "fittissst" / "FITTEST" | the slur, or a typo of the shipped game |
| 337 | Thrynn | 2.5.3 (1237) | 33176 | "we" / "THE" | different wording (STRINFO's) |
| 340 | Thrynn | 2.5.3 (1243) | 33206 | "rise" / "R ISE" | typo in STRINFO |
| 333 | Thrynn | 2.5.4 (1258) | 33136 | "the" / "A" | different wording (STRINFO's) |
| 341 | Thrynn | 2.5.5 (1263) | 33216 | "98,79" / "98, 78" | different number (STRINFO's): 98,79 is the flux that leads to 69,66, next to the Shimmering Ball's 68,66 (STRINFO 6.1, line 2078: "98, 79 - 69, 66") |
| 77 | Gazurtoid | 2.6 (1285) | 30576 | "the" / "THIS" | different wording (STRINFO's) |
| 81 | Gazurtoid | 2.6 (1293) | 30616 | "is it written ... sufference" / "IT IS WRITTEN ... SUFFERANCE" | word order (STRINFO's); typo in the shipped game, corrected by STRINFO |
| 212 | Spemin | 2.7 (1317) | 31926 | "Your entire race" / "IN FACT, YOUR ENTIRE RACE" | words added in STRINFO |
| 294 | Spemin | 2.7 (1344) | 32746 | "occurances" / "OCCURRENCES" | typo in the shipped game, corrected by STRINFO |
| 247 | Spemin | 2.7.1 (1366) | 32276 | "Gurtblunk" / "GERTBLUNK" | spelling (STRINFO's) |
| 264 | Spemin | 2.7.1 (1383) | 32446 | "We're" / "WE ARE" | different wording (STRINFO's) |
| 242 | Spemin | 2.7.2 (1406) | 32226 | "know about the Uhlek" / "KNOW ALL ABOUT THE UHLEK" | word added in STRINFO |
| 230 | Spemin | 2.7.5 (1437) | 32106 | "Alright" / "ALL RIGHT" | spelling (STRINFO's) |
| 504 | Minstrel | 2.8 (1446) | 34846 | "share the tale" / "TELL THE TALE" | different wording (STRINFO's) |
| 191 | Nomad probe | 2.9 (1496) | 31716 | begins "Planet 67826AK - third planet of four" / begins "- SYSTEM COORDINATES" | missing words in STRINFO |
| 551, 552 | Noah 9 | 2.11 (1510, 1512) | 35316, 35326 | "Smelenuf" / "SMELEMUF" | spelling (STRINFO's) |

Two STRINFO paragraphs join two lines that the game data and the shipped game keep apart: 2.5 (line 1119) is GD 315 followed by GD 316, and 2.5.1 (line 1179) is GD 359 followed by GD 313. The lines are all there (CONFIRMED); whether the original says them one after the other is not known (STRINFO silent). This was reported elsewhere as "two Thrynn sentences missing"; they are not missing.

Where the shipped game has a typo that STRINFO corrects (412, 46, 379, 368, 354, 81, 294), the player of the original saw the typo. Keeping the game data as it is keeps the original's words, typos included; correcting them is a matter of taste and the owner's call.

### 1.2 Differences of punctuation or the Thrynn slur only

Not listed one by one: 43 lines differ only in spacing or punctuation (for example 148, 151, 152, 161, 129, 494, 467, 442, 68, 16, 512, the Minstrel songs 505 to 509, the Nomad data 188 to 190), and 58 Thrynn lines only in the slur. Of the game data's lines against the shipped game, 15 differ in punctuation only and 8 in spacing that the decompiled dump runs together ("FREQUENCIESPLEASE").

### 1.3 STRINFO silent

178 game data lines have no STRINFO paragraph (CONFIRMED by script). STRINFO lists one or a few lines per subject and posture; the game data has the whole set. By race: Human 38 (the Noah lines), Spemin 41, Veloxi 27, Mechan 26, Thrynn 24, Elowan 4, Gazurtoid 6, Nomad probe 3, Mysterion 2, Veloxi drone 2, Minstrel 2, Noah 9 3. Nothing to fix.

### 1.4 How the code fills the lines

`Encounter.AddComm` (`Encounter.cs:3281` to `3327`) fills `&` with "ISS " and the ship's name, `*` with the captain's name, and `%` and `+` (alien lines only) with a name made of the race's garble syllables (`GetAlienName`, `Encounter.cs:2869`); `WithAmount` (`Encounter.cs:2966`) fills `X` and `Y` where they stand between spaces. Everything filled in is garbled like the other words. CONFIRMED in code; nothing was run.

| GD id | Code | What the player sees | STRINFO (line) | Shipped game (`COMMSPEC-OV.c`) | Category |
|---|---|---|---|---|---|
| 300 (GD line 32806) | `Encounter.cs:1705` to `1708` | "...for 145 cubic Arth unitxx of energy crysstalsss": the template's "unitxx" is printed as it stands | "[AMOUNT] CUBIC ARTH UNIT(S)" (1112) | "FOR XXXX CUBIC ARTH UNITXX": XX is a field the game fills (INFERRED: "S " or blanks) | missing field: a bug |
| 300 | same | "your sship a Crystal Orb thiss artifact": no full stop after the name | "A(N) [ARTIFACT].  THIS ARTIFACT" (1112) | "SSHIP AX XXXX... THISS": no full stop either | alike with the shipped game; STRINFO adds the stop |
| 300 | same | "an A Mulligan" for the artifact named "A Mulligan" (vowel rule) | "A(N) [ARTIFACT]" | "AX" field (INFERRED: the same rule) | edge case of the port's |
| 299 (GD line 32796) | `Encounter.cs:1718` | "Let uss ssay 1 unitss" for one cubic meter | "[AMOUNT] UNITS" (1115) | "XXX UNITSS" | alike |
| 5 (GD line 29856) | `Encounter.cs:1420`, `1427` | "15 Arth units of fuel" while the gift is what fits in the hold, down to none; the Thrynn's offers likewise pay what fits (`Encounter.cs:1772`, `1790`) | "15 ARTH UNITS OF FUEL" (992) | | the words promise more than is given (the port's behaviour; STRINFO silent on a full hold) |
| 399 (GD line 33796) | `Encounter.cs:2958` | "tribute of 3 Arth energy crystals" | "TRIBUTE OF 3" (762) | "TRIBUTE OF X" | alike |
| 380, 381 | `GetAlienName` | one name per encounter for every `%`, so "third order commander %" and "Iamm called %" give the same name | YRHEY in 380 (1133), NG'SENNIHSARYRH in 381 (1140) | | different wording; that the original made the names up too is INFERRED from STRINFO's bracketed alternatives (roadmap Phase 7, done 2026-10-11) |
| 553 (GD line 35336) | `Encounter.cs` (DefaultEncounterUpdate) | "Captain, we're picking up a distress beacon signal" with no name | "CAPTAIN [CAPTAIN'S NAME], ..." (1505) | | missing field (see section 6) |

Lines STRINFO gives that the code never shows, or shows otherwise (CONFIRMED by reading the code paths, not run):

- **The Veloxi homeworld refusals.** `FindHomeworldWarning` (`Encounter.cs:3006` to `3054`) steps one counter through the hail, response and refusal lists together, and a greeting always comes first, so GD 473 (GD line 34536, "Us are not answer the questions...", STRINFO 2.2 line 810, the first refusal) is never said. When the player hails first, the fleet's only homeworld response is GD 483 (not in STRINFO), the last of its list, and the fleet attacks after it without any of STRINFO's Votiputox lines (808 to 816).
- **The Thrynn homeworld's last warning.** The game data marks GD 394 (GD line 33746, "You arre nnot wellcome in thiss ssspace...Akteron treaty") as a homeworld line, so the hail list is 386, 387, 388, 394 and the last warning before the attack is 394. STRINFO puts that line before its homeworld bracket (line 1166, the bracket at 1168) and ends the homeworld list with "YOU MUST PAY THE CONSEQUENCES" (1173, GD 388). The flag is game data (the owner's call); the order is in the code.
- **The Thrynn when the Elowan were met first.** The code says GD 298 ("Our inntelligenccce inndicatess that you havve allied with the Ellowannn"), and so does the shipped game's special code (`COMMSPEC-OV.c`). STRINFO puts GD 384 ("FEW ENEMIES HAVE LIVED TO REGRET...", line 1164) under that bracket; GD 384 is an ordinary hostile greeting in the data. The port follows the shipped game: no change.
- **The Veloxi question about the energy crystals** (GD 408, GD line 33886, STRINFO line 784): STRINFO's note says a yes makes the Veloxi raise their shields; nothing in the code handles the answer.
- **The Noah 9 beacon and the Minstrels' arrival** are the communications officer's reports in STRINFO (lines 1443, 1504) and are shown as alien "Receiving:" lines; the beacon's report and "SOS... SOS" come in random order (section 6).
- **The Gazurtoid**: their nine lines (STRINFO 2.6, lines 1285 to 1301) are subject 5 in the data and are said only as answers to the player's questions; the Gazurtoid have no statements of their own. STRINFO gives no order or occasion for them.
- **Aside, STRINFO silent:** the game data's subject 6 lines ("waiting for a response": Elowan 62 and 63, Mechan 175 and 176, Spemin 265 to 270, Thrynn 371 and 372, Veloxi 470 to 472) are never selected; the Gazurtoid hails 94 and 95 ("the planet of the Gazurtoid") are not marked as homeworld lines.

Closers STRINFO gives are all reachable (Mechan "NOW TERMINATING COMMUNICATIONS.", Veloxi "ENOUGH THE TALKING." and the buddy line, the Elowan's, the Thrynn's "FAREWELL FRIENDS" and the Spemin's).

### 1.5 The port's own comm lines

GD 513 "[Communications Terminated]", 534 "No.", 535 "Yes." are not in the shipped game's phrases (CONFIRMED); they are the port's. STRINFO is silent on them.

### 1.6 Missing: the Interstel Police (2.13)

The warning screen (2.13.1) and the police's four lines (2.13.2, lines 1549 to 1557) are not in the port; the shipped game has them (`instance.txt` line 321, `COMMSPEC-OV.c`). Left out under D4 (the copy protection arrest, recommendation: leave it out).

## 2. Starport notices (1.1)

The game data's 20 notices agree with STRINFO word for word (the one exception is "15TH" for STRINFO's "15 TH"). The shipped game differs from both in four places (CONFIRMED, `instance.txt` lines 1988 to 2058). These are changes to the game data, so they were the owner's call. Ruled 2026-10-12: the shipped game's words; done in the game data PR of that day (`fix/gamedata-shipped-words`):

| GD notice (date) | GD line | Game data and STRINFO | Shipped game | Category |
|---|---|---|---|---|
| 1 (02-01-4620), paragraph 8 | 26 | "and penalized recommendations of planets" | "AND PENALIZED FOR RECOMMENDATIONS OF PLANETS" | missing word |
| 3 (05-01-4620), paragraph 2 | 36 | "lost several ships in this vicinity" | "IN ITS VICINITY" | different wording |
| 9 (23-01-4620) | 66 | "Extensive experience on board the ISS Intrepid" | "EXTENSIVE EXPERIENCE AS COMMUNICATIONS OFFICER ON BOARD THE ISS INTREPID" | missing words |
| 14 (06-03-4620) | 91 | "minimial or no life" | "MINIMAL OR NO LIFE" | typo |

The shipped game also ends the notices with a "THE END" record (`instance.txt` line 2058), which is a terminator, not a notice (INFERRED).

How the notices are shown (`OperationsPanel.cs`, CONFIRMED in code): each starts with its date as DD-MM-YYYY, as STRINFO prints it; every paragraph is revealed by a "More" press with a blank line before it, so the eight objectives of 01-01-4620 and the four flux pairs of 18-01-4620 come one per press where STRINFO prints them as one list (different layout). "Today is DD-MM-YYYY" above the notice is the port's own (STRINFO silent). The original's notice screen has "NO NEW NOTICES", "REPEAT LAST NOTICE? [N Y]" and "PRESS [] TO ABORT CURRENT NOTICE" (`OP-OV.c`); STRINFO is silent on them, and the notices screen is one of the screens the screen comparison has left.

## 3. Evaluations (1.2)

The six texts are in RD `m_storyTextList` (from STRINFO 1.2) and shown by `PlayerData.cs` (colony evaluations), `DistressYesButton.cs` (towing), `DockingBay.cs` (terrain vehicle) and `OperationsPanel.cs`.

| Text | Where | Port (after the code's replacements) | STRINFO (line) | Shipped game (`OP-OV.c`) | Category |
|---|---|---|---|---|---|
| TerrainVehicleLost | RD line 1281 | "CONSEQUENLY, YOU HAVE BEEN FINED 10,000 MU." | "CONSEQUENLY" (353) | "CONSEQUENTLY" | STRINFO's typo, copied into RD |
| TowingCharges | RD line 1287 | "LIABLE FOR ANY COSTS INCURRED ... ON BEHALF SAID CREW." | same (359) | "LIABLE FOR ALL COSTS INCURRED ... ON BEHALF OF SAID CREW." | different wording and a missing word in STRINFO |
| EvaluationUnsuitable | `PlayerData.cs:254` | "DUE TO ITS CONDITIONS, THIS PLANET PROVED UNSUITABLE ..." when no reason applies | "[REASON: EXTREME HEAT, ...]" (339) | the reasons only ("EXTREME HEAT," "EXTREME COLD," "CRUSHING GRAVITY," "VIOLENT STORMS," "LACK OF WATER," "LACK OF OXYGEN,") | port's own text where STRINFO has one |
| Every evaluation | `OperationsPanel.cs` | the text alone; several evaluations at one docking give identical lines with nothing to tell them apart | STRINFO prints the texts only | "RECOMMENDATION: n", "SYSTEM: x,y", "ORBIT: n", "EVALUATION:" above each, "PRESS SPACEBAR FOR NEXT EVALUATION", "END OF EVALUATIONS" | missing lines (STRINFO silent; the shipped game has them) |
| The three supplemental evaluations | RD, `OperationsPanel.cs` | "SUBJECT: ..." and the body on consecutive lines | a blank line after the SUBJECT line (343 to 359) | | layout |
| No recommendations filed | `Starport.unity` | "You have not filed any colony world recommendations" | STRINFO silent | "YOU HAVE NOT FILED ANY" / "COLONY WORLD RECOMMENDATIONS" | alike |

What agrees (CONFIRMED in code): the unsuitable reasons are STRINFO's six words, joined "A, B AND C"; the first fine is 100 MU with STRINFO's warning, later fines drop the warning; `[REWARD]` and `[AMOUNT]` are filled with the bonus and the fine. The amounts of later fines (5000 MU a reason) and of towing (15,000 to 80,000 by distance) are the port's choices inside STRINFO's ranges.

## 4. Starport analyses and prices (1.3, 1.3.1, 1.4)

The analyses are in GD `m_artifactList` (`m_analysisText`), shown by `TradeDepotPanel.cs:462`.

| Artifact | GD line | Game data | STRINFO (line) | Shipped game (`data.h`) | Category |
|---|---|---|---|---|---|
| Rod Device (36) | 889 | "an enhanced laer shield" | "LASER SHIELD" (494) | "LASER SHIELD" | typo in the game data; fixed 2026-10-12 (the owner's ruling) |
| Hypercube (22) | 735 | "atarxia" | "ATARXIA" (470) | "ATARAXIA" | STRINFO's typo, copied into the game data; fixed 2026-10-12 (the owner's ruling) |
| Whining Orb (50) | 1043 | "what it does ... communications" | same (512) | "WHAT IS DOES ... COMMUNICATION." | the shipped game's typo ("IS"), STRINFO's wording ("COMMUNICATIONS") |
| Every analysis | `TradeDepotPanel.cs:462` | the text alone | "STARPORT ANALYSIS:" above each (416 and on) | | missing line (STRINFO's heading; INFERRED that it is the screen's and not the editor's) |
| Ellipsoid (15) | 658 | name "Ellipsoid" | "ELLIPSOID (FOCUSING STONE)" (455) | | STRINFO's note, most likely (INFERRED); no change |
| Terrain Vehicle Debris (44) | 977 | 200.0 m3, 0 MU, no analysis text of its own in GD | not in STRINFO | "THE REMAINS OF AN OLDER, INFERIOR MODEL TERRAIN VEHICLE, DOUBTLESSLY LEFT HERE BY SOME PRIOR EXPLORATION PARTY TOO FOOLISH TO RETURN TO THEIR SHIP BEFORE EXHAUSTING THEIR ENERGY." | STRINFO silent; the shipped analysis is missing (INFERRED that GD 44 is that item) |

Prices and volumes (CONFIRMED against the `.DOC`'s tables): Humming Gizzy (21) is 500 MU in the game data and 550 in STRINFO; the Shimmering Ball (38) is 0.3 m3 in the game data and 0.1 in STRINFO. The original's own data (`Research/Data/Starflight 1 Data.xls`, its Artifacts sheet from the game's files) has the game data's values: HUMMING GIZZY 0.2 m3 at 500 MU (1000 to buy), SHIMMERING BALL 0.3 m3. So STRINFO is the one that differs, and the game data stays as it is (the owner's ruling of 2026-10-12: the original's text and values); Adrynna's Gold (1) has 0.1 m3 and 2500 MU in the game data where STRINFO prints "?". Every other artifact, every Thrynn price and all 21 mineral prices agree, and Endurium's 1000, 1500 and 2000 follow the notices (`GD_Element.cs`).

## 5. Ruin messages (3.1)

The 38 messages of RD `m_planetMessageList` were recovered from STRINFO and agree with it word for word (another audit on 2026-10-12, PRs 227 and 228, compared all 163 recovered texts). Against the shipped game (`instance.txt`, MESSAGE records; CONFIRMED where the record is readable):

| RD id | STRINFO (line) | RD and STRINFO | Shipped game | Category |
|---|---|---|---|---|
| 0 (RD line 35) | 3.1 (1577) | "AND THE RECENT INFESTATION" | "AND A RECENT INFESTATION" | different wording (STRINFO's) |
| 22 (RD line 321) | 3.1 (1644) | "SURFACE TEMP. IT IS AT 150 DEGREES" | "SURFACE TEMP. IS AT 150 DEGREES" | word added in STRINFO |
| 29 (RD line 412) | 3.1 (1661) | "SIGN - MARDAN 4 MINING STATION NO. [NUMBER]", shown as it stands: the player sees the literal "[NUMBER]" (`TVCargoButton.cs:187`; nothing replaces it, CONFIRMED by grep) | "[NUMBER]" is the editor's field | "SIGN - MARDAN 4 MINING STATION NO. 17" (`instance.txt` line 1736; the site sign 28 is NO. 82 in both) | missing field: a bug |
| 29 | 3.1 (1660) | one ruin at a place picked from the seed (`TerrainRuins.cs:86`) | "SEVERAL LOCATIONS" | | missing places (the port's ruling of 2026-10-09: one ruin) |
| 33 (RD line 464) | 3.1 (1673) | "DECIPERABLE" | same | "DECIPHERABLE" | STRINFO's typo, copied into RD |
| 34 (RD line 477) | 3.1 (1676) | "AMIDST FILES OF ALIEN BOOKS" | same | "AMIDST PILES OF ALIEN BOOKS" | STRINFO's typo, copied into RD |

The shipped dump has junk in place of messages 2, 11, 18, 21, 31 and parts of 36 and 37, so those cannot be checked there. Two STRINFO entries have no quotation marks (3, "SOME ARCANE AND INDECIPHERABLE PHLEGMAK WRITING", and 33, "THE ONLY THING DECIPERABLE ..."): the port records them as messages found in the ruins ("Message recorded in the ruins:"), as the shipped game keeps them as MESSAGE records too (CONFIRMED for 3, line 1738, and 33, line 1826), so this is not a difference.

## 6. Story texts and reports (2.11, 2.12, 2.14, 5.1)

| Text | Where | Port | STRINFO (line) | Shipped game | Category |
|---|---|---|---|---|---|
| Noah 9's beacon | GD 555, `Encounter.cs:3504` | "Receiving:" then "Captain *, we're picking up a distress beacon signal", as if the derelict sent it | "[UPON ORBITING ..., THE COMMUNICATIONS OFFICER REPORTS:]" (1504) | | different framing: the officer's report is shown as a transmission |
| Noah 9's beacon, hailed first | GD 553, `Encounter.cs` (DefaultEncounterUpdate) | "Captain, we're picking up a distress beacon signal" | "CAPTAIN [CAPTAIN'S NAME], ..." (1505) | | missing field (the name) |
| Noah 9, order | `Encounter.cs` FindComm | the report and "SOS... SOS" come at random, 10 to 30 s apart | the report, then SOS (1505, 1507) | | order (INFERRED from the code; not run) |
| Crystal Planet destroyed | RD, `BlackEgg.cs:231` | "INCOMING HIGH PRIORITY MESSAGE" then the text, no picture | a picture between the two (1518, "EMBED") | | missing picture (HYPOTHESIZED: the medal) |
| Crystal Planet destroyed | RD | "RETURN TO TO STARPORT", "END TRANSMISSION)" | same (1522, 1523) | | STRINFO's own slips, copied; the shipped text is not in the files fetched |
| Flare death | RD, `StellarFlares.cs:93` | "... AND CREW WERE INCINERATED." then "GAME OVER" on the next line | a blank line before GAME OVER (1566 to 1568) | two new lines before "GAME OVER" (`HYPERMSG-OV.c`) | layout |
| Flare death, the date | `StellarFlares.cs:93` | DD-MM-YYYY | "[DD-MM-YY]" (1564) | a 10-character date, DD-MM-YYYY (`HYPERMSG-OV.c`, `DATE$>ADR` typed with length 10) | alike: STRINFO's field name only |
| Distress after the flare | RD, `DistressYesButton.cs:34` | "THERE'S NO RESPONSE!" then "CANCELLED" on the next line | a blank line between (1571, 1573) | "THERE'S NO RESPONSE!" (`COMM-OV.c`) | layout |
| Distress after the flare, in an encounter | `DistressYesButton.cs:24` | "Transmission is being jammed." | "THERE'S NO RESPONSE!" (1571), no exception given | | which check comes first is not known (HYPOTHESIZED) |
| Elan and the Uhlek | RD, `BlackEgg.cs:180, 184` | as STRINFO | 5.1 (1846, 2051) | | alike |

## 7. Texts written in C#

Almost every alien and story text comes from the two data files; very few C# literals restate a STRINFO text (CONFIRMED by the three passes over `Encounter.cs`, `CombatController.cs`, the console buttons, the Starport panels, `DockingBay.cs`, `PlayerData.cs`, `BlackEgg.cs`, `StellarFlares.cs` and the terrain vehicle's code).

Port's own text where STRINFO has one:

| File:line | Port | STRINFO (line) | Category |
|---|---|---|---|
| `DistressYesButton.cs:34` | fallback "THERE'S NO RESPONSE!" without "CANCELLED", used only if the RD text is missing | "THERE'S NO RESPONSE!" / "CANCELLED" (1571, 1573) | missing line, fallback only |
| `StellarFlares.cs:89` | fallback "INCINERATED.\nGAME OVER", used only if the RD text is missing | 2.14 (1562 to 1568) | different wording, fallback only |
| `PlayerData.cs:254` | "ITS CONDITIONS" | 1.2 (339), section 3 | port's own text |
| `DockingBay.cs:91` | "A new terrain vehicle has been installed. Interstel has fined you 10,000 MU - see Operations." | 1.2 (353) | a paraphrase on the docking screen; the full text is in Operations |
| `TVCargoButton.cs:172` | the ship's log heads a message with RD's place name ("FORMER ELOWAN HOME", "NOAH 9 WRECK SITE") | 3.1 headings (1583, 1589): the bracketed names are the editor's notes (INFERRED) | port's own wording |

Typos in C# texts the player sees (STRINFO silent, CONFIRMED): `TradeDepotPanel.cs:1020` "Insufficent funds", `SaveGamePanel.cs:376` "Missle Launcher", `PersonnelPanel.cs:726` "Androids skills are hard-wired".

The port's own texts where STRINFO is silent are many (the docking screen's lines, the Trade Depot's and Personnel's prompts, the bank ledger's labels, the distress call's "Transmit emergency distress call?" and TOW-US lines, the terrain vehicle's and lifeforms' reports from the 1984 notes, the combat messages, "Orbit established.", "There's no one to hail."). They are not differences from STRINFO, and the screen comparison is the place for them: several already follow the decompiled original (PRs 219 to 226).

## 8. Outside this audit

- STRINFO 5.1 (planet readings), 6.1 (fluxes), 7.x (ship readings) and 8.1 (the theme music) are data the sensors and analyses show, not messages. The alien ship analysis was matched to the original's words in PR 220.
- The screens (`Research/Screenshots/`): the other half of the Phase 7 item.

## Step 2: the fixes, one small PR per area

Under D1 the shipped game's text and STRINFO are both sources of the original's behaviour. Where they differ, the shipped game is the original itself, so the recommendation below takes its text; where the change would touch `Starflight Game Data.json`, it waits for the owner.

1. **Ruin messages** (RD only): message 29's "[NUMBER]" becomes "17", as in the shipped game (a bug: the player sees the bracket). Optionally, messages 0, 22, 33 and 34 in the shipped game's words. Probe check: the `ruins` scenario can read the recorded text.
2. **Evaluations** (RD and `PlayerData.cs`, `OperationsPanel.cs`): "CONSEQUENTLY", the towing text's "ALL COSTS ... ON BEHALF OF SAID CREW", the blank line after SUBJECT, and the original's "RECOMMENDATION: / SYSTEM: / ORBIT: / EVALUATION:" heading above each evaluation. Probe check: a recommendation filed and evaluated on docking.
3. **Story and reports** (RD, `StellarFlares.cs`, `DistressYesButton.cs`, `Encounter.cs`): the blank line before GAME OVER and after THERE'S NO RESPONSE!; the Noah 9 beacon as the officer's report with the captain's name, before the SOS.
4. **Comms in code**: whatever section 1.4 shows of the filled placeholders.
5. **Owner's call, the game data**: the four notice words of section 2, "laser" and "ataraxia" in the analyses (section 4), the Humming Gizzy's price and the Shimmering Ball's volume. No comm line of the game data needs a change.
