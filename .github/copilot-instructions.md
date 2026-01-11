# Unity Development Instructions (Starflight Port)

## 1. Project Context & Architecture
**Project:** Starflight Port (1986 EA Space Exploration RPG)
**Engine Version:** Unity 6 (6000.3.2f1)

### Key Architectural Pillars
- **SpaceflightController:** Main game controller singleton. Use `m_instance` pattern strictly.
- **Persistence (`PD_*`):** All persistent data resides in `PD_` classes (e.g., `PD_General`, `PD_Ship`, `PD_CrewRoster`).
- **State Machine:** Game flow is strictly driven by the `PD_General.Location` enum.
- **Save System:** Uses `JsonSaveSystem`. Serializes to `Application.persistentDataPath`.

## 2. Core Coding Standards
- **Naming Conventions:**
  - **Public/Properties:** `PascalCase`
  - **Private Fields:** `m_camelCase` (e.g., `m_playerHealth`)
  - **Constants:** `UPPER_SNAKE_CASE`
- **Serialization:** Always use `[SerializeField] private` for exposed variables. Avoid `public` fields for Inspector exposure.
- **Null Checking:** Use `if (myObject != null)` explicitly for Unity Objects (overloads `==`). Use `?.` for standard C# objects.
- **Documentation:** Add XML documentation (`///`) to all public methods and complex algorithms.

## 3. Unity 6 & Performance Rules
- **Modern API Usage:**
  - Prefer `FindFirstObjectByType<T>()` over the legacy `FindObjectOfType<T>()`.
  - Use `TryGetComponent<T>(out var component)` instead of `GetComponent<T>()` when existence is uncertain.
- **Update Loop Hygiene:**
  - **NEVER** use `GetComponent`, `Find`, or `FindWithTag` inside `Update`, `FixedUpdate`, or `LateUpdate`. Cache these in `Awake`.
  - **NEVER** perform memory allocation (new List, string concatenation) inside `Update`.
- **Physics:** Always use `FixedUpdate` for Rigidbody calculations. Use `Time.fixedDeltaTime`.
- **Tags:** Use `CompareTag("Tag")` instead of `tag == "Tag"` to avoid garbage collection.

## 4. Defensive Coding (CRITICAL)
*Recent debugging has highlighted these specific failure points. Always validate:*
- **Array/List Bounds:** Check `index >= 0 && index < array.Length` before access.
- **Math Safety:** Check `denominator != 0` before division.
- **Loop Safety:** Add `maxIteration` guards to `while` and `do-while` loops to prevent editor freezes.
- **String Safety:** Check `!string.IsNullOrEmpty(str)` before accessing specific characters.
- **References:** When accessing a singleton (e.g., `SpaceflightController.m_instance`), always null-check it first unless inside a guaranteed flow.

## 5. Error Handling & Auto-Fix Protocol
When fixing Unity compilation errors or runtime exceptions:
1.  **Analyze:** Identify if the error is standard C# (syntax) or Unity lifecycle related (destroyed object access).
2.  **Locate:** Pinpoint the exact file/line.
3.  **Verify Context:**
    - Are `using UnityEngine;` or namespace declarations missing?
    - Is the script strictly inheriting from `MonoBehaviour` if it needs to be attached to a GameObject?
4.  **Fix Priority:**
    - Fix Compiler Errors > Runtime Exceptions > Warnings.
    - Ignore `Library/Temp` warnings.
    - **Test:** Ensure the fix does not break the `PD_*` persistence chain.

## 6. Composition & Style
- **Composition over Inheritance:** Logic should reside in Components. Avoid deep inheritance trees for GameObjects.
- **Separation of Concerns:** Keep logic (C# classes) distinct from data (ScriptableObjects or `PD_` classes).
- **Coroutines/Async:** Prefer `UniTask` (if installed) or standard Coroutines. Avoid `async void` in Unity lifecycle methods.

## 7. Documentation & Verification
- **Online Verification:** If you are unsure of a Unity 6 specific API (e.g., `RenderGraph`, `UIToolkit`) or suspect a method is deprecated, you must **search the web** before generating code.
- **Search Prompting:** Explicitly note in your thought process: "Searching Unity 6 documentation for [Feature]..." to trigger your web search tools.
- **Project Context:** Always use `@workspace` knowledge to check if a class (like `SpaceflightController`) is already instantiated before suggesting new singletons.