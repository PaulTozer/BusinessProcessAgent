namespace BusinessProcessAgent.Core.Models;

/// <summary>
/// An assembled business process built from a sequence of observed steps.
/// </summary>
public sealed record BusinessProcess
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
    public int TimesObserved { get; init; } = 1;
    public required IReadOnlyList<ProcessStep> Steps { get; init; }
}
