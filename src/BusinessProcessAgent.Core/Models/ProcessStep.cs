namespace BusinessProcessAgent.Core.Models;

/// <summary>
/// A single observed step in a business process, produced by LLM analysis
/// of a screenshot + application context.
/// </summary>
public sealed record ProcessStep
{
    public long Id { get; init; }
    public required string SessionId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string ApplicationName { get; init; }
    public required string WindowTitle { get; init; }

    /// <summary>High-level description, e.g. "Creating a purchase order".</summary>
    public required string HighLevelAction { get; init; }

    /// <summary>Low-level description, e.g. "Entering vendor name in the Supplier field".</summary>
    public required string LowLevelAction { get; init; }

    /// <summary>Inferred user intent, e.g. "User is filling in order details for a new supplier".</summary>
    public required string UserIntent { get; init; }

    /// <summary>Name of the business process this step belongs to, e.g. "Purchase Order Creation".</summary>
    public string? BusinessProcessName { get; init; }

    /// <summary>Sequential position within the current process flow.</summary>
    public int StepNumber { get; init; }

    /// <summary>Relative path to the stored screenshot for this step.</summary>
    public string? ScreenshotPath { get; init; }

    /// <summary>Additional structured context extracted by the LLM.</summary>
    public string? AdditionalContext { get; init; }

    /// <summary>Confidence score from the LLM (0.0–1.0).</summary>
    public double Confidence { get; init; }
}
