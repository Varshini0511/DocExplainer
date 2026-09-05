namespace MyFirstAI.Models;

// ===================================================
// WHAT YOUR API RECEIVES
// ===================================================

// Simple single message: { "message": "What is RAG?" }
public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
}

// Multi-turn conversation with history
// [{ "role": "user", "content": "Hi" }, { "role": "assistant", "content": "Hello!" }]
public class ConversationRequest
{
    public List<ConversationMessage> Messages { get; set; } = new();
}

public class ConversationMessage
{
    public string Role    { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
}


// ===================================================
// WHAT YOUR API RETURNS
// ===================================================

// Success response: { "reply": "Claude's answer", "tokensUsed": 123 }
public class ChatResponse
{
    public string Reply      { get; set; } = string.Empty;
    public int    TokensUsed { get; set; }
}


// ===================================================
// INTERNAL - used between service and controller
// ===================================================

public class ClaudeResult
{
    public bool   Success      { get; set; }
    public string Reply        { get; set; } = string.Empty;
    public int    TokensUsed   { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;

    // Quick helpers to create success/failure results
    public static ClaudeResult Ok(string reply, int tokens) =>
        new() { Success = true, Reply = reply, TokensUsed = tokens };

    public static ClaudeResult Fail(string error) =>
        new() { Success = false, ErrorMessage = error };
}
