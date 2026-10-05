---
paths:
  - "Assets/Scripts/Player Data/**/*.cs"
  - "Assets/Scripts/Game Data/**/*.cs"
  - "Assets/Scripts/Persistent/DataController.cs"
  - "Assets/Scripts/Systems/SaveSystem/**/*.cs"
  - "Assets/Resources/Starflight Game Data.json"
---

# Save data and game data serialization

Saves are read and written with `JsonUtility` (`JsonSaveSystem`). The game data is read with `JsonUtility` (`DataController.cs`) and never written. Do not break the `PD_*` persistence chain.

- Only Unity-serializable fields persist. Dictionaries, properties and multi-dimensional arrays are silently dropped (`PD_General.m_lastCommIds` used to be `int[,]` and lost its data on every load; it is now a flat `int[]` behind `GetLastCommId()` / `SetLastCommId()`).
- Nested containers (arrays or lists of lists) are dropped too: wrap the inner list in a `[Serializable]` class (see `PD_ShipsLog.EntryList`).
- `PlayerData.c_currentVersion`: any slot with a different version is reset to a new game on load. Bump it only for intentionally breaking `PD_*` changes; otherwise add backward-compat null/length checks for fields missing from older saves.
- Enums are stored as ints: append new values, never reorder.
- Game data JSON keys must match field names exactly: a misspelled key is silently ignored.
