# The original's records

The original game keeps its data as a tree of records ("instances") in `STARB.COM`: every race, phrase, planet, artifact and so on. The decompiled original ([s-macke/starflight-reverse](https://github.com/s-macke/starflight-reverse)) prints the tree with its strings in `starflt1-out/data/instance.txt`, but not the records' other fields, the numbers. This tool reads them.

## The format

As `src/extract/instance.c` of the decompiled original reads it:

- The tree starts at offset `0x1006` of `STARB.COM`.
- A record starts with an 11-byte header: the next record, the previous one and the first child (3 bytes each, little-endian), then the class (1 byte) and the species (1 byte).
- The fields come after it, at the offsets the overlays name: `IFieldType BASE_dash_EDL = {ORIGINATORIDX, 0x12, 0x01}` is the byte at offset `0x12` of an ORIGINATOR record.
- The tool walks the tree depth first, in the same order as `instance.txt`, so its n-th record is the n-th line there. That is how a class number is found: the class of the record on the line of its name.

Classes found so far: `0x34` ORIGINATOR (a race; its species is the port's `GameData.Race` value).

## Using it

Read the original's files where they are and never change them. The new game's records are in the GOG install's `STARFLT/START/STARB.COM`.

```powershell
python -I "DevTools\OriginalRecords\walk_records.py" "C:\Program Files\GOG Galaxy\Games\Starflight\STARFLT\START\STARB.COM" --races
```

- No option: the first 60 records with their class, species and address, and the count (3489).
- `--class N`: every record of that class with its field bytes, `--from` and `--to` for which ones (default `0x0b` to `0x1a`).
- `--races`: the race records' conversation fields (COMM-OV), one row per race.

## The races' conversation fields

What `--races` shows, as COMM-OV uses them:

| Field | Offset | What COMM-OV does with it |
|---|---|---|
| TC | 0x0b | (not looked into) |
| T% | 0x0c | talkativeness (`%TALK`) |
| CSF | 0x0d | (not looked into) |
| BASE-EDL | 0x12 | the "emotional disposition level" an encounter starts with (`INIT_ACOMM`), plus 5 if the player's shields are up and 10 if the weapons are armed |
| AET, BET, CET | 0x13 to 0x15 | the EDL thresholds (`EDL>POSTURE`): how many the EDL reaches gives the posture, 0 friendly, 1 neutral, 2 hostile, 3 fight, obsequious instead of hostile or fight when the player is much stronger |
| OPE, FPE, HPE | 0x16 to 0x18 | signed: how much the player's obsequious, friendly and hostile posture moves the EDL (`POSTURE-EFFECT`) |
| MSW | 0x19 | (not looked into) |
