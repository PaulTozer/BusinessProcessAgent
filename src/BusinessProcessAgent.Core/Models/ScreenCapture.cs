namespace BusinessProcessAgent.Core.Models;

/// <summary>
/// A captured screenshot associated with an application context.
/// </summary>
public sealed record ScreenCapture
{
    public required string FilePath { get; init; }
    public required string Base64Jpeg { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required ApplicationContext Context { get; init; }
}
