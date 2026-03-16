using Microsoft.Win32;

namespace BusinessProcessAgent.Core.Services;

/// <summary>
/// Detects screensaver, session lock, and power suspend/resume events.
/// Exposes <see cref="IsScreenUnavailable"/> so the poller can skip
/// captures when the user is idle.
/// </summary>
public sealed class ScreenStateMonitor : IDisposable
{
    private volatile bool _isSuspended;
    private volatile bool _isLocked;

    public bool IsScreenUnavailable => _isSuspended || _isLocked || IsScreensaverRunning();

    public ScreenStateMonitor()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        _isSuspended = e.Mode == PowerModes.Suspend;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _isLocked = e.Reason == SessionSwitchReason.SessionLock;
    }

    private static bool IsScreensaverRunning()
    {
        bool running = false;
        NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETSCREENSAVERRUNNING, 0, ref running, 0);
        return running;
    }
}
