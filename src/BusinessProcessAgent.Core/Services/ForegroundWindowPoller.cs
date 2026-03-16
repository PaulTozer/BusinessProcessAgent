using System.Text;
using BusinessProcessAgent.Core.Models;

namespace BusinessProcessAgent.Core.Services;

/// <summary>
/// Polls the foreground window at a configurable interval, detects
/// context changes, and raises events when the active application changes.
/// Skips capture when the screen is unavailable (locked, screensaver, etc.).
/// </summary>
public sealed class ForegroundWindowPoller : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly ScreenStateMonitor _screenState;
    private readonly ILogger<ForegroundWindowPoller> _logger;
    private readonly HashSet<string> _excludedProcesses;
    private Timer? _timer;
    private ApplicationContext? _lastContext;
    private readonly object _gate = new();

    /// <summary>Raised when the foreground application context changes.</summary>
    public event Action<ApplicationContext>? ContextChanged;

    public ForegroundWindowPoller(
        TimeSpan interval,
        ScreenStateMonitor screenState,
        ILogger<ForegroundWindowPoller> logger,
        IEnumerable<string>? excludedProcessNames = null)
    {
        _interval = interval;
        _screenState = screenState;
        _logger = logger;
        _excludedProcesses = new HashSet<string>(
            excludedProcessNames ?? ["explorer", "ShellExperienceHost", "RuntimeBroker", "BusinessProcessAgent"],
            StringComparer.OrdinalIgnoreCase);
    }

    public void Start()
    {
        _timer?.Dispose();
        _timer = new Timer(Poll, null, TimeSpan.Zero, _interval);
        _logger.LogInformation("Foreground polling started (interval: {Interval}s)", _interval.TotalSeconds);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _logger.LogInformation("Foreground polling stopped");
    }

    /// <summary>Returns the most recently observed context, or null if none.</summary>
    public ApplicationContext? CurrentContext
    {
        get { lock (_gate) return _lastContext; }
    }

    private void Poll(object? state)
    {
        if (_screenState.IsScreenUnavailable) return;

        try
        {
            var hWnd = NativeMethods.GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            var process = Process.GetProcessById((int)pid);
            var processName = process.ProcessName;

            if (_excludedProcesses.Contains(processName)) return;

            var sb = new StringBuilder(512);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            var windowTitle = sb.ToString();
            if (string.IsNullOrWhiteSpace(windowTitle)) return;

            var documentName = WindowContextResolver.ResolveDocumentName(processName, windowTitle);

            var context = new ApplicationContext
            {
                WindowHandle = hWnd,
                ProcessName = processName,
                WindowTitle = windowTitle,
                DocumentName = documentName,
                CapturedAt = DateTimeOffset.UtcNow,
            };

            lock (_gate)
            {
                if (_lastContext is null ||
                    _lastContext.ProcessName != context.ProcessName ||
                    _lastContext.DocumentName != context.DocumentName)
                {
                    _lastContext = context;
                    ContextChanged?.Invoke(context);
                }
                else
                {
                    _lastContext = context;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Polling tick failed");
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
