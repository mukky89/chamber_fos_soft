using ClosedXML.Excel;
using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationWiringExporterTests
{
    [Fact]
    public void ExportsStandaloneWiringAndPreservesOriginalOnResume()
    {
        string directory = Path.Combine(Path.GetTempPath(), "wiring-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var run = new CalibrationRunRecord { ProfileName = "Testovací profil", ChamberName = "Komora 1" };
            var setup = new CalibrationSetup { Mappings = new()
            {
                new() { Channel = "1.3", PeakId = "P1", PeakIndex = 1, SerialNumber = "000123/0001",
                    ChannelSerialNumber = "000123/0001", Selected = true, CurrentWavelengthNm = 1552.569,
                    Notes = "=SUM(A1:A2)", ProductDescription = "Dlhší popis snímača" },
                new() { Channel = "2.4", PeakId = "P2", SerialNumber = "000002", Selected = false }
            }};
            CalibrationWiringExporter.Export(directory, run, setup);
            string path = Path.Combine(directory, CalibrationWiringExporter.FileName);
            byte[] original = File.ReadAllBytes(path);
            using (var book = new XLWorkbook(path))
            {
                Assert.Single(book.Worksheets);
                var sheet = book.Worksheet("Zapojenie");
                Assert.Equal("000123/0001", sheet.Cell("E6").GetString());
                Assert.Equal("Áno", sheet.Cell("A6").GetString());
                Assert.Equal("Nie", sheet.Cell("A7").GetString());
                Assert.Equal(1552.569, sheet.Cell("I6").GetDouble());
                Assert.Equal("=SUM(A1:A2)", sheet.Cell("N6").GetString());
                Assert.False(sheet.Cell("N6").HasFormula);
                Assert.Single(sheet.Tables);
                Assert.True(sheet.Table("Zapojenie").ShowAutoFilter);
            }
            setup.Mappings[0].SerialNumber = "CHANGED";
            CalibrationWiringExporter.Export(directory, run, setup);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
