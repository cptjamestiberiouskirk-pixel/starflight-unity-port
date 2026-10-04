---
paths:
  - "Assets/Scripts/Panel/*.cs"
  - "Assets/Scripts/Persistent/*.cs"
---

# Panels and persistent objects

- `PanelController` allows one active panel at a time and calls its `Tick()` each frame.
- `SaveGamePanel` lives in Persistent and is opened on Cancel with `SetCallbackObject(this)`. It forgets the callback object once it has called it: the panel outlives the scenes and must not hold on to their controllers.
