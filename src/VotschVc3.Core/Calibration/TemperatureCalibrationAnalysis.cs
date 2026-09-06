using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace VotschVc3.Core.Calibration;

public sealed class TemperatureCalibrationResult
{
    public string SerialNumber { get; set; } = string.Empty;
    public string PeakLoggerDeviceSerialNumber { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string PeakId { get; set; } = string.Empty;
    public int PeakIndex { get; set; }
    public int PointCount { get; set; }
    public double MinimumTemperatureC { get; set; }
    public double MaximumTemperatureC { get; set; }
    public double ReferenceTemperatureC { get; set; }
    public double LambdaTRefNm { get; set; }
    public double SensitivityPmPerC { get; set; }
    public double CoefficientA { get; set; }
    public double CoefficientB { get; set; }
    public double CoefficientC { get; set; }
    public double? CoefficientD { get; set; }
    public double MaxErrorC { get; set; }
    public double ErrorToleranceC { get; set; }
    public double RSquared { get; set; }
    public string Result { get; set; } = "N/A";

    public string Identity => $"{SerialNumber}|{Channel}|{PeakId}";
}

/// <summary>Temperature-coefficient calculation compatible with Auto_calibrator_Pali/SensTemp/TempCalculations.py.</summary>
public static class TemperatureCalibrationAnalyzer
{
    public const double DefaultReferenceTemperatureC = 22.5;

    public static List<TemperatureCalibrationResult> Analyze(CalibrationRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var samples = run.Plateaus
            .Where(p => p.ReferenceTemperatureC is { } t && double.IsFinite(t))
            .SelectMany(p => p.Targets
                .Where(IsAccepted)
                .Where(target => double.IsFinite(target.MeanWavelengthNm))
                .Select(target => new AnalysisPoint(p.ReferenceTemperatureC!.Value, target)))
            .GroupBy(point => $"{point.Target.PeakLoggerDeviceSerialNumber}|{point.Target.SerialNumber}|{point.Target.Channel}|{point.Target.PeakId}|{point.Target.PeakIndex}");

        var results = new List<TemperatureCalibrationResult>();
        foreach (IGrouping<string, AnalysisPoint> group in samples)
        {
            AnalysisPoint[] points = group.OrderBy(point => point.TemperatureC).ToArray();
            if (points.Length < 3 || points.Select(point => point.TemperatureC).Distinct().Count() < 3) continue;

            try
            {
            double[] temperature = points.Select(point => point.TemperatureC).ToArray();
            double[] wavelength = points.Select(point => point.Target.MeanWavelengthNm).ToArray();
            double min = temperature.Min(), max = temperature.Max();
            double referenceTemperature = DefaultReferenceTemperatureC;
            if (min >= 0 && (min > referenceTemperature || referenceTemperature - min > 12.5))
                referenceTemperature = (min + max) / 2d;

            int order = max > 100 ? 3 : 2;
            double[] wavelengthFromTemperature = FitPolynomial(temperature, wavelength, order);
            double lambdaTRef = Evaluate(wavelengthFromTemperature, referenceTemperature);
            if (!double.IsFinite(lambdaTRef) || Math.Abs(lambdaTRef) < 1e-12) continue;

            double[] normalizedWavelength = wavelength.Select(value => (value - lambdaTRef) / lambdaTRef).ToArray();
            double[] coefficientsAscending = FitPolynomial(normalizedWavelength, temperature, order);
            double[] predicted = normalizedWavelength.Select(value => Evaluate(coefficientsAscending, value)).ToArray();
            double maxError = predicted.Zip(temperature, (estimate, actual) => Math.Abs(actual - estimate)).Max();
            double average = temperature.Average();
            double totalSquares = temperature.Sum(value => Square(value - average));
            double residualSquares = predicted.Zip(temperature, (estimate, actual) => Square(actual - estimate)).Sum();
            double rSquared = totalSquares <= 1e-20 ? 1 : 1 - residualSquares / totalSquares;
            double sensitivity = LinearSlope(temperature, wavelength) * 1000d;
            double tolerance = (max - min) * 0.01d;
            CalibrationMeasurementResult first = points[0].Target;

            results.Add(new TemperatureCalibrationResult
            {
                SerialNumber = first.SerialNumber,
                PeakLoggerDeviceSerialNumber = first.PeakLoggerDeviceSerialNumber,
                Channel = first.Channel,
                PeakId = first.PeakId,
                PeakIndex = first.PeakIndex,
                PointCount = points.Length,
                MinimumTemperatureC = min,
                MaximumTemperatureC = max,
                ReferenceTemperatureC = referenceTemperature,
                LambdaTRefNm = lambdaTRef,
                SensitivityPmPerC = sensitivity,
                CoefficientA = coefficientsAscending[order],
                CoefficientB = coefficientsAscending[order - 1],
                CoefficientC = coefficientsAscending[order - 2],
                CoefficientD = order == 3 ? coefficientsAscending[0] : null,
                MaxErrorC = maxError,
                ErrorToleranceC = tolerance,
                RSquared = rSquared,
                Result = maxError <= tolerance ? "PASS" : "FAIL",
            });
            }
            catch (InvalidOperationException)
            {
                // Degenerate or numerically singular points must not invalidate the run.
                // They simply do not produce a misleading coefficient row.
            }
        }
        return results.OrderBy(result => result.SerialNumber).ThenBy(result => result.Channel).ThenBy(result => result.PeakIndex).ToList();
    }

    public static void Export(CalibrationRunRecord run, string directory)
    {
        Directory.CreateDirectory(directory);
        run.CalibrationResults = Analyze(run);
        ExportCsv(run.CalibrationResults, Path.Combine(directory, "calibration-coefficients.csv"));
        ExportWorkbook(run, Path.Combine(directory, "calibration-coefficients.xlsx"));
    }

    public static void ExportCsv(IEnumerable<TemperatureCalibrationResult> results, string path)
    {
        var text = new StringBuilder("SerialNumber;PeakLoggerDeviceSN;Channel;PeakId;PeakIndex;Points;MinTemperatureC;MaxTemperatureC;TRefC;LambdaTRefNm;SensitivityPmPerC;CoefA;CoefB;CoefC;CoefD;MaxErrorC;ErrorToleranceC;R2;Result\r\n");
        foreach (TemperatureCalibrationResult item in results)
            text.AppendJoin(';', E(item.SerialNumber), E(item.PeakLoggerDeviceSerialNumber), E(item.Channel), E(item.PeakId), item.PeakIndex,
                item.PointCount, F(item.MinimumTemperatureC), F(item.MaximumTemperatureC), F(item.ReferenceTemperatureC), F(item.LambdaTRefNm),
                F(item.SensitivityPmPerC), F(item.CoefficientA), F(item.CoefficientB), F(item.CoefficientC),
                item.CoefficientD is { } d ? F(d) : string.Empty, F(item.MaxErrorC), F(item.ErrorToleranceC), F(item.RSquared), item.Result).Append("\r\n");
        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
    }

    private static void ExportWorkbook(CalibrationRunRecord run, string path)
    {
        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.Worksheets.Add("Koeficienty");
        sheet.Cell("A1").Value = "FBG TEPLOTNÁ KALIBRÁCIA – KOEFICIENTY";
        sheet.Range("A1:S1").Merge().Style.Fill.SetBackgroundColor(XLColor.FromHtml("#182A40"));
        sheet.Cell("A1").Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(XLColor.White);
        sheet.Cell("A2").Value = $"{run.DisplayRunId} · {run.DisplayProfileId} · {run.ChamberName}";
        string[] headers = ["SN", "PeakLogger SN", "Kanál", "Peak", "Index", "Body", "Min [°C]", "Max [°C]", "Tref [°C]", "λTref [nm]", "Citlivosť [pm/°C]", "A", "B", "C", "D", "Max. chyba [°C]", "Limit [°C]", "R²", "Výsledok"];
        for (int i = 0; i < headers.Length; i++) sheet.Cell(4, i + 1).Value = headers[i];
        sheet.Range(4, 1, 4, headers.Length).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#2C4770")).Font.SetBold().Font.SetFontColor(XLColor.White);
        int row = 5;
        foreach (TemperatureCalibrationResult item in run.CalibrationResults)
        {
            object?[] values = [item.SerialNumber, item.PeakLoggerDeviceSerialNumber, item.Channel, item.PeakId, item.PeakIndex, item.PointCount,
                item.MinimumTemperatureC, item.MaximumTemperatureC, item.ReferenceTemperatureC, item.LambdaTRefNm, item.SensitivityPmPerC,
                item.CoefficientA, item.CoefficientB, item.CoefficientC, item.CoefficientD, item.MaxErrorC, item.ErrorToleranceC, item.RSquared, item.Result];
            for (int col = 0; col < values.Length; col++)
                if (values[col] is not null) sheet.Cell(row, col + 1).Value = XLCellValue.FromObject(values[col]);
            sheet.Cell(row, 19).Style.Font.SetBold().Font.SetFontColor(item.Result == "PASS" ? XLColor.FromHtml("#087F5B") : XLColor.FromHtml("#C92A2A"));
            row++;
        }
        sheet.SheetView.FreezeRows(4);
        sheet.RangeUsed()?.SetAutoFilter();
        sheet.Columns().AdjustToContents(8, 26);
        workbook.SaveAs(path);
    }

    private static bool IsAccepted(CalibrationMeasurementResult target) =>
        target.Status is CalibrationTargetState.Stable or CalibrationTargetState.Overridden && target.SampleCount > 0;

    private static double[] FitPolynomial(double[] x, double[] y, int order)
    {
        double center = (x.Min() + x.Max()) / 2d;
        double scale = x.Max(value => Math.Abs(value - center));
        if (scale < 1e-20) throw new InvalidOperationException("Kalibračné teploty nemajú použiteľný rozsah.");
        double[] normalized = x.Select(value => (value - center) / scale).ToArray();
        int size = order + 1;
        var matrix = new double[size, size];
        var vector = new double[size];
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++) matrix[row, col] = normalized.Sum(value => Math.Pow(value, row + col));
            vector[row] = normalized.Select((value, index) => y[index] * Math.Pow(value, row)).Sum();
        }
        double[] scaledCoefficients = Solve(matrix, vector);
        var coefficients = new double[size];
        for (int power = 0; power <= order; power++)
            for (int targetPower = 0; targetPower <= power; targetPower++)
                coefficients[targetPower] += scaledCoefficients[power] * Binomial(power, targetPower) *
                    Math.Pow(-center, power - targetPower) / Math.Pow(scale, power);
        return coefficients;
    }

    private static double[] Solve(double[,] matrix, double[] vector)
    {
        int size = vector.Length;
        for (int pivot = 0; pivot < size; pivot++)
        {
            int best = Enumerable.Range(pivot, size - pivot).MaxBy(row => Math.Abs(matrix[row, pivot]));
            if (Math.Abs(matrix[best, pivot]) < 1e-20) throw new InvalidOperationException("Kalibračné body nevytvárajú riešiteľnú regresiu.");
            if (best != pivot)
            {
                for (int col = pivot; col < size; col++) (matrix[pivot, col], matrix[best, col]) = (matrix[best, col], matrix[pivot, col]);
                (vector[pivot], vector[best]) = (vector[best], vector[pivot]);
            }
            double divisor = matrix[pivot, pivot];
            for (int col = pivot; col < size; col++) matrix[pivot, col] /= divisor;
            vector[pivot] /= divisor;
            for (int row = 0; row < size; row++)
            {
                if (row == pivot) continue;
                double factor = matrix[row, pivot];
                for (int col = pivot; col < size; col++) matrix[row, col] -= factor * matrix[pivot, col];
                vector[row] -= factor * vector[pivot];
            }
        }
        return vector;
    }

    private static double Evaluate(IReadOnlyList<double> coefficients, double x) => coefficients.Select((coefficient, power) => coefficient * Math.Pow(x, power)).Sum();
    private static int Binomial(int n, int k) => (n, k) switch
    {
        (_, 0) => 1,
        (1, 1) => 1,
        (2, 1) => 2,
        (2, 2) => 1,
        (3, 1) => 3,
        (3, 2) => 3,
        (3, 3) => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };
    private static double LinearSlope(double[] x, double[] y)
    {
        double xMean = x.Average(), yMean = y.Average();
        return x.Select((value, index) => (value - xMean) * (y[index] - yMean)).Sum() / x.Sum(value => Square(value - xMean));
    }
    private static double Square(double value) => value * value;
    private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static string E(string value) => value.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
    private sealed record AnalysisPoint(double TemperatureC, CalibrationMeasurementResult Target);
}
