<!-- Source: https://github.com/managedcode/dotnet-skills, MIT License. Trimmed to the patterns relevant for this app. -->

# WPF Patterns Reference

## MVVM Structure

```
View (XAML) ←→ ViewModel (C#) ←→ Model (C#)
     ↑              ↑
  DataBinding    Services
```

- **Model**: business logic and data (in this repo: `VotschVc3.Core`)
- **View**: XAML UI, no business logic (`VotschVc3.App/Views`)
- **ViewModel**: presentation logic, exposes data and commands (`VotschVc3.App/ViewModels`)

Note: this repo uses its own lightweight MVVM base (`ObservableObject`, `RelayCommand`,
`AsyncRelayCommand` in `VotschVc3.App/Mvvm`) instead of the CommunityToolkit source
generators — follow the existing hand-written property pattern:

```csharp
private double _value;
public double Value { get => _value; set { if (SetProperty(ref _value, value)) Recalculate(); } }
```

## Collection Binding with Filtering

```csharp
public ICollectionView ItemsView { get; }

public MyViewModel()
{
    ItemsView = CollectionViewSource.GetDefaultView(_allItems);
    ItemsView.Filter = o => string.IsNullOrWhiteSpace(FilterText)
        || ((Item)o).Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
}
// call ItemsView.Refresh() when FilterText changes
```

## Master–Detail Binding

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="300"/>
        <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>
    <ListBox Grid.Column="0" ItemsSource="{Binding Items}" SelectedItem="{Binding SelectedItem}"/>
    <ContentControl Grid.Column="1" Content="{Binding SelectedItem}"/>
</Grid>
```

## Parameterized Commands from Item Templates

```xml
<Button Content="Zmazať"
        Command="{Binding DataContext.DeleteItemCommand,
                  RelativeSource={RelativeSource AncestorType=ListBox}}"
        CommandParameter="{Binding}"/>
```

## Binding Helpers

```xml
<!-- Fallbacks -->
<TextBlock Text="{Binding City, FallbackValue='—', TargetNullValue='—'}"/>
<!-- String format -->
<TextBlock Text="{Binding Total, StringFormat='{}{0:0.0} °C'}"/>
<!-- MultiBinding -->
<TextBlock>
    <TextBlock.Text>
        <MultiBinding StringFormat="{}{0}, {1}">
            <Binding Path="LastName"/>
            <Binding Path="FirstName"/>
        </MultiBinding>
    </TextBlock.Text>
</TextBlock>
```

## State Management Pattern (Loading / Loaded / Empty / Error)

Expose one state enum + message on the ViewModel and switch panel visibility with
converters, instead of many independent bool flags that can drift apart.

## Attached Behavior Pattern

For view-only behaviors (e.g. select-all-on-focus), use attached dependency
properties instead of code-behind event wiring — keeps views declarative.
