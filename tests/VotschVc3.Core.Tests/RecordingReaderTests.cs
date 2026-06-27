using VotschVc3.Core.Recording;
using Xunit;

namespace VotschVc3.Core.Tests;

public class RecordingReaderTests
{
    [Fact]
    public void Reads_numeric_series_with_statistics_and_skips_text_columns()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, string.Join('\n',
                "Timestamp;Temperature;Humidity;Digital;Raw",
                "2026-06-27 10:00:00.000;20.0;50.0;000;x",
                "2026-06-27 10:00:01.000;30.0;60.0;000;y"));

            RecordingData data = RecordingReader.Read(path);

            Assert.Equal(2, data.RowCount);
            RecordingSeries temp = data.Series.First(s => s.Name == "Temperature");
            Assert.Equal(20, temp.Min);
            Assert.Equal(30, temp.Max);
            Assert.Equal(25, temp.Mean);
            Assert.Equal(2, temp.Count);
            Assert.DoesNotContain(data.Series, s => s.Name is "Digital" or "Raw");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Handles_blank_cells_as_gaps()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, string.Join('\n',
                "Timestamp;Temperature;Reference",
                "2026-06-27 10:00:00.000;20.0;",
                "2026-06-27 10:00:01.000;22.0;21.5"));

            RecordingData data = RecordingReader.Read(path);
            RecordingSeries reference = data.Series.First(s => s.Name == "Reference");

            Assert.Equal(1, reference.Count);
            Assert.Equal(21.5, reference.Mean);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
