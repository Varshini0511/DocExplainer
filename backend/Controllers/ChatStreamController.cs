using Microsoft.AspNetCore.Mvc;
using MyFirstAI.Models;
using MyFirstAI.Services;
using System.Text.Json;

namespace MyFirstAI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatStreamController : ControllerBase
{
    private readonly IGeminiService _geminiService;
    private readonly ILogger<ChatStreamController> _logger;

    public ChatStreamController(IGeminiService geminiService, ILogger<ChatStreamController> logger)
    {
        _geminiService = geminiService;
        _logger = logger;
    }

    // POST /api/chatstream
    // Body: { "message": "your question" }
    // Returns: SSE stream — each event is data: {"text":"..."}, ends with data: [DONE]
    [HttpPost]
    public async Task Stream([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("""data: {"error":"Message cannot be empty."}\n\n""", ct);
            return;
        }

        Response.Headers.ContentType   = "text/event-stream";
        Response.Headers.CacheControl  = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        _logger.LogInformation("Stream request received. Message length: {Length}", request.Message.Length);

        try
        {
            await foreach (var chunk in _geminiService.StreamMessageAsync(request.Message, ct))
            {
                var json = JsonSerializer.Serialize(new { text = chunk });
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }

            await Response.WriteAsync("data: [DONE]\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Stream cancelled by client");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during stream");
            var errorJson = JsonSerializer.Serialize(new { error = "An unexpected error occurred." });
            await Response.WriteAsync($"data: {errorJson}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
