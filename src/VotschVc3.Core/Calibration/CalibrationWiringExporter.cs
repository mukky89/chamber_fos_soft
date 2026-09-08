using ClosedXML.Excel;

namespace VotschVc3.Core.Calibration;

/// <summary>Immutable wiring snapshot belonging to one calibration run.</summary>
public static class CalibrationWiringExporter
{
    public const string FileName = "zapojenie.xlsx";

    public static void Export(string directory, CalibrationRunRecord run, CalibrationSetup setup)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileName);
        if (File.Exists(path)) return; // Resume must preserve the original run snapshot.
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add("Zapojenie");
        sheet.ShowGridLines = false;
        sheet.Style.Font.FontName = "Calibri";
        sheet.Style.Font.FontSize = 11;
        sheet.Range("A1:N1").Merge().Value = "FBG KALIBRÁCIA • ZAPOJENIE";
        sheet.Range("A1:N1").Style.Fill.BackgroundColor = XLColor.FromHtml("#18334E");
        sheet.Range("A1:N1").Style.Font.FontColor = XLColor.White;
        sheet.Range("A1:N1").Style.Font.FontSize = 20;
        sheet.Range("A1:N1").Style.Font.Bold = true;
        sheet.Row(1).Height = 36;
        sheet.Range("A2:N2").Merge().Value = $"Beh: {run.DisplayRunId}   |   Profil: {run.ProfileName}   |   Komora: {run.ChamberName}";
        sheet.Range("A3:N3").Merge().Value = $"Začiatok: {run.StartedAt.LocalDateTime:dd.MM.yyyy HH:mm:ss}   |   Operátor: {run.Operator}   |   Vybrané peaky: {setup.Mappings.Count(m => m.Selected)} / {setup.Mappings.Count}";
        sheet.Rows(2, 3).Height = 25;
        sheet.Range("A2:N3").Style.Font.FontColor = XLColor.FromHtml("#52677E");
        string[] headers = { "Kalibrovať", "Kanál", "Peak ID", "FBG index", "SN snímača", "SN kanála", "SN CHAIN", "SN interrogátora", "λ pri štarte [nm]", "Nominálna λ [nm]", "Zákazník", "Zákazka", "Popis produktu", "Poznámky" };
        for (int c = 0; c < headers.Length; c++) sheet.Cell(5, c + 1).Value = headers[c];
        int row = 6;
        foreach (var m in setup.Mappings)
        {
            string[] values = { m.Selected ? "Áno" : "Nie", m.Channel, m.PeakId, m.PeakIndex.ToString(),
                m.SerialNumber, m.ChannelSerialNumber, m.ChainSerialNumber, m.PeakLoggerDeviceSerialNumber,
                "", "", m.Customer, m.Order, m.ProductDescription, m.Notes };
            for (int c = 0; c < values.Length; c++) sheet.Cell(row, c + 1).Value = values[c];
            if (m.CurrentWavelengthNm is double current && double.IsFinite(current)) sheet.Cell(row, 9).Value = current;
            if (m.NominalWavelengthNm is double nominal && double.IsFinite(nominal)) sheet.Cell(row, 10).Value = nominal;
            sheet.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml(m.Selected ? "#087A52" : "#69788A");
            sheet.Cell(row, 1).Style.Font.Bold = m.Selected;
            row++;
        }
        int last = Math.Max(6, row - 1);
        var table = sheet.Range(5, 1, last, 14).CreateTable("Zapojenie");
        table.Theme = XLTableTheme.TableStyleMedium2;
        table.ShowAutoFilter = true;
        sheet.Range(5, 1, last, 14).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Range(5, 1, last, 14).Style.Alignment.WrapText = true;
        sheet.Columns(1, 4).Width = 12;
        sheet.Columns(5, 8).Width = 23;
        sheet.Columns(9, 10).Width = 19;
        sheet.Columns(11, 12).Width = 23;
        sheet.Columns(13, 14).Width = 42;
        sheet.Range(6, 9, last, 10).Style.NumberFormat.Format = "0.000000";
        sheet.Row(5).Height = 32;
        sheet.Rows(6, last).AdjustToContents();
        sheet.SheetView.FreezeRows(5);
        sheet.SheetView.FreezeColumns(2);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PaperSize = XLPaperSize.A3Paper;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(1, 5);
        string temporary = Path.Combine(directory, $".zapojenie-{Guid.NewGuid():N}.xlsx");
        try
        {
            book.SaveAs(temporary);
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
