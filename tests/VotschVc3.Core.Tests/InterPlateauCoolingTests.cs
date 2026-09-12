using VotschVc3.Core.Calibration;
using VotschVc3.App.ViewModels;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class InterPlateauCoolingTests
{
    [Fact] public void CoolingRequiresEqualConsecutiveTargets()
    {
        Assert.True(InterPlateauCooling.RequiresCooling(50, 50));
        Assert.False(InterPlateauCooling.RequiresCooling(40, 50));
        Assert.False(InterPlateauCooling.RequiresCooling(null, 50));
    }
    [Fact] public void HoldStartsOnlyAfterCoolingAndResetsOnExcursionOrMissingReference()
    {
        var hold = new InterPlateauCooling();
        Assert.False(hold.Observe(TimeSpan.Zero, 50, 40));
        Assert.False(hold.Observe(TimeSpan.FromMinutes(30), 40, 40));
        Assert.False(hold.Observe(TimeSpan.FromMinutes(59), 39.9, 40));
        Assert.False(hold.Observe(TimeSpan.FromMinutes(60), double.NaN, 40));
        Assert.False(hold.Observe(TimeSpan.FromMinutes(61), 40, 40));
        Assert.True(hold.Observe(TimeSpan.FromMinutes(91), 39.9, 40));
        Assert.False(hold.Observe(TimeSpan.FromMinutes(92), 40.2, 40));
        Assert.Equal(TimeSpan.Zero, hold.Held);
    }
    [Fact] public void RoadmapHasGreyUncountedStepBetweenEqualPlateaus()
    {
        var model = new CalibrationDashboardViewModel();
        model.Configure("Test", "Chamber", new[] { 40d, 50, 50, 40 }, true, "");
        Assert.Equal(4, model.Points.Count);
        Assert.Equal(5, model.RoadmapPoints.Count);
        Assert.Equal("Cooling", model.RoadmapPoints[2].State);
        Assert.Null(model.RoadmapPoints[2].PlateauIndex);
        Assert.False(model.RoadmapPoints[2].CanRequestRecalibration);
        model.Begin(DateTimeOffset.Now);
        model.Apply(new CalibrationProgressSnapshot(CalibrationRunState.InterPlateauCooling, 2, 4, 40, 40, 40,
            0, 0, TimeSpan.FromMinutes(5), Array.Empty<CalibrationTargetProgress>(), "Ochladenie"), DateTimeOffset.Now);
        Assert.Equal(0, model.CompletedPoints);
        Assert.Equal("Pending", model.Points[2].State);
        Assert.Contains("ochladenia", model.Eta);
    }
}
