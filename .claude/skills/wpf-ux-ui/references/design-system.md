# Design system inventory

## Resource loading

`App.xaml` merges resources in this order:

1. `Themes/Styles.xaml`
2. `Themes/Icons.xaml`
3. `Themes/CrispButtonStyles.xaml`

For duplicate keys, the later merged dictionary is effective. `CrispButtonStyles.xaml` intentionally replaces selected button styles from `Styles.xaml`; inspect both definitions before editing `GhostButton`, `AccentOutlineButton`, or `AccentButton`.

## Semantic palette

Use the brush resources rather than copying their current hex values:

- surfaces: `BackgroundBrush`, `SurfaceBrush`, `SurfaceAltBrush`, `BorderBrush`
- actions: `AccentBrush`, `AccentHoverBrush`, `AccentPressedBrush`, `DangerBrush`, `DangerHoverBrush`, `DangerPressedBrush`
- text and state: `TextBrush`, `MutedBrush`, `OkBrush`, `WarnBrush`, `ErrorBrush`
- data: `TemperatureBrush`, `TemperatureSetpointBrush`, `HumidityBrush`, `PendingRowBrush`

Decorative illustrations and deliberately data-driven per-series colors may use locally constructed frozen brushes. Do not create brushes repeatedly in property getters or redraw loops.

## Reusable components

- content: `Card`, `DeviceCard`, `PanelGroup`
- text: `Heading`, `Label`, `Caption`, `Metric`, `MetricSmall`, `MetricSub`
- actions: `AccentButton`, `DangerButton`, `GhostButton`, `AccentOutlineButton`, `IconActionButton`, `TransportButton`, `PresetChip`
- forms and collections: `FieldStack`, `DataGridEditTextBox`, `ListWithEmptyHint`
- progress: `ProfileProgressBar`
- icons: geometries named `Icon.*` with `IconPath` or `TransportIcon`

Do not infer a component's exact padding or margin from old documentation. Inspect the active resource because the theme evolves.

## Layout conventions

- Use `Grid` for page structure and aligned forms, `WrapPanel` for value groups that must wrap, and an explicit `ScrollViewer` for page overflow.
- Give wide or long `DataGrid` controls their own scrolling and usable minimum column widths.
- Do not let expanding charts make later workflow sections unreachable.
- Use `TextWrapping="Wrap"` where Slovak labels, device names, statuses, or profile names can grow.
- For unsupported device capabilities, bind visibility to the existing capability property rather than cloning a view.

## Operator semantics

- measured temperature: orange; temperature setpoint: lighter/dashed orange
- humidity: blue; humidity setpoint: lighter/dashed blue where present
- running/available: green; warning/paused/setpoint emphasis: amber; alarm/stop/destructive: red
- a live metric must include a clear unit and distinguish measured value from target
- play, pause, stop, alarm, and ownership states must remain consistent across dashboard and detail views
