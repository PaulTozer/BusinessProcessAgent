using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using BusinessProcessAgent.Core.Configuration;
using BusinessProcessAgent.Core.Models;
using OpenAI.Chat;

namespace BusinessProcessAgent.Core.Services;

/// <summary>
/// Sends screenshot + application context to Azure OpenAI for analysis.
/// Returns structured <see cref="LlmAnalysisResult"/> with high-level action,
/// low-level action, user intent, and inferred business process name.
/// </summary>
public sealed class ProcessAnalysisService
{
    private readonly ILogger<ProcessAnalysisService> _logger;
    private ChatClient? _chatClient;
    private readonly object _lock = new();

    private const string SystemPrompt = """
        You are a business process analyst observing a user's desktop activity.
        You receive a screenshot of the user's current screen along with application context.

        Analyze what the user is doing and respond with a JSON object containing:
        - highLevelAction: A concise description of the high-level business activity
          (e.g., "Creating a purchase order", "Reviewing an invoice", "Writing a report").
        - lowLevelAction: A specific description of the exact step the user is performing
          (e.g., "Entering vendor name in the Supplier field", "Scrolling through line items").
        - userIntent: Your inference of what the user is trying to accomplish and why
          (e.g., "User is procuring office supplies from a new vendor").
        - businessProcessName: The name of the overall business process this step belongs to
          (e.g., "Purchase Order Creation", "Invoice Approval", "Monthly Reporting").
          Use consistent names when the same process is observed again.
        - additionalContext: Any other relevant observations about the screen content,
          data visible, or workflow state. Null if nothing noteworthy.
        - confidence: A number from 0.0 to 1.0 indicating how confident you are in the analysis.

        Guidelines:
        - Be specific and actionable in your descriptions.
        - Identify the business process by name using standard business terminology.
        - If the user appears to be between tasks or idle, say so explicitly.
        - If you cannot determine the activity, set confidence below 0.3.
        - Respond ONLY with the JSON object, no markdown fences or extra text.
        """;

    public ProcessAnalysisService(ILogger<ProcessAnalysisService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Configures the service with Azure OpenAI credentials. Must be called
    /// before <see cref="AnalyzeAsync"/>.
    /// </summary>
    public void Configure(AzureAiSettings settings)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(settings.Endpoint) || string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                _chatClient = null;
                return;
            }

            var client = new AzureOpenAIClient(
                new Uri(settings.Endpoint),
                new ApiKeyCredential(settings.ApiKey));
            _chatClient = client.GetChatClient(settings.Model);
            _logger.LogInformation("ProcessAnalysisService configured for model {Model}", settings.Model);
        }
    }

    public bool IsConfigured
    {
        get { lock (_lock) return _chatClient is not null; }
    }

    /// <summary>
    /// Analyzes a screenshot with application context and returns a structured result.
    /// </summary>
    public async Task<LlmAnalysisResult?> AnalyzeAsync(
        ApplicationContext context,
        string screenshotBase64,
        CancellationToken cancellationToken = default)
    {
        ChatClient? client;
        lock (_lock) client = _chatClient;

        if (client is null)
        {
            _logger.LogDebug("Analysis skipped — service not configured");
            return null;
        }

        try
        {
            var userContent = new List<ChatMessageContentPart>
            {
                ChatMessageContentPart.CreateTextPart(
                    $"Application: {context.ProcessName}\n" +
                    $"Window Title: {context.WindowTitle}\n" +
                    $"Document: {context.DocumentName}\n" +
                    $"Timestamp: {context.CapturedAt:u}"),
                ChatMessageContentPart.CreateImagePart(
                    BinaryData.FromBytes(Convert.FromBase64String(screenshotBase64)),
                    "image/jpeg"),
            };

            var messages = new ChatMessage[]
            {
                new SystemChatMessage(SystemPrompt),
                new UserChatMessage(userContent),
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 0.2f,
                MaxOutputTokenCount = 500,
            };

            var response = await client.CompleteChatAsync(messages, options, cancellationToken);
            var json = response.Value.Content[0].Text;

            return JsonSerializer.Deserialize<LlmAnalysisResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM analysis failed");
            return null;
        }
    }

    /// <summary>
    /// Text-only analysis when screenshot is unavailable.
    /// </summary>
    public async Task<LlmAnalysisResult?> AnalyzeTextOnlyAsync(
        ApplicationContext context,
        CancellationToken cancellationToken = default)
    {
        ChatClient? client;
        lock (_lock) client = _chatClient;

        if (client is null) return null;

        try
        {
            var messages = new ChatMessage[]
            {
                new SystemChatMessage(SystemPrompt),
                new UserChatMessage(
                    $"Application: {context.ProcessName}\n" +
                    $"Window Title: {context.WindowTitle}\n" +
                    $"Document: {context.DocumentName}\n" +
                    $"Timestamp: {context.CapturedAt:u}\n\n" +
                    "No screenshot is available. Analyze based on the application context only."),
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 0.2f,
                MaxOutputTokenCount = 500,
            };

            var response = await client.CompleteChatAsync(messages, options, cancellationToken);
            var json = response.Value.Content[0].Text;

            return JsonSerializer.Deserialize<LlmAnalysisResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Text-only LLM analysis failed");
            return null;
        }
    }
}
