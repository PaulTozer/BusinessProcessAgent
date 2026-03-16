using BusinessProcessAgent.App.Views;
using BusinessProcessAgent.Core.Compliance;
using BusinessProcessAgent.Core.Configuration;
using BusinessProcessAgent.Core.Models;
using BusinessProcessAgent.Core.Services;
using BusinessProcessAgent.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace BusinessProcessAgent.App.Tray;

/// <summary>
/// Manages the system-tray icon, context menu, and coordinates the
/// lifecycle of the observation engine. Bridges Core services to the
/// WinUI 3 UI thread.
/// </summary>
internal sealed class TrayIconManager : IDisposable
{
    private readonly TrayIcon _tray;
    private readonly ObservationCoordinator _coordinator;
    private readonly ProcessAnalysisService _analysisService;
    private readonly RedactionService _redaction;
    private readonly EncryptionService _encryption;
    private readonly AuditLogger _audit;
    private readonly DispatcherQueue _dispatcher;
    private readonly ILogger<TrayIconManager> _logger;

    private AppSettings _settings;
    private Window? _mainWindow;
    private bool _disposed;

    public TrayIconManager(
        ObservationCoordinator coordinator,
        ProcessAnalysisService analysisService,
        RedactionService redaction,
        EncryptionService encryption,
        AuditLogger audit,
        AppSettings settings,
        DispatcherQueue dispatcher,
        ILogger<TrayIconManager> logger)
    {
        _coordinator = coordinator;
        _analysisService = analysisService;
        _redaction = redaction;
        _encryption = encryption;
        _audit = audit;
        _settings = settings;
        _dispatcher = dispatcher;
        _logger = logger;

        _tray = new TrayIcon("Business Process Agent — Idle");
        _tray.LeftClick += OnLeftClick;
        _tray.RightClick += OnRightClick;

        _coordinator.StepRecorded += OnStepRecorded;
        _coordinator.ContextChanged += OnContextChanged;
    }

    /// <summary>
    /// Shows or activates the main window. Creates it on first call.
    /// </summary>
    public void ShowMainWindow()
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (_mainWindow is null)
            {
                _mainWindow = new Window();
                _mainWindow.Title = "Business Process Agent";
                _mainWindow.Content = new Frame();
                ((Frame)_mainWindow.Content).Navigate(typeof(MainPage));
                _mainWindow.Closed += (_, _) => _mainWindow = null;
            }
            _mainWindow.Activate();
        });
    }

    /// <summary>Toggles observation on/off.</summary>
    public async Task ToggleObservationAsync()
    {
        if (_coordinator.IsObserving)
        {
            await _coordinator.StopAsync();
            _tray.UpdateTooltip("Business Process Agent — Paused");
        }
        else
        {
            await _coordinator.StartAsync();
            _tray.UpdateTooltip("Business Process Agent — Observing");
        }
    }

    public ObservationCoordinator Coordinator => _coordinator;

    private void OnLeftClick()
    {
        ShowMainWindow();
    }

    private async void OnRightClick()
    {
        // Toggle observation on right-click for now.
        // A full context menu can be built with a WinUI 3 MenuFlyout
        // attached to the tray position in a future iteration.
        await ToggleObservationAsync();
    }

    private void OnStepRecorded(ProcessStep step)
    {
        _tray.UpdateTooltip(
            $"Business Process Agent — Step #{step.StepNumber}\n" +
            $"{step.HighLevelAction}");
    }

    private void OnContextChanged(ApplicationContext context)
    {
        _tray.UpdateTooltip(
            $"Business Process Agent — {context.ProcessName}\n" +
            $"{context.DocumentName}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coordinator.StepRecorded -= OnStepRecorded;
        _coordinator.ContextChanged -= OnContextChanged;
        _tray.LeftClick -= OnLeftClick;
        _tray.RightClick -= OnRightClick;
        _tray.Dispose();
        _coordinator.Dispose();
    }
}
