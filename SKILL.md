# chamber_fos_soft — Engineering Skill

## Purpose

This file is the working guide for AI agents and developers modifying this repository. Read it before changing code. Prefer small, verifiable changes and preserve existing behavior unless the task explicitly requires a behavior change.

## Project identity

- Repository: `mukky89/chamber_fos_soft`
- Default branch: `main`
- Desktop app: `src/VotschVc3.App`
- Core library: `src/VotschVc3.Core`
- Bridge agent: `src/VotschVc3.Agent`
- Framework: .NET 8 / Windows WPF
- Serial dependency: `System.IO.Ports` 8.0.0
- Product: laboratory equipment control software

## Important terminology

- The current thermometer is **WIKA CTH7000**.
- Do **not** describe the current device as “ASL F100”.
- `F100Client` is intentionally retained as a source-compatible class name; its user-facing and diagnostic terminology should refer to WIKA CTH7000.
- Legacy ASL F100 detection may remain only where compatibility requires it.

## Versioning and changelog — mandatory

For **every code change** in this repository:

1. Bump the application version in `src/VotschVc3.App/VotschVc3.App.csproj`.
2. Add a dated entry to the root `CHANGELOG.md`.
3. Keep the changelog history; never replace it with a shortened version.
4. If a change is significant, also add/update a dedicated `CHANGELOG_<version>.md` release note.
5. Verify the version and changelog are on `main` before reporting completion.

Current baseline at the time of this change: `1.76.9`.

## USB / WIKA CTH7000 rules

Serial communication is safety- and reliability-sensitive.

### Concurrency and COM ownership

- All physical `SerialPort` operations must be serialized.
- Never allow scan, identify, read, reconnect, or dispose to close/write the same port concurrently.
- Keep each client's port lifecycle under its own synchronization gate.
- In addition, use the process-wide `SerialPortLease` so two `F100Client` instances or diagnostics cannot open/probe the same COM port concurrently.
- A live client keeps its COM lease for the complete open lifetime and releases it only after the physical port is closed/disposed.
- Diagnostic probing must acquire the same lease and skip a port already owned by the live application.
- `UnauthorizedAccessException` from `SerialPort.Open()` means the COM port is externally busy; report it as an occupied port rather than pretending it is a normal transient read failure.
- Disposal must wait for an in-flight operation before closing the port.
- Check the disposing state before scheduling work and again inside the worker operation.

### Open / reconnect

- Automatic COM discovery must remain supported.
- Manual COM selection must remain supported.
- Opening a port should clear stale RX/TX state before communication.
- Reconnect after a temporary USB/COM failure should close safely, reopen, and clear stale RX data before retrying.
- Do not assume a COM number remains present after a USB disconnect/reconnect.
- A port held by another process must not crash the application or cause a reconnect loop; the operator should get a clear busy/occupied state.

### Timeouts and UI

- Never perform blocking `SerialPort` I/O on the WPF UI thread.
- Reads/writes need bounded timeouts.
- Temporary failures should not freeze the UI.
- Retry transient `TimeoutException`, `IOException`, and closed-port failures where safe.
- A silent device/query timeout should be treated as a communication failure, not as a successful empty measurement.

### UI buttons and hover effects

- Do not use `DropShadowEffect` on interactive buttons where it causes text or icons to render through a blurred intermediate bitmap.
- Dashboard and FBG calibration buttons should use crisp outline/fill hover states consistent with the main menu.
- Hover feedback should be communicated by border/background/foreground changes, not by a blur/glow effect on the button content.
- Preserve keyboard focus visibility and disabled-state contrast when changing button templates.

### Protocol / diagnostics

- CTH7000 uses serial communication compatible with the existing protocol implementation: 9600 8N1, no flow control, CR-terminated commands, with the configured inter-character delay.
- Keep `*IDN?`, `SYSTEM:REMOTE`, `SYSTEM:LOCAL`, and `READ?` behavior compatible with the existing protocol layer.
- Preserve A/B channel support.
- TX and RX diagnostic logging should include device/port context and attempt information, while avoiding excessive log spam.
- Preserve robust error-response detection (`ERR`, probe errors, over/under range, and supported numeric error forms).

## Existing architecture

### App thermometer path

- `src/VotschVc3.App/Thermometers/F100Client.cs`
  - Owns physical CTH7000 serial communication.
  - Uses per-client synchronization plus process-wide COM ownership.
  - Contains retry/reconnect and TX/RX diagnostics.
- `src/VotschVc3.App/Thermometers/SerialPortLease.cs`
  - Provides process-wide ownership of physical COM ports.
  - Shared by live thermometer clients and diagnostic probes.
- `src/VotschVc3.App/Thermometers/SerialPortEnumerator.cs`
  - Enumerates COM ports and supports automatic/manual selection and device probing.
- `src/VotschVc3.App/ViewModels/ThermometersViewModel.cs`
  - Thermometer UI/application orchestration.
- `src/VotschVc3.App/ViewModels/ThermometerDeviceViewModel.cs`
  - Individual thermometer presentation state.
- `src/VotschVc3.App/ViewModels/CalibrationViewModel.cs`
  - Calibration-related thermometer behavior.

### Bridge path

- `src/VotschVc3.Agent/BridgeClient.cs`
  - Bridge command handling and device communication entry point.
- `src/VotschVc3.Agent/DeviceManager.cs`
  - Owns configured device runtimes and polling.
- `src/VotschVc3.Agent/F100Client.cs`
  - Legacy/bridge-side serial implementation; changes here must be kept compatible with the App-side protocol and device configuration.
- `src/VotschVc3.Core/Thermometers/F100Protocol.cs`
  - Shared protocol definitions and parsing.

## Authentication / navigation rule

- Changelog must not provide an unauthenticated route into the main application.
- Login page must not expose navigation that bypasses authentication.
- Any new public/login-page navigation must be reviewed for authentication bypass before merging.

## Change workflow

1. Inspect the existing implementation and related callers before editing.
2. Identify whether the change affects App, Agent, Core, UI, or all of them.
3. Make the smallest coherent change.
4. Update version + root changelog immediately as part of the same change.
5. Search for old terminology and stale references when changing device identity.
6. Build/test using the repository's GitHub Actions workflow when possible.
7. Check the resulting commit/branch and confirm the change is actually on `main`.

## Regression checklist

Before declaring a USB/thermometer fix complete, verify conceptually or with tests:

- [ ] Automatic COM scan works.
- [ ] Manual COM selection still works.
- [ ] Opening an already-present CTH7000 succeeds.
- [ ] Two clients in one process cannot open the same COM port concurrently.
- [ ] Diagnostic scanning cannot steal a COM port from a live thermometer client.
- [ ] A COM port held by another process is reported as OBSADENÝ instead of crashing the app.
- [ ] Disconnect/reconnect does not race with an in-flight write/read.
- [ ] RX/TX buffers are cleared at the correct lifecycle points.
- [ ] Query timeout cannot freeze the UI.
- [ ] Temporary USB failure triggers safe retry/reconnect.
- [ ] CTH7000 identification is preferred over legacy naming.
- [ ] A and B channels remain functional.
- [ ] TX/RX diagnostics are available.
- [ ] Silent query responses do not become false successful readings.
- [ ] Dashboard button hover has no blur/glow effect.
- [ ] FBG calibration button hover has no blur/glow effect.
- [ ] Button hover remains visually consistent with the main menu.
- [ ] Login/changelog navigation cannot bypass authentication.
- [ ] Version is bumped and `CHANGELOG.md` is updated.

## Do not do

- Do not reintroduce “ASL F100” as the current product name.
- Do not remove manual COM selection while improving auto-detection.
- Do not close/dispose a shared `SerialPort` from a different task while another task can still write/read it.
- Do not put blocking serial I/O directly on the WPF UI thread.
- Do not silently drop changelog/version updates.
- Do not rewrite the historical changelog just to add a new entry.
- Do not reintroduce button blur/glow effects when fixing hover styling.
