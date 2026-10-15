# Contributing

Thanks for your interest in the Starflight Unity port! Bug reports, fixes and improvements are all welcome. This page covers how the project is run, so your change can go in smoothly.

## Getting set up

You need **Unity 6 (6000.3.13f1)**. Clone the repository, add its root folder in Unity Hub and open any of the four scenes in `Assets/Scenes/`; each one loads `Persistent` first. The [README](README.md#-build-from-source) has the details.

## The guiding principle: faithful to the original

The goal of this port is a *Starflight* that plays exactly like the 1986 game. The 270 star systems, 811 planets, prices and alien dialogue are the original's fixed data, so preserve original values whenever you touch game data. Where the research material in `Research/` (STRINFO, the original data sheets, the design notes and the decompiled source) documents how the original behaves, the port follows it. Behaviour found only in the 1984 draft notes, or anything new, deserves a discussion in an issue first.

## Making a change

- **One change per pull request**, on its own branch from the latest `master`.
- **Conventional commit messages**: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`, `ci:`, with an optional scope, for example `fix(combat): ...`.
- **Every change gets a `CHANGELOG.md` entry** under `## [Unreleased]`, and new features get a mention in the README.
- **Match the style of the file you edit.** Most of the code is the original remake's style: tabs, Allman braces, spaces inside parentheses (`Foo( a, b )`), `m_` fields, `c_` constants and a comment above nearly every statement. Newer code (`PlanetManager`, `ProceduralAdapter`) uses four spaces, `_camelCase` private fields and `[SerializeField] private`.
- **Input goes through the Input System.** The legacy `Input` class throws at run time in this project.
- **Be defensive**: bounds checks, divide-by-zero guards and iteration limits on `while` loops have all prevented real bugs here. Compare `UnityEngine.Object` references with an explicit `!= null`, not `?.` or `??`.
- **No em dashes** in code, comments or documentation.
- **No spoilers** in the README, the manual or release notes: the game's secrets are for players to discover.

## Testing

Every pull request runs a headless Unity compile check in CI, and a pull request is merged only once that check is green. You can run the same check locally with `DevTools/HeadlessProbe/compile-check.ps1`.

Gameplay changes are tested with the headless probe in `DevTools/HeadlessProbe/`, which plays the real Spaceflight and Starport scenes in batch mode with an in-memory save system. A gameplay fix comes with a probe scenario in the same pull request, and that scenario should fail on the old code and pass on the new. The probe's [README](DevTools/HeadlessProbe/README.md) explains how to run and write scenarios.

## Unity packages

Update one package per pull request, and compile-check it before you commit. The CI log lists `com.unity.sdk.linux-x86_64`, `com.unity.toolchain.linux-x86_64-linux` and `com.unity.sysroot.base` as added packages; that is noise from the build container, so never commit them.

## Reporting bugs

Please use the issue templates, and include your Windows version, what you did, what you expected and what happened instead. A saved game from `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Starflight\` helps a lot.
