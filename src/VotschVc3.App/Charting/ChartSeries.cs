using System.Windows;
using System.Windows.Media;

namespace VotschVc3.App.Charting;

/// <summary>One line series rendered by <see cref="Views.ChartView"/>.</summary>
public sealed class ChartSeries
{
    public ChartSeries(string name, Brush stroke, IReadOnlyList<Point> points, bool dashed = false)
    {
        Name = name;
        Stroke = stroke;
        Points = points;
        Dashed = dashed;
    }

    /// <summary>Legend label.</summary>
    public string Name { get; }

    /// <summary>Line colour.</summary>
    public Brush Stroke { get; }

    /// <summary>Data points in data space (X, Y).</summary>
    public IReadOnlyList<Point> Points { get; }

    /// <summary>Render the line dashed (used for set point lines).</summary>
    public bool Dashed { get; }
}
