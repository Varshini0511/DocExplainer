using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;
using MyFirstAI.Models;

namespace MyFirstAI.Services;

public interface IGeminiService
{
    Task<ClaudeResult> SendMessageAsync(string userMessage, CancellationToken ct = default);
    Task<ClaudeResult> SendConversationAsync(List<ConversationMessage> messages, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamMessageAsync(string userMessage, CancellationToken ct = default);
}

public class GeminiService : IGeminiService
{
    private readonly ILogger<GeminiService> _logger;
    private readonly GenerativeModel _model;
    private readonly GenerationConfig _generationConfig;

    private static readonly RequestOptions TimeoutOptions = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const string SystemPrompt = """
        You are a helpful AI assistant for a software development team.
        You are expert in .NET, C#, Azure, Angular, and SQL Server.
        Keep your answers clear, practical, and concise.
        When showing code, always use C# unless the user asks for something else.
        If you do not know something, say so honestly.
        """;

    public GeminiService(IConfiguration configuration, ILogger<GeminiService> logger)
    {
        _logger = logger;

        var apiKey = configuration["Gemini:ApiKey"]
            ?? throw new InvalidOperationException(
                "Gemini API key not found. Set 'Gemini:ApiKey' in appsettings or environment variables.");

        _generationConfig = new GenerationConfig
        {
            MaxOutputTokens = configuration.GetValue<int?>("Gemini:MaxOutputTokens") ?? 1024,
            Temperature     = configuration.GetValue<float?>("Gemini:Temperature") ?? 0.7f
        };

        var googleAI = new GoogleAI(apiKey);
        _model = googleAI.GenerativeModel(
            model: "gemini-2.5-flash",
            systemInstruction: new Content(SystemPrompt, "system"));
    }

    public async Task<ClaudeResult> SendMessageAsync(string userMessage, CancellationToken ct = default)
    {
        try
        {
            var response = await _model.GenerateContent(
                userMessage,
                _generationConfig,
                null, null, null,
                TimeoutOptions,
                ct);
            var replyText = response.Text ?? "No reply received.";
            var tokensUsed = response.UsageMetadata?.CandidatesTokenCount ?? 0;
            return ClaudeResult.Ok(replyText, tokensUsed);
        }
        catch (StopCandidateException ex)
        {
            _logger.LogWarning(ex, "Gemini stopped early — likely hit MaxOutputTokens");
            return ClaudeResult.Fail("Response was cut off. Try increasing MaxOutputTokens in appsettings.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Gemini API request timed out or was cancelled");
            return ClaudeResult.Fail("The AI service took too long to respond. Please try again.");
        }
        catch (GeminiApiException ex) when (ex.Message.Contains("429") || ex.Message.Contains("RESOURCE_EXHAUSTED"))
        {
            _logger.LogError(ex, "Gemini API quota exceeded");
            return ClaudeResult.Fail("API quota exceeded. Please generate a new API key or enable billing.");
        }
        catch (GeminiApiException ex)
        {
            _logger.LogError(ex, "Gemini API error");
            return ClaudeResult.Fail($"Gemini API error: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error calling Gemini API");
            return ClaudeResult.Fail("Could not reach the AI service. Check your network connection.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in GeminiService");
            return ClaudeResult.Fail("An unexpected error occurred.");
        }
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        IAsyncEnumerable<GenerateContentResponse> stream;
        try
        {
            stream = _model.GenerateContentStream(userMessage, _generationConfig, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Gemini stream");
            yield break;
        }

        await foreach (var chunk in stream.WithCancellation(ct))
        {
            var text = chunk.Text;
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    public async Task<ClaudeResult> SendConversationAsync(List<ConversationMessage> messages, CancellationToken ct = default)
    {
        try
        {
            var history = messages.SkipLast(1)
                .Select(m => new ContentResponse(
                    m.Content,
                    m.Role == "assistant" ? "model" : "user"))
                .ToList();

            var chat = _model.StartChat(history);
            var response = await chat.SendMessage(
                messages.Last().Content,
                _generationConfig,
                null, null, null,
                TimeoutOptions,
                ct);
            var replyText = response.Text ?? "No reply received.";
            var tokensUsed = response.UsageMetadata?.CandidatesTokenCount ?? 0;
            return ClaudeResult.Ok(replyText, tokensUsed);
        }
        catch (StopCandidateException ex)
        {
            _logger.LogWarning(ex, "Gemini stopped early — likely hit MaxOutputTokens");
            return ClaudeResult.Fail("Response was cut off. Try increasing MaxOutputTokens in appsettings.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Gemini API request timed out or was cancelled");
            return ClaudeResult.Fail("The AI service took too long to respond. Please try again.");
        }
        catch (GeminiApiException ex) when (ex.Message.Contains("429") || ex.Message.Contains("RESOURCE_EXHAUSTED"))
        {
            _logger.LogError(ex, "Gemini API quota exceeded");
            return ClaudeResult.Fail("API quota exceeded. Please generate a new API key or enable billing.");
        }
        catch (GeminiApiException ex)
        {
            _logger.LogError(ex, "Gemini API error");
            return ClaudeResult.Fail($"Gemini API error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in conversation");
            return ClaudeResult.Fail("An unexpected error occurred.");
        }
    }
}
