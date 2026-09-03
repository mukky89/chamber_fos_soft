namespace VotschVc3.Core.Calibration;

/// <summary>
/// Final temperature-calibration mathematics ported from the proven Auto_calibrator_Pali workflow.
/// It consumes completed plateau means, calculates product-specific coefficients and returns explicit
/// PASS/FAIL diagnostics. No external numerical package is required; the small least-squares systems
/// are solved locally with pivoted Gaussian elimination.
/// </summary>
public sealed class TemperatureCalibrationCalculator
{
    public IReadOnlyList<TemperatureCalibrationResult> CalculateRun(
        CalibrationRunRecord run,
        CalibrationSetup setup,
        IReadOnlyList<TemperatureCalibrationRecipe>? recipes = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(setup);
        recipes ??= TemperatureCalibrationRecipeCatalog.Defaults;

        var results = new List<TemperatureCalibrationResult>();

        // Recipe peak masks describe the physical peak order of the sensor, not merely the subset
        // selected by the operator. Therefore index all mappings first and only then skip unselected
        // rows. This keeps e.g. SC-01/T peaks:[true,false] deterministic.
        foreach (IGrouping<string, CalibrationSensorMapping> sensorGroup in setup.Mappings
                     .Where(m => !string.IsNullOrWhiteSpace(m.SerialNumber))
                     .GroupBy(m => m.SerialNumber, StringComparer.OrdinalIgnoreCase))
        {
            List<CalibrationSensorMapping> ordered = sensorGroup
                .OrderBy(m => m.PeakIndex)
                .ThenBy(m => m.PeakId, StringComparer.Ordinal)
                .ToList();

            for (int physicalPeakOrder = 0; physicalPeakOrder < ordered.Count; physicalPeakOrder++)
            {
                CalibrationSensorMapping mapping = ordered[physicalPeakOrder];
                if (!mapping.Selected) continue;

                TemperatureCalibrationRecipe recipe =
                    TemperatureCalibrationRecipeCatalog.Resolve(mapping, recipes)
                    ?? TemperatureCalibrationRecipeCatalog.GenericTemp();

                if (!recipe.AppliesToPeakIndex(physicalPeakOrder)) continue;

                List<CalibrationPointData> points = BuildPoints(run, mapping);
                TemperatureCalibrationResult result = recipe.CalculationType switch
                {
                    TemperatureCalibrationCalculationType.FBGS => CalculateFbgs(mapping, recipe, points),
                    TemperatureCalibrationCalculationType.D0X => CalculateD0x(mapping, recipe, points),
                    _ => CalculateTemp(mapping, recipe, points),
                };
                results.Add(result);
            }
        }

        return results;
    }

    public TemperatureCalibrationResult CalculateTemp(
        CalibrationSensorMapping mapping,
        TemperatureCalibrationRecipe recipe,
        IReadOnlyList<CalibrationPointData> sourcePoints)
    {
        List<CalibrationPointData> points = NormalizePoints(sourcePoints);
        TemperatureCalibrationResult result = CreateBaseResult(mapping, recipe);
        if (points.Count < 3)
            return Fail(result, $"TEMP výpočet potrebuje aspoň 3 platné kalibračné body; dostupné: {points.Count}.");

        double[] temp = points.Select(p => p.ReferenceTemperatureC).ToArray();
        double[] wl = points.Select(p => p.MeanWavelengthNm).ToArray();
        double tempConst = ResolveTemperatureConstant(recipe.TemperatureConstantC, temp);
        int degree = temp.Max() > 100d ? 3 : 2;

        // Pali temp_calc first approximates wavelength as a polynomial of temperature in order to
        // evaluate the wavelength at TempConst. It then fits temperature against normalized Δλ.
        double[] wavelengthVsTemp = FitPolynomial(temp, wl, degree);
        double tRef = EvaluatePolynomial(wavelengthVsTemp, tempConst);
        if (!double.IsFinite(tRef) || Math.Abs(tRef) < 1e-12)
            return Fail(result, "TEMP výpočet nevytvoril platnú referenčnú wavelength TRef.");

        double[] normalized = wl.Select(value => (value - tRef) / tRef).ToArray();
        double[] temperatureCoefficients = FitPolynomial(normalized, temp, degree);
        double[] predicted = normalized.Select(value => EvaluatePolynomial(temperatureCoefficients, value)).ToArray();

        result.A = degree == 3 ? temperatureCoefficients[3] : temperatureCoefficients[2];
        result.B = degree == 3 ? temperatureCoefficients[2] : temperatureCoefficients[1];
        result.C = degree == 3 ? temperatureCoefficients[1] : temperatureCoefficients[0];
        result.D = degree == 3 ? temperatureCoefficients[0] : null;
        result.SensitivityPmPerC = LinearSlope(temp, wl) * 1000d;
        result.TRefNm = tRef;
        result.TemperatureConstantC = tempConst;
        result.MaxErrorC = temp.Zip(predicted, (actual, calc) => Math.Abs(actual - calc)).Max();
        result.ErrorToleranceC = (temp.Max() - temp.Min()) * recipe.ErrorTolerancePercentOfRange / 100d;
        result.R2 = R2(temp, predicted);
        ApplyValidation(result, recipe);
        result.Points = BuildPointResults(points, predicted);
        result.StatusMessage = BuildStatus(result, recipe);
        return result;
    }

    public TemperatureCalibrationResult CalculateFbgs(
        CalibrationSensorMapping mapping,
        TemperatureCalibrationRecipe recipe,
        IReadOnlyList<CalibrationPointData> sourcePoints)
    {
        List<CalibrationPointData> points = NormalizePoints(sourcePoints);
        TemperatureCalibrationResult result = CreateBaseResult(mapping, recipe);
        if (points.Count < 3)
            return Fail(result, $"FBGS výpočet potrebuje aspoň 3 platné kalibračné body; dostupné: {points.Count}.");

        double[] temp = points.Select(p => p.ReferenceTemperatureC).ToArray();
        double[] wl = points.Select(p => p.MeanWavelengthNm).ToArray();
        double tempConst = ResolveTemperatureConstant(recipe.TemperatureConstantC, temp, adjustLikeLegacyTemp: false);

        // Legacy Pali FBGS_calc:
        //   T = A*lambda^2 + B*lambda + C
        // and then solve T(lambda)=TempConst. The exact Python branch simplifies algebraically to
        // (-B + sqrt(B^2 - 4*A*(C-TempConst))) / (2*A), also when A is negative.
        double[] tFromWl = FitPolynomial(wl, temp, 2);
        double qa = tFromWl[2];
        double qb = tFromWl[1];
        double qc = tFromWl[0] - tempConst;
        double tRef = SolveLegacyReferenceRoot(qa, qb, qc);
        if (!double.IsFinite(tRef) || tRef <= 0)
            return Fail(result, "FBGS výpočet nevytvoril platnú referenčnú wavelength TRef.");

        double[] deltaT = temp.Select(t => t - tempConst).ToArray();
        double[] deltaT2 = deltaT.Select(t => t * t).ToArray();
        double[] logWl = wl.Select(value => Math.Log(value / tRef)).ToArray();
        double[] regression = FitTwoPredictors(deltaT, deltaT2, logWl); // c0 + s1*dT + s2*dT^2
        double intercept = regression[0];
        double s1 = regression[1];
        double s2 = regression[2];

        // In ideal legacy data the fitted intercept is zero. Real least-squares data can leave a
        // tiny offset. Folding it into TRef preserves Pali's two reported coefficients while making
        // the inverse model numerically self-consistent: log(lambda/TRef') = s1*dT+s2*dT^2.
        double effectiveTRef = tRef * Math.Exp(intercept);
        double[] predicted = wl.Select(value => InvertFbgs(value, effectiveTRef, tempConst, s1, s2)).ToArray();
        if (predicted.Any(v => !double.IsFinite(v)))
            return Fail(result, "FBGS inverzný model vytvoril neplatnú hodnotu teploty.");

        result.S1 = s1;
        result.S2 = s2;
        result.SensitivityPmPerC = LinearSlope(temp, wl) * 1000d;
        result.TRefNm = effectiveTRef;
        result.TemperatureConstantC = tempConst;
        result.MaxErrorC = temp.Zip(predicted, (actual, calc) => Math.Abs(calc - actual)).Max();
        result.ErrorToleranceC = (temp.Max() - temp.Min()) * recipe.ErrorTolerancePercentOfRange / 100d;
        result.R2 = R2(temp, predicted);
        ApplyValidation(result, recipe);
        result.Points = BuildPointResults(points, predicted);
        result.StatusMessage = BuildStatus(result, recipe);
        return result;
    }

    public TemperatureCalibrationResult CalculateD0x(
        CalibrationSensorMapping mapping,
        TemperatureCalibrationRecipe recipe,
        IReadOnlyList<CalibrationPointData> sourcePoints)
    {
        TemperatureCalibrationResult result = CreateBaseResult(mapping, recipe);
        result.A = 0;
        result.B = 0;
        result.C = 0;
        result.D = 0;
        result.SensitivityPmPerC = 0;
        result.TRefNm = sourcePoints.FirstOrDefault()?.MeanWavelengthNm ?? 0;
        result.MaxErrorC = 0;
        result.ErrorToleranceC = 0;
        result.TemperatureConstantC = recipe.TemperatureConstantC;
        result.R2 = 1;
        result.SensitivityPassed = true;
        result.ErrorPassed = true;
        result.R2Passed = true;
        result.OverallPassed = true;
        result.StatusMessage = "D0X recipe nevyžaduje numerický teplotný fit.";
        result.Points = sourcePoints.Select(point => new TemperatureCalibrationPointResult
        {
            PlateauIndex = point.PlateauIndex,
            TargetTemperatureC = point.TargetTemperatureC,
            ReferenceTemperatureC = point.ReferenceTemperatureC,
            ChamberTemperatureC = point.ChamberTemperatureC,
            MeanWavelengthNm = point.MeanWavelengthNm,
            PredictedTemperatureC = point.ReferenceTemperatureC,
            ErrorC = 0,
        }).ToList();
        return result;
    }

    private static List<TemperatureCalibrationPointResult> BuildPointResults(
        IReadOnlyList<CalibrationPointData> points,
        IReadOnlyList<double> predicted) => points.Select((point, index) => new TemperatureCalibrationPointResult
    {
        PlateauIndex = point.PlateauIndex,
        TargetTemperatureC = point.TargetTemperatureC,
        ReferenceTemperatureC = point.ReferenceTemperatureC,
        ChamberTemperatureC = point.ChamberTemperatureC,
        MeanWavelengthNm = point.MeanWavelengthNm,
        PredictedTemperatureC = predicted[index],
        ErrorC = predicted[index] - point.ReferenceTemperatureC,
    }).ToList();

    private static List<CalibrationPointData> BuildPoints(CalibrationRunRecord run, CalibrationSensorMapping mapping)
    {
        var points = new List<CalibrationPointData>();
        foreach (CalibrationPlateauResult plateau in run.Plateaus.OrderBy(p => p.PlateauIndex))
        {
            CalibrationMeasurementResult? measurement = plateau.Targets.FirstOrDefault(t =>
                string.Equals(t.SerialNumber, mapping.SerialNumber, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.Channel, mapping.Channel, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.PeakId, mapping.PeakId, StringComparison.Ordinal));
            if (measurement is null || measurement.Status is not (CalibrationTargetState.Stable or CalibrationTargetState.Overridden))
                continue;

            double reference = measurement.MeanReferenceTemperatureC
                ?? plateau.ReferenceTemperatureC
                ?? measurement.MeanChamberTemperatureC
                ?? plateau.ActualTemperatureC;
            double chamber = measurement.MeanChamberTemperatureC ?? plateau.ActualTemperatureC;
            if (!double.IsFinite(reference) || !double.IsFinite(measurement.MeanWavelengthNm)) continue;

            points.Add(new CalibrationPointData(
                plateau.PlateauIndex,
                plateau.TargetTemperatureC,
                reference,
                chamber,
                measurement.MeanWavelengthNm));
        }
        return points;
    }

    private static List<CalibrationPointData> NormalizePoints(IReadOnlyList<CalibrationPointData> points) => points
        .Where(p => double.IsFinite(p.ReferenceTemperatureC) && double.IsFinite(p.MeanWavelengthNm))
        .OrderBy(p => p.ReferenceTemperatureC)
        .ToList();

    private static TemperatureCalibrationResult CreateBaseResult(
        CalibrationSensorMapping mapping,
        TemperatureCalibrationRecipe recipe) => new()
    {
        SerialNumber = mapping.SerialNumber,
        PeakId = mapping.PeakId,
        PeakIndex = mapping.PeakIndex,
        Channel = mapping.Channel,
        ProductDescription = mapping.ProductDescription,
        RecipeKey = recipe.EffectiveKey,
        CalculationType = recipe.CalculationType,
        TemperatureConstantC = recipe.TemperatureConstantC,
    };

    private static TemperatureCalibrationResult Fail(TemperatureCalibrationResult result, string message)
    {
        result.SensitivityPassed = false;
        result.ErrorPassed = false;
        result.R2Passed = false;
        result.OverallPassed = false;
        result.StatusMessage = message;
        return result;
    }

    private static void ApplyValidation(TemperatureCalibrationResult result, TemperatureCalibrationRecipe recipe)
    {
        result.SensitivityPassed =
            (!recipe.SensitivityMinPmPerC.HasValue || result.SensitivityPmPerC >= recipe.SensitivityMinPmPerC.Value) &&
            (!recipe.SensitivityMaxPmPerC.HasValue || result.SensitivityPmPerC <= recipe.SensitivityMaxPmPerC.Value);
        result.ErrorPassed = !recipe.CheckErrorTolerance || result.MaxErrorC <= result.ErrorToleranceC;
        result.R2Passed = !recipe.MinimumR2.HasValue || result.R2 >= recipe.MinimumR2.Value;
        result.OverallPassed = result.SensitivityPassed && result.ErrorPassed && result.R2Passed;
    }

    private static string BuildStatus(TemperatureCalibrationResult result, TemperatureCalibrationRecipe recipe)
    {
        string sensitivityLimit = recipe.SensitivityMinPmPerC.HasValue || recipe.SensitivityMaxPmPerC.HasValue
            ? $"sens {result.SensitivityPmPerC:F3} pm/°C [{recipe.SensitivityMinPmPerC?.ToString("F3") ?? "-∞"}; {recipe.SensitivityMaxPmPerC?.ToString("F3") ?? "+∞"}] {(result.SensitivityPassed ? "PASS" : "FAIL")}"
            : $"sens {result.SensitivityPmPerC:F3} pm/°C";
        string error = recipe.CheckErrorTolerance
            ? $"max error {result.MaxErrorC:F4}/{result.ErrorToleranceC:F4} °C {(result.ErrorPassed ? "PASS" : "FAIL")}"
            : $"max error {result.MaxErrorC:F4} °C";
        string r2 = recipe.MinimumR2.HasValue
            ? $"R² {result.R2:F6}/{recipe.MinimumR2:F6} {(result.R2Passed ? "PASS" : "FAIL")}"
            : $"R² {result.R2:F6}";
        return $"{(result.OverallPassed ? "PASS" : "FAIL")} · {sensitivityLimit} · {error} · {r2}";
    }

    private static double ResolveTemperatureConstant(double configured, IReadOnlyList<double> temp, bool adjustLikeLegacyTemp = true)
    {
        if (!adjustLikeLegacyTemp || temp.Count == 0) return configured;
        double min = temp.Min();
        double max = temp.Max();
        if (min >= 0 && (min > configured || configured - min > 12.5))
            return (min + max) / 2d;
        return configured;
    }

    private static double SolveLegacyReferenceRoot(double a, double b, double c)
    {
        if (Math.Abs(a) < 1e-18)
            return Math.Abs(b) < 1e-18 ? double.NaN : -c / b;

        double discriminant = b * b - 4d * a * c;
        double scale = Math.Max(1d, b * b + Math.Abs(4d * a * c));
        if (discriminant < 0 && discriminant > -1e-12 * scale) discriminant = 0;
        if (discriminant < 0) return double.NaN;

        double root = Math.Sqrt(discriminant);
        // Exact algebraic equivalent of Pali:
        // -B/(2*A) - sqrt(B^2/(4*A^2)-C/A+TempConst/A) when A<0,
        // +sqrt(...) when A>=0. Both reduce to the '+' numerator root below.
        return (-b + root) / (2d * a);
    }

    private static double InvertFbgs(double wavelengthNm, double tRefNm, double tempConst, double s1, double s2)
    {
        if (!double.IsFinite(wavelengthNm) || !double.IsFinite(tRefNm) || wavelengthNm <= 0 || tRefNm <= 0)
            return double.NaN;

        double log = Math.Log(wavelengthNm / tRefNm);
        if (Math.Abs(s2) < 1e-18)
            return Math.Abs(s1) < 1e-18 ? double.NaN : tempConst + log / s1;

        double half = s1 / (2d * s2);
        double discriminant = half * half + log / s2;
        double scale = Math.Max(1d, half * half + Math.Abs(log / s2));
        if (discriminant < 0 && discriminant > -1e-12 * scale) discriminant = 0;
        if (discriminant < 0) return double.NaN;

        double root = Math.Sqrt(discriminant);
        double delta = s2 > 0 ? -half + root : -half - root;
        return tempConst + delta;
    }

    private static double[] FitPolynomial(IReadOnlyList<double> x, IReadOnlyList<double> y, int degree)
    {
        if (x.Count != y.Count || x.Count < degree + 1)
            throw new InvalidOperationException("Nedostatok bodov pre polynomial least-squares fit.");

        int n = degree + 1;
        var matrix = new double[n, n];
        var rhs = new double[n];
        for (int row = 0; row < n; row++)
        {
            for (int col = 0; col < n; col++)
                matrix[row, col] = x.Sum(v => Math.Pow(v, row + col));
            rhs[row] = x.Select((v, i) => Math.Pow(v, row) * y[i]).Sum();
        }
        return Solve(matrix, rhs);
    }

    private static double[] FitTwoPredictors(IReadOnlyList<double> x1, IReadOnlyList<double> x2, IReadOnlyList<double> y)
    {
        if (x1.Count != x2.Count || x1.Count != y.Count || x1.Count < 3)
            throw new InvalidOperationException("Nedostatok bodov pre FBGS least-squares fit.");

        var matrix = new double[3, 3];
        var rhs = new double[3];
        for (int i = 0; i < y.Count; i++)
        {
            double[] row = { 1d, x1[i], x2[i] };
            for (int r = 0; r < 3; r++)
            {
                rhs[r] += row[r] * y[i];
                for (int c = 0; c < 3; c++) matrix[r, c] += row[r] * row[c];
            }
        }
        return Solve(matrix, rhs);
    }

    private static double[] Solve(double[,] matrix, double[] rhs)
    {
        int n = rhs.Length;
        var a = (double[,])matrix.Clone();
        var b = (double[])rhs.Clone();

        for (int pivot = 0; pivot < n; pivot++)
        {
            int best = pivot;
            for (int row = pivot + 1; row < n; row++)
                if (Math.Abs(a[row, pivot]) > Math.Abs(a[best, pivot])) best = row;

            if (Math.Abs(a[best, pivot]) < 1e-20)
                throw new InvalidOperationException("Kalibračný least-squares systém je singulárny.");

            if (best != pivot)
            {
                for (int col = pivot; col < n; col++)
                    (a[pivot, col], a[best, col]) = (a[best, col], a[pivot, col]);
                (b[pivot], b[best]) = (b[best], b[pivot]);
            }

            double divisor = a[pivot, pivot];
            for (int col = pivot; col < n; col++) a[pivot, col] /= divisor;
            b[pivot] /= divisor;

            for (int row = 0; row < n; row++)
            {
                if (row == pivot) continue;
                double factor = a[row, pivot];
                if (Math.Abs(factor) < 1e-30) continue;
                for (int col = pivot; col < n; col++) a[row, col] -= factor * a[pivot, col];
                b[row] -= factor * b[pivot];
            }
        }

        return b;
    }

    private static double EvaluatePolynomial(IReadOnlyList<double> coefficients, double x)
    {
        double value = 0;
        for (int i = coefficients.Count - 1; i >= 0; i--) value = value * x + coefficients[i];
        return value;
    }

    private static double LinearSlope(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        double xMean = x.Average();
        double yMean = y.Average();
        double numerator = 0;
        double denominator = 0;
        for (int i = 0; i < x.Count; i++)
        {
            double dx = x[i] - xMean;
            numerator += dx * (y[i] - yMean);
            denominator += dx * dx;
        }
        return denominator <= double.Epsilon ? 0 : numerator / denominator;
    }

    private static double R2(IReadOnlyList<double> actual, IReadOnlyList<double> predicted)
    {
        double mean = actual.Average();
        double ssTot = actual.Sum(v => Math.Pow(v - mean, 2));
        double ssRes = actual.Select((v, i) => Math.Pow(v - predicted[i], 2)).Sum();
        return ssTot <= double.Epsilon ? (ssRes <= double.Epsilon ? 1d : 0d) : 1d - ssRes / ssTot;
    }
}

public sealed record CalibrationPointData(
    int PlateauIndex,
    double TargetTemperatureC,
    double ReferenceTemperatureC,
    double ChamberTemperatureC,
    double MeanWavelengthNm);
