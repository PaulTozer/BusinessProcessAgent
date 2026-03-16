using BusinessProcessAgent.Core.Compliance;
using BusinessProcessAgent.Core.Configuration;
using BusinessProcessAgent.Core.Models;
using BusinessProcessAgent.Core.Storage;

namespace BusinessProcessAgent.Core.Services;

/// <summary>
/// Orchestrates foreground polling, screenshot capture, and LLM analysis
/// into a continuous observation loop. All data flows through the compliance
/// pipeline: exclusion checks → redaction → LLM → encryption → storage → audit.
/// </summary>
public sealed class ObservationCoordinator : IDisposable
{
    private readonly ForegroundWindowPoller _poller;
    private readonly ScreenCaptureService _capture;
    private readonly ProcessAnalysisService _analysis;
    private readonly ProcessAssembler _assembler;
    private readonly ProcessStore _store;
    private readonly RedactionService _redaction;
    private readonly EncryptionService _encryption;
    private readonly AuditLogger _audit;
    private readonly ILogger<ObservationCoordinator> _logger;

    private Timer? _captureTimer;
    private ObservationSession? _currentSession;
    private int _stepCount;
    private readonly object _gate = new();
    private ObservationSettings _observationSettings = new();
    private ComplianceSettings _complianceSettings = new();

    /// <summary>Raised when a new step is recorded.</summary>
    public event Action<ProcessStep>? StepRecorded;

    /// <summary>Raised when the observed application context changes.</summary>
    public event Action<ApplicationContext>? ContextChanged;

    public ObservationCoordinator(
        ForegroundWindowPoller poller,
        ScreenCaptureService capture,
        ProcessAnalysisService analysis,
        ProcessAssembler assembler,
        ProcessStore store,
        RedactionService redaction,
        EncryptionService encryption,
        AuditLogger audit,
        ILogger<ObservationCoordinator> logger)
    {
        _poller = poller;
        _capture = capture;
        _analysis = analysis;
        _assembler = assembler;
        _store = store;
        _redaction = redaction;
        _encryption = encryption;
        _audit = audit;
        _logger = logger;

        _poller.ContextChanged += OnContextChanged;
    }

    public void Configure(ObservationSettings observationSettings, ComplianceSettings complianceSettings)
    {
        _observationSettings = observationSettings;
        _complianceSettings = complianceSettings;
    }

    public bool IsObserving { get; private set; }
    public ObservationSession? CurrentSession => _currentSession;

    /// <summary>
    /// Starts a new observation session. Begins polling and periodic captures.
    /// </summary>
    public async Task StartAsync(string? label = null)
    {
        if (IsObserving) return;
        if (!_complianceSettings.CaptureEnabled)
        {
            _logger.LogWarning("Capture is disabled by compliance settings");
            return;
        }

        var session = new ObservationSession
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            StartedAt = DateTimeOffset.UtcNow,
            Label = label,
        };

        await _store.StartSessionAsync(session);
        _currentSession = session;
        _stepCount = 0;
        IsObserving = true;

        _poller.Start();

        var interval = TimeSpan.FromSeconds(_observationSettings.CaptureIntervalSeconds);
        _captureTimer = new Timer(OnCaptureTimerTick, null, interval, interval);

        _logger.LogInformation("Observation started — session {SessionId}", session.Id);
    }

    /// <summary>
    /// Stops the current observation session.
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsObserving) return;

        _captureTimer?.Dispose();
        _captureTimer = null;
        _poller.Stop();
        IsObserving = false;

        if (_currentSession is not null)
        {
            await _store.EndSessionAsync(_currentSession.Id, DateTimeOffset.UtcNow, _stepCount);
            _logger.LogInformation("Observation stopped — session {SessionId}, {Steps} steps",
                _currentSession.Id, _stepCount);
            _currentSession = null;
        }
    }

    /// <summary>
    /// Retrieves the assembled business processes for a given session.
    /// </summary>
    public async Task<IReadOnlyList<BusinessProcess>> GetProcessesForSessionAsync(string sessionId)
    {
        var steps = await _store.GetStepsBySessionAsync(sessionId);
        return _assembler.Assemble(steps);
    }

    /// <summary>
    /// Retrieves recent steps across all sessions and assembles them.
    /// </summary>
    public async Task<IReadOnlyList<BusinessProcess>> GetRecentProcessesAsync(int stepCount = 100)
    {
        var steps = await _store.GetRecentStepsAsync(stepCount);
        return _assembler.Assemble(steps);
    }

    public Task<IReadOnlyList<ObservationSession>> GetSessionsAsync(int take = 20)
        => _store.GetSessionsAsync(take);

    private void OnContextChanged(ApplicationContext context)
    {
        // ── Exclusion checks ──
        if (_redaction.IsApplicationExcluded(context.ProcessName))
        {
            _audit.LogExclusion(context.ProcessName, "excluded_application");
            return;
        }
        if (_redaction.IsTitleExcluded(context.WindowTitle))
        {
            _audit.LogExclusion(context.ProcessName, "excluded_title_keyword");
            return;
        }

        ContextChanged?.Invoke(context);

        if (_observationSettings.CaptureOnContextChange)
        {
            _ = CaptureAndAnalyzeAsync(context);
        }
    }

    private void OnCaptureTimerTick(object? state)
    {
        var context = _poller.CurrentContext;
        if (context is null) return;

        // Re-check exclusions for timed captures
        if (_redaction.IsApplicationExcluded(context.ProcessName)) return;
        if (_redaction.IsTitleExcluded(context.WindowTitle)) return;

        _ = CaptureAndAnalyzeAsync(context);
    }

    private async Task CaptureAndAnalyzeAsync(ApplicationContext context)
    {
        if (_currentSession is null || !_analysis.IsConfigured) return;

        try
        {
            // ── 1. Capture ──
            var rawScreenshot = _capture.CaptureActiveWindowAsBase64();
            _audit.LogCapture(_currentSession.Id, context.ProcessName, context.DocumentName, rawScreenshot is not null);

            // ── 2. Redact text BEFORE LLM ──
            var (cleanTitle, titleRedactions) = _redaction.RedactText(context.WindowTitle);
            var (cleanDoc, docRedactions) = _redaction.RedactText(context.DocumentName);
            int totalRedactions = titleRedactions + docRedactions;

            // Build redacted context for the LLM
            var redactedContext = context with
            {
                WindowTitle = cleanTitle,
                DocumentName = cleanDoc,
            };

            // ── 3. Redact screenshot BEFORE LLM ──
            string? screenshotForLlm = rawScreenshot is not null
                ? _redaction.RedactScreenshot(rawScreenshot)
                : null;

            bool screenshotRedacted = rawScreenshot is not null && screenshotForLlm != rawScreenshot;
            _audit.LogRedaction(_currentSession.Id, totalRedactions, screenshotRedacted);

            // ── 4. Call LLM with redacted data only ──
            LlmAnalysisResult? result;
            if (screenshotForLlm is not null)
            {
                _audit.LogLlmCall(_currentSession.Id, "configured-model", true, EstimateTokens(redactedContext, true));
                result = await _analysis.AnalyzeAsync(redactedContext, screenshotForLlm);
            }
            else
            {
                _audit.LogLlmCall(_currentSession.Id, "configured-model", false, EstimateTokens(redactedContext, false));
                result = await _analysis.AnalyzeTextOnlyAsync(redactedContext);
            }

            if (result is null) return;

            // ── 5. Store screenshot (encrypted or ephemeral) ──
            string? screenshotPath = null;
            bool isEphemeral = _complianceSettings.EphemeralScreenshots;

            if (!isEphemeral && rawScreenshot is not null)
            {
                if (_complianceSettings.EncryptAtRest && _encryption.IsConfigured)
                {
                    // Encrypt the redacted screenshot (never store the raw version)
                    var bytesToStore = Convert.FromBase64String(screenshotForLlm ?? rawScreenshot);
                    var dir = _observationSettings.ScreenshotDirectory;
                    Directory.CreateDirectory(dir);
                    var fileName = $"{_currentSession.Id}_{context.CapturedAt:yyyyMMdd_HHmmss}.enc";
                    screenshotPath = Path.Combine(dir, fileName);
                    _encryption.EncryptFile(bytesToStore, screenshotPath);
                }
                else
                {
                    screenshotPath = _capture.SaveScreenshot(
                        screenshotForLlm ?? rawScreenshot,
                        _observationSettings.ScreenshotDirectory,
                        _currentSession.Id,
                        context.CapturedAt);
                }
            }

            // ── 6. Persist step ──
            var stepNumber = Interlocked.Increment(ref _stepCount);

            var step = new ProcessStep
            {
                SessionId = _currentSession.Id,
                Timestamp = context.CapturedAt,
                ApplicationName = context.ProcessName,
                WindowTitle = cleanTitle,      // Store redacted version
                HighLevelAction = result.HighLevelAction,
                LowLevelAction = result.LowLevelAction,
                UserIntent = result.UserIntent,
                BusinessProcessName = result.BusinessProcessName,
                StepNumber = stepNumber,
                ScreenshotPath = screenshotPath,
                AdditionalContext = result.AdditionalContext,
                Confidence = result.Confidence,
            };

            var id = await _store.InsertStepAsync(step);
            step = step with { Id = id };

            _audit.LogStorage(_currentSession.Id, id,
                _complianceSettings.EncryptAtRest, isEphemeral);

            StepRecorded?.Invoke(step);

            _logger.LogDebug("Step #{Num}: [{Process}] {HighLevel} → {LowLevel}",
                stepNumber, result.BusinessProcessName, result.HighLevelAction, result.LowLevelAction);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Capture-and-analyze cycle failed");
        }
    }

    private static int EstimateTokens(ApplicationContext context, bool hasScreenshot)
    {
        int textTokens = (context.WindowTitle.Length + context.DocumentName.Length + context.ProcessName.Length) / 4;
        return hasScreenshot ? textTokens + 1000 : textTokens + 200; // rough estimate
    }

    public void Dispose()
    {
        _captureTimer?.Dispose();
        _poller.ContextChanged -= OnContextChanged;
        _poller.Dispose();
        _store.Dispose();
        _audit.Dispose();
    }
}
