# Settings inventory

## Root layout

`AppPaths` defines the canonical root `Documents/Lab Control` and its Profiles, App log, Profilelog, Recordings, Calibration, Profile recovery, and settings locations. Startup performs a best-effort, idempotent migration from the legacy `Documents/VotschVc3` root.

Do not duplicate these paths in new features. Add a named `AppPaths` member when a new persistent category is justified.

## Global/settings-root files

- `ui.json` — `UiSettings` / `UiSettingsStore`; global layout and behavior preferences
- `chambers.json` — `ChamberConfigStore`; chamber/device configuration and ordering
- `email.json` — `EmailSettingsStore`; notification configuration and secrets
- `users.json` — `UserStore`; accounts and password hashes
- `sylex-fos-api.json` — `SylexFosApiSettingsStore`; integration endpoint and credentials
- `bridge.json` and `bridge-status.json` — bridge configuration/status with dedicated Shell/Agent behavior
- `fbg-reference-thermometers.json` — persistent exclusive WIKA-to-chamber assignments
- `fbg-calibration-workspaces.json` — last profile and exact PeakLogger endpoint per chamber

Search current callers before adding to this list; the calibration subsystem also has focused metadata and device-option stores.

## Structured directories

- `Profiles/` — one JSON file per profile via `ProfileStore`; legacy `profiles.json` migration is non-destructive
- `Profile recovery/` — one atomic checkpoint per chamber via `ProfileRunCheckpointStore`
- `Calibration/` — setup, run summary, samples, results, and checkpoint data via `CalibrationStore`

CSV recordings and diagnostic logs are application data but are not settings. Do not route them through `UiSettingsStore`.

## Ownership rules

- Global presentation preferences belong in `UiSettings` only when they truly apply across devices/users.
- Hardware configuration belongs with the device/chamber model, not UI preferences.
- Long-running workflow recovery belongs in checkpoint stores with stronger write guarantees.
- Secrets remain in their domain model/store and must not be copied into UI state, logs, changelog text, or diagnostics.
