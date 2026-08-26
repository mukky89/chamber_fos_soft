using VotschVc3.Core.Charting;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// Zoom/pan window of the chart X axis: what the mouse wheel over a profile or a
/// measurement chart actually computes.
/// </summary>
public class TimeAxisViewportTests
{
    [Fact]
    public void FreshViewportShowsWholeRange()
    {
        var viewport = new TimeAxisViewport();

        AxisWindow window = viewport.Resolve(0, 600);

        Assert.False(viewport.IsZoomed);
        Assert.False(window.IsZoomed);
        Assert.Equal(0d, window.Min);
        Assert.Equal(600d, window.Max);
        Assert.Equal(1d, window.Zoom);
    }

    [Fact]
    public void ZoomKeepsTheValueUnderTheCursorInPlace()
    {
        var viewport = new TimeAxisViewport();

        // Cursor in the middle of a 0–600 min profile, zoom 2x -> 300 min window
        // centred on minute 300.
        Assert.True(viewport.Zoom(2, 0.5, 0, 600));
        AxisWindow window = viewport.Resolve(0, 600);

        Assert.Equal(300d, window.Span, 6);
        Assert.Equal(150d, window.Min, 6);
        Assert.Equal(300d, window.Min + 0.5 * window.Span, 6);
        Assert.Equal(2d, window.Zoom, 6);
    }

    [Fact]
    public void ZoomAtTheLeftEdgeStaysInsideTheData()
    {
        var viewport = new TimeAxisViewport();

        viewport.Zoom(4, 0, 0, 600);
        AxisWindow window = viewport.Resolve(0, 600);

        Assert.Equal(0d, window.Min, 6);
        Assert.Equal(150d, window.Max, 6);
    }

    [Fact]
    public void ZoomingBackOutResetsToTheWholeRange()
    {
        var viewport = new TimeAxisViewport();
        viewport.Zoom(4, 0.5, 0, 600);

        Assert.True(viewport.Zoom(0.25, 0.5, 0, 600));

        Assert.False(viewport.IsZoomed);
        Assert.Equal(new AxisWindow(0, 600, 1), viewport.Resolve(0, 600));
    }

    [Fact]
    public void ZoomStopsAtTheMinimumWindow()
    {
        var viewport = new TimeAxisViewport(minimumSpan: 10);

        for (int i = 0; i < 50; i++)
        {
            viewport.Zoom(2, 0.5, 0, 600);
        }

        Assert.Equal(10d, viewport.Resolve(0, 600).Span, 6);
        Assert.False(viewport.Zoom(2, 0.5, 0, 600));
    }

    [Fact]
    public void ZoomIsCappedByMaxZoomEvenWithATinyMinimumSpan()
    {
        var viewport = new TimeAxisViewport(minimumSpan: 0.001);

        for (int i = 0; i < 100; i++)
        {
            viewport.Zoom(2, 0.5, 0, 600);
        }

        Assert.Equal(TimeAxisViewport.MaxZoom, viewport.Resolve(0, 600).Zoom, 6);
    }

    [Fact]
    public void PanMovesTheWindowAndStopsAtTheEnd()
    {
        var viewport = new TimeAxisViewport();
        viewport.Zoom(4, 0.5, 0, 600); // 150 min window, 225–375

        Assert.True(viewport.MoveTo(100, 0, 600));
        Assert.Equal(100d, viewport.Resolve(0, 600).Min, 6);

        // Past the end – clamped to the last full window.
        Assert.True(viewport.MoveTo(10_000, 0, 600));
        AxisWindow window = viewport.Resolve(0, 600);
        Assert.Equal(450d, window.Min, 6);
        Assert.Equal(600d, window.Max, 6);
    }

    [Fact]
    public void PanDoesNothingWhileTheWholeRangeIsShown()
    {
        var viewport = new TimeAxisViewport();

        Assert.False(viewport.MoveTo(120, 0, 600));
        Assert.False(viewport.IsZoomed);
    }

    [Fact]
    public void GrowingLiveChartKeepsTheZoomedWindowInPlace()
    {
        var viewport = new TimeAxisViewport();
        viewport.Zoom(2, 0.5, 0, 100); // 50 min window, 25–75

        // A live chart keeps appending points; the operator's window must not move.
        AxisWindow window = viewport.Resolve(0, 400);

        Assert.Equal(25d, window.Min, 6);
        Assert.Equal(75d, window.Max, 6);
        Assert.Equal(8d, window.Zoom, 6);
    }

    [Fact]
    public void ShrinkingRangeClampsTheWindowBackIntoTheData()
    {
        var viewport = new TimeAxisViewport();
        viewport.Zoom(4, 1, 0, 600); // window at the very end: 450–600

        // A shorter profile is loaded – the window must fall back inside it.
        AxisWindow window = viewport.Resolve(0, 200);

        Assert.True(window.Min >= 0);
        Assert.True(window.Max <= 200);
        Assert.Equal(150d, window.Span, 6);
    }

    [Fact]
    public void DegenerateRangeIsHandled()
    {
        var viewport = new TimeAxisViewport();

        Assert.False(viewport.Zoom(2, 0.5, 5, 5));
        Assert.Equal(new AxisWindow(5, 5, 1), viewport.Resolve(5, 5));
    }

    [Fact]
    public void ResetGoesBackToTheWholeRange()
    {
        var viewport = new TimeAxisViewport();
        viewport.Zoom(4, 0.5, 0, 600);

        viewport.Reset();

        Assert.False(viewport.IsZoomed);
        Assert.Equal(600d, viewport.Resolve(0, 600).Span, 6);
    }
}
