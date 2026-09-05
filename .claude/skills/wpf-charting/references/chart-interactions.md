# Chart interaction invariants

## Read-only/live charts

- Mouse wheel zooms around the data value under the cursor.
- Left-drag range selection must survive redraws triggered by incoming live samples until the gesture completes.
- Horizontal pan keeps the visible span stable and clamps to the full data range.
- Double-click resets the whole range.
- The minimap/track shows the selected portion whenever zoomed.
- Y scaling follows visible finite points, not unrelated historical plateaus.
- Hover selects data by X/time and must not mutate the underlying series.
- A live append may expand the full range but must not shift an explicitly zoomed data-unit window.

## Profile editor

- Read-only mode disables mutations without changing chart meaning.
- Dragging a handle preserves valid segment duration and temperature constraints.
- Zoom and pan must not change which segment a handle represents.
- Cycle bands, hold bands, tooltip step identity, and displayed duration must agree.
- Cancelled or aborted mouse capture must clear temporary drag state.

## Dense and long-running data

- Keep the complete represented time span.
- Reduce rendering data chronologically with first/last points plus bucket minima/maxima.
- Never show an X axis measured from run start while silently retaining only the newest samples.
- Validate short spikes, flat plateaus, sharp setpoint steps, gaps, and non-finite samples.
