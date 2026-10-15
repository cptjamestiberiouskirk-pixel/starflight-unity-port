# Original game records

The original game stores its data as a tree of records ("instances") in `STARB.COM`: every race, phrase, planet, artifact and so on. The decompiled original ([s-macke/starflight-reverse](https://github.com/s-macke/starflight-reverse)) prints the tree with its strings in `starflt1-out/data/instance.txt`, but not the records' other fields, the numbers. This tool reads those.

## The format

As `src/extract/instance.c` in the decompiled source reads it:

- The tree starts at offset `0x1006` of `STARB.COM`.
- Each record starts with an 11-byte header: the next record, the previous record and the first child (3 bytes each, little-endian), followed by the class (1 byte) and the species (1 byte).
- The fields follow the header, at the offsets the overlays name: `IFieldType BASE_dash_EDL = {ORIGINATORIDX, 0x12, 0x01}` is the byte at offset `0x12` of an ORIGINATOR record.
- The tool walks the tree depth first, in the same order as `instance.txt`, so its n-th record is the n-th line there. That is how a class number is found: it is the class of the record on the line that names it.

Classes identified so far: `0x34` ORIGINATOR (a race; its species is the port's `GameData.Race` value).

## Usage

Read the original game's files in place and never modify them. The records a new game starts from are in `STARFLT/START/STARB.COM` of the GOG install.

```powershell
python -I "DevTools\OriginalRecords\walk_records.py" "C:\Program Files\GOG Galaxy\Games\Starflight\STARFLT\START\STARB.COM" --races
```

- No option: the first 60 records with their class, species and address, and the total count (3489).
- `--class N`: every record of that class with its field bytes; `--from` and `--to` select which bytes (default `0x0b` to `0x1a`).
- `--races`: the conversation fields (COMM-OV) of the race records, one row per race.

## Conversation fields of the races

What `--races` shows, and how COMM-OV uses each field:

| Field | Offset | What COMM-OV does with it |
|---|---|---|
| TC | 0x0b | (not yet investigated) |
| T% | 0x0c | talkativeness (`%TALK`) |
| CSF | 0x0d | (not yet investigated) |
| BASE-EDL | 0x12 | the "emotional disposition level" an encounter starts with (`INIT_ACOMM`), plus 5 if the player's shields are up and 10 if the weapons are armed |
| AET, BET, CET | 0x13 to 0x15 | the EDL thresholds (`EDL>POSTURE`): the number of thresholds the EDL reaches gives the posture (0 friendly, 1 neutral, 2 hostile, 3 fight), with obsequious instead of hostile or fight when the player is much stronger |
| OPE, FPE, HPE | 0x16 to 0x18 | signed: how far the player's obsequious, friendly and hostile postures move the EDL (`POSTURE-EFFECT`) |
| MSW | 0x19 | (not yet investigated) |
