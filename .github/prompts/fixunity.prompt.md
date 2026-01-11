---
agent: 'agent'
model: 'Claude Opus 4.5'
tools:
  - codebase
  - editFiles
  - terminalLastCommand
description: 'Scan and automatically fix all Unity C# compilation errors'
---

Scan all C# files in the workspace for Unity compilation errors, identify all errors with file paths and line numbers, then automatically fix each error.

## Steps
1. Use #tool:codebase to search for error patterns
2. Read each file with errors to understand context
3. Apply minimal, targeted fixes
4. Verify no new errors introduced

## Requirements
- Review recent git changes to understand context
- Locate all .cs files with compilation errors
- Fix errors following Unity C# best practices
- Ensure MonoBehaviour lifecycle methods are correct
- Add missing using statements (UnityEngine, System.Collections, etc.)
- Fix null reference and type mismatch errors
- Verify GetComponent<T>() patterns are correct

Constraints:
- Maintain Unity naming conventions (PascalCase public, camelCase private)
- Don't modify Unity-generated files in Library/ or Temp/
- Preserve existing code architecture and patterns
- Keep fixes minimal and targeted to the specific error

Success criteria:
- All compilation errors are resolved
- No new errors introduced by fixes
- Code compiles successfully in Unity Editor
- Report summary of what was fixed with file names and line numbers
