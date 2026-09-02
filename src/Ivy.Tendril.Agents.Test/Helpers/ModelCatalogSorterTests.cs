using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Test.Helpers;

public class ModelCatalogSorterTests
{
    [Fact]
    public void Sort_ClaudeModels_SortsByTierAndDescendingVersion()
    {
        var models = new List<ModelInfo>
        {
            new() { Id = "claude-3-5-sonnet", DisplayName = "Claude Sonnet 3.5", Provider = "anthropic" },
            new() { Id = "claude-opus-4-5", DisplayName = "Claude Opus 4.5", Provider = "anthropic" },
            new() { Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5", Provider = "anthropic" },
            new() { Id = "claude-opus-5", DisplayName = "Claude Opus 5", Provider = "anthropic" },
            new() { Id = "claude-haiku-4-5", DisplayName = "Claude Haiku 4.5", Provider = "anthropic" },
            new() { Id = "claude-opus-4-8", DisplayName = "Claude Opus 4.8", Provider = "anthropic" },
            new() { Id = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6", Provider = "anthropic" },
            new() { Id = "claude-3.7-sonnet", DisplayName = "Claude Sonnet 3.7", Provider = "anthropic" },
            new() { Id = "claude-3-haiku", DisplayName = "Claude Haiku 3", Provider = "anthropic" },
        };

        var sorted = ModelCatalogSorter.Sort(models);

        var expectedIds = new[]
        {
            "claude-opus-5",
            "claude-opus-4-8",
            "claude-opus-4-5",
            "claude-sonnet-5",
            "claude-sonnet-4-6",
            "claude-3.7-sonnet",
            "claude-3-5-sonnet",
            "claude-haiku-4-5",
            "claude-3-haiku",
        };

        Assert.Equal(expectedIds, sorted.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void Sort_OpenAiModels_SortsFlagshipsGpt5Gpt4AndOSeries()
    {
        var models = new List<ModelInfo>
        {
            new() { Id = "gpt-4.1", DisplayName = "GPT-4.1", Provider = "openai" },
            new() { Id = "gpt-5.4", DisplayName = "GPT-5.4", Provider = "openai" },
            new() { Id = "o1", DisplayName = "O1", Provider = "openai" },
            new() { Id = "gpt-5.6-terra", DisplayName = "GPT-5.6-Terra", Provider = "openai" },
            new() { Id = "o4-mini", DisplayName = "O4 Mini", Provider = "openai" },
            new() { Id = "gpt-5.6-sol", DisplayName = "GPT-5.6-Sol", Provider = "openai" },
            new() { Id = "gpt-5.6-luna", DisplayName = "GPT-5.6-Luna", Provider = "openai" },
            new() { Id = "gpt-5.5", DisplayName = "GPT-5.5", Provider = "openai" },
            new() { Id = "o3", DisplayName = "O3", Provider = "openai" },
        };

        var sorted = ModelCatalogSorter.Sort(models);

        var expectedIds = new[]
        {
            "gpt-5.6-sol",
            "gpt-5.6-terra",
            "gpt-5.6-luna",
            "gpt-5.5",
            "gpt-5.4",
            "gpt-4.1",
            "o4-mini",
            "o3",
            "o1",
        };

        Assert.Equal(expectedIds, sorted.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void Sort_GeminiModels_SortsDescendingVersion()
    {
        var models = new List<ModelInfo>
        {
            new() { Id = "gemini-2.0-flash", DisplayName = "Gemini 2.0 Flash", Provider = "google" },
            new() { Id = "gemini-3.7-flash", DisplayName = "Gemini 3.7 Flash", Provider = "google" },
            new() { Id = "gemini-2.5-flash", DisplayName = "Gemini 2.5 Flash", Provider = "google" },
            new() { Id = "gemini-3.1-pro", DisplayName = "Gemini 3.1 Pro", Provider = "google" },
        };

        var sorted = ModelCatalogSorter.Sort(models);

        var expectedIds = new[]
        {
            "gemini-3.7-flash",
            "gemini-3.1-pro",
            "gemini-2.5-flash",
            "gemini-2.0-flash",
        };

        Assert.Equal(expectedIds, sorted.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void Sort_MixedVersionDelimiters_HandlesHyphensAndDotsCorrectly()
    {
        var models = new List<ModelInfo>
        {
            new() { Id = "claude-3-5-sonnet", DisplayName = "Claude Sonnet 3.5", Provider = "anthropic" },
            new() { Id = "claude-3.7-sonnet", DisplayName = "Claude Sonnet 3.7", Provider = "anthropic" },
            new() { Id = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6", Provider = "anthropic" },
            new() { Id = "claude-opus-4.7", DisplayName = "Claude Opus 4.7", Provider = "anthropic" },
            new() { Id = "claude-opus-4-8", DisplayName = "Claude Opus 4.8", Provider = "anthropic" },
        };

        var sorted = ModelCatalogSorter.Sort(models);

        var expectedIds = new[]
        {
            "claude-opus-4-8",
            "claude-opus-4.7",
            "claude-sonnet-4-6",
            "claude-3.7-sonnet",
            "claude-3-5-sonnet",
        };

        Assert.Equal(expectedIds, sorted.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void Sort_PreserveDefault_KeepsDefaultModelAtTop()
    {
        var models = new List<ModelInfo>
        {
            new() { Id = "gemini-2.5-flash", DisplayName = "Gemini 2.5 Flash", Provider = "google" },
            new() { Id = "gemini-3.6-flash", DisplayName = "Gemini 3.6 Flash", Provider = "google", IsDefault = true },
            new() { Id = "gemini-3.7-flash", DisplayName = "Gemini 3.7 Flash", Provider = "google" },
        };

        var sorted = ModelCatalogSorter.Sort(models, preserveDefault: true);

        Assert.Equal("gemini-3.6-flash", sorted[0].Id);
        Assert.Equal("gemini-3.7-flash", sorted[1].Id);
        Assert.Equal("gemini-2.5-flash", sorted[2].Id);
    }
}
