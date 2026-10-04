---
paths:
  - "Assets/Scripts/Spaceflight/Buttons/**/*.cs"
  - "Assets/Scripts/Spaceflight/Displays/*.cs"
  - "Assets/Scripts/Spaceflight/Locations/Encounter.cs"
---

# Ship console: buttons, displays, scan types

- A button whose `Execute()` returns true becomes the running button: `ButtonController` calls its `Update()` every frame, and returning true from that keeps the stick and the fire button away from the console. A long sequence (launch, landing, disembarking) returns true for as long as it runs and is ended by whatever changes the button set; for the landing and the launch that is an event of the camera animation (`PlayerCamera.PlayerHasLanded`, `PlayerHasLaunched`).
- A display's `Update` builds its text only when a value it shows has changed (see `StatusDisplay`). Building it every frame makes garbage every frame.
- `SensorsDisplay.ScanType` order matches vessel IDs and indexes Inspector arrays (`Encounter.m_alienShipModelTemplate`, debris templates, sensor textures): never reorder it, and bounds-check those lookups.
