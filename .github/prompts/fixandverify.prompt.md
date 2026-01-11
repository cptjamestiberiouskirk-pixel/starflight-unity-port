---
agent: 'agent'
model: 'Claude Opus 4.5'
tools:
  - codebase
  - editFiles
  - problems
description: 'Fix all Unity errors and verify fixes work without creating new errors'
---

Scan all C# files for Unity compilation errors, fix each error, then verify fixes work correctly.

## Fix-Verify Loop
1. Get all current errors using Problems panel
2. Fix each error with minimal changes
3. Wait for Unity recompilation
4. Check for remaining/new errors
5. Repeat until clean (max 5 iterations)

## Requirements
- Scan all .cs files and identify compilation errors
- Fix each error following Unity C# best practices
- After applying fixes, save all files
- Wait 5 seconds for Unity auto-compilation
- Check if any errors remain or new errors appeared
- If errors exist, analyze and fix them
- Repeat the fix-verify cycle until Unity Console is clean
- Add missing using statements (UnityEngine, System.Collections, etc.)
- Fix null reference and type mismatch errors
- Verify GetComponent<T>() patterns are correct

Constraints:
- Maintain Unity naming conventions (PascalCase public, camelCase private)
- Don't modify Unity-generated files in Library/ or Temp/
- Preserve existing code architecture and patterns
- Keep fixes minimal and targeted
- Maximum 5 fix-verify iterations to prevent infinite loops

Success criteria:
- All compilation errors are resolved
- No new errors introduced by fixes
- Unity Console shows zero compilation errors
- Report detailed summary:
  * Total errors fixed
  * Files modified with line numbers
  * Number of fix iterations required
  * Confirmation that Unity compiles successfully
