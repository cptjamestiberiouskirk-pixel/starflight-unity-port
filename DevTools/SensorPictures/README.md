# Sensor pictures traced from the original

The sensor display (`SensorsDisplay`) shows a vessel as two 1024 x 512 textures: a background (the ship black on white, RGB) and a mask (white, opaque where the scan's colour noise shows, RGBA). Ten of the 23 vessel types had a picture, drawn by hand. This tool makes the others from the screenshots of the original's sensor window in `Research/Screenshots/Ships/` (`<Vessel> Analysis.png` shows the window as well as the analysis text).

## How a picture is made

1. **The window**: the magenta box in the top right quarter of the screenshot. Every pixel in it that is not magenta is the ship; every pixel that is neither magenta nor black is a coloured part (the pods, lights and markings that the original draws in colour), which goes into the mask.
2. **Tracing**: the outline of the ship (and of each coloured part, holes included) is followed along the pixel edges and turned into the midpoints of those edges, so a regular staircase becomes a straight line. Douglas-Peucker then drops the points that are less than the tolerance away from the outline: at 0.4 pixels a single step in a straight edge stays a step (it is 0.5 away) while 1:1 and 1:2 staircases become straight; at 0.6 a single step becomes a slope too.
3. **Size and place**: 10.8 canvas pixels per pixel of the 320 x 200 screen across and 12.96 down (measured on the existing Spemin Scout picture, which is the original's 1.2 pixel aspect on a 4:3 screen; an 800 x 600 screenshot is converted). The ship is centred on the canvas, as in the existing pictures, and made smaller if it would be wider than 80% or taller than 88% of it.
4. **Drawing**: the polygons are filled even-odd with 4 x 4 samples per pixel, so the edges are smooth.

## Making one

From the project root, in PowerShell (the script runs its C# in Windows PowerShell 5.1, which has `System.Drawing` whole):

```powershell
& "DevTools\SensorPictures\make.ps1" -Vessel "Elowan Transport" -Slot 5 -Tolerance 0.6
```

- `-Vessel`: the name in `Research/Screenshots/Ships/<Vessel> Analysis.png` and in the files it writes, `Assets/Game Objects/UI/Sensors/Sensors - <Vessel>.png` and `... Mask.png`. A file that has no `.meta` yet gets one with the import settings of the Spemin Scout picture and a new guid; one that has keeps it.
- `-Slot`: the scan type (`SensorsDisplay.ScanType`, the vessel id) whose entries in `m_backgroundTextures` and `m_maskTextures` of `Spaceflight.unity` get the picture. Leave it out to make the files only.
- `-Tolerance`: 0.4 for a machine (keeps single steps), 0.6 for an organic hull (turns them into slopes).
- `-SplitDiagonals`: keep pixels that touch only at a corner apart (by default they are joined, which keeps thin diagonal lines whole).
- `-Screenshot`: another screenshot than the Analysis one.

It writes a review sheet to `previews/<vessel>.png`: the original's window as it looks on a 4:3 screen on the left, and on the right roughly what the port shows at the end of a scan.

## Files

| File | What it is |
|---|---|
| `make.ps1` | The command above |
| `SensorPicture.cs` | Finding the window, the scale and the place, writing the textures, the review sheet |
| `Vectorize.cs` | Tracing, simplifying and filling the outlines |
| `previews/` | The review sheets |
