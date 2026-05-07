using Ivy.Tendril.Services.SessionParsers;
using System.Reflection;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace Ivy.Tendril.Services;

public record ModelPricing
{
    public double Input { get; init; }
    public double Output { get; init; }
    public double CacheWrite { get; init; }
    public double CacheRead { get; init; }
}

public record CostCalculation
{
    public int TotalTokens { get; init; }
    public double TotalCost { get; init; }
}

public class ModelPricingService : IModelPricingService
{
    private static readonly IDeserializer DefaultDeserializer = new DeserializerBuilder().Build();
    private readonly ILogger<ModelPricingService> _logger;
    private readonly Dictionary<string, ISessionParser> _parsers;

    public ModelPricingService(ILogger<ModelPricingService> logger, IEnumerable<ISessionParser> parsers)
    {
        _logger = logger;
        Pricing = LoadEmbeddedPricing();
        _parsers = parsers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    internal Dictionary<string, ModelPricing> Pricing { get; }

    public ModelPricing GetPricing(string modelName)
    {
        foreach (var key in Pricing.Keys.OrderByDescending(k => k.Length))
            if (modelName.Contains(key, StringComparison.OrdinalIgnoreCase))
                return Pricing[key];

        // Fallback to Opus 4.6 (current default model)
        _logger.LogWarning(
            "Model '{ModelName}' not found in pricing database. Falling back to Claude Opus 4.6 pricing ($5.00/$25.00). " +
            "Check config.yaml for typos or add the model to Assets/models.yaml.",
            modelName);

        return Pricing.TryGetValue("claude-opus-4-6", out var fallback)
            ? fallback
            : new ModelPricing { Input = 5.0, Output = 25.0, CacheWrite = 6.25, CacheRead = 0.50 };
    }

    public CostCalculation CalculateSessionCost(string sessionId)
    {
        return CalculateSessionCost(sessionId, "claude");
    }

    public CostCalculation CalculateSessionCost(string sessionId, string provider)
    {
        return provider.ToLower() switch
        {
            "claude" => CalculateClaudeCost(sessionId),
            "codex" => CalculateCodexCost(sessionId),
            "gemini" => CalculateGeminiCost(sessionId),
            _ => CalculateClaudeCost(sessionId)
        };
    }

    private static Dictionary<string, ModelPricing> LoadEmbeddedPricing()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Ivy.Tendril.Assets.models.yaml";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) throw new InvalidOperationException($"Embedded resource '{resourceName}' not found");

        using var reader = new StreamReader(stream);
        var yaml = reader.ReadToEnd();

        var config = DefaultDeserializer.Deserialize<Dictionary<string, object>>(yaml);

        var result = new Dictionary<string, ModelPricing>();
        if (config.TryGetValue("models", out var modelsObj) && modelsObj is Dictionary<object, object> models)
            foreach (var kvp in models)
            {
                var modelName = kvp.Key.ToString() ?? "";
                if (kvp.Value is Dictionary<object, object> props)
                    result[modelName] = new ModelPricing
                    {
                        Input = Convert.ToDouble(props["input"], System.Globalization.CultureInfo.InvariantCulture),
                        Output = Convert.ToDouble(props["output"], System.Globalization.CultureInfo.InvariantCulture),
                        CacheWrite = Convert.ToDouble(props["cacheWrite"], System.Globalization.CultureInfo.InvariantCulture),
                        CacheRead = Convert.ToDouble(props["cacheRead"], System.Globalization.CultureInfo.InvariantCulture)
                    };
            }

        return result;
    }

    private CostCalculation CalculateClaudeCost(string sessionId)
    {
        var claudeProjectsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects"
        );

        var sessionFile = FindSessionFile(claudeProjectsDir, sessionId);
        if (sessionFile == null) return new CostCalculation();

        var parser = _parsers["claude"];
        var mainCost = parser.Parse(sessionFile, this);
        var totalCost = mainCost.TotalCost;
        var totalTokens = mainCost.TotalTokens;

        // Parse subagent files
        var subagentDir = Path.Combine(
            Path.GetDirectoryName(sessionFile)!,
            Path.GetFileNameWithoutExtension(sessionFile),
            "subagents"
        );

        if (Directory.Exists(subagentDir))
            foreach (var subFile in Directory.GetFiles(subagentDir, "*.jsonl"))
            {
                var subCost = parser.Parse(subFile, this);
                totalCost += subCost.TotalCost;
                totalTokens += subCost.TotalTokens;
            }

        return new CostCalculation { TotalTokens = totalTokens, TotalCost = totalCost };
    }

    private CostCalculation CalculateCodexCost(string sessionId)
    {
        var codexSessionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex", "sessions"
        );

        if (!Directory.Exists(codexSessionsDir)) return new CostCalculation();

        var sessionFile = Directory.GetFiles(codexSessionsDir, "*.jsonl", SearchOption.AllDirectories)
            .FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).EndsWith(sessionId, StringComparison.OrdinalIgnoreCase));

        if (sessionFile == null) return new CostCalculation();

        var parser = _parsers["codex"];
        return parser.Parse(sessionFile, this);
    }

    private CostCalculation CalculateGeminiCost(string sessionId)
    {
        var geminiDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gemini", "tmp"
        );

        if (!Directory.Exists(geminiDir)) return new CostCalculation();

        var sessionFile = Directory.GetFiles(geminiDir, "*.json", SearchOption.AllDirectories)
            .FirstOrDefault(f => Path.GetFileName(f).Contains(sessionId, StringComparison.OrdinalIgnoreCase));

        if (sessionFile == null) return new CostCalculation();

        var parser = _parsers["gemini"];
        return parser.Parse(sessionFile, this);
    }

    internal CostCalculation CalculateFromFile(string filePath)
    {
        var parser = _parsers["claude"];
        return parser.Parse(filePath, this);
    }

    private static string? FindSessionFile(string claudeProjectsDir, string sessionId)
    {
        if (!Directory.Exists(claudeProjectsDir)) return null;

        return Directory.GetFiles(claudeProjectsDir, $"{sessionId}.jsonl", SearchOption.AllDirectories)
            .FirstOrDefault(f => !f.Contains("\\subagents\\") && !f.Contains("/subagents/"));
    }

}
