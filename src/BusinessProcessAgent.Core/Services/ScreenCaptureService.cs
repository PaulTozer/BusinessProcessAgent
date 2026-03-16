using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

namespace BusinessProcessAgent.Core.Services;

/// <summary>
/// Captures screenshots of the foreground window. Uses PrintWindow with
/// fallback to BitBlt. Resizes large captures and returns base64 JPEG
/// for LLM vision analysis.
/// </summary>
public sealed class ScreenCaptureService
{
    private const int MaxWidth = 1280;
    private const int MaxHeight = 720;
    private const long JpegQuality = 75L;

    private readonly ILogger<ScreenCaptureService> _logger;

    public ScreenCaptureService(ILogger<ScreenCaptureService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Captures the current foreground window and returns a base64-encoded JPEG.
    /// Returns null if the capture fails.
    /// </summary>
    public string? CaptureActiveWindowAsBase64()
    {
        var hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return null;
        return CaptureWindowAsBase64(hWnd);
    }

    /// <summary>
    /// Captures a specific window by handle and returns base64 JPEG.
    /// </summary>
    public string? CaptureWindowAsBase64(IntPtr hWnd)
    {
        try
        {
            if (!NativeMethods.GetWindowRect(hWnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
                return null;

            using var bitmap = new Bitmap(rect.Width, rect.Height);
            using var graphics = Graphics.FromImage(bitmap);
            var hdc = graphics.GetHdc();

            // Try PrintWindow first (better for layered/DWM windows)
            bool captured = NativeMethods.PrintWindow(hWnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
            if (!captured)
            {
                // Fallback to BitBlt
                var srcDc = NativeMethods.GetDC(hWnd);
                NativeMethods.BitBlt(hdc, 0, 0, rect.Width, rect.Height, srcDc, 0, 0, NativeMethods.SRCCOPY);
                NativeMethods.ReleaseDC(hWnd, srcDc);
            }

            graphics.ReleaseHdc(hdc);

            var resized = ResizeIfNeeded(bitmap);
            return BitmapToBase64Jpeg(resized);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture window screenshot");
            return null;
        }
    }

    /// <summary>
    /// Saves a base64 JPEG screenshot to disk and returns the file path.
    /// </summary>
    public string? SaveScreenshot(string base64Jpeg, string directory, string sessionId, DateTimeOffset timestamp)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var fileName = $"{sessionId}_{timestamp:yyyyMMdd_HHmmss}.jpg";
            var filePath = Path.Combine(directory, fileName);
            var bytes = Convert.FromBase64String(base64Jpeg);
            File.WriteAllBytes(filePath, bytes);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save screenshot to disk");
            return null;
        }
    }

    private static Bitmap ResizeIfNeeded(Bitmap source)
    {
        if (source.Width <= MaxWidth && source.Height <= MaxHeight)
            return source;

        double scale = Math.Min((double)MaxWidth / source.Width, (double)MaxHeight / source.Height);
        int newW = (int)(source.Width * scale);
        int newH = (int)(source.Height * scale);

        var resized = new Bitmap(newW, newH);
        using var g = Graphics.FromImage(resized);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, newW, newH);
        return resized;
    }

    private static string BitmapToBase64Jpeg(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        var encoderParams = new EncoderParameters(1)
        {
            Param = { [0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality) }
        };
        bitmap.Save(ms, encoder, encoderParams);
        return Convert.ToBase64String(ms.ToArray());
    }
}
