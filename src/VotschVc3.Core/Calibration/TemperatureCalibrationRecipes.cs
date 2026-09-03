using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Calibration;

/// <summary>
/// Product calibration recipes migrated from Auto_calibrator_Pali/preset_temp_cal.yaml.
/// Production can override them in Documents\Lab Control\temperature-calibration-recipes.json
/// without recompiling the application. An environment variable can point to another file.
/// </summary>
public static class TemperatureCalibrationRecipeCatalog
{
    public const string OverrideEnvironmentVariable = "SYLEX_TEMPERATURE_CALIBRATION_RECIPES";
    public const string DefaultOverrideFileName = "temperature-calibration-recipes.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<TemperatureCalibrationRecipe> Defaults { get; } = new[]
    {
        new TemperatureCalibrationRecipe
        {
            Key = "ES-02",
            ProductCode = "ES-02",
            CalculationType = TemperatureCalibrationCalculationType.TEMP,
            Peaks = new List<bool> { true, true },
            SensitivityMinPmPerC = 17.2,
            SensitivityMaxPmPerC = 20.2,
        },
        new TemperatureCalibrationRecipe
        {
            Key = "FOS4STRAIN_1,75",
            ProductCode = "FOS4STRAIN_1,75",
            Aliases = new List<string> { "FOS4STRAIN 1,75", "FOS4STRAIN_1.75", "FOS4STRAIN 1.75" },
            CalculationType = TemperatureCalibrationCalculationType.FBGS,
            Peaks = new List<bool> { true, true, true },
            SensitivityMinPmPerC = 17.2,
            SensitivityMaxPmPerC = 20.2,
        },
        new TemperatureCalibrationRecipe
        {
            Key = "SC-01/T",
            ProductCode = "SC-01/T",
            CalculationType = TemperatureCalibrationCalculationType.TEMP,
            Peaks = new List<bool> { true, false },
        },
        new TemperatureCalibrationRecipe
        {
            Key = "SWS-02/T",
            ProductCode = "SWS-02/T",
            CalculationType = TemperatureCalibrationCalculationType.TEMP,
            Peaks = new List<bool> { true, true },
            SensitivityMinPmPerC = 20.0,
            SensitivityMaxPmPerC = 30.0,
            CheckErrorTolerance = true,
        },
        new TemperatureCalibrationRecipe
        {
            Key = "TP-03",
            ProductCode = "TP-03",
            CalculationType = TemperatureCalibrationCalculationType.TEMP,
            Peaks = new List<bool> { true },
            SensitivityMinPmPerC = 20.0,
            SensitivityMaxPmPerC = 25.0,
            CheckErrorTolerance = true,
        },
    };

    public static string ProductionOverridePath
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured.Trim());
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "Lab Control", DefaultOverrideFileName);
        }
    }

    /// <summary>
    /// Loads the production catalog and creates a human-editable template on first use. Failure to
    /// create/read the override never blocks calibration; built-in Pali recipes remain available.
    /// </summary>
    public static IReadOnlyList<TemperatureCalibrationRecipe> LoadProductionCatalog()
    {
        string path = ProductionOverridePath;
        try
        {
            SaveTemplate(path);
        }
        catch
        {
            // A read-only Documents folder must not block calibration.
        }
        return LoadWithDefaults(path);
    }

    public static IReadOnlyList<TemperatureCalibrationRecipe> LoadWithDefaults(string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            return Defaults.Select(Clone).ToArray();

        try
        {
            List<TemperatureCalibrationRecipe>? custom = JsonSerializer.Deserialize<List<TemperatureCalibrationRecipe>>(
                File.ReadAllText(jsonPath), JsonOptions);
            if (custom is null || custom.Count == 0) return Defaults.Select(Clone).ToArray();

            Dictionary<string, TemperatureCalibrationRecipe> merged = Defaults
                .Select(Clone)
                .ToDictionary(x => x.EffectiveKey, StringComparer.OrdinalIgnoreCase);
            foreach (TemperatureCalibrationRecipe recipe in custom.Where(r => !string.IsNullOrWhiteSpace(r.EffectiveKey)))
                merged[recipe.EffectiveKey] = recipe;
            return merged.Values.OrderBy(x => x.EffectiveKey, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Defaults.Select(Clone).ToArray();
        }
    }

    public static void SaveTemplate(string jsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        string? directory = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        if (!File.Exists(jsonPath))
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(Defaults, JsonOptions));
    }

    public static TemperatureCalibrationRecipe? Resolve(
        CalibrationSensorMapping mapping,
        IReadOnlyList<TemperatureCalibrationRecipe>? recipes = null)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        // TemperatureCalibrationCalculator passes the built-in catalog when the caller did not
        // provide an explicit catalog. Treat that as the production-default path so the editable
        // JSON override is actually used during normal application runs.
        if (recipes is null || ReferenceEquals(recipes, Defaults))
            recipes = LoadProductionCatalog();

        if (!string.IsNullOrWhiteSpace(mapping.CalibrationRecipeKey))
        {
            TemperatureCalibrationRecipe? explicitMatch = recipes.FirstOrDefault(r =>
                string.Equals(r.EffectiveKey, mapping.CalibrationRecipeKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.ProductCode, mapping.CalibrationRecipeKey, StringComparison.OrdinalIgnoreCase));
            if (explicitMatch is not null) return explicitMatch;
        }

        string haystack = mapping.ProductDescription ?? string.Empty;
        if (string.IsNullOrWhiteSpace(haystack)) return null;

        // Prefer the longest token to avoid a short generic product code winning over a more
        // specific alias.
        return recipes
            .Select(recipe => new
            {
                Recipe = recipe,
                Tokens = new[] { recipe.ProductCode, recipe.EffectiveKey }
                    .Concat(recipe.Aliases)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            })
            .SelectMany(x => x.Tokens.Select(token => (x.Recipe, Token: token)))
            .Where(x => haystack.Contains(x.Token, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Token.Length)
            .Select(x => x.Recipe)
            .FirstOrDefault();
    }

    public static TemperatureCalibrationRecipe GenericTemp(string key = "GENERIC-TEMP") => new()
    {
        Key = key,
        ProductCode = key,
        CalculationType = TemperatureCalibrationCalculationType.TEMP,
        CheckErrorTolerance = true,
    };

    private static TemperatureCalibrationRecipe Clone(TemperatureCalibrationRecipe source) => new()
    {
        Key = source.Key,
        ProductCode = source.ProductCode,
        Aliases = source.Aliases.ToList(),
        CalculationType = source.CalculationType,
        Peaks = source.Peaks.ToList(),
        SensitivityMinPmPerC = source.SensitivityMinPmPerC,
        SensitivityMaxPmPerC = source.SensitivityMaxPmPerC,
        CheckErrorTolerance = source.CheckErrorTolerance,
        ErrorTolerancePercentOfRange = source.ErrorTolerancePercentOfRange,
        TemperatureConstantC = source.TemperatureConstantC,
        MinimumR2 = source.MinimumR2,
    };
}
