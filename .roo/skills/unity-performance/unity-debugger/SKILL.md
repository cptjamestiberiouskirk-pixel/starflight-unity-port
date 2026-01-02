---
name: unity-debugger
description: Diagnoses Unity crashes, editor errors, and build failures by automatically reading the Unity Editor.log or Player.log files.
---

# Unity 6 Debugging & Log Analysis

When the user asks to "debug", "fix crash", "check logs", or "why did it break", follow this protocol:

## 1. Locate the Logs (Windows)
You have permission to access the user's local AppData folders to read logs.
- **Editor Log:** `%LOCALAPPDATA%\Unity\Editor\Editor.log`
  *(Note: This file contains the console output of the currently open Unity Editor.)*
- **Player Log (Builds):** `%USERPROFILE%\AppData\LocalLow\[CompanyName]\[ProductName]\Player.log`
  *(Note: Ask the user for their Company/Product name if checking a build crash.)*

## 2. Analysis Protocol
1.  **Read the file:** Use `read_file` to get the **last 300 lines** of the `Editor.log`. (Do not read the whole file; it can be gigabytes).
2.  **Identify the Error Type:**
    - **Native Crash:** Look for `Stacktrace:` followed by memory addresses (0x00...). This usually means an infinite loop or engine bug.
    - **C# Exception:** Look for `NullReferenceException`, `IndexOutOfRangeException`, etc.
    - **Unity 6 Specifics:**
        - **RenderGraph:** Look for errors involving `UnityEngine.Rendering.RenderGraphModule`.
        - **DOTS/Jobs:** Look for `JobTempAlloc` or `InvalidOperationException` in `Unity.Jobs`.
3.  **Cross-Reference:** Check the timestamp. Ensure the error is recent (at the bottom of the file).

## 3. Immediate Fix Actions
- If the error is a **Shader/Material** issue (common in Unity 6 URP), suggest checking the "Always Included Shaders" list.
- If the error is a **Lock/Busy** issue, suggest checking if a `Process` or `FileStream` was left open.
- If it is a **NullReference**, find the exact line number in the stack trace and cross-reference the user's script file.