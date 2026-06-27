using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Recording;

namespace VotschVc3.App.ViewModels;

/// <summary>Summary statistics of one recorded series, formatted for the table.</summary>
public sealed class RecordingStatRow
{
    public RecordingStatRow(RecordingSeries series, Brush color)
    {
        Name = series.Name;
        Color = color;
        Min = Format(series.Min);
        Max = Format(series.Max);
        Mean = Format(series.Mean);
        Count = series.Count;
    }

    public string Name { get; }
    public Brush Color { get; }
    public string Min { get; }
    public string Max { get; }
    public string Mean { get; }
    public int Count { get; }

    private static string Format(double? v) => v?.ToString("0.000", CultureInfo.InvariantCulture) ?? "—";
}

/// <summary>
/// Opens a recorded CSV (chamber or thermometer) and shows it as a chart with
/// per-series statistics – the analysis / "access past test data" workflow.
/// </summary>
public sealed class RecordingViewerViewModel : ObservableObject
{
    private static readonly Brush[] Palette =
    {
        Make(0xFF, 0x8A, 0x5C), Make(0x4F, 0xB6, 0xFF), Make(0x4F, 0xC1, 0x7A),
        Make(0xE2, 0xC5, 0x55), Make(0xB0, 0x8C, 0xFF), Make(0xFF, 0x7A, 0x9A),
    };

    public RecordingViewerViewModel()
    {
        OpenCommand = new RelayCommand(Open);
    }

    private string _filePath = string.Empty;
    public string FilePath { get => _filePath; private set => SetProperty(ref _filePath, value); }

    private string _statusMessage = "Otvor uložený CSV záznam (komory alebo teplomera).";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    private IReadOnlyList<ChartSeries> _series = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> Series { get => _series; private set => SetProperty(ref _series, value); }

    public ObservableCollection<RecordingStatRow> Stats { get; } = new();

    public RelayCommand OpenCommand { get; }

    private void Open()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Otvoriť záznam",
            Filter = "CSV záznamy (*.csv)|*.csv|Všetky súbory (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Load(dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Načítanie zlyhalo: {ex.Message}";
        }
    }

    private void Load(string path)
    {
        RecordingData data = RecordingReader.Read(path);
        FilePath = path;

        DateTimeOffset t0 = data.Timestamps.Count > 0 ? data.Timestamps[0] : DateTimeOffset.Now;

        double X(int i)
        {
            if (i >= data.Timestamps.Count)
            {
                return i;
            }

            DateTimeOffset ts = data.Timestamps[i];
            if (ts <= DateTimeOffset.MinValue || t0 <= DateTimeOffset.MinValue)
            {
                return i;
            }

            return (ts - t0).TotalMinutes;
        }

        var chartSeries = new List<ChartSeries>();
        Stats.Clear();

        for (int s = 0; s < data.Series.Count; s++)
        {
            RecordingSeries series = data.Series[s];
            Brush color = ColorFor(series.Name, s);
            bool dashed = series.Name.Contains("Setpoint", StringComparison.OrdinalIgnoreCase);

            var points = new List<Point>();
            for (int i = 0; i < series.Values.Count; i++)
            {
                if (series.Values[i] is { } v)
                {
                    points.Add(new Point(X(i), v));
                }
            }

            if (points.Count > 0)
            {
                chartSeries.Add(new ChartSeries(series.Name, color, points, dashed));
            }

            Stats.Add(new RecordingStatRow(series, color));
        }

        Series = chartSeries;
        StatusMessage = $"{System.IO.Path.GetFileName(path)} · {data.RowCount} riadkov · {data.Series.Count} sérií.";
    }

    private static Brush ColorFor(string name, int index)
    {
        string n = name.ToLowerInvariant();
        if (n.StartsWith("temp")) return Palette[0];
        if (n.StartsWith("hum")) return Palette[1];
        if (n.StartsWith("reference")) return Palette[2];
        if (n.StartsWith("deviation")) return Palette[3];
        return Palette[index % Palette.Length];
    }

    private static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
