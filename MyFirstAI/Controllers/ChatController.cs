using Microsoft.AspNetCore.Mvc;
using MyFirstAI.Models;
using MyFirstAI.Services;

namespace MyFirstAI.Controllers;

// ===================================================
// THIS IS YOUR MAIN CONTROLLER
// URL: POST /api/chat
// Send a message, get Claude's reply back as JSON
// ===================================================

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IGeminiService _claudeService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IGeminiService claudeService, ILogger<ChatController> logger)
    {
        _claudeService = claudeService;
        _logger = logger;
    }

    // -------------------------------------------------------
    // POST /api/chat
    // Body: { "message": "your question here" }
    // Returns: { "reply": "Claude's answer", "tokensUsed": 123 }
    // -------------------------------------------------------
    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken ct)
    {
        // 1. Validate the incoming request
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message cannot be empty." });
        }

        _logger.LogInformation("Chat request received. Message length: {Length}", request.Message.Length);

        // 2. Call Claude via the service layer
        var result = await _claudeService.SendMessageAsync(request.Message, ct);

        // 3. Return success or error response
        if (!result.Success)
        {
            _logger.LogError("Claude API error: {Error}", result.ErrorMessage);
            return StatusCode(500, new { error = result.ErrorMessage });
        }

        _logger.LogInformation("Reply received. Tokens used: {Tokens}", result.TokensUsed);

        return Ok(new ChatResponse
        {
            Reply      = result.Reply,
            TokensUsed = result.TokensUsed
        });
    }

    // -------------------------------------------------------
    // POST /api/chat/conversation
    // Body: { "messages": [{ "role": "user", "content": "Hi" }] }
    // Returns: { "reply": "...", "tokensUsed": 123 }
    // Use this for multi-turn conversations (chat history)
    // -------------------------------------------------------
    [HttpPost("conversation")]
    public async Task<IActionResult> Conversation([FromBody] ConversationRequest request, CancellationToken ct)
    {
        if (request.Messages == null || request.Messages.Count == 0)
        {
            return BadRequest(new { error = "Messages list cannot be empty." });
        }

        var result = await _claudeService.SendConversationAsync(request.Messages, ct);

        if (!result.Success)
        {
            return StatusCode(500, new { error = result.ErrorMessage });
        }

        return Ok(new ChatResponse
        {
            Reply      = result.Reply,
            TokensUsed = result.TokensUsed
        });
    }
}
