---
name: wpf
description: "Build and modernize WPF applications on .NET with correct XAML, data binding, commands, threading, styling, and Windows desktop migration decisions. USE FOR: working on WPF UI, MVVM, binding, commands, or desktop modernization; migrating WPF from .NET Framework to .NET; integrating newer Windows capabilities into a WPF app. DO NOT USE FOR: unrelated stacks; generic tasks that do not need this specific guidance."
compatibility: "Requires a WPF project on .NET or .NET Framework."
---

<!-- Source: https://github.com/managedcode/dotnet-skills (catalog/Frameworks/WPF/skills/wpf), MIT License. -->

# WPF

## Trigger On

- working on WPF UI, MVVM, binding, commands, or desktop modernization
- migrating WPF from .NET Framework to .NET
- implementing data binding, styles, templates, or control customization

## Documentation

- [WPF Overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [Data Binding Overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/)
- [Styles and Templates](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/styles-templates-overview)

### References

- [patterns.md](references/patterns.md) — MVVM patterns, binding patterns, command patterns
- [anti-patterns.md](references/anti-patterns.md) — common WPF mistakes and how to avoid them

## Workflow

1. **Apply MVVM pattern** — keep views dumb, logic in ViewModels, use commands
2. **Manage data binding explicitly** — choose correct binding modes, validate at runtime
3. **Use styles and templates deliberately** — keep UI composable, avoid page-specific hacks
4. **Handle threading correctly** — use Dispatcher for UI updates, async/await for long operations
5. **Validate both designer and runtime** — XAML composition failures often surface only at runtime

## Data Binding Modes

```xml
<!-- OneTime: read once at initialization -->
<TextBlock Text="{Binding CreatedDate, Mode=OneTime}"/>
<!-- OneWay: source to target only (default for most properties) -->
<TextBlock Text="{Binding Name, Mode=OneWay}"/>
<!-- TwoWay: bidirectional synchronization -->
<TextBox Text="{Binding Name, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"/>
```

## Threading

```csharp
// Prefer async/await: continuation resumes on the UI thread automatically.
private async Task LoadDataAsync()
{
    IsLoading = true;
    try
    {
        var data = await _service.GetDataAsync(); // background
        Items = new ObservableCollection<Item>(data); // back on UI thread
    }
    finally
    {
        IsLoading = false;
    }
}
```

## Anti-Patterns to Avoid

| Anti-Pattern | Why It's Bad | Better Approach |
|--------------|--------------|-----------------|
| Logic in code-behind | Hard to test, tight coupling | Use MVVM with ViewModels |
| Synchronous blocking calls | UI freezes | Use async/await |
| Hardcoded colors/sizes | Inconsistent, hard to theme | Use resource dictionaries |
| Direct Dispatcher.Invoke everywhere | Complex, error-prone | Prefer async/await marshaling |
| God ViewModel | Unmaintainable | Split into focused ViewModels |
| Event handlers for everything | Memory leaks, coupling | Use commands and bindings |

## Best Practices

1. **Freeze Freezables** — `brush.Freeze()` for thread safety and performance.
2. **Virtualize large collections** — `VirtualizingPanel.IsVirtualizing="True"`, `VirtualizationMode="Recycling"`.
3. **Derived styles need `BasedOn`** — otherwise the base styling is lost.
4. **Never create brushes in property getters** — cache static frozen brushes.
5. **Weak events for long-lived subscriptions** — or unsubscribe in `Dispose`.

## Validate

- binding and command flows are explicit
- code-behind is not carrying hidden business logic
- threading and dispatcher usage is correct
- styles and resources are properly organized
