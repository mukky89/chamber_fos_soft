---
name: wpf-mvvm-navigation
description: Implement or review ViewModels, commands, bindings and navigation in the VotschVc3 WPF application. Use for ShellViewModel.CurrentView, DataTemplate routing, role-aware commands, async operations and ViewModel boundaries. Preserve the repository's hand-written MVVM layer unless migration is explicitly requested.
---

# VotschVc3 MVVM and navigation

Use the existing `ObservableObject`, `RelayCommand`, `RelayCommand<T>`, `AsyncRelayCommand`, and `AsyncRelayCommand<T>` implementations. Do not add CommunityToolkit.Mvvm merely to create a property, ViewModel, or command.

## Navigation model

- Main-window pages are ViewModel instances assigned to `ShellViewModel.CurrentView`.
- Register a new main page through an implicit DataTemplate in `MainWindow.xaml`.
- `CurrentView = this` represents Home; `DetailView` becomes null there.
- Preserve the permanently instantiated `HomeView` unless a deliberate lifecycle redesign is requested. Hiding it retains dashboard and live state.
- Use a separate `Window` only for a workflow that genuinely needs independent long-running, modal, comparison, or enlarged-chart space.
- Never expose a login-page route that bypasses authentication.

## Ownership

- ViewModels own application state, validation, commands, and orchestration.
- Views own focus, drawing, mouse capture, visual hit testing, and window lifecycle.
- Core owns device protocols, profiles, persistence, calibration rules, and reusable numerical logic.
- Do not add another responsibility to `ShellViewModel` or `ChamberViewModel` when a focused ViewModel or service can own it.
- Do not move custom-control rendering into a ViewModel merely to eliminate code-behind.

## Commands and lifecycle

- Mutating device commands encode role, connection, capability, ownership/interlock, and running state in `CanExecute`.
- Raise command-state changes whenever any dependency changes.
- Use async commands for I/O and long-running work, with bounded cancellation and an operator-visible error handler.
- Prevent duplicate execution; treat cancellation as a normal outcome.
- Long-lived subscriptions, polling, hardware clients, windows, and timers require explicit disposal or unsubscription.
- Repeated navigation must not create duplicate polling loops, event handlers, or clients.

## Routing

- Read [references/navigation-map.md](references/navigation-map.md) before adding a page, route, dialog, or window.
- Read [references/viewmodel-boundaries.md](references/viewmodel-boundaries.md) before extending `ShellViewModel`, `ChamberViewModel`, or calibration workspace code.
- Also use `wpf-reliability-performance` for polling, async overlap, startup/shutdown, reconnect, disposal, or UI responsiveness.

## Verification

Verify navigation forward/back, repeated page entry, role changes, logout/login, command enablement, active FBG interlocks, retained dashboard state, and shutdown disposal.
