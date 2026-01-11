---
agent: 'agent'
model: 'Claude Opus 4.5'
tools:
  - codebase
  - githubRepo
description: 'Analyze project for implemented features, missing functionality, and bugs'
---

Perform comprehensive analysis of the Unity project to identify implemented features, missing functionality, and potential bugs.

## Analysis Scope
- Focus on `Assets/Scripts/` and `Assets/UI/`
- Ignore Library/, Temp/, obj/ folders
- Check for TODO/FIXME/HACK comments
- Identify incomplete implementations

## Output Format
Generate PROJECT_ANALYSIS_REPORT.md with:

### 1. Implemented Features
| Feature | File | Status |

### 2. Missing Functionality  
| Item | Location | Priority |

### 3. Bugs & Issues
| Severity | Description | File:Line |

### 4. Recommendations
Prioritized list with complexity estimates

## Requirements
- Scan all code files to identify implemented features
- Compare against TODO comments and placeholder code
- List missing functionality and incomplete features
- Search for potential bugs (null references, memory leaks, logic errors)
- Check for Unity-specific issues (lifecycle problems, serialization)
- Detect performance bottlenecks

Constraints:
- Focus on C# scripts in Assets/ folder only
- Ignore Unity-generated files (Library/, Temp/, obj/)
- Prioritize critical bugs over minor issues
- Consider Unity best practices in recommendations

Success criteria:
- Comprehensive markdown report with sections:
  * Implemented Features (with file paths)
  * Missing Functionality (with TODO references)
  * Bugs & Errors (with severity: Critical/High/Medium/Low)
  * Recommendations (prioritized by impact)
- Each item includes file path and line numbers
- Estimated complexity for each task
- Recommended implementation order
