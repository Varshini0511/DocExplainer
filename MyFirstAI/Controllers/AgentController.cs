using Microsoft.AspNetCore.Mvc;
using MyFirstAI.Services;

namespace MyFirstAI.Controllers;

// ============================================================
// AGENT CONTROLLER
// POST /api/agent/ask
// Body: { "question": "your question here" }
//
// The agent will automatically decide which tool to use
// and return a smart answer
// ============================================================

[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly IAgentService _agentService;
    private readonly ILogger<AgentController> _logger;

    public AgentController(IAgentService agentService, ILogger<AgentController> logger)
    {
        _agentService = agentService;
        _logger       = logger;
    }

    // ----------------------------------------------------------
    // POST /api/agent/ask
    // Body: { "question": "What is the refund policy?" }
    //
    // Try these different questions to see the agent pick
    // different tools automatically:
    //
    // "What is the refund policy?"
    //   → Agent calls SearchDocuments
    //
    // "How many document chunks do I have?"
    //   → Agent calls GetChunkCount
    //
    // "Tell me about AI agents and how many docs I have"
    //   → Agent calls BOTH tools in sequence!
    // ----------------------------------------------------------
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "Question cannot be empty" });

        _logger.LogInformation("Agent question received: {Question}", request.Question);

        var answer = await _agentService.AskAgentAsync(request.Question);

        return Ok(new AgentResponse
        {
            Question = request.Question,
            Answer   = answer
        });
    }
}

public class AgentRequest  { public string Question { get; set; } = string.Empty; }
public class AgentResponse
{
    public string Question { get; set; } = string.Empty;
    public string Answer   { get; set; } = string.Empty;
}
