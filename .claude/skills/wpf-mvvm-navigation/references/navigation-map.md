# Navigation map

## Main window

`MainWindow` creates one `ShellViewModel` and assigns it as `DataContext`.

The visual tree contains:

- a permanent `HomeView`, visible when `ShellViewModel.IsHome` is true
- a `ContentControl` bound to `DetailView`
- implicit DataTemplates mapping supported detail ViewModel types to views

`ShellViewModel.CurrentView` is the route state. Setting it to the Shell itself means Home. `IsHome` and `DetailView` must be notified whenever `CurrentView` changes.

Current detail routes include chamber, thermometers, recording viewer, profile library, quick profile, login, audit, app log, changelog, and administration.

## Windows and dialogs

- FBG calibration uses a separate reusable window so long-running live workspace state is not rebuilt during main navigation.
- Chart and zoom windows provide independent visualization space.
- Confirmation, password, exit, import, and export workflows use focused dialogs/windows.

Before adding a new window, decide its owner, modality, lifetime, close-vs-hide behavior, state restoration, and shutdown behavior.

## Authentication and roles

- Login is a route represented by `LoginViewModel`.
- Admin navigation and mutations depend on the existing role state and command predicates.
- Logout returns to login and must not leave a privileged page reachable.
- Public/login content must not link into an authenticated main route.

## New route checklist

1. Create or select a focused ViewModel.
2. Add an implicit DataTemplate to `MainWindow.xaml` for main-window content.
3. Add the navigation command with correct role/state predicate.
4. Define Home/back behavior and whether the instance is reused.
5. Refresh data at the correct lifecycle point without duplicating subscriptions.
6. Verify logout, repeated entry, and application shutdown.
