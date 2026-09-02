---
name: release
description: Release/versioning workflow for chamber_fos_soft. USE FOR every code change in this repo — bump the SemVer version, update the root CHANGELOG, merge to main, and hand back the version number plus the git command to update. DO NOT USE FOR pure questions or investigations that change no code.
---

# Release workflow (chamber_fos_soft)

The user (repo owner) makes **no code edits themselves** — every change goes
through Claude. On **every change that touches code or config**, follow this
workflow so the user always gets a version number and a one-line update command.

## Rules

1. **Bump the version on every change.** Single source of truth is
   `<Version>` in `src/VotschVc3.App/VotschVc3.App.csproj` (the app reads it from
   the assembly). Use SemVer:
   - **patch** (x.y.**Z+1**) — bugfix, doc/config-only, refactor, test-only.
   - **minor** (x.**Y+1**.0) — new feature or protocol/behavior addition.
   - **major** (**X+1**.0.0) — breaking change to behavior or config the user
     relies on. When unsure between two levels, ask the user briefly.

2. **Update the root CHANGELOG.** Add a new `## [x.y.z] – YYYY-MM-DD` section at the
   top of `CHANGELOG.md` (today's date), Slovak, in the existing Keep-a-Changelog style.
   `CHANGELOG.md` is the single canonical changelog used by the application.
   Do not create `CHANGELOG_<version>.md` release-note files. Never leave an
   `[Nezaradené]`, `[Nezverejnené]`, or `[Unreleased]` section rendered as a fake version.
   If such a heading exists in source markdown, the application parser must ignore it.

3. **Build & test before releasing.** Run the cross-platform Core tests
   (`dotnet test tests/VotschVc3.Core.Tests/VotschVc3.Core.Tests.csproj`). The WPF `VotschVc3.App` project is `net8.0-windows` and does not build on Linux — say so instead of claiming it was verified.

4. **Commit, then merge to `main`.** Commit on the working branch, push it, then fast-forward/merge into `main` and push `main`. Tag the release `vX.Y.Z` and push the tag. (The user has standing permission for these `main` pushes — this is the one repo where that is expected.)

5. **Report back, every time, with exactly:**
   - the **new version number** (e.g. `v1.25.0`), and
   - the **git command** the user runs to update their local copy to it:
     ```
     git checkout main && git pull origin main
     ```
     (or, to pin the exact tag: `git fetch --tags && git checkout vX.Y.Z`).

## Notes
- Keep commit messages descriptive; do not include model identifiers.
- If a change is genuinely code-free (a pure question/investigation), this workflow does not apply — don't bump the version.
