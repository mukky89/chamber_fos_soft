using System.Text.Json;
using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public class FbgWavelengthComparisonTests
{
    [Fact]
    public void Types_follow_dropped_wavelength_not_api_order()
    {
        SylexFbgWavelength[] api = [new(1531.9,1531.9,null,"S"), new(1533.3,1530.9,2.4,"T")];
        Assert.Equal(new string?[] { "T", "S" }, FbgWavelengthComparison.ResolveTypes([1530.574,1531.670], api));
        Assert.All(FbgWavelengthComparison.ResolveTypes([1500,1531.670], api), value => Assert.Null(value));
        Assert.All(FbgWavelengthComparison.ResolveTypes([1531,1531], api), value => Assert.Null(value));
    }

    [Fact]
    public void Uses_dropped_wavelength_and_matches_reversed_api_order()
    {
        var expected = JsonSerializer.Deserialize<SylexFbgWavelength[]>("""
            [{"wl":1531.9,"wlDropped":1531.9},{"wl":1533.3,"wlDrop":2.4,"wlDropped":1530.9}]
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(FbgWavelengthComparison.Evaluate([("P2",1531.659),("P1",1530.567)], expected).Passed);
    }

    [Theory]
    [InlineData(1530.4, true)]
    [InlineData(1531.4, true)]
    [InlineData(1531.401, false)]
    public void Applies_half_nanometre_tolerance(double measured, bool pass)
    {
        Assert.Equal(pass, FbgWavelengthComparison.Evaluate([("P1", measured)], [new(1533.3,null,2.4)]).Passed);
    }

    [Fact]
    public void Does_not_reuse_one_expected_peak_for_multiple_measured_peaks()
    {
        Assert.False(FbgWavelengthComparison.Evaluate([("P1",1530.9),("P2",1530.95)], [new(1530.9,null)]).Passed);
        Assert.False(FbgWavelengthComparison.Evaluate([("P1",1530.9),("P2",1530.95)], [new(1530.9,null),new(1532,null)]).Passed);
    }

    [Fact]
    public void Missing_or_invalid_data_is_not_a_pass()
    {
        Assert.Null(FbgWavelengthComparison.Evaluate([("P1",1530.9)], null).Passed);
        Assert.Null(FbgWavelengthComparison.Evaluate([("P1",1530.9)], [new(null,null)]).Passed);
        Assert.Null(FbgWavelengthComparison.Evaluate([("P1",double.NaN)], [new(1530.9,null)]).Passed);
        Assert.Null(FbgWavelengthComparison.Evaluate([], [new(1530.9,null)]).Passed);
    }
}
