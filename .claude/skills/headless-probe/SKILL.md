---
name: headless-probe
description: Verify a gameplay fix in starflight-unity-port by running the real game headless in play mode, or run the headless compile check. Use when asked to reproduce a bug, check that a fix works, run or verify something "in the game" without the GUI Editor, or compile-check a branch or worktree of the Starflight Unity port.
---

# Headless probe for starflight-unity-port

The scripts live in `DevTools/HeadlessProbe/` and are tracked with the project. `DevTools/HeadlessProbe/README.md` is the reference: requirements, what the probe does, the scenario table, the helpers and the pitfalls. Read it before writing or changing a scenario. This skill only says how to run the scripts.

Run everything from the project root (the main checkout or a worktree) in PowerShell. Each checkout needs its own `Library/`: the first run in a new worktree imports everything and takes about four minutes. Unity refuses a project that another Editor already has open; an Editor on a different checkout is not disturbed.

## Compile check

```powershell
& "DevTools\HeadlessProbe\compile-check.ps1" -Name some-label
```

It compiles when Unity's exit code is 0 and the log shows `Tundra build success`. Look for `error CS` lines first.

## One scenario

```powershell
& "DevTools\HeadlessProbe\probe.ps1" -Scenario h6
```

The scenario names are in the README's "Scenarios that exist" table. Read the `[ClaudeProbe]` lines: the `RESULT` line, every failed `Check`, and the exception count. `-TimeoutSeconds 100` shortens the wait for a scenario that is expected to hang on the old code.

## All scenarios

```powershell
& "DevTools\HeadlessProbe\run-all-scenarios.ps1" -Tag some-label
```

One summary block per scenario. The README gives the count and the duration.

## A gameplay fix

1. Run the scenario on the old code and see it fail. A check that cannot fail proves nothing.
2. Run it on the new code and see it pass.
3. Add the scenario to `DevTools/HeadlessProbe/ClaudeProbe.cs` and its row to the README table in the same pull request.

## After a run

Run `git status --porcelain`. The probe deletes its copy in `Assets/`. Stage files by name. If `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset` is listed with an empty diff, the checkout is older than its `.gitattributes` line: restore it with `git checkout -- "<path>"`.
