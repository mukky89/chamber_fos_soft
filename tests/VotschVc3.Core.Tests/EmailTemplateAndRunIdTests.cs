using VotschVc3.Core.Calibration;
using VotschVc3.Core.Notifications;

namespace VotschVc3.Core.Tests;

public sealed class EmailTemplateAndRunIdTests
{
    [Fact]
    public void EmailTemplate_RendersDetailsAndWarningTone()
    {
        string html = LabControlEmailTemplate.Create(
            "Kalibrácia FBG – WARNING – P-0214",
            "Run ID: 01-2026-09-04\nProfil ID: P-0214\nKomora: Komora 1\n\nWIKA sa neustálila.");

        Assert.Contains("SYLEX · LAB CONTROL", html);
        Assert.Contains("P-0214", html);
        Assert.Contains("01-2026-09-04", html);
        Assert.Contains("UPOZORNENIE", html);
        Assert.Contains("WIKA sa neustálila.", html);
    }

    [Fact]
    public void HumanRunId_IncrementsPerDayAndResetsNextDay()
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-control-run-id-tests", Guid.NewGuid().ToString("N"));
        try
        {
            DateTimeOffset day1 = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
            DateTimeOffset day2 = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

            Assert.Equal("01-2026-09-04", HumanReadableRunId.Allocate(root, day1));
            Assert.Equal("02-2026-09-04", HumanReadableRunId.Allocate(root, day1.AddMinutes(10)));
            Assert.Equal("01-2026-09-05", HumanReadableRunId.Allocate(root, day2));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CalibrationRunRecord_UsesShortOperatorFacingIds()
    {
        var run = new CalibrationRunRecord
        {
            HumanRunId = "01-2026-09-04",
            ProfileCode = "P-0214",
        };

        Assert.Equal("01-2026-09-04", run.DisplayRunId);
        Assert.Equal("P-0214", run.DisplayProfileId);
    }
}
