namespace BusinessProcessAgent.Core.Models;

/// <summary>
/// Snapshot of the current application context captured by the poller.
/// </summary>
public sealed record ApplicationContext
{
    public required IntPtr WindowHandle { get; init; }
    public required string ProcessName { get; init; }
    public required string WindowTitle { get; init; }
    public required string DocumentName { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
}
