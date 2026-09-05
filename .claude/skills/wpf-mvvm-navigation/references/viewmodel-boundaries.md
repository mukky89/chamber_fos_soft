# ViewModel boundaries

## Existing pressure points

`ShellViewModel` composes application-wide services, authentication, navigation, device collection, global settings, bridge status, administration, and several commands. `ChamberViewModel` combines extensive device, profile, recording, alarm, chart, and UI state. Treat both as integration roots, not automatic homes for every new feature.

## Placement decisions

Choose a focused ViewModel or service when a feature has its own:

- collection and selection state
- load/save lifecycle
- group of commands
- background operation or cancellation scope
- validation/status surface
- testable orchestration independent of the Shell or chamber's core state

Keep a thin property or command on the parent only when it delegates to that focused owner and is needed for binding/navigation.

## Code-behind boundary

Code-behind is acceptable for:

- custom chart drawing and input gestures
- focus and edit-transaction coordination
- window ownership, close/hide, placement, and tray restoration
- visual-only animation and responsive layout adjustments

Move behavior out when it decides device commands, calibration acceptance, persistence policy, security/role rules, or business validation.

## Notifications and command state

- Use `SetProperty` for stored bindable values and notify computed dependent properties explicitly.
- An action's `CanExecute` dependencies must be identifiable and refreshed together.
- Do not make background refresh steal focus, rebuild an edited collection, or execute a foreground button command.
- Async command errors must reach a status/notification surface; do not let dispatcher exceptions become the reporting mechanism.

## Lifetime

- A ViewModel that owns timers, polling, subscriptions, clients, or cancellation sources implements an explicit cleanup path.
- Reused views/ViewModels must not subscribe again on every navigation.
- Temporary windows detach handlers and release resources on final close; hidden long-running windows retain only the resources their workflow requires.
