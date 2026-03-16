using System.Text;
using System.Text.RegularExpressions;

namespace BusinessProcessAgent.Core.Services;

/// <summary>
/// Normalizes window titles to extract meaningful document/context names.
/// Strips application suffixes, browser chrome, tab counts, etc.
/// </summary>
public static partial class WindowContextResolver
{
    private static readonly Dictionary<string, string> OfficeSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "WINWORD",    " - Word" },
        { "EXCEL",      " - Excel" },
        { "POWERPNT",   " - PowerPoint" },
        { "ONENOTE",    " - OneNote" },
        { "VISIO",      " - Visio" },
    };

    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore",
    };

    public static string ResolveDocumentName(string processName, string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
            return processName;

        // Office applications — strip the suffix
        if (OfficeSuffixes.TryGetValue(processName, out var suffix))
        {
            var idx = windowTitle.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
            return idx > 0 ? windowTitle[..idx].Trim() : windowTitle;
        }

        // Browsers — strip tab counts, profile, and browser name
        if (BrowserProcesses.Contains(processName))
            return CleanBrowserTitle(windowTitle);

        // Teams — extract meaningful part from pipe-delimited title
        if (processName.Equals("Teams", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("ms-teams", StringComparison.OrdinalIgnoreCase))
            return CleanTeamsTitle(windowTitle);

        // Outlook — strip suffix
        if (processName.Equals("OUTLOOK", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("olk", StringComparison.OrdinalIgnoreCase))
        {
            var idx = windowTitle.LastIndexOf(" - Outlook", StringComparison.OrdinalIgnoreCase);
            return idx > 0 ? windowTitle[..idx].Trim() : windowTitle;
        }

        return windowTitle;
    }

    private static string CleanBrowserTitle(string title)
    {
        // Remove leading tab count like "(3) "
        var cleaned = TabCountRegex().Replace(title, "");
        // Remove trailing browser name like " - Google Chrome" or " — Mozilla Firefox"
        cleaned = BrowserSuffixRegex().Replace(cleaned, "");
        // Remove profile suffix like " - Profile 1"
        cleaned = ProfileSuffixRegex().Replace(cleaned, "");
        return cleaned.Trim();
    }

    private static string CleanTeamsTitle(string title)
    {
        // Teams titles often look like "Chat | Person Name | Microsoft Teams"
        var parts = title.Split('|', StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? parts[1] : title;
    }

    [GeneratedRegex(@"^\(\d+\)\s*")]
    private static partial Regex TabCountRegex();

    [GeneratedRegex(@"\s[-–—]\s*(Google Chrome|Microsoft Edge|Mozilla Firefox|Brave|Opera|Vivaldi).*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex BrowserSuffixRegex();

    [GeneratedRegex(@"\s*-\s*Profile\s+\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileSuffixRegex();
}
