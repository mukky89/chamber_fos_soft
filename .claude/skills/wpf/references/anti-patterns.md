<!-- Source: https://github.com/managedcode/dotnet-skills, MIT License. Trimmed. -->

# WPF Anti-Patterns Reference

## ⛔ XAML that fails to COMPILE (learned the hard way — always check these)

These do not error in an XML validator but break the WPF build (`dotnet build`
on Windows). WPF only compiles on Windows, so this environment cannot catch them —
review every new Style/ControlTemplate against this list before committing:

- **`Setter TargetName` can only target a named FrameworkElement** in the template,
  NOT a `Freezable` such as a `ScaleTransform`/`TranslateTransform`/`Brush`.
  Targeting a named transform in a `<Setter>` → error **MC4111 "Cannot find the
  Trigger target"**. To animate a named transform use a `Storyboard`
  (`Storyboard.TargetName="Sc"` works); for a static change, set the whole
  `RenderTransform` on the element (`<Setter TargetName="Bd" Property="RenderTransform">`).
- **`Setter.Value` cannot be a `{Binding}`** → "A Binding cannot be set on the Value
  property of type Setter." Also `{TemplateBinding}` is unreliable there. To reveal
  a per-instance colour on hover, fade in an overlay `Border` whose `Background` is a
  `{TemplateBinding Foreground}` (Opacity 0→1 via a Setter), don't bind Setter.Value.
- **Local values beat template/style trigger Setters.** If you set
  `TextElement.Foreground` (or any property) directly as an attribute on the element,
  a hover/checked trigger Setter on the SAME element will NOT override it. Put the
  default on a PARENT element (so the child inherits it) and let the trigger Setter on
  the child win, or drive it entirely from triggers.
- **Derived Style must use `BasedOn`** for `{x:Type Button}` etc., or it silently
  loses the base template.
- Completed `Storyboard`s hold their end value (`FillBehavior=HoldEnd`) and can
  "stick", overriding later trigger Setters on the same property. Prefer a single
  mechanism (all-storyboard or all-setter) per property; keep hover scale as
  storyboards and do not also set the same ScaleTransform via Setters.

**Process:** WPF build errors surface only in Windows CI (`.github/workflows/build.yml`).
Wait for green CI before merging XAML changes — do not merge a PR whose build hasn't
passed.

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
