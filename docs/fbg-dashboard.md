# FBG calibration dashboard

The operator workspace now opens on **Prehľad**, with the run/profile header, completed-point progress, observed-duration ETA, workflow, live measurements, temperature stability score, plateau roadmap and a bounded operator event log. Hardware setup and wiring remain under **Nastavenia**; raw runner details remain under **Diagnostika**. Existing commands, exports and calibration decision rules are retained.

**Live dáta** switches between FBG, WIKA and chamber traces. FBG starts with one active peak and supports all or manually selected peaks. Chart filtering only affects presentation.

The dashboard consumes structured runner snapshots. Stabilization samples and measurement samples are separate; FBG stabilization and measurement can both be active. WIKA controls the temperature gate when configured, so the chamber gate is marked N/A rather than inventing an independent stability decision. Temperature progress is the existing block score (+5 / -10), not continuous wall time, and reaching the score threshold does not itself imply gate approval. Plateau completion is published after result validation. Failed target states are preserved when returning to temperature waiting.

ETA is approximate and becomes available after a measured plateau completes. It uses the average observed plateau duration, subtracts time spent in the active plateau, is suppressed during pause/after stop, and reports an exceeded estimate instead of a false zero. Different target temperatures can have very different ramp times. Point durations come from runner results. Total and phase elapsed labels represent wall time; point elapsed is supplied by the runner.

Validation:
- .NET solution build succeeds, 0 errors; existing obsolete-API/nullability warnings remain.
- Full suite: 339 passed / 340 total. `Runner_WithReference_DoesNotStartPeakStabilityUntilWikaIsStable` also fails on the original main commit: a zero-duration reference gate does not throw the timeout expected by the test. Calibration gate behavior was not changed for this UI task.
- All 7 new dashboard projection tests pass, covering parallel samples, temperature loss, partial progress/ETA, pause/failure, stale/empty data, warning results and bounded/deduplicated logs.
- Actual WPF control rendered at 1080 and 1440 pixels. Preview data is synthetic; no hardware run was performed.

Before production use, exercise the complete window with a simulator and then the chamber/WIKA/PeakLogger combination, including hide/reopen, pause/resume and signal loss. The preview verifies the dashboard control, not a complete hardware session.
