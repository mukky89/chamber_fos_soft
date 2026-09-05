using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using ClosedXML.Excel;

#pragma warning disable CA1416 // The production application and CI target Windows; System.Drawing renders the PNG reports.

namespace VotschVc3.Core.Calibration;

/// <summary>Creates operator-friendly, per-plateau reports from the same samples used by calibration.</summary>
internal static class CalibrationPointReportExporter
{
    private static readonly Color[] SeriesColors =
    [
        Color.FromArgb(0x16, 0xA3, 0xE8), Color.FromArgb(0xF5, 0x9E, 0x0B),
        Color.FromArgb(0x10, 0xB9, 0x81), Color.FromArgb(0xA7, 0x8B, 0xFA),
        Color.FromArgb(0xF4, 0x72, 0xB6), Color.FromArgb(0x22, 0xD3, 0xEE),
        Color.FromArgb(0xFB, 0x71, 0x85), Color.FromArgb(0x84, 0xCC, 0x16),
    ];

    public static void ExportCompletedPlateaus(
        CalibrationRunRecord run,
        string runDirectory,
        CalibrationProfileSettings? settings)
    {
        settings ??= new CalibrationProfileSettings();
        List<TraceRow> trace = ReadTrace(Path.Combine(runDirectory, "wavelength-trace.csv"));
        string reportsDirectory = Path.Combine(runDirectory, "reports");

        foreach (CalibrationPlateauResult plateau in run.Plateaus.Where(p => p.Targets.Count > 0))
        {
            string pointDirectory = Path.Combine(reportsDirectory,
                $"plato-{plateau.PlateauIndex + 1:000}_{SafeTemperature(plateau.TargetTemperatureC)}C");
            Directory.CreateDirectory(pointDirectory);

            List<TraceRow> pointTrace = trace
                .Where(row => IsWithin(row.Timestamp, plateau.StartedAt, plateau.CompletedAt))
                .OrderBy(row => row.Timestamp)
                .ToList();
            List<TemperatureRow> temperatures = BuildTemperatures(plateau, pointTrace);
            List<Series> stabilization = BuildStabilizationSeries(plateau, pointTrace);
            List<Series> finalSamples = BuildFinalSeries(plateau);

            string temperatureChart = Path.Combine(pointDirectory, "wika-stabilna-teplota.png");
            string stabilizationChart = Path.Combine(pointDirectory, "fbg-stabilizacia.png");
            string finalChart = Path.Combine(pointDirectory, "fbg-finalne-meranie.png");
            string workbookPath = Path.Combine(pointDirectory, "kalibracny-bod.xlsx");
            if (File.Exists(temperatureChart) && File.Exists(stabilizationChart) &&
                File.Exists(finalChart) && File.Exists(workbookPath))
                continue;

            RenderTemperatureChart(temperatureChart, plateau, temperatures, settings);
            RenderWavelengthChart(stabilizationChart, "Stabilizácia FBG vlnovej dĺžky", stabilization, "Stabilizačné vzorky");
            RenderWavelengthChart(finalChart, "Finálne meranie FBG vlnovej dĺžky", finalSamples, "Finálne vzorky");
            WriteWorkbook(workbookPath, run, plateau, settings,
                temperatures, stabilization, finalSamples, temperatureChart, stabilizationChart, finalChart);
        }

        string previousError = Path.Combine(runDirectory, "report-generation-error.txt");
        if (File.Exists(previousError)) File.Delete(previousError);
    }

    private static List<TraceRow> ReadTrace(string path)
    {
        var result = new List<TraceRow>();
        if (!File.Exists(path)) return result;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        _ = reader.ReadLine();
        while (reader.ReadLine() is { } line)
        {
            string[] cells = line.Split(';');
            if (cells.Length < 11 ||
                !DateTimeOffset.TryParse(cells[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp) ||
                !int.TryParse(cells[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int peakIndex) ||
                !double.TryParse(cells[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double wavelength))
                continue;

            result.Add(new TraceRow(timestamp, cells[2], cells[3], cells[4], cells[5], peakIndex, wavelength,
                ParseNullable(cells[8]), ParseNullable(cells[9]), ParseNullable(cells[10])));
        }
        return result;
    }

    private static List<TemperatureRow> BuildTemperatures(CalibrationPlateauResult plateau, List<TraceRow> trace)
    {
        var rows = trace
            .GroupBy(row => row.Timestamp)
            .Select(group => new TemperatureRow(group.Key,
                group.Where(x => x.ReferenceTemperatureC.HasValue).Select(x => x.ReferenceTemperatureC!.Value).DefaultIfEmpty(double.NaN).Average(),
                group.Where(x => x.ChamberTemperatureC.HasValue).Select(x => x.ChamberTemperatureC!.Value).DefaultIfEmpty(double.NaN).Average()))
            .Where(row => !double.IsNaN(row.ReferenceTemperatureC) || !double.IsNaN(row.ChamberTemperatureC))
            .OrderBy(row => row.Timestamp)
            .ToList();

        if (rows.Count == 0)
        {
            rows = plateau.Targets.SelectMany(target => target.StableSamples)
                .GroupBy(sample => sample.Timestamp)
                .Select(group => new TemperatureRow(group.Key,
                    group.Where(x => x.ReferenceTemperatureC.HasValue).Select(x => x.ReferenceTemperatureC!.Value).DefaultIfEmpty(double.NaN).Average(),
                    group.Select(x => x.ActualTemperatureC).Average()))
                .OrderBy(row => row.Timestamp)
                .ToList();
        }
        return rows;
    }

    private static List<Series> BuildStabilizationSeries(CalibrationPlateauResult plateau, List<TraceRow> trace)
    {
        var result = new List<Series>();
        foreach (CalibrationMeasurementResult target in plateau.Targets)
        {
            DateTimeOffset? firstFinal = target.StableSamples.Count == 0 ? null : target.StableSamples.Min(x => x.Timestamp);
            List<PlotPoint> points = trace.Where(row => Matches(row, target) && (!firstFinal.HasValue || row.Timestamp < firstFinal.Value))
                .Select(row => new PlotPoint(row.Timestamp, row.WavelengthNm)).ToList();
            result.Add(new Series(Label(target), target.Status, target.Problem, points));
        }
        return result;
    }

    private static List<Series> BuildFinalSeries(CalibrationPlateauResult plateau) => plateau.Targets
        .Select(target => new Series(Label(target), target.Status, target.Problem,
            target.StableSamples.OrderBy(sample => sample.Timestamp).Select(sample => new PlotPoint(sample.Timestamp, sample.WavelengthNm)).ToList()))
        .ToList();

    private static void RenderTemperatureChart(string path, CalibrationPlateauResult plateau, List<TemperatureRow> rows, CalibrationProfileSettings settings)
    {
        var series = new List<Series>();
        List<PlotPoint> wika = rows.Where(x => !double.IsNaN(x.ReferenceTemperatureC)).Select(x => new PlotPoint(x.Timestamp, x.ReferenceTemperatureC)).ToList();
        List<PlotPoint> chamber = rows.Where(x => !double.IsNaN(x.ChamberTemperatureC)).Select(x => new PlotPoint(x.Timestamp, x.ChamberTemperatureC)).ToList();
        if (wika.Count > 0) series.Add(new Series("WIKA CTH7000", CalibrationTargetState.Stable, null, wika));
        if (chamber.Count > 0) series.Add(new Series("Komora (informatívne)", CalibrationTargetState.Live, null, chamber));

        DateTimeOffset start = rows.Count > 0 ? rows.Min(x => x.Timestamp) : plateau.StartedAt;
        DateTimeOffset end = rows.Count > 0 ? rows.Max(x => x.Timestamp) : plateau.CompletedAt;
        series.Add(new Series($"Cieľ {plateau.TargetTemperatureC:0.###} °C", CalibrationTargetState.Waiting, null,
            [new PlotPoint(start, plateau.TargetTemperatureC), new PlotPoint(end, plateau.TargetTemperatureC)]));
        RenderChart(path, "Stabilná referenčná teplota", $"WIKA limit: cieľ ± {settings.ChamberToleranceC:0.###} °C", "Teplota [°C]", series);
    }

    private static void RenderWavelengthChart(string path, string title, List<Series> series, string subtitle) =>
        RenderChart(path, title, subtitle, "Vlnová dĺžka [nm]", series);

    private static void RenderChart(string path, string title, string subtitle, string yLabel, List<Series> series)
    {
        const int width = 1400, height = 760;
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.FromArgb(0xF7, 0xF9, 0xFC));
        using var titleFont = new Font("Segoe UI", 22, FontStyle.Bold);
        using var normalFont = new Font("Segoe UI", 11);
        using var smallFont = new Font("Segoe UI", 9);
        using var axisPen = new Pen(Color.FromArgb(0x94, 0xA3, 0xB8), 1);
        using var gridPen = new Pen(Color.FromArgb(0xDF, 0xE6, 0xEF), 1) { DashStyle = DashStyle.Dash };
        using var textBrush = new SolidBrush(Color.FromArgb(0x18, 0x2A, 0x40));

        graphics.DrawString(title, titleFont, textBrush, 55, 26);
        graphics.DrawString(subtitle, normalFont, Brushes.SlateGray, 58, 68);
        var all = series.SelectMany(x => x.Points).ToList();
        if (all.Count == 0)
        {
            graphics.DrawString("Pre tento bod nie sú dostupné vzorky.", normalFont, Brushes.DarkOrange, 55, 145);
            bitmap.Save(path, ImageFormat.Png);
            return;
        }

        DateTimeOffset minTime = all.Min(x => x.Timestamp), maxTime = all.Max(x => x.Timestamp);
        double minY = all.Min(x => x.Value), maxY = all.Max(x => x.Value);
        if (Math.Abs(maxY - minY) < 1e-9) { minY -= 0.001; maxY += 0.001; }
        double padding = (maxY - minY) * 0.08;
        minY -= padding; maxY += padding;
        var plot = new RectangleF(115, 125, 1210, 500);

        for (int i = 0; i <= 5; i++)
        {
            float y = plot.Top + plot.Height * i / 5f;
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            double value = maxY - (maxY - minY) * i / 5d;
            graphics.DrawString(value.ToString(yLabel.Contains("nm") ? "0.000000" : "0.000", CultureInfo.CurrentCulture), smallFont, Brushes.SlateGray, 14, y - 8);
        }
        for (int i = 0; i <= 5; i++)
        {
            float x = plot.Left + plot.Width * i / 5f;
            graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            double minutes = (maxTime - minTime).TotalMinutes * i / 5d;
            graphics.DrawString($"{minutes:0.#} min", smallFont, Brushes.SlateGray, x - 18, plot.Bottom + 10);
        }
        graphics.DrawRectangle(axisPen, plot.X, plot.Y, plot.Width, plot.Height);
        graphics.DrawString(yLabel, normalFont, textBrush, plot.Left, plot.Bottom + 47);

        double timeRange = Math.Max(0.001, (maxTime - minTime).TotalSeconds);
        for (int seriesIndex = 0; seriesIndex < series.Count; seriesIndex++)
        {
            Series item = series[seriesIndex];
            Color color = SeriesColors[seriesIndex % SeriesColors.Length];
            using var pen = new Pen(color, item.Label.StartsWith("Cieľ", StringComparison.Ordinal) ? 2 : 3)
            { DashStyle = item.Label.StartsWith("Cieľ", StringComparison.Ordinal) ? DashStyle.Dash : DashStyle.Solid };
            PointF[] points = item.Points.Select(point => new PointF(
                plot.Left + (float)((point.Timestamp - minTime).TotalSeconds / timeRange * plot.Width),
                plot.Bottom - (float)((point.Value - minY) / (maxY - minY) * plot.Height))).ToArray();
            if (points.Length > 1) graphics.DrawLines(pen, points);
            else if (points.Length == 1)
            {
                using var pointBrush = new SolidBrush(color);
                graphics.FillEllipse(pointBrush, points[0].X - 3, points[0].Y - 3, 6, 6);
            }
            int legendX = 120 + (seriesIndex % 4) * 300;
            int legendY = 685 + (seriesIndex / 4) * 24;
            graphics.DrawLine(pen, legendX, legendY + 7, legendX + 28, legendY + 7);
            string suffix = IsPass(item.Status) ? "PASS" : item.Status is CalibrationTargetState.Live or CalibrationTargetState.Waiting ? "" : "FAIL";
            graphics.DrawString($"{item.Label} {suffix}".Trim(), smallFont, textBrush, legendX + 35, legendY);
        }
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void WriteWorkbook(string path, CalibrationRunRecord run, CalibrationPlateauResult plateau,
        CalibrationProfileSettings settings, List<TemperatureRow> temperatures, List<Series> stabilization,
        List<Series> finalSamples, string temperatureChart, string stabilizationChart, string finalChart)
    {
        using var workbook = new XLWorkbook();
        IXLWorksheet overview = workbook.Worksheets.Add("Prehľad");
        WriteOverview(overview, run, plateau, settings);
        IXLWorksheet temperatureSheet = workbook.Worksheets.Add("WIKA teplota");
        WriteTemperatureData(temperatureSheet, plateau, temperatures);
        IXLWorksheet stabilizationSheet = workbook.Worksheets.Add("Stabilizácia FBG");
        WriteSeriesData(stabilizationSheet, stabilization);
        IXLWorksheet finalSheet = workbook.Worksheets.Add("Finálne vzorky");
        WriteSeriesData(finalSheet, finalSamples);
        IXLWorksheet charts = workbook.Worksheets.Add("Grafy");
        charts.Cell("A1").Value = "Grafy kalibračného bodu";
        charts.Cell("A1").Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(XLColor.FromHtml("#182A40"));
        charts.AddPicture(temperatureChart).MoveTo(charts.Cell("A3")).WithSize(980, 532);
        charts.AddPicture(stabilizationChart).MoveTo(charts.Cell("A31")).WithSize(980, 532);
        charts.AddPicture(finalChart).MoveTo(charts.Cell("A59")).WithSize(980, 532);
        charts.SheetView.FreezeRows(1);
        workbook.SaveAs(path);
    }

    private static void WriteOverview(IXLWorksheet sheet, CalibrationRunRecord run, CalibrationPlateauResult plateau, CalibrationProfileSettings settings)
    {
        sheet.Cell("A1").Value = "FBG TEPLOTNÁ KALIBRÁCIA – REPORT BODU";
        sheet.Range("A1:H1").Merge().Style.Fill.SetBackgroundColor(XLColor.FromHtml("#182A40"));
        sheet.Cell("A1").Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(XLColor.White);
        string status = plateau.Targets.All(target => IsPass(target.Status)) ? "PASS" : "FAIL";
        object[,] info =
        {
            { "Run ID", run.DisplayRunId, "Profil", $"{run.DisplayProfileId} · {run.ProfileName}" },
            { "Komora", run.ChamberName, "Operátor", run.Operator },
            { "Plato", plateau.PlateauIndex + 1, "Cieľ", plateau.TargetTemperatureC },
            { "WIKA [°C]", plateau.ReferenceTemperatureC is { } reference ? reference : "—", "Komora [°C]", plateau.ActualTemperatureC },
            { "Začiatok", plateau.StartedAt.LocalDateTime, "Koniec", plateau.CompletedAt.LocalDateTime },
            { "Výsledok", status, "Počet peakov", plateau.Targets.Count },
        };
        sheet.Cell("A3").InsertData(info);
        sheet.Range("A3:A8").Style.Font.SetBold(); sheet.Range("C3:C8").Style.Font.SetBold();
        sheet.Cell("A10").Value = "Výsledky peakov";
        string[] headers = ["SN", "Kanál", "Peak", "Stav", "Vzorky", "Priemer [nm]", "Range [pm]", "σ [pm]", "Drift [pm/min]", "Čas stabilizácie [s]", "Dôvod / poznámka"];
        for (int i = 0; i < headers.Length; i++) sheet.Cell(11, i + 1).Value = headers[i];
        int row = 12;
        foreach (CalibrationMeasurementResult result in plateau.Targets)
        {
            sheet.Cell(row, 1).Value = result.SerialNumber; sheet.Cell(row, 2).Value = result.Channel;
            sheet.Cell(row, 3).Value = result.PeakId; sheet.Cell(row, 4).Value = IsPass(result.Status) ? "PASS" : "FAIL";
            sheet.Cell(row, 5).Value = result.SampleCount; sheet.Cell(row, 6).Value = result.MeanWavelengthNm;
            sheet.Cell(row, 7).Value = result.RangePm; sheet.Cell(row, 8).Value = result.StandardDeviationPm;
            sheet.Cell(row, 9).Value = result.DriftPmPerMinute; sheet.Cell(row, 10).Value = result.StabilizationTime.TotalSeconds;
            sheet.Cell(row, 11).Value = result.Problem ?? result.Status.ToString();
            sheet.Cell(row, 4).Style.Font.SetBold().Font.SetFontColor(IsPass(result.Status) ? XLColor.FromHtml("#087F5B") : XLColor.FromHtml("#C92A2A"));
            row++;
        }
        sheet.Cell(row + 1, 1).Value = "Použité limity";
        sheet.Cell(row + 2, 1).Value = $"WIKA: cieľ ± {settings.ChamberToleranceC:0.###} °C; stabilný čas {settings.ChamberStableDuration.TotalSeconds:0} s; drift ≤ {settings.MaxChamberDriftCPerMinute:0.###} °C/min";
        sheet.Cell(row + 3, 1).Value = $"FBG: {settings.RequiredStableSamples} stabilizačných + {settings.RequiredMeasurementSamples} finálnych vzoriek; range ≤ {settings.MaxWavelengthRangePm:0.###} pm; σ ≤ {settings.MaxWavelengthStdDevPm:0.###} pm; drift ≤ {settings.MaxWavelengthDriftPmPerMinute:0.###} pm/min";
        sheet.Range(row + 2, 1, row + 3, 11).Merge();
        FormatSheet(sheet, 11, Math.Max(11, row - 1));
        sheet.Column(11).Width = 42;
    }

    private static void WriteTemperatureData(IXLWorksheet sheet, CalibrationPlateauResult plateau, List<TemperatureRow> rows)
    {
        string[] headers = ["Čas", "Uplynulo [min]", "Cieľ [°C]", "WIKA [°C]", "Komora [°C]"];
        WriteHeaders(sheet, headers);
        DateTimeOffset origin = rows.Count > 0 ? rows[0].Timestamp : plateau.StartedAt;
        int row = 2;
        foreach (TemperatureRow item in rows)
        {
            sheet.Cell(row, 1).Value = item.Timestamp.LocalDateTime;
            sheet.Cell(row, 2).Value = (item.Timestamp - origin).TotalMinutes;
            sheet.Cell(row, 3).Value = plateau.TargetTemperatureC;
            if (!double.IsNaN(item.ReferenceTemperatureC)) sheet.Cell(row, 4).Value = item.ReferenceTemperatureC;
            if (!double.IsNaN(item.ChamberTemperatureC)) sheet.Cell(row, 5).Value = item.ChamberTemperatureC;
            row++;
        }
        FormatSheet(sheet, headers.Length, row - 1);
    }

    private static void WriteSeriesData(IXLWorksheet sheet, List<Series> series)
    {
        string[] headers = ["SN / kanál / peak", "Výsledok", "Dôvod", "Čas", "Poradie vzorky", "Uplynulo [s]", "Vlnová dĺžka [nm]"];
        WriteHeaders(sheet, headers);
        int row = 2;
        foreach (Series item in series)
        {
            DateTimeOffset? origin = item.Points.Count > 0 ? item.Points[0].Timestamp : null;
            if (item.Points.Count == 0)
            {
                sheet.Cell(row, 1).Value = item.Label; sheet.Cell(row, 2).Value = IsPass(item.Status) ? "PASS" : "FAIL";
                sheet.Cell(row, 3).Value = item.Problem ?? "Bez dostupných vzoriek"; row++; continue;
            }
            for (int index = 0; index < item.Points.Count; index++)
            {
                PlotPoint point = item.Points[index];
                sheet.Cell(row, 1).Value = item.Label; sheet.Cell(row, 2).Value = IsPass(item.Status) ? "PASS" : "FAIL";
                sheet.Cell(row, 3).Value = item.Problem ?? string.Empty; sheet.Cell(row, 4).Value = point.Timestamp.LocalDateTime;
                sheet.Cell(row, 5).Value = index + 1; sheet.Cell(row, 6).Value = (point.Timestamp - origin!.Value).TotalSeconds;
                sheet.Cell(row, 7).Value = point.Value; row++;
            }
        }
        FormatSheet(sheet, headers.Length, row - 1);
    }

    private static void WriteHeaders(IXLWorksheet sheet, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
    }

    private static void FormatSheet(IXLWorksheet sheet, int columns, int lastRow)
    {
        int headerRow = sheet.Name == "Prehľad" ? 11 : 1;
        sheet.Range(headerRow, 1, headerRow, columns).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#2563EB"));
        sheet.Range(headerRow, 1, headerRow, columns).Style.Font.SetBold().Font.SetFontColor(XLColor.White);
        if (lastRow >= headerRow) sheet.Range(headerRow, 1, lastRow, columns).SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns(1, columns).AdjustToContents(5, 40);
        sheet.Rows().Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        sheet.RangeUsed()?.Style.Border.SetBottomBorder(XLBorderStyleValues.Hair).Border.SetBottomBorderColor(XLColor.FromHtml("#DDE5EF"));
    }

    private static bool Matches(TraceRow row, CalibrationMeasurementResult target) =>
        string.Equals(row.SerialNumber, target.SerialNumber, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(row.Channel, target.Channel, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(row.PeakId, target.PeakId, StringComparison.OrdinalIgnoreCase) && row.PeakIndex == target.PeakIndex;

    private static bool IsWithin(DateTimeOffset value, DateTimeOffset start, DateTimeOffset end) =>
        start == default || end == default || (value >= start && value <= end);
    private static bool IsPass(CalibrationTargetState state) => state is CalibrationTargetState.Stable or CalibrationTargetState.Overridden;
    private static string Label(CalibrationMeasurementResult target) => $"{target.SerialNumber} · {target.Channel}/{target.PeakId}";
    private static double? ParseNullable(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : null;
    private static string SafeTemperature(double value) => value.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture).Replace('.', '_');

    private sealed record TraceRow(DateTimeOffset Timestamp, string SerialNumber, string DeviceSerialNumber,
        string Channel, string PeakId, int PeakIndex, double WavelengthNm, double? Intensity,
        double? ChamberTemperatureC, double? ReferenceTemperatureC);
    private sealed record TemperatureRow(DateTimeOffset Timestamp, double ReferenceTemperatureC, double ChamberTemperatureC);
    private sealed record PlotPoint(DateTimeOffset Timestamp, double Value);
    private sealed record Series(string Label, CalibrationTargetState Status, string? Problem, List<PlotPoint> Points);
}
#pragma warning restore CA1416
