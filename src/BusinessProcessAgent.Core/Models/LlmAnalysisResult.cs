namespace BusinessProcessAgent.Core.Models;

/// <summary>
/// Raw structured output from the LLM analysis of a screenshot + context.
/// Parsed from JSON before being mapped to a <see cref="ProcessStep"/>.
/// </summary>
public sealed record LlmAnalysisResult
{
    public required string HighLevelAction { get; init; }
    public required string LowLevelAction { get; init; }
    public required string UserIntent { get; init; }
    public string? BusinessProcessName { get; init; }
    public string? AdditionalContext { get; init; }
    public double Confidence { get; init; }
}
