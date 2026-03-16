using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessProcessAgent.Core.Compliance;

/// <summary>
/// Compliance and data-governance settings. Enterprise admins control
/// what is captured, what is redacted, how data is stored, and for how long.
/// </summary>
public sealed class ComplianceSettings
{
    /// <summary>Master switch — disables all capture when false.</summary>
    public bool CaptureEnabled { get; set; } = true;

    // ── Redaction ──────────────────────────────────────────────

    /// <summary>
    /// Regex patterns applied to ALL text (window titles, document names,
    /// context fields) before the text reaches the LLM or is persisted.
    /// Matched regions are replaced with <c>[REDACTED]</c>.
    /// </summary>
    public List<string> RedactionPatterns { get; set; } =
    [
        // Email addresses
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        // UK National Insurance numbers
        @"(?i)\b[A-Z]{2}\s?\d{2}\s?\d{2}\s?\d{2}\s?[A-Z]\b",
        // US Social Security numbers
        @"\b\d{3}[-–]?\d{2}[-–]?\d{4}\b",
        // Credit card numbers (basic)
        @"\b(?:\d[ \-]*?){13,19}\b",
        // Phone numbers (international)
        @"\+?\d[\d\s\-()]{7,}\d",
        // IP addresses
        @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b",
    ];

    /// <summary>
    /// Exact keywords that are always replaced with <c>[REDACTED]</c>
    /// (case-insensitive). Useful for org-specific terms, client names,
    /// or project codenames that must never leave the device.
    /// </summary>
    public List<string> RedactionKeywords { get; set; } = [];

    /// <summary>
    /// When true, screenshot regions that contain text matching any
    /// redaction rule are blurred before the image reaches the LLM.
    /// Requires OCR (Windows.Media.Ocr or similar).
    /// Falls back to full-image blur if OCR is unavailable.
    /// </summary>
    public bool RedactScreenshots { get; set; } = true;

    // ── Application Controls ──────────────────────────────────

    /// <summary>
    /// Process names that are never captured (e.g., password managers,
    /// HR portals, banking apps). Case-insensitive.
    /// </summary>
    public List<string> ExcludedApplications { get; set; } =
    [
        "1Password", "KeePass", "LastPass", "Bitwarden",
        "mstsc",     // Remote Desktop (may show other users' data)
    ];

    /// <summary>
    /// Window-title substrings that trigger an automatic skip.
    /// Captures are suppressed when the active title contains any of these.
    /// </summary>
    public List<string> ExcludedTitleKeywords { get; set; } =
    [
        "password", "credential", "secret", "token",
        "Private Browsing", "InPrivate",
    ];

    // ── Storage ───────────────────────────────────────────────

    /// <summary>
    /// When true, screenshots are held in memory only and are never
    /// written to disk. They are sent to the LLM and then discarded.
    /// </summary>
    public bool EphemeralScreenshots { get; set; } = false;

    /// <summary>
    /// When true, all data written to the local SQLite database and
    /// screenshot files is encrypted with AES-256. The key is derived
    /// from <see cref="EncryptionKeySource"/>.
    /// </summary>
    public bool EncryptAtRest { get; set; } = true;

    /// <summary>
    /// Source for the encryption key.
    /// <list type="bullet">
    ///   <item><c>DPAPI</c> — Windows Data Protection API (user-scoped, no key management)</item>
    ///   <item><c>Environment</c> — Read from <c>BPA_ENCRYPTION_KEY</c> environment variable</item>
    ///   <item><c>KeyVault</c> — Fetch from Azure Key Vault (requires Managed Identity / az login)</item>
    /// </list>
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EncryptionKeySource EncryptionKeySource { get; set; } = EncryptionKeySource.DPAPI;

    /// <summary>Azure Key Vault URI, used when <see cref="EncryptionKeySource"/> is <c>KeyVault</c>.</summary>
    public string? KeyVaultUri { get; set; }

    /// <summary>Key name inside Key Vault.</summary>
    public string? KeyVaultKeyName { get; set; }

    // ── Retention ─────────────────────────────────────────────

    /// <summary>
    /// Automatically delete captured data older than this many days.
    /// Set to 0 to disable automatic deletion.
    /// </summary>
    public int DataRetentionDays { get; set; } = 30;

    // ── Audit ─────────────────────────────────────────────────

    /// <summary>
    /// When true, every capture/redaction/LLM-call/storage event is
    /// written to an append-only audit log (no sensitive content is logged).
    /// </summary>
    public bool AuditLoggingEnabled { get; set; } = true;
}

public enum EncryptionKeySource
{
    DPAPI,
    Environment,
    KeyVault,
}
