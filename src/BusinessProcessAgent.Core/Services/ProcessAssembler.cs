using BusinessProcessAgent.Core.Models;

namespace BusinessProcessAgent.Core.Services;

/// <summary>
/// Takes a temporal sequence of <see cref="ProcessStep"/> observations and
/// groups them into coherent <see cref="BusinessProcess"/> flows. Detects
/// process boundaries when the business process name changes or a significant
/// time gap occurs.
/// </summary>
public sealed class ProcessAssembler
{
    private readonly ILogger<ProcessAssembler> _logger;

    /// <summary>Time gap (in minutes) that signals a process boundary.</summary>
    private const int ProcessBoundaryGapMinutes = 10;

    public ProcessAssembler(ILogger<ProcessAssembler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Groups a chronologically ordered list of steps into business processes.
    /// </summary>
    public IReadOnlyList<BusinessProcess> Assemble(IReadOnlyList<ProcessStep> steps)
    {
        if (steps.Count == 0) return [];

        var processes = new List<BusinessProcess>();
        var currentSteps = new List<ProcessStep> { steps[0] };
        var currentName = steps[0].BusinessProcessName ?? "Unknown Process";

        for (int i = 1; i < steps.Count; i++)
        {
            var step = steps[i];
            var prev = steps[i - 1];
            var gap = step.Timestamp - prev.Timestamp;
            var stepName = step.BusinessProcessName ?? "Unknown Process";

            bool isBoundary = !string.Equals(stepName, currentName, StringComparison.OrdinalIgnoreCase)
                              || gap.TotalMinutes > ProcessBoundaryGapMinutes;

            if (isBoundary)
            {
                processes.Add(BuildProcess(currentName, currentSteps));
                currentSteps = [step];
                currentName = stepName;
            }
            else
            {
                currentSteps.Add(step);
            }
        }

        // Flush final group
        if (currentSteps.Count > 0)
            processes.Add(BuildProcess(currentName, currentSteps));

        _logger.LogInformation("Assembled {Count} business processes from {Steps} steps",
            processes.Count, steps.Count);

        return processes;
    }

    private static BusinessProcess BuildProcess(string name, List<ProcessStep> steps)
    {
        // Re-number steps sequentially
        var numbered = steps.Select((s, i) => s with { StepNumber = i + 1 }).ToList();

        return new BusinessProcess
        {
            Name = name,
            FirstSeen = numbered[0].Timestamp,
            LastSeen = numbered[^1].Timestamp,
            Steps = numbered,
        };
    }
}
