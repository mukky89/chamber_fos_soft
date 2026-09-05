---
name: wpf-reliability-performance
description: Protect responsiveness, recovery and operational reliability in the VotschVc3 WPF application. Use for polling, live data, background work, device or network I/O, reconnects, checkpoints, startup/shutdown, long-running workflows, high-frequency UI updates, performance regressions, hangs, crashes or systemic failures.
---

# VotschVc3 responsiveness and reliability

The application controls real laboratory equipment. A feature is incomplete if it works only on the happy path, blocks the UI, loses recoverable work, creates duplicate background loops, or leaves device ownership ambiguous after failure.

## Required invariants

- Never perform device, serial, network, filesystem, discovery, parsing of large inputs, or long computation synchronously on the WPF dispatcher thread.
- Keep every external wait bounded by a timeout and cancellation path. A retry loop must have a ceiling, backoff, and a single owner.
- Coalesce or throttle high-frequency UI notifications and redraws; background sampling frequency must not force the same rendering frequency.
- Preserve complete data meaning while reducing rendering cost. Never improve speed by silently discarding history, spikes, alarms, or recovery state.
- A disconnect, timeout, malformed response, unavailable file, or failed integration must degrade to an explicit stale/disconnected/error state without freezing or crashing the application.
- Recovery operations must be idempotent. Repeated startup, reconnect, restore, close, or dispose must not duplicate commands, subscriptions, timers, clients, or profile steps.
- Persist interruption-critical state at safe transition boundaries using atomic replacement. Never resume a hardware workflow from assumptions that were not recorded.
- After recovery, re-read authoritative device state before sending control commands. Do not treat a restored socket, COM port, cached value, or saved setpoint as proof of physical state.
- Serialize ownership-sensitive operations. Connect, read, write, reconnect, close, and dispose must not race on the same device resource.
- Fail safe: if command outcome is uncertain, show it as uncertain and require verification before issuing a conflicting mutation.

## Performance workflow

1. Identify the user-visible latency path and which work runs on the dispatcher.
2. Measure or capture a reproducible baseline before optimizing.
3. Remove blocking I/O, repeated enumeration, unnecessary allocations, redundant parsing, duplicate subscriptions, and unbounded visual creation from the hot path.
4. Keep UI updates incremental and bounded; virtualize large collections and reduce dense chart rendering with the existing envelope strategy.
5. Re-measure the same scenario. Do not claim a speed improvement from code inspection alone.

## Failure and recovery workflow

1. Define the authoritative state, persisted recovery state, transient UI state, and resource owner.
2. Enumerate interruption points: startup, connect, active command, polling, save, restore, window close, application shutdown, and process/power loss.
3. Make cleanup safe after partial initialization and safe when invoked more than once.
4. Reconcile saved state with fresh hardware/service state before resuming.
5. Surface the current state and required operator action; keep diagnostics contextual but free of secrets.

## Routing

- Read [references/responsiveness-checklist.md](references/responsiveness-checklist.md) for live UI, polling, charts, lists, startup, or perceived lag.
- Read [references/failure-recovery-checklist.md](references/failure-recovery-checklist.md) for I/O, checkpoints, reconnect, restore, resource ownership, or crash prevention.
- Also use the domain skill for the component being changed: `wpf-charting`, `wpf-settings-persistence`, `wpf-mvvm-navigation`, or `wpf-ux-ui`.

## Verification

Add a regression test for every reproducible bug when the behavior can be isolated. Test the normal path plus timeout, cancellation, disconnect, malformed data, denied write, repeated retry, repeated dispose, and restart/restore scenarios relevant to the change. Build and test in Release; use fault injection or fakes for failure paths instead of claiming physical-device validation that did not occur.
