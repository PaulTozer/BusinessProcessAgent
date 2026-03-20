using System.Text.Json;
using BusinessProcessAgent.Core.Compliance;

namespace BusinessProcessAgent.Core.Configuration;

public sealed class AppSettings
{
    public AzureAiSettings AzureAi { get; set; } = new();
    public DatabaseSettings Database { get; set; } = new();
    public ObservationSettings Observation { get; set; } = new();
    public ComplianceSettings Compliance { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static AppSettings Load(string filePath)
    {
        if (!File.Exists(filePath))
            return new AppSettings();

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    public void Save(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(filePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}

public sealed class AzureAiSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public bool Enabled { get; set; }
}

public sealed class ObservationSettings
{
    /// <summary>Polling interval in seconds for foreground window checks.</summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>Interval in seconds between automatic screenshot captures.</summary>
    public int CaptureIntervalSeconds { get; set; } = 15;

    /// <summary>Whether to capture on every context (app) change, in addition to timed captures.</summary>
    public bool CaptureOnContextChange { get; set; } = true;

    /// <summary>Directory where screenshots are stored.</summary>
    public string ScreenshotDirectory { get; set; } = "data/screenshots";
}

public sealed class DatabaseSettings
{
    /// <summary>
    /// PostgreSQL connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
