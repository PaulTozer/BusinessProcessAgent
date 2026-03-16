using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace BusinessProcessAgent.Core.Compliance;

/// <summary>
/// Redacts PII and sensitive content from text and screenshots
/// <b>before</b> they reach the LLM or are persisted. Nothing
/// leaves this service un-scrubbed.
/// </summary>
public sealed class RedactionService
{
    private readonly ILogger<RedactionService> _logger;
    private List<Regex> _patterns = [];
    private HashSet<string> _keywords = new(StringComparer.OrdinalIgnoreCase);
    private ComplianceSettings _settings = new();

    private const string Placeholder = "[REDACTED]";

    public RedactionService(ILogger<RedactionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Reload rules from settings. Call after settings change.
    /// </summary>
    public void Configure(ComplianceSettings settings)
    {
        _settings = settings;

        _patterns = settings.RedactionPatterns
            .Select(p =>
            {
                try { return new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(ex, "Invalid redaction pattern skipped: {Pattern}", p);
                    return null;
                }
            })
            .Where(r => r is not null)
            .Cast<Regex>()
            .ToList();

        _keywords = new HashSet<string>(settings.RedactionKeywords, StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation(
            "RedactionService configured — {Patterns} patterns, {Keywords} keywords",
            _patterns.Count, _keywords.Count);
    }

    // ── Text Redaction ────────────────────────────────────────

    /// <summary>
    /// Scrubs all sensitive content from <paramref name="text"/>.
    /// Returns the cleaned string and the number of redactions applied.
    /// </summary>
    public (string Cleaned, int RedactionCount) RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return (string.Empty, 0);

        int count = 0;
        var result = text;

        // Regex patterns
        foreach (var regex in _patterns)
        {
            var matches = regex.Matches(result);
            count += matches.Count;
            result = regex.Replace(result, Placeholder);
        }

        // Keyword replacement
        foreach (var keyword in _keywords)
        {
            if (result.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                int before = result.Length;
                result = Regex.Replace(result, Regex.Escape(keyword), Placeholder, RegexOptions.IgnoreCase);
                if (result.Length != before) count++;
            }
        }

        return (result, count);
    }

    // ── Screenshot Redaction ──────────────────────────────────

    /// <summary>
    /// Applies a redaction overlay to a screenshot. When OCR is available,
    /// only regions containing sensitive text are blurred. Otherwise the
    /// entire image is mildly blurred as a conservative fallback.
    /// Returns the redacted base64 JPEG.
    /// </summary>
    public string? RedactScreenshot(string? base64Jpeg)
    {
        if (string.IsNullOrEmpty(base64Jpeg) || !_settings.RedactScreenshots)
            return base64Jpeg;

        try
        {
            var bytes = Convert.FromBase64String(base64Jpeg);
            using var ms = new MemoryStream(bytes);
            using var bitmap = new Bitmap(ms);

            // Conservative approach: apply a light Gaussian-style blur
            // to the full image to prevent readable PII in the screenshot.
            // A production build should integrate Windows.Media.Ocr to
            // selectively blur only regions containing sensitive text.
            ApplyPixelationOverlay(bitmap);

            using var outMs = new MemoryStream();
            var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
            var encoderParams = new EncoderParameters(1)
            {
                Param = { [0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L) }
            };
            bitmap.Save(outMs, encoder, encoderParams);
            return Convert.ToBase64String(outMs.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Screenshot redaction failed — dropping screenshot");
            return null; // Fail closed: if we can't redact, don't send it
        }
    }

    // ── Guards ─────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the given process name is on the exclusion list.
    /// </summary>
    public bool IsApplicationExcluded(string processName)
    {
        return _settings.ExcludedApplications
            .Any(e => e.Equals(processName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true if the window title contains an excluded keyword.
    /// </summary>
    public bool IsTitleExcluded(string windowTitle)
    {
        return _settings.ExcludedTitleKeywords
            .Any(k => windowTitle.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    // ── Private Helpers ───────────────────────────────────────

    /// <summary>
    /// Applies a mosaic/pixelation effect that preserves layout structure
    /// for the LLM (buttons, fields, menus remain identifiable) while
    /// making individual text characters unreadable.
    /// </summary>
    private static void ApplyPixelationOverlay(Bitmap bitmap)
    {
        const int blockSize = 8;

        for (int y = 0; y < bitmap.Height; y += blockSize)
        {
            for (int x = 0; x < bitmap.Width; x += blockSize)
            {
                int w = Math.Min(blockSize, bitmap.Width - x);
                int h = Math.Min(blockSize, bitmap.Height - y);

                // Average the block
                int r = 0, g = 0, b = 0, count = 0;
                for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    var pixel = bitmap.GetPixel(x + dx, y + dy);
                    r += pixel.R; g += pixel.G; b += pixel.B; count++;
                }

                var avg = Color.FromArgb(r / count, g / count, b / count);

                for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    bitmap.SetPixel(x + dx, y + dy, avg);
                }
            }
        }
    }
}
