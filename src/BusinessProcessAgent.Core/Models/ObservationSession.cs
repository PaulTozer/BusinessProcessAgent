namespace BusinessProcessAgent.Core.Models;

/// <summary>
/// Represents a recording session — a continuous period of observation
/// from start to stop.
/// </summary>
public sealed record ObservationSession
{
    public required string Id { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public string? Label { get; init; }
    public int StepCount { get; init; }
}
