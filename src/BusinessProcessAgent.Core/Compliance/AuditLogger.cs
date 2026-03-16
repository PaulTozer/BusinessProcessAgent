using System.Collections.Concurrent;

namespace BusinessProcessAgent.Core.Compliance;

/// <summary>
/// Append-only audit log that records capture, redaction, LLM-call, and
/// storage events. Logs metadata only — never sensitive content.
/// </summary>
public sealed class AuditLogger : IDisposable
{
    private readonly ILogger<AuditLogger> _logger;
    private readonly ConcurrentQueue<AuditEntry> _buffer = new();
    private StreamWriter? _writer;
    private readonly object _writerLock = new();
    private bool _enabled;

    public AuditLogger(ILogger<AuditLogger> logger)
    {
        _logger = logger;
    }

    public void Configure(ComplianceSettings settings, string auditLogDirectory)
    {
        _enabled = settings.AuditLoggingEnabled;
        if (!_enabled) return;

        Directory.CreateDirectory(auditLogDirectory);
        var logPath = Path.Combine(auditLogDirectory, $"audit-{DateTime.UtcNow:yyyyMMdd}.log");

        lock (_writerLock)
        {
            _writer?.Dispose();
            _writer = new StreamWriter(logPath, append: true) { AutoFlush = true };
        }

        _logger.LogInformation("Audit logging enabled at {Path}", logPath);
    }

    public void LogCapture(string sessionId, string processName, string documentName, bool screenshotTaken)
    {
        Write(new AuditEntry
        {
            Event = AuditEvent.Capture,
            SessionId = sessionId,
            Details = $"process={processName}, document_length={documentName.Length}, screenshot={screenshotTaken}",
        });
    }

    public void LogRedaction(string sessionId, int textRedactions, bool screenshotRedacted)
    {
        Write(new AuditEntry
        {
            Event = AuditEvent.Redaction,
            SessionId = sessionId,
            Details = $"text_redactions={textRedactions}, screenshot_redacted={screenshotRedacted}",
        });
    }

    public void LogLlmCall(string sessionId, string model, bool includesScreenshot, int promptTokenEstimate)
    {
        Write(new AuditEntry
        {
            Event = AuditEvent.LlmCall,
            SessionId = sessionId,
            Details = $"model={model}, screenshot={includesScreenshot}, est_tokens={promptTokenEstimate}",
        });
    }

    public void LogStorage(string sessionId, long stepId, bool encrypted, bool ephemeralScreenshot)
    {
        Write(new AuditEntry
        {
            Event = AuditEvent.Storage,
            SessionId = sessionId,
            Details = $"step_id={stepId}, encrypted={encrypted}, ephemeral_screenshot={ephemeralScreenshot}",
        });
    }

    public void LogExclusion(string processName, string reason)
    {
        Write(new AuditEntry
        {
            Event = AuditEvent.Exclusion,
            Details = $"process={processName}, reason={reason}",
        });
    }

    public void LogRetentionPurge(int recordsDeleted, int screenshotsDeleted)
    {
        Write(new AuditEntry
        {
            Event = AuditEvent.RetentionPurge,
            Details = $"records_deleted={recordsDeleted}, screenshots_deleted={screenshotsDeleted}",
        });
    }

    private void Write(AuditEntry entry)
    {
        if (!_enabled) return;

        var line = $"{entry.Timestamp:o}\t{entry.Event}\t{entry.SessionId ?? "-"}\t{entry.Details}";

        lock (_writerLock)
        {
            _writer?.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_writerLock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}

internal sealed record AuditEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required AuditEvent Event { get; init; }
    public string? SessionId { get; init; }
    public required string Details { get; init; }
}

internal enum AuditEvent
{
    Capture,
    Redaction,
    LlmCall,
    Storage,
    Exclusion,
    RetentionPurge,
}
