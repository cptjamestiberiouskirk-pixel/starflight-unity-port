---
agent: 'agent'
description: 'Update documentation, commit the changes on a branch and open a pull request'
---

Update project documentation based on changes, generate a conventional commit message, then commit on a branch and open a pull request.

## Workflow
1. Run `git status` and `git diff --stat` to see changed files
2. Run `git diff` on key files for details
3. Unless already on a branch for this change: `git fetch`, then `git switch -c <branch> origin/master` (one branch and one pull request per change)
4. Update CHANGELOG.md with a bullet under `## [Unreleased]`
5. Update README.md if features changed
6. Stage the changed files by name: `git add <path> <path>` (never stage everything at once: a probe run or Unity can leave unrelated files changed)
7. Commit with multi-line conventional message
8. Push the branch: `git push -u origin <branch>`
9. Open a pull request against `master`: `gh pr create --base master`
10. Squash-merge (`gh pr merge <number> --squash`) only once the `compile` check has passed

## Requirements
- Run git diff to analyze all changes
- Update README.md with new features and changes
- Update CHANGELOG.md with a bullet under `## [Unreleased]`
- Generate comprehensive conventional commit message (feat:, fix:, docs:, etc.)
- Include Unity-specific changes (scenes, prefabs, systems)
- Stage the changed files by name
- Create commit with detailed multi-line message
- Push the branch and open a pull request against `master`

Constraints:
- Commit message must follow conventional format
- Must list all modified files and systems
- Note any breaking changes with BREAKING CHANGE: prefix
- Include game mechanics or features affected
- Reference Unity version compatibility if relevant

Success criteria:
- All documentation accurately reflects current code
- Commit message is detailed and comprehensive
- Pull request open, and squash-merged once the `compile` check is green
- No merge conflicts
