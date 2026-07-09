<!-- Source: https://github.com/managedcode/dotnet-skills, MIT License. Trimmed. -->

# WPF Anti-Patterns Reference

## MVVM Violations
- **Logic in code-behind** → move to ViewModel commands; code-behind only for pure view concerns (focus, scroll, animations).
- **God ViewModel** → split into focused ViewModels composed by a shell.
- **ViewModel referencing UI elements** → use services/abstractions (dialog service, navigation).

## Data Binding Mistakes
- **Missing INotifyPropertyChanged** → UI silently never updates; always raise change notifications (this repo: `SetProperty`).
- **Wrong binding mode** → `TextBox` needs `TwoWay` (+ `UpdateSourceTrigger` chosen deliberately: `PropertyChanged` for live values, `LostFocus` for numeric fields the user types into); display-only bindings should be `OneWay`/`OneTime`.
- **Typos in binding paths fail silently** → watch the debug output; enable `PresentationTraceSources` warnings in DEBUG.
- **Bulk-updating ObservableCollection item-by-item** → replace the whole collection for bulk updates.

## Threading Mistakes
- **Blocking the UI thread** with synchronous I/O → async/await.
- **Touching UI/bound collections from a background thread** → crash; marshal via Dispatcher (this repo: `RunOnUi` helper) or let `await` resume on the UI thread.
- **Dispatcher.Invoke in a loop** → batch into a single dispatch.

## Memory Leaks
- **Event subscriptions never removed** → unsubscribe in `Dispose`/detach (this repo does this for `PropertyChanged` on chambers and thermometers — keep it symmetrical).
- **Static events** hold subscribers forever.

## Style Mistakes
- **Hardcoded colors/sizes in views** → use the resource dictionary tokens (see the `wpf-ux-ui` skill for this project's palette).
- **Derived style without `BasedOn`** → loses the base template (e.g. custom Button styles must be `BasedOn="{StaticResource {x:Type Button}}"`).
- **Repeated inline styling** → define a named style once.

## Performance
- **No virtualization for long lists** → `VirtualizingPanel.IsVirtualizing="True"`.
- **new SolidColorBrush(...) in getters/converters** → cache static frozen brushes (this repo: `Freeze(r,g,b)` helpers).
- **Heavy computation in converters** → precompute in the ViewModel.

## Command Mistakes
- **async void handlers** → exceptions vanish; use AsyncRelayCommand with an onError callback (this repo's pattern).
- **Forgetting RaiseCanExecuteChanged** → buttons stuck enabled/disabled; this repo centralises it in `RefreshCommands()` — add new commands there.
