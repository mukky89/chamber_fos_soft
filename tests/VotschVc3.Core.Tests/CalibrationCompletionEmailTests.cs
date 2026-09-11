using System.Net;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Notifications;
using Xunit;
namespace VotschVc3.Core.Tests;
public sealed class CalibrationCompletionEmailTests
{
    [Theory]
    [InlineData(0.3906556, 20.81438, 59.87994, "1 % kalibračného rozsahu")]
    [InlineData(1.0, 20, 60, "2,5 % kalibračného rozsahu")]
    [InlineData(0.1, 20, 20, "% rozsahu: N/A")]
    public void FailureShowsPercentageFromEachModelsSavedTolerance(double tolerance, double min, double max, string expected)
    {
        var model = Model("ABC", "FAIL");
        model.ErrorToleranceC = tolerance;
        model.MinimumTemperatureC = min;
        model.MaximumTemperatureC = max;
        var message = CalibrationCompletionEmail.Create(new() { CalibrationResults = [model] }, null);
        Assert.Contains(expected, message.Text);
        Assert.Contains(expected, WebUtility.HtmlDecode(message.Html));
    }

    [Fact]
    public void MissingServerPathNeverFallsBackToLocalFiles()
    {
        var message = CalibrationCompletionEmail.Create(new() { LocalRunDirectory = @"C:\private\run" }, null);
        Assert.Empty(message.Attachments);
        Assert.Contains("Serverový priečinok nie je nastavený", message.Text);
        Assert.DoesNotContain(@"C:\private", message.Html);
        Assert.Contains("Kalibrácia: N/A", message.Text);
    }
    [Fact]
    public void CountsUniquePeaksAndSeparatesModelFailureFromReferenceWarning()
    {
        var run = new CalibrationRunRecord
        {
            State = CalibrationRunState.CompletedWithWarnings,
            CalibrationResults = [Model("ABC", "PASS"), Model("ABCD", "FAIL"), Model("FBGS", "FAIL")],
        };
        var message = CalibrationCompletionEmail.Create(run, @"G:\Výsledky & merania\run 01");
        string html = WebUtility.HtmlDecode(message.Html);
        Assert.Contains("Nevyhovujúce peaky: 1", message.Text);
        Assert.Contains("Kalibrácia: FAIL", message.Text);
        Assert.Contains("Záverečné overenie: WARNING", message.Text);
        Assert.Contains("DOKONČENÁ S UPOZORNENIAMI", message.Text);
        Assert.Contains("Max. chyba 0,500 °C; limit 0,100 °C", html);
        Assert.Equal(4, html.Split("Nestabilná WIKA").Length - 1);
        Assert.Contains("Teplota z koef. [°C]", html);
        Assert.Contains("WIKA [°C]", html);
        Assert.Contains("Problém", html);
        Assert.Contains("max-width:1600px", html);
        Assert.Equal(1, html.Split("<h2 style=\"font-size:17px\">Záverečné overenie pri 25 °C").Length - 1);
        Assert.Contains("file:///G:/", message.Html);
        Assert.Contains("&amp;", message.Html);
        Assert.Empty(message.Attachments);
        Assert.DoesNotContain("KoeficientA", html);
        string finalTable = html[html.IndexOf("<h2 style=\"font-size:17px\">Záverečné overenie pri 25 °C")..];
        Assert.Equal(3, finalTable.Split("SN-1").Length - 1);
        Assert.Contains("Lambda at T [nm]", finalTable);
        Assert.Contains("1550,123456", finalTable);
    }
    [Fact]
    public void MissingModelsAndDifferentDevicesAreNotCountedAsPassing()
    {
        var run = new CalibrationRunRecord { Plateaus = [new() { Targets = [
            new() { SerialNumber = "SN", Channel = "1", PeakId = "P1", PeakLoggerDeviceSerialNumber = "A", Problem = "Bez vzoriek" },
            new() { SerialNumber = "SN", Channel = "1", PeakId = "P1", PeakLoggerDeviceSerialNumber = "B", Problem = "Bez vzoriek" }
        ] }] };
        var message = CalibrationCompletionEmail.Create(run, null);
        Assert.Contains("Plata / peaky: 1 / 2", message.Text);
        Assert.Contains("nevyhodnotené: 2", message.Text);
        Assert.Contains("Bez vzoriek", message.Text);
    }
    [Fact]
    public void UntrustedLabelsAreEncoded()
    {
        var model = Model("<script>", "FAIL");
        model.SerialNumber = "<img src=x>";
        var message = CalibrationCompletionEmail.Create(new() { CalibrationResults = [model] }, null);
        Assert.DoesNotContain("<script>", message.Html);
        Assert.Contains("&lt;script&gt;", message.Html);
        Assert.DoesNotContain("<img src=x>", message.Html);
    }
    private static TemperatureCalibrationResult Model(string type, string result) => new()
    {
        SerialNumber = "SN-1", Channel = "1", PeakId = "P1", CalibrationType = type,
        Result = result, MaxErrorC = 0.5, ErrorToleranceC = 0.1,
        FinalExpectedLambdaNm = 1550.123456, FinalTemperatureErrorC = -0.2, FinalReferenceTemperatureC = 25.6,
        FinalCheckStatus = "WARNING", FinalCheckProblem = "Nestabilná WIKA"
    };
}
