# XAML review checklist

## Structure and binding

- Reuse application resources and implicit DataTemplates.
- Select `OneWay`, `TwoWay`, and `UpdateSourceTrigger` deliberately. Numeric fields normally commit on lost focus so intermediate input such as `-` is valid while typing.
- Every editable `DataGridTextColumn` uses `DataGridEditTextBox` unless a reviewed replacement preserves the visible caret and edit transaction.
- Do not refresh a collection view or rebuild its source while a `DataGrid` edit transaction is active.
- New commands are included in the owning ViewModel's command-state refresh path.

## Common WPF build/runtime traps

- Do not target a transform or another `Freezable` with a style setter `TargetName` pattern that causes MC4111.
- Do not put a binding into `Setter.Value` using invalid attribute syntax.
- Remember that a local property value overrides style-trigger setters.
- Derived keyed styles use `BasedOn` when base appearance must remain.
- Freeze reusable brushes and geometries where appropriate; never freeze an object that must animate.
- Avoid synchronous I/O and blocking waits on the dispatcher thread.

## UX states

- Test normal, hover, pressed, keyboard-focused, disabled, read-only, empty, loading, warning, error, and active-run states.
- Test Admin and Operator roles.
- Test devices with and without humidity plus special device types relevant to the view.
- Confirm destructive actions cannot be confused with primary actions.
- Confirm transient notifications do not steal keyboard focus.

## Layout

- Test the declared minimum window size and maximized 1080p layout.
- Test long names, multi-line errors, large numeric values, and localized text.
- Confirm page-level and nested grid scrolling remain reachable.
- Confirm hover and state changes do not alter card geometry.

## Verification

- Parse/compile XAML as part of a Windows Release build.
- Exercise the affected navigation path repeatedly to expose duplicate handlers or lost state.
- Treat binding errors, inconsistent line endings, hidden controls, and inaccessible content as release blockers.
