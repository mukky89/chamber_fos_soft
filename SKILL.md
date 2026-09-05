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
- `F100Client` and `F100Protocol` are retained only as source-compatible code symbols in the renamed CTH7000 files; user-facing and diagnostic terminology refers to WIKA CTH7000.
- Legacy ASL F100 detection may remain only where compatibility requires it.

## Versioning and changelog — mandatory

For **every code change** in this repository:

1. Bump the application version in `src/VotschVc3.App/VotschVc3.App.csproj`.
2. Add a dated entry to the root `CHANGELOG.md`.
3. Keep the changelog history; never replace it with a shortened version.
4. `CHANGELOG.md` is the **single canonical changelog** used by the application and release workflow.
5. Do **not** create `CHANGELOG_<version>.md` files for individual releases; keep release history in the root changelog only.
6. Commit every completed change directly to `main` and push it to `origin/main` (GitHub).
7. Never force-push. If `main` has moved or is protected, integrate safely or report the blocker instead of overwriting history.
8. Before reporting completion, verify that local `HEAD` matches GitHub `refs/heads/main` and that no task changes remain uncommitted.

Current fallback baseline at the time of this change: `1.76.58`.

## Changelog format

- Use `## [x.y.z] – YYYY-MM-DD` for release headings.
- Keep the language Slovak and the existing Keep-a-Changelog structure.
- An optional `## [Nezverejnené]` / `## [Unreleased]` heading may exist in the source markdown, but the application parser must ignore it and must never render it as a version.
- Do not use per-version changelog files as a second source of truth.

## USB / WIKA CTH7000 rules

Serial communication is safety- and reliability-sensitive.

### Validated physical-device baseline — do not regress

The following settings were validated on the real production reference thermometer and are the current compatibility baseline:

- Instrument identity: `WIKA,CTH7000,000000,V1.0,01/05/2013`.
- USB serial: **9600 baud, 8 data bits, no parity, 1 stop bit, no flow control, CR terminator**.
- Current desktop/RAW test uses **DTR=True, RTS=True**.
- Production transmit pacing is **25 ms between every character/byte**. The WIKA documentation mentions a shorter delay, but the physical V1.0 unit timed out with the former 2 ms implementation and worked reliably with the AutoOptical/Pali 25 ms timing.
- Do not replace character-by-character transmission with one bulk `SerialPort.Write` call.
- Validated fresh-session order is:
  `Open COM -> SYSTEM:REMOTE -> >=1000 ms settle -> *IDN? (first session only) -> MEASURE:CHANNEL? 1/2 -> SYSTEM:LOCAL`.
- On the validated device, fresh-open `*IDN?` sent before `SYSTEM:REMOTE` with 2 ms pacing produced a zero-byte 8 s timeout.
- `MEASURE:CHANNEL? 1` returned a valid channel-A temperature frame such as `1,24.707,"CEL"`.
- `MEASURE:CHANNEL? 2` can legitimately return `2,NoProbe,"CEL"` when no probe is connected to B.
- `SYSTEM:LOCAL` must be attempted in `finally`/dispose/error paths so the physical front panel is not left locked in REMOTE.
- The RAW debug **Pali / AutoOptical preset** must continue to reproduce this exact compatibility setup for bench diagnostics.
- Do **not** shorten the 25 ms pacing or 1000 ms REMOTE settle as a generic performance optimization without a physical-device regression test. Speed up UI/WMI/reuse overhead first.
- Repeated one-shot reads should reuse the existing live COM client and cached identity instead of performing a fresh detailed Windows/WMI enumeration for every button click.
- The automatic 5 s dashboard/workspace refresh must read the existing reference client directly in the background. It must **not** execute the foreground `Načítať teplotu` command or make that button appear to click itself.

### Persistent FBG reference assignment

- A physical WIKA CTH7000 may be assigned to only **one FBG calibration workspace/chamber at a time**.
- Treat persistent assignment and temporary COM ownership as separate concepts: `CalibrationReferenceStatusStore` owns the persistent business assignment; `CalibrationResourceRegistry` / `SerialPortLease` protect active process/serial usage.
- Match a physical reference primarily by USB serial number and use COM port as a fallback. A COM number alone must not be assumed to be a permanent hardware identity when a USB serial is available.
- Opening another FBG calibration window must never automatically steal or auto-select a reference already assigned elsewhere.
- USB disconnect, read timeout, hiding/closing a calibration window, or application restart must **not** silently free the persistent assignment.
- Live temperature is transient: after disconnect/restart show no stale live temperature, while retaining the saved assignment/COM information.
- Switching a chamber deliberately to another reference may free that chamber's previous assignment, but a reference owned by another chamber must be rejected with a clear operator message naming the owner.
- Persistent assignments are stored in `fbg-reference-thermometers.json` under the application settings directory.
- Dashboard reference readouts must remain compact and stable: show reference temperature plus COM port when live; when disconnected keep the assigned port and show `—` instead of a stale temperature.

### FBG calibration execution — reference and peak stability

- `CalibrationSetup.CalibrationSegmentIndices` is the source of truth for which non-ramp profile segments are calibration plateaus. `ProfileSegment.IsCalibrationPoint` is only a backward-compatible fallback when an older setup has no explicit saved indices.
- Never silently calibrate a plateau the operator did not select in the FBG workspace.
- **WIKA CTH7000 is the authoritative calibration temperature.** When a WIKA reference is assigned, FBG wavelength stability evaluation may begin only after the **WIKA reference itself** satisfies the configured target tolerance, stable duration, and maximum drift.
- **Chamber temperature is informational for FBG stability and must not block a calibration plateau.** It may still be displayed, logged and compared to WIKA for diagnostics/alerts, but the chamber controller reading is not the stable-temperature gate.
- A missing, invalid, or unstable WIKA reference must not be treated as a stable calibration temperature. After the configured stability timeout, require operator action rather than recording a nominally valid plateau.
- When WIKA continues to return a valid temperature but has not yet completed the stability gate, extend the base stability timeout in 15-minute increments, with an absolute maximum of one additional hour per plateau. Record and notify every extension. After the first extension budget is exhausted, defer that plateau, continue with the other selected plateaus, then retry the deferred plateau once. Persist the deferred queue in the checkpoint. If the retry also exhausts its budget, stop automatic progression, require operator action, show explicit next steps and send the configured operator e-mail; never accept the unstable plateau automatically and never loop retries indefinitely.
- In reference charts, keep the calibration target as a separate line. Never label `target ± tolerance` as the stability band; show dynamic stability bounds only from the WIKA samples that currently contribute to the accumulated stability score. A physically steady reference outside target tolerance is still not an acceptable calibration point.
- Scale the WIKA settling/reference chart from the samples of the current plateau, not from the temperature range of every earlier plateau in the run. This keeps the target and dynamic stability-limit lines visually distinct; the complete trace remains persisted for history/audit.
- Long-running FBG live charts must retain the complete visible time span. Bound rendering cost with chronological min/max envelope reduction; never drop the oldest samples while continuing to label X from the run start, because that makes an 11-hour run look like an 11-hour flat/noisy trace when only the last minutes remain.
- The operator may explicitly bypass the temperature-stability wait for the current plateau only when a valid authoritative temperature is visible. This override must be limited to that plateau and recorded as a warning with target, WIKA and chamber temperatures in the run history.
- Do not introduce a second chamber-stability dwell after the WIKA reference becomes stable.
- The calibration setpoint ramp is command shaping only: it may limit how quickly the application moves the requested chamber setpoint, but the chamber must continue regulating from its own internal sensor. It must never turn WIKA into a PID/feedback loop. Keep WIKA setpoint correction separately opt-in and disabled by default; it may adjust the chamber setpoint only while WIKA is outside the configured target tolerance, must remain rate- and magnitude-limited, and must be locked while a calibration run is active. Reflect the active ramp rate in workflow `?` help.
- After reference stability is achieved, **each selected PeakLogger peak must independently satisfy its own wavelength stability criteria** before its result is accepted. One stable peak must never make another peak stable.
- The operator may select one suggested peak per channel or explicitly select all peaks. `Vybrať všetky peaky` means every discovered peak is independently evaluated and recorded.
- Default peak stability remains 50 samples, 5 pm max range, 1.5 pm max standard deviation, and 1 pm/min max drift unless the saved setup explicitly changes these values.
- The operator-facing run monitor must show the planned plateau order before start and, during a run, the current step, what the runner is waiting for, current plateau, reference temperature, active peak/SN/channel, wavelength sample progress and stable-peak count.
- Every operator-facing workflow step must expose a visible `?` help affordance. Its text must explain what the step does, its gates, sample counts, reset/failure behaviour and timing. Build these descriptions from the active `CalibrationProfileSettings` and observed acquisition cadence; whenever calibration logic or defaults change, update the workflow help in the same change so it never becomes stale documentation.
- Calibration ETA must use profile/plateau history when available, account for the configured setpoint ramp, and subtract only elapsed time from the active plateau. If the active WIKA/FBG stability wait exceeds the available historical evidence, show an explicitly uncertain estimate and no fabricated finish time.

### FBG workspace persistence / restore

- Detailed wiring is persisted per chamber+profile through `CalibrationStore`: selected peaks, production FBG SN, channel SN, CHAIN override SN, core metadata, notes, product/customer/order fields, per-peak timeout, stability settings, and selected calibration segment indices.
- Closing/hiding the FBG calibration window must force a final setup save when the run is not active; do not rely only on the debounce autosave.
- After a run fails or waits for operator action, keep `Ukončiť a uložiť` available. Finalizing must preserve completed plateaus and measured files in history, mark the run aborted, and remove only its resume checkpoint after explicit confirmation. A manual stop intended for restart must preserve any deferred-plateau queue already stored in the checkpoint.
- Production SN/CHAIN editing must remain continuously protected by the existing short debounce autosave; a completed cell edit may additionally force a final setup save.
- `CalibrationWorkspaceStateStore` stores the last selected calibration profile and exact PeakLogger host/port per chamber in `fbg-calibration-workspaces.json`.
- Reopening the FBG workspace or restarting the application must restore the last profile for that chamber.
- After the first UI render, the app may reconnect only to the **exact previously saved PeakLogger endpoint** to rebuild the wiring table. Do not reintroduce broad PeakLogger discovery into the initial render path.
- When PeakLogger reconnects, restore saved mappings by stable source identity `PeakLoggerDeviceSerialNumber|Channel|PeakId`; wavelength is measurement data, not identity.
- A failed automatic reconnect must leave the FBG window responsive and editable and must not clear the saved wiring.
- Persistent WIKA assignment is separate from workspace/profile persistence and remains governed by `CalibrationReferenceStatusStore`.

### FBG wiring edit transaction — never interrupt operator input

- An operator editing `FBG sensor SN (kanál)`, `FBG sensor SN CHAIN` or another wiring cell owns the DataGrid until the edit is committed or cancelled.
- Text-field validation, API lookup failures and operator warning popups must run only after the value is committed with Enter or the editor loses focus. Never validate or notify on every keystroke while the operator is still composing a value.
- Never call `Items.Refresh()`, `CollectionView.Refresh()`, clear/rebuild `Peaks`, or execute a topology-driven `RefreshSensorsCommand` while the DataGrid has an active `AddNew`/`EditItem` transaction or focused editor.
- Sylex metadata refreshes and PeakLogger topology changes that arrive during an edit must be queued/deferred and applied only after the edit has ended.
- A background timer must never move focus, change `CurrentCell`, cancel `BeginEdit`, or cause typed SN data to disappear.
- The WPF exception `'Refresh' is not allowed during an AddNew or EditItem transaction` is a regression and must be treated as a release blocker.

### FBG chamber ownership while a run is active

- A running FBG calibration owns control of its chamber/device for the duration of the run.
- On the device dashboard, manual quick control and Testovací profil controls must be disabled for that chamber while its FBG run is active.
- The control-mode badge must show `FBG CALIBRATION` instead of `MANUÁL`/`PROFIL` while the FBG runner owns the chamber.
- The active chamber's `FBG Kalibrácia` button should use a slow, smooth red pulse as a visible run indicator and return to its normal style immediately after the run ends.
- These rules are per chamber: an FBG run on one chamber must not disable another chamber's independent controls.

### Concurrency and COM ownership

- All physical `SerialPort` operations must be serialized.
- Never allow scan, identify, read, reconnect, or dispose to close/write the same port concurrently.
- Keep each client's port lifecycle under its own synchronization gate.
- In addition, use the process-wide `SerialPortLease` so two thermometer clients or diagnostics cannot open/probe the same COM port concurrently.
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
- The FBG run indicator is an explicit exception to the static hover rule: a **slow red color/opacity pulse** is allowed while a calibration is actively running, but must not blur the text/icon or flash rapidly.

### Application notification UX

- `src/VotschVc3.App/Notifications/AppNotificationService.cs` is the single in-app pipeline for **transient** operator notifications (`Info`, `Success`, `Warning`, `Error`).
- Do not add new temporary orange/red explanatory TextBlocks inside device cards when the information is an event or action result; route it through `AppNotificationService` so card geometry stays stable.
- Persistent state belongs inline: connection status, alarm badge, lock state, active `FBG CALIBRATION` ownership, temperatures and similar current-state indicators must remain visible on the relevant card/workspace.
- Popup notifications must be non-activating, must not steal keyboard focus, and must be de-duplicated/queued so background polling cannot spam the operator.
- When the application is hidden/minimized to tray, do not open a floating top-most WPF popup over another program. `DesktopNotifier` owns Windows tray balloon/sound/taskbar behavior in that state.
- Existing `DesktopNotifier.Notify(...)` events should mirror to the central in-app popup while a desktop window is visible, while preserving tray/background behavior.
- Overall Sylex FOS API health may use a central popup; **per-symbol/SN metadata lookups remain quiet** and must not produce one popup per scanned/typed sensor.

### FBG calibration layout

- The FBG calibration workspace must remain usable on common 1080p operator displays.
- Expanding the reference-temperature chart must never make the `Zapojenie` table unreachable.
- Keep a page-level vertical scrollbar for content overflow and independent scrollbars for wide/long DataGrids.
- The `Zapojenie` workspace should provide enough vertical space to see approximately **16 production rows** at once when the operator scrolls to that section; extra rows remain independently scrollable.
- Do not compress production table columns until headers/text overlap; prefer column minimum widths plus horizontal scrolling.
- Dynamic status/port text must not visually collide with section headings.
- The dashboard FBG run card must be a separate sibling **above the entire `Rýchle ovládanie` section**. Never inject it into the Quick-control header/DockPanel where it can overlap `Rýchle ovládanie` or `Upraviť predvoľby`.
- The FBG run card should remain collapsed while no FBG run is active; the FBG button/control-mode badge already communicate inactive availability without consuming vertical space.

## Changelog UI architecture

- `src/VotschVc3.App/Changelog/ChangelogParser.cs` parses the root `CHANGELOG.md`.
- `src/VotschVc3.App/ViewModels/ChangelogViewModel.cs` loads the embedded root `CHANGELOG.md` resource from the application assembly.
- `src/VotschVc3.App/Views/ChangelogView.xaml` renders parsed releases as version cards.
- `src/VotschVc3.App/Changelog/ChangelogHtmlWriter.cs` renders the same parsed releases for HTML export.
- The parser must accept real three-part numeric versions such as `1.76.11` and ignore non-release headings such as `[Nezverejnené]`.

## Protocol / diagnostics

- Keep `*IDN?`, `SYSTEM:REMOTE`, `SYSTEM:LOCAL`, and `MEASURE:CHANNEL? 1/2` behavior compatible with the validated physical-device baseline above.
- Preserve A/B channel support.
- TX and RX diagnostic logging should include device/port context and attempt information, while avoiding excessive log spam.
- Preserve robust error-response detection (`ERR`, `NoProbe`, over/under range, and supported numeric error forms).
- Treat the physical Pali/AutoOptical trace as stronger compatibility evidence for the installed CTH7000 V1.0 than an unverified timing optimization.

## Existing architecture

### App thermometer path

- `src/VotschVc3.App/Thermometers/CTH7000Client.cs`
  - Owns physical CTH7000 serial communication.
  - File was renamed from the historical `F100Client.cs`; the `F100Client` class name remains only for source compatibility.
  - Uses per-client synchronization plus process-wide COM ownership.
  - Contains retry/reconnect and TX/RX diagnostics.
- `src/VotschVc3.App/Thermometers/CTH7000Client.cs`
  - `SerialPortLease` remains a shared helper in `SerialPortLease.cs` for process-wide COM ownership.
- `src/VotschVc3.App/Thermometers/SerialPortEnumerator.cs`
  - Enumerates COM ports and supports automatic/manual selection and device probing.
- `src/VotschVc3.App/ViewModels/ThermometersViewModel.cs`
  - Thermometer UI/application orchestration.
- `src/VotschVc3.App/ViewModels/ThermometerDeviceViewModel.cs`
  - Individual thermometer presentation state.
- `src/VotschVc3.App/ViewModels/CalibrationViewModel.cs`
  - Calibration-related thermometer behavior.
- `src/VotschVc3.App/ViewModels/CalibrationReferenceStatusStore.cs`
  - Persistent one-reference-per-chamber assignment plus dashboard snapshot state.
- `src/VotschVc3.App/ViewModels/CalibrationWorkspaceStateStore.cs`
  - Persists the last FBG profile and exact PeakLogger endpoint per chamber for reopen/restart recovery.
- `src/VotschVc3.App/Views/CalibrationWindow.ReferenceAssignment.cs`
  - Enforces selection-time exclusivity and reconnect restoration for the assigned reference.
- `src/VotschVc3.App/Views/CalibrationWindow.WorkflowEnhancements.cs`
  - Forces close-time setup persistence, restores the saved workspace and exposes select-all-peaks.
- `src/VotschVc3.App/Views/CalibrationWindow.ProductionWorkspaceV3.cs`
  - Owns edit-safe wiring refresh, silent 5 s reference refresh, page/16-row workspace sizing, and explicit plan/current-step/wait telemetry.
- `src/VotschVc3.App/Views/HomeView.FbgRunInterlock.cs`
  - Dashboard per-chamber FBG run interlock, `FBG CALIBRATION` badge, and slow red FBG-button pulse.
- `src/VotschVc3.App/Notifications/AppNotificationService.cs`
  - Central non-activating in-app popup queue for transient operator notifications.
- `src/VotschVc3.App/Views/HomeView.AppNotifications.cs`
  - Routes dashboard manual/profile conflict events to the central popup system and hides legacy inline warning rows.
- `src/VotschVc3.App/Views/CalibrationWindow.AppNotifications.cs`
  - Routes overall Sylex FOS API health to the same central popup system while keeping per-SN metadata lookup quiet.

### Core calibration path

- `src/VotschVc3.Core/Calibration/CalibrationProfileRunner.cs`
  - Executes only explicitly selected calibration plateaus and owns the **WIKA reference stability gate**; chamber temperature is informational for FBG stability.
- `src/VotschVc3.Core/Calibration/CalibrationOrchestrator.cs`
  - Owns per-peak independent wavelength stability tracking, raw samples, failure policies, and plateau results.
- `src/VotschVc3.Core/Calibration/StabilityDetectors.cs`
  - Temperature and rolling wavelength stability criteria.
- `tests/VotschVc3.Core.Tests/CalibrationWorkflowRegressionTests.cs`
  - Regression coverage for explicit plateau selection and WIKA stability gating.

### Core protocol path

- `src/VotschVc3.Core/Thermometers/CTH7000Protocol.cs`
  - Shared CTH7000 protocol encoding/parsing.
  - The historical `F100Protocol` symbol remains in this file for source compatibility.
- `tests/VotschVc3.Core.Tests/CTH7000ProtocolTests.cs`
  - Protocol/parser regression coverage for WIKA CTH7000 and compatible legacy frames.

### Bridge path

- `src/VotschVc3.Agent/BridgeClient.cs`
  - Bridge command handling and device communication entry point.
- `src/VotschVc3.Agent/DeviceManager.cs`
  - Owns configured device runtimes and polling.
- `src/VotschVc3.Agent/CTH7000Client.cs`
  - Bridge-side thermometer serial implementation; renamed from the historical `F100Client.cs`.
  - The `F100Client` class name remains for compatibility with existing bridge code.
- `src/VotschVc3.Core/Thermometers/CTH7000Protocol.cs`
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
7. Commit the completed change to `main`, push it to `origin/main`, and verify the remote commit matches local `HEAD`.

## Regression checklist

Before declaring a USB/thermometer or FBG calibration fix complete, verify conceptually or with tests:

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
- [ ] Verified production 25 ms inter-character transmit pacing remains intact.
- [ ] Fresh session enters `SYSTEM:REMOTE` before the first `*IDN?` and waits at least 1000 ms before querying.
- [ ] `SYSTEM:LOCAL` is attempted after every measurement/failure/dispose path.
- [ ] Repeated manual temperature-button reads do not require a fresh detailed WMI scan while the CTH7000 is already connected.
- [ ] Automatic 5 s reference refresh does not execute or visually press the manual `Načítať teplotu` button.
- [ ] One physical CTH7000 cannot be persistently assigned to two different FBG calibration workspaces.
- [ ] USB disconnect/window close does not silently release a persistent FBG reference assignment.
- [ ] A disconnected assigned reference never leaves a stale live temperature on the dashboard.
- [ ] Explicit UI-selected calibration plateaus are the plateaus actually executed for FBG measurement.
- [ ] With WIKA assigned, WIKA alone is the authoritative temperature stability gate; chamber temperature does not block peak stability.
- [ ] WIKA charts distinguish the target from the dynamic observed stability window and do not present target tolerance as measured stability limits.
- [ ] Every selected FBG peak is tracked independently and produces its own result/raw samples only after its own stability criteria are met.
- [ ] `Vybrať všetky peaky` truly selects every discovered PeakLogger peak.
- [ ] Closing/reopening the FBG workspace restores the last profile, wiring, selected peaks, SN/CHAIN, timeouts, plateau selection and exact PeakLogger endpoint.
- [ ] Automatic workspace restore uses only the known PeakLogger endpoint and does not reintroduce broad startup discovery.
- [ ] Typing/editing an SN cannot be interrupted by `CollectionView.Refresh`, Sylex metadata refresh, or PeakLogger topology refresh.
- [ ] SN format, duplicate and Sylex API warnings appear only after Enter or leaving the edited cell, never repeatedly while typing.
- [ ] FBG page remains vertically scrollable when the reference-temperature chart is expanded.
- [ ] `Zapojenie` table retains usable vertical/horizontal scrolling, readable column widths, and approximately 16 visible working rows.
- [ ] Live monitor shows the planned plateaus before start and current step / wait reason / WIKA / active peak samples during a run.
- [ ] Every workflow `?` describes the current implementation and dynamically reflects active stability limits, sample counts, timeouts and observed sampling cadence.
- [ ] While FBG calibration is running, manual quick control and Testovací profil are disabled on that chamber.
- [ ] During an FBG run the device mode badge reads `FBG CALIBRATION` and the FBG button uses a slow red pulse only for that chamber.
- [ ] FBG status card is inserted above the complete `Rýchle ovládanie` section and never overlaps its title/action header.
- [ ] Inactive FBG status cards remain collapsed.
- [ ] Transient manual/profile/API warnings use the central in-app popup instead of changing device-card layout.
- [ ] Central popup notifications do not steal focus and repeated background events are de-duplicated/queued.
- [ ] Per-symbol Sylex FOS API metadata lookup does not create popup spam.
- [ ] Dashboard button hover has no blur/glow effect.
- [ ] FBG calibration button hover has no blur/glow effect.
- [ ] Button hover remains visually consistent with the main menu.
- [ ] Changelog UI does not render `[Nezverejnené]` as a fake version.
- [ ] Changelog UI reads the root `CHANGELOG.md` as its only source.
- [ ] Login/changelog navigation cannot bypass authentication.
- [ ] Version is bumped and `CHANGELOG.md` is updated.

## Do not do

- Do not reintroduce “ASL F100” as the current product name.
- Do not remove manual COM selection while improving auto-detection.
- Do not close/dispose a shared `SerialPort` from a different task while another task can still write/read it.
- Do not put blocking serial I/O directly on the WPF UI thread.
- Do not silently drop changelog/version updates.
- Do not rewrite the historical changelog just to add a new entry.
- Do not create duplicate per-version `CHANGELOG_<version>.md` files.
- Do not rename the shared CTH7000 files back to the historical F100 filenames.
- Do not reintroduce button blur/glow effects when fixing hover styling.
- Do not revert the validated CTH7000 V1.0 timing/command order to the old 2 ms + pre-REMOTE `*IDN?` sequence without a new physical-device validation.
- Do not auto-release or auto-steal a persistent CTH7000 assignment merely because its COM port temporarily disappears.
- Do not use wavelength as a persistent PeakLogger mapping identity.
- Do not record a calibration plateau as stable solely because the chamber controller is stable; WIKA reference stability is authoritative.
- Do not make chamber temperature a blocking stability condition when WIKA reference is available and configured for the FBG run.
- Do not call DataGrid/CollectionView refresh or rebuild PeakLogger rows while the operator is editing a wiring cell.
- Do not route background WIKA polling through foreground UI commands.
- Do not add transient warning text blocks into dashboard cards when a central popup can convey the event without changing layout.
- Do not show one in-app popup per Sylex FOS symbol lookup.
- Do not clear saved FBG wiring when a background PeakLogger reconnect fails.
