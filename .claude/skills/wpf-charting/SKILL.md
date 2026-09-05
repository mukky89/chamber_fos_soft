---
name: wpf-charting
description: Extend, diagnose or review charts and profile-chart editing in VotschVc3. Use for ChartView, ProfileEditorChart, ChartSeries, axes, zoom, pan, hover, downsampling and live time-series behavior. Do not introduce a third-party chart library unless the user explicitly requests an architectural migration.
---

# VotschVc3 charting

The repository uses custom WPF `Canvas`-based chart controls, not LiveCharts, Syncfusion, or another chart package.

## Select the correct control

- Use `ChartView` for live data, recorded data, profile previews, and other read-only time series.
- Use `ProfileEditorChart` only when profile segments must be selected or edited.
- Put reusable numerical behavior in `VotschVc3.Core/Charting`.
- Keep WPF drawing, hit testing, mouse capture, and visual interaction state in the App custom controls.

## Preserve contracts

- Treat dependency properties as the public API of each control.
- Supply line data through `ChartSeries`; do not expose `Canvas` children from a ViewModel.
- Preserve semantic series colors, dashed setpoints, titles, units, legends, and meaningful empty states.
- Preserve cursor-centered wheel zoom, range selection, horizontal pan, double-click reset, minimap indication, and visible-range Y scaling.
- When a live chart is zoomed, appended samples must not drag the operator's selected viewport.
- Preserve spikes during reduction. Use the chronological min/max envelope reducer rather than every-Nth-point sampling or dropping the oldest data while retaining a run-start time axis.
- Keep target, configured tolerance, and observed stability bounds semantically distinct.

## Performance and ownership

- Do not rebuild series or allocate brushes in frequently evaluated property getters.
- Do not add one WPF visual per raw sample for long recordings without measuring redraw cost.
- View code-behind is appropriate for drawing and gestures. Profile execution, device control, persistence, and calibration decisions are not.
- Freeze immutable brushes and drawing resources; preserve animatable resources where required.

## Routing

- Read [references/chart-contracts.md](references/chart-contracts.md) before changing dependency properties, `ChartSeries`, or call sites.
- Read [references/chart-interactions.md](references/chart-interactions.md) before changing zoom, pan, selection, editing, or live-update behavior.
- Also use `wpf-reliability-performance` for live sampling, redraw performance, long-running charts, cancellation, or failure recovery.

## Verification

Test empty, single-point, constant-value, negative-value, long-duration, multi-cycle, high-density, and heavily zoomed data. Verify all existing chart hosts and run relevant Core charting tests.
