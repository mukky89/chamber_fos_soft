---
name: wpf-settings-persistence
description: Add, change, migrate or review persisted settings and local JSON data in VotschVc3. Use for UI preferences, chamber configuration, users, notifications, integrations, profiles and recovery checkpoints. Do not use for temporary ViewModel-only state or unrelated CSV recording logic.
---

# VotschVc3 settings and persistence

All user-owned application data belongs under `AppPaths`. Do not introduce a second root path or write settings into the application installation directory.

## Before changing a setting

1. Classify it as global, per chamber, per user, per profile, per integration, or per calibration device/workspace.
2. Locate the existing model, store, owning ViewModel, load path, save path, and settings surface.
3. Define a safe default for fresh and existing installations.
4. Decide whether a missing JSON member is sufficient or an explicit one-time migration marker is required.
5. Preserve legacy and unknown user data whenever practical.

## Store behavior

- JSON load failures must not crash startup.
- Do not silently overwrite a malformed source file during load or immediate autosave.
- Surface actionable save failures through the owning ViewModel or operator notification path.
- Follow the serialization and locking conventions of the selected store.
- Use a temporary-file replacement for recovery checkpoints and other interruption-critical state.
- Never log or expose passwords, password hashes, API keys, SMTP secrets, or bearer tokens.
- Deletion, reset, reassignment, and migration cleanup require explicit user intent or an established non-destructive migration.
- Keep migrations idempotent and leave recoverable source data or a marker where the existing workflow does so.

## UI integration

A persisted preference is incomplete until its model, store, load, save, ViewModel notification, and Admin UI binding are consistent. If it affects visibility, navigation, command availability, or layout selection, notify all dependent properties and refresh the relevant commands.

## Routing

- Read [references/settings-inventory.md](references/settings-inventory.md) to choose the correct existing store and path.
- Read [references/json-migrations.md](references/json-migrations.md) when changing defaults, property names, enums, file shape, or storage location.

## Verification

Test a missing file, an old file without the new member, malformed JSON, denied write, repeated save, application restart, and any applicable migration. Add Core tests for non-trivial store or migration behavior.
