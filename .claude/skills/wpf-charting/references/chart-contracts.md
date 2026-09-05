# Chart contracts

## Shared types

- `ChartSeries` carries name, frozen stroke brush, data-space points, dashed state, optional point label, and stroke thickness.
- `TimeAxisViewport` owns a time window in data units and clamps it as data changes.
- `NiceAxis` owns readable numeric/time scale selection and decimal precision.
- `TimeSeriesEnvelopeReducer` keeps chronological bucket minima and maxima so trends, steps, and short spikes survive reduction.

Keep numerical helpers WPF-independent in `VotschVc3.Core/Charting` unless they genuinely require WPF types.

## ChartView

`ChartView` is the general-purpose read-only/live control. Its public dependency-property contract includes:

- `Series`
- `YMin`, `YMax`
- `Unit`
- `MinimumYDecimals`
- `EmptyText`
- `ChartTitle`
- `AllowZoom`
- `ShowStages`
- `CycleStartX`, `CycleEndX`, `CycleCount`

Before changing a property, search every XAML and C# caller. Current hosts include chamber detail, home/profile preview, thermometers, recording viewer, calibration dashboards/windows, profile picker, chart windows, and zoom windows.

## ProfileEditorChart

`ProfileEditorChart` renders and optionally edits profile segments. Its public dependency-property contract includes:

- `Segments`
- `MeasuredStart`
- `CycleStart`, `CycleEnd`, `CycleCount`
- `IsReadOnly`

Its handles change segment duration and temperature. Keep segment semantics in ViewModels/Core and coordinate conversion, handle drawing, capture, and drag feedback in the control.

## Series semantics

- Temperature and humidity must remain visually distinguishable without relying on legend order.
- Setpoints are dashed and lighter than measured series.
- Calibration target lines are not observed stability bands.
- Point labels are presentation metadata, not a persistence format.
- Units belong to the chart surface; do not bake units into numeric point values.
