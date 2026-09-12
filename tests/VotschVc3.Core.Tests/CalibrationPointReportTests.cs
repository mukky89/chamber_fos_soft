using System.Reflection;
using ClosedXML.Excel;
using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationPointReportTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void MissingTemperaturesAndStatisticsRemainExplicitInWorkbook(double missing)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Prehľad");
        var plateau = new CalibrationPlateauResult
        {
            TargetTemperatureC = 40, ActualTemperatureC = missing, ReferenceTemperatureC = missing,
            Targets = { new CalibrationMeasurementResult { Status = CalibrationTargetState.SkippedIdentityUncertain,
                MeanWavelengthNm = missing, RangePm = missing, StandardDeviationPm = missing, DriftPmPerMinute = missing } }
        };
        var exporter = typeof(CalibrationStore).Assembly.GetType("VotschVc3.Core.Calibration.CalibrationPointReportExporter")!;
        exporter.GetMethod("WriteOverview", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { sheet, new CalibrationRunRecord(), plateau, new CalibrationProfileSettings() });
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        using var reopened = new XLWorkbook(stream);
        var actual = reopened.Worksheet(1);
        Assert.Equal(40, actual.Cell("D5").GetDouble());
        Assert.Equal("—", actual.Cell("B6").GetString());
        Assert.Equal("—", actual.Cell("D6").GetString());
        Assert.Equal("FAIL", actual.Cell("D12").GetString());
        for (int column = 6; column <= 9; column++) Assert.Equal("—", actual.Cell(12, column).GetString());
    }
}
