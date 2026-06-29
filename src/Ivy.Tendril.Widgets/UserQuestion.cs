namespace Ivy.Tendril.Widgets;

public sealed record UserQuestion
{
    /// <summary>Full question text shown to the user; also the key answers are returned under.</summary>
    public required string Question { get; init; }

    /// <summary>Very short chip/tag label (≤12 chars), e.g. "Auth method".</summary>
    public required string Header { get; init; }

    /// <summary>When true the user may pick more than one option.</summary>
    public bool MultiSelect { get; init; }

    /// <summary>
    /// The selectable answers (2–4). The UI always appends an "Other" free-text choice,
    /// so it is not represented here.
    /// </summary>
    public required IReadOnlyList<UserChoice> Options { get; init; }
    
    public string Answer { get; init; }
}

/// <summary>A selectable answer for a <see cref="UserQuestion"/>.</summary>
public sealed record UserChoice
{
    /// <summary>Concise choice text the user selects (1–5 words).</summary>
    public required string Label { get; init; }

    /// <summary>What choosing this option means / its trade-offs.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// Optional markdown preview (mockup, code snippet, diagram) for side-by-side
    /// comparison. Single-select questions only.
    /// </summary>
    public string? Preview { get; init; }
}
