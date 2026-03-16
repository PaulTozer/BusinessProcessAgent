using BusinessProcessAgent.Core.Configuration;

namespace BusinessProcessAgent.App.Views;

public sealed partial class SettingsPage : Page
{
    private AppSettings? _settings;

    public SettingsPage()
    {
        this.InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "data", "settings.json");
        _settings = AppSettings.Load(settingsPath);

        // AI
        toggleAiEnabled.IsOn = _settings.AzureAi.Enabled;
        txtEndpoint.Text = _settings.AzureAi.Endpoint;
        txtApiKey.Password = _settings.AzureAi.ApiKey;
        txtModel.Text = _settings.AzureAi.Model;

        // Observation
        numPollingInterval.Value = _settings.Observation.PollingIntervalSeconds;
        numCaptureInterval.Value = _settings.Observation.CaptureIntervalSeconds;
        toggleCaptureOnChange.IsOn = _settings.Observation.CaptureOnContextChange;

        // Compliance
        toggleEphemeral.IsOn = _settings.Compliance.EphemeralScreenshots;
        toggleEncrypt.IsOn = _settings.Compliance.EncryptAtRest;
        toggleAudit.IsOn = _settings.Compliance.AuditLoggingEnabled;
        toggleRedactScreenshots.IsOn = _settings.Compliance.RedactScreenshots;
        numRetention.Value = _settings.Compliance.DataRetentionDays;
        txtExcludedApps.Text = string.Join(", ", _settings.Compliance.ExcludedApplications);
        txtExcludedKeywords.Text = string.Join(", ", _settings.Compliance.ExcludedTitleKeywords);
        txtRedactionKeywords.Text = string.Join(", ", _settings.Compliance.RedactionKeywords);
    }

    private void OnAiToggled(object sender, RoutedEventArgs e)
    {
        // Visual feedback only — actual save happens on Save click
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;

        // AI
        _settings.AzureAi.Enabled = toggleAiEnabled.IsOn;
        _settings.AzureAi.Endpoint = txtEndpoint.Text;
        _settings.AzureAi.ApiKey = txtApiKey.Password;
        _settings.AzureAi.Model = txtModel.Text;

        // Observation
        _settings.Observation.PollingIntervalSeconds = (int)numPollingInterval.Value;
        _settings.Observation.CaptureIntervalSeconds = (int)numCaptureInterval.Value;
        _settings.Observation.CaptureOnContextChange = toggleCaptureOnChange.IsOn;

        // Compliance
        _settings.Compliance.EphemeralScreenshots = toggleEphemeral.IsOn;
        _settings.Compliance.EncryptAtRest = toggleEncrypt.IsOn;
        _settings.Compliance.AuditLoggingEnabled = toggleAudit.IsOn;
        _settings.Compliance.RedactScreenshots = toggleRedactScreenshots.IsOn;
        _settings.Compliance.DataRetentionDays = (int)numRetention.Value;

        _settings.Compliance.ExcludedApplications = txtExcludedApps.Text
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        _settings.Compliance.ExcludedTitleKeywords = txtExcludedKeywords.Text
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        _settings.Compliance.RedactionKeywords = txtRedactionKeywords.Text
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "data", "settings.json");
        _settings.Save(settingsPath);
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }
}
