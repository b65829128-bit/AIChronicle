# Contributing to AI Chronicle

Thank you for your interest! This is an experimental hobby project open-sourced so the *concept* of agent-driven game worlds can grow beyond its original author. Pull requests, issues, and design discussions are all welcome.

## First: read the docs

- **`README.md`** — what the mod does.
- **`README_MOD.md`** — full Chinese feature documentation (features, UI entries, MCM settings).
- **`AGENTS.md`** — architecture, build commands, Harmony patterns, directory map. Written for AI agents, but it's the best human onboarding guide too.
- **`docs/linux-dev.md`** — Linux development workflow (environment, build, package, Proton runtime, logs).

## Project status / expectations

- The codebase was largely written with AI assistance; the author's role was design and logic review.
- It targets **Bannerlord v1.4.8**. Game updates may break it. Please state your game version when reporting issues.
- All comments and prompts are currently in **Chinese**. English contributions are welcome, but please keep existing Chinese comments intact unless you update the corresponding docs too.
- No test framework exists yet. **Do not break the build**; verify by compiling locally.

## Local build

The primary development environment is Linux. Windows is only needed for the `net6` Xbox/Store target and dnSpy/attach debugging.

```bash
# 1. Game path: copy local.props.example -> local.props and edit it, or export:
export BANNERLORD_GAME_DIR="/path/to/Mount & Blade II Bannerlord"

# 2. Build (Linux builds net472 only; auto-deploys to Modules/AIChronicle)
dotnet build -c Release -f net472

# 3. Full clean build when in doubt
dotnet clean -c Release && dotnet build -c Release -f net472

# 4. Package a release zip
python3 scripts/package.py
```

The project builds against game assemblies (not NuGet-only), so you need a local Bannerlord install (on Linux this is the Steam/Proton Windows install). See [`docs/linux-dev.md`](docs/linux-dev.md) for deployment, Proton runtime, logs and debugging limits.

## The documentation rule (important)

This project treats documentation as part of the work, not an afterthought. **Every code change must update the docs.**

Before finishing a change, run through the self-check in `AGENTS.md` ("代码修改后文档自检清单"):

- New/deleted files → update `README_MOD.md` file structure + `AGENTS.md` directory map
- New/modified feature → update `README_MOD.md`
- New/changed MCM setting → update `README_MOD.md` settings table
- Architecture/entry-point changes → update `AGENTS.md`
- New NuGet dependency → update `AGENTS.md`
- Changed default behavior → update `README_MOD.md`

Changes to `AGENTS.md` / `README_MOD.md` require the author's explicit consent first (they are the project's contract). Unsubmitted doc updates are grounds for a review request.

## Code conventions (summary)

- Follow existing style; this is a C# net472 project (net6 for Store/Xbox on Windows) with nullable enabled.
- Harmony patches: `PatchAll()` silently skips some campaign-behavior types — for those, register manually in `OnGameStart` via `Type.GetType` + `harmony.Patch` (see `SubModule.cs`).
- New tools need three synchronized places: the tool definition JSON, the `ToolExecutor` switch case, and (for capability-gated tools) `ContextBuilder.CapabilityToolMap`.
- Tool descriptions live in `tools.json` / `agent_tools.json` (English, opencode-style) — they drive tool-calling behavior, not the system prompt.
- New game-state-mutating tools must dispatch through `MainThreadExecutor` (game objects are main-thread-only).

## What to contribute

- **Bug fixes** — please include reproduction steps and your game version.
- **Tool / prompt improvements** — prompt files are hot-reloadable; often the highest-value, lowest-risk contribution.
- **Tests / CI** — there is no test framework yet. The manual build workflow (`.github/workflows/build.yml`, Ubuntu) still needs a game-DLL source; local Linux builds are the primary verification.
- **Documentation** — English docs, API-key quick-starts, setup videos.
- **Design discussion** — the agent-driven-world concept is the real point of the project; open an issue for anything you think could be more alive.

## Pull request process

1. Fork, branch, change.
2. Compile locally (`dotnet build -c Release -f net472`, 0 errors).
3. Update the docs per the documentation rule.
4. Open a PR describing *what*, *why*, and *how you tested* (even "loaded a save and chatted with a lord" is useful).

Keep PRs focused and small — review bandwidth is limited and everything here is single-file-heavy until refactors land.

## Issue template (minimal)

- Game version: ______
- Prerequisite versions: ______
- API provider/model: ______
- What you did / what happened / what you expected
- Any `ButterLib` error popup text or `ModLogs` excerpts
