# Sensor pictures traced from the original

The sensor display (`SensorsDisplay`) shows a vessel as two 1024 x 512 textures: a background (the ship in black on white, RGB) and a mask (white, opaque where the scan's colour noise shows, RGBA). Only ten of the 23 vessel types had a picture, drawn by hand. This tool makes the others from screenshots of the original game's sensor window in `Research/Screenshots/Ships/` (each `<Vessel> Analysis.png` shows the window alongside the analysis text).

## How a picture is made

1. **Window**: the magenta box in the top right quarter of the screenshot. Every pixel inside it that is not magenta belongs to the ship; every pixel that is neither magenta nor black is a coloured part (the pods, lights and markings the original draws in colour) and goes into the mask.
2. **Tracing**: the outline of the ship (and of each coloured part, holes included) is followed along the pixel edges and converted to the midpoints of those edges, so a regular staircase becomes a straight line. Douglas-Peucker simplification then drops the points that lie closer to the outline than the tolerance: at 0.4 pixels a single step in a straight edge stays a step (it is 0.5 away), while 1:1 and 1:2 staircases become straight; at 0.6 a single step becomes a slope as well.
3. **Size and position**: 10.8 canvas pixels per pixel of the 320 x 200 screen horizontally and 12.96 vertically (measured on the existing Spemin Scout picture, which matches the original game's 1.2 pixel aspect ratio on a 4:3 screen; an 800 x 600 screenshot is converted). The ship is centred on the canvas, as in the existing pictures, and scaled down if it would be wider than 80% or taller than 88% of it.
4. **Drawing**: the polygons are filled with the even-odd rule at 4 x 4 samples per pixel, so the edges come out smooth.

## Making one

From the project root, in PowerShell (the script runs its C# in Windows PowerShell 5.1, which has the complete `System.Drawing`):

```powershell
& "DevTools\SensorPictures\make.ps1" -Vessel "Elowan Transport" -Slot 5 -Tolerance 0.6
```

- `-Vessel`: the name used in `Research/Screenshots/Ships/<Vessel> Analysis.png` and in the files it writes, `Assets/Game Objects/UI/Sensors/Sensors - <Vessel>.png` and `... Mask.png`. A file without a `.meta` gets one with the import settings of the Spemin Scout picture and a new GUID; a file that already has one keeps it.
- `-Slot`: the scan type (`SensorsDisplay.ScanType`, the vessel id) whose entries in `m_backgroundTextures` and `m_maskTextures` of `Spaceflight.unity` receive the picture. Omit it to write the files only.
- `-Tolerance`: 0.4 for a machine (keeps single steps), 0.6 for an organic hull (turns them into slopes).
- `-SplitDiagonals`: keeps pixels that touch only at a corner apart (by default they are joined, which keeps thin diagonal lines whole).
- `-Screenshot`: uses a screenshot other than the Analysis one.

It also writes a review sheet to `previews/<vessel>.png`: on the left, the original game's window as it looks on a 4:3 screen; on the right, roughly what the port shows at the end of a scan.

## Files

| File | What it is |
|---|---|
| `make.ps1` | The command above |
| `SensorPicture.cs` | Finds the window, the scale and the position, and writes the textures and the review sheet |
| `Vectorize.cs` | Traces, simplifies and fills the outlines |
| `make-debris.ps1` | Makes a vessel's debris picture (see below) |
| `previews/` | The review sheets |

## Debris

A destroyed ship leaves debris, whose sensor picture is made from the ship's sensor picture by the repository's `process_texture_debris.py` (which needs Pillow). Its "Light Battle Damage" preset (seed 12345, 25% damage, scatter 2, pieces of at least 150 pixels) reproduces every debris picture made before this tool, mask included, pixel for pixel.

```powershell
& "DevTools\SensorPictures\make-debris.ps1" -Vessel "Elowan Transport" -Slot 5 -Python "C:\path\to\python.exe"
```

It writes `Assets/Game Objects/UI/Sensors Debris/Sensors - <Vessel>_debris.png` and `..._debris_mask.png`, gives new files the import settings of the Spemin Scout debris, and with `-Slot` wires the vessel's entries in `SensorsDisplay.m_debrisBackgroundTextures` and `m_debrisMaskTextures`. Regenerate the debris whenever a vessel's sensor picture changes.
