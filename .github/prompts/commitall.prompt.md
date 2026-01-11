---
agent: 'agent'
model: 'Claude Opus 4.5'
description: 'Update documentation and commit all changes to GitHub'
---

Update project documentation based on changes, generate a conventional commit message, then commit and push to GitHub.

## Workflow
1. Run `git diff --stat` to see changed files
2. Run `git diff` on key files for details
3. Update CHANGELOG.md with dated entry
4. Update README.md if features changed
5. Stage all: `git add -A`
6. Commit with multi-line conventional message
7. Pull with rebase: `git pull --rebase`
8. Push: `git push`

## Requirements
- Run git diff to analyze all changes
- Update README.md with new features and changes
- Update CHANGELOG.md with dated entry
- Generate comprehensive conventional commit message (feat:, fix:, docs:, etc.)
- Include Unity-specific changes (scenes, prefabs, systems)
- Stage all changes with git add
- Create commit with detailed multi-line message
- Pull with rebase and push to origin

Constraints:
- Commit message must follow conventional format
- Must list all modified files and systems
- Note any breaking changes with BREAKING CHANGE: prefix
- Include game mechanics or features affected
- Reference Unity version compatibility if relevant

Success criteria:
- All documentation accurately reflects current code
- Commit message is detailed and comprehensive
- Changes successfully pushed to GitHub
- No merge conflicts
