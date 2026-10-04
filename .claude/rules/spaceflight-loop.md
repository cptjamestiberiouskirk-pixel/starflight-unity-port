---
paths:
  - "Assets/Scripts/Spaceflight/**/*.cs"
---

# Spaceflight per-frame logic

- Per-frame logic early-outs on `SpaceflightController.m_instance.m_gameIsPaused`.
- `SpaceflightController.m_instance` is cleared in `OnDestroy`, so it is null in the other scenes: code that can run there has to check it.
- Hyperspace and the inside of a star system are separate coordinate spaces whose numbers overlap, so a mix-up gives positions that look possible. Take the player's position from `m_lastHyperspaceCoordinates` or `m_lastStarSystemCoordinates` to match the location of the encounter.
