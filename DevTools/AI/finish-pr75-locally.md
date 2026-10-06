# Finish PR #75 in a local Claude Code session

Prompt for a Claude Code session on the Windows machine that has the Unity Editor and Blender. The cloud session that
wrote PR #75 could not reach either. Delete this file once PR #75 is merged.

Start Claude Code in the repo root with the Unity Editor open on this project, then paste the prompt below (or say
"do what DevTools/AI/finish-pr75-locally.md says").

```text
Finish PR #75 (branch ccr-79c4aca3-rkdg9p) in this repo on this machine. A cloud session wrote the code; CI has compiled it,
but nothing has run in the Unity Editor, in play mode, on Windows, or in Blender. Follow CLAUDE.md (evidence labels,
no em dashes, stage files by name, CHANGELOG bullet for each change). Do not change game code outside Assets/Scripts/AI,
Assets/Tools/Editor/SemanticKernelDefineSync.cs, Assets/Tools/Editor/SciFiCrateImporter.cs and DevTools/Blender.

1. git fetch, check out ccr-79c4aca3-rkdg9p, pull. Read the "AI orchestration" section of CLAUDE.md, the "AI Orchestration"
   section of README.md and the PR description.

2. Unity Editor: I have the project open (or will open it). Use the Unity CLI bridge (`unity status`, then
   `unity command console`, `recompile`, `recompile_status`, `eval`, `editor_play`, `editor_stop`; run
   `unity command --help` to confirm syntax). If the bridge is not reachable, tell me and stop.
   - Trigger a recompile and wait for it. Confirm NuGetForUnity restored Microsoft.SemanticKernel.Core 1.80.1 and its
     dependencies into Assets/Packages, that the console shows
     "SemanticKernelDefineSync: ... added STARFLIGHT_SEMANTIC_KERNEL", and that Starflight.AI and Starflight.AI.Editor
     compiled.
   - Read the whole console. List every error and warning that mentions Assets/Packages, Starflight.AI, NuGet, or the
     new editor scripts. Expected and harmless: "will not be loaded due to errors" for
     System.Text.Json.SourceGeneration.dll and Microsoft.Extensions.Logging.Generators.dll under analyzers/. Check
     whether those two come back after a second recompile and report it. If there are "Assembly Version Validation"
     errors, report them before changing that Player setting.

3. Check that `claude --version` works from a plain shell and that claude.exe (native install) or claude.cmd (npm) is
   on the PATH that the Unity Editor sees.

4. Blender: find blender.exe (PATH, or C:\Program Files\Blender Foundation\*). Run
   `blender --background --python DevTools/Blender/make_scifi_crate.py` from the repo root. It writes
   Assets/Models/SciFiCrate.fbx. Confirm the file exists and report the face count it prints.
   If blender.exe can't be found, ask me for the path. Only use the Blender MCP if it's already in `claude mcp list`.

5. Scene: create and open a new empty scene Assets/Scenes/AI Sandbox.unity (do NOT add it to Build Settings, do NOT
   touch Persistent, Intro, Starport or Spaceflight). Through eval, run
   EditorApplication.ExecuteMenuItem("Starflight Remake/AI/Add Semantic Orchestrator To Active Scene") and
   EditorApplication.ExecuteMenuItem("Starflight Remake/AI/Import SciFi Crate Into Active Scene"). Confirm in the
   console and the scene that SemanticOrchestrator has MainThreadDispatcher and MetagameOrchestrator, that
   Assets/Models/SciFiCrate.prefab exists, and that the crate is at (0,0,0). Save the scene.

6. Play-mode test: set "Simulate On Start" on the MetagameOrchestrator (field m_simulateOnStart, via SerializedObject
   in eval; the Starflight.AI assembly is not auto-referenced, so find the type by name if needed), save, then
   editor_play. Wait up to 3 minutes, reading the console. Pass = a "[WorldState] Faction event 1: ..." line from the
   plugin followed by a "[WorldState] <narration>" line from the orchestrator, with no exceptions. Then editor_stop
   and set Simulate On Start back to false. If it fails, root-cause it (the claude process's stderr is in the
   exception message), fix it in Assets/Scripts/AI, and run the test again. Report what you saw, labelled
   CONFIRMED/INFERRED.

7. Commit on ccr-79c4aca3-rkdg9p, by name: Assets/Packages/ (with .meta files), Assets/packages.config,
   ProjectSettings/ProjectSettings.asset (only the STARFLIGHT_SEMANTIC_KERNEL define line plus whatever Unity
   rewrote; tell me what else changed before staging it), Assets/Models/ (FBX, prefab, metas), the sandbox scene, and
   any fixes. Do not stage the Unity container-noise packages listed in CLAUDE.md. Update the CHANGELOG bullet and the
   PR description's "How it was checked" with the new results. Push, then watch CI on PR #75 until compile is green.
   Do not merge; tell me when it's ready.
```

If step 2 fails because the Editor can't find `claude`, the Editor's PATH is the first suspect: an Editor started before
Claude Code was installed may not see it. The service also looks in `%USERPROFILE%\.local\bin` and `%APPDATA%\npm`.
