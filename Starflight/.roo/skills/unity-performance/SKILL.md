---
name: unity-performance
description: Analyzes Unity C# scripts for common performance bottlenecks like garbage collection, heavy Update loops, and correct component caching.
---

# Unity Performance Optimization Guide

When the user asks to check performance or optimize a script, enforce these rules:

1.  **Update Loop Hygiene**
    - Flag any usage of `GetComponent()`, `Find()`, or `FindObjectOfType()` inside `Update()`, `FixedUpdate()`, or `LateUpdate()`.
    - Suggest moving these to `Awake()` or `Start()`.

2.  **String Concatenation**
    - Flag frequent string concatenation (e.g., `text.text = "Score: " + score;`) inside Update loops.
    - Suggest using `StringBuilder` or cached strings.

3.  **Physics Raycasts**
    - Ensure `Physics.Raycast` uses a `LayerMask` to avoid checking against every collider in the scene.
    - Warn if `RaycastNonAlloc` is not being used in hot paths.

4.  **Material Access**
    - Flag usage of `.material` (which creates a copy instance). Suggest `.sharedMaterial` if read-only access is needed.