using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Npgsql;
using Pgvector;

namespace MyFirstAI.Services;

public interface IAgentService
{
    Task<string> AskAgentAsync(string userQuestion);
}

public class AgentService : IAgentService
{
    private readonly string _geminiApiKey;
    private readonly string _groqApiKey;
    private readonly string _groqModel;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AgentService> _logger;
    private NpgsqlDataSource? _dataSource;

    private const string GroqUrl = "https://api.groq.com/openai/v1/chat/completions";

    // ── ReAct system prompt ───────────────────────────────────────────
    private const string ReactSystemPrompt = """
        You are an AI assistant with access to a document database.
        For every question, reason step by step and use the available tools.

        ALWAYS follow this EXACT format — no exceptions:

        Thought: [your reasoning about what to do next]
        Action: [tool to call]

        OR when you have enough information to answer:

        Thought: [I now have everything I need]
        Final Answer: [your complete answer to the user]

        Available tools:
        - GetChunkCount
          Use when: asked about how many documents, chunks, or files are stored
          Format: Action: GetChunkCount

        - SearchDocuments[query]
          Use when: asked about content, topics, or anything in the documents
          Format: Action: SearchDocuments[your search query here]

        Rules:
        - Always start with Thought:
        - Call only ONE tool per step
        - Wait for the Observation before your next Thought
        - Never make up tool results — always wait for Observation
        - When you have enough info, write Final Answer:
        """;

    public AgentService(
        IConfiguration config,
        HttpClient httpClient,
        ILogger<AgentService> logger)
    {
        _geminiApiKey = config["Gemini:ApiKey"]!;
        _groqApiKey   = config["Groq:ApiKey"]!;
        _groqModel    = config["Groq:Model"] ?? "llama-3.3-70b-versatile";
        _httpClient   = httpClient;
        _logger       = logger;

        var connStr = config["ConnectionStrings:DefaultConnection"]
            ?? "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres123";

        var dsBuilder = new NpgsqlDataSourceBuilder(connStr);
        dsBuilder.UseVector();
        _dataSource = dsBuilder.Build();
    }

    // ── Public entry point ────────────────────────────────────────────

    public async Task<string> AskAgentAsync(string userQuestion)
    {
        // Build conversation history — this grows with each Thought/Action/Observation
        var messages = new List<object>
        {
            new { role = "system", content = ReactSystemPrompt },
            new { role = "user",   content = userQuestion      }
        };

        _logger.LogInformation("ReAct agent started for: {Question}", userQuestion);

        // ── ReAct loop — max 5 steps ──────────────────────────────────
        for (int step = 0; step < 5; step++)
        {
            _logger.LogInformation("ReAct step {Step}", step + 1);

            // Ask Groq to think and decide next action
            var llmResponse = await CallGroqWithHistoryAsync(messages);
            _logger.LogInformation("LLM output: {Response}", llmResponse);

            // ── Check for Final Answer ────────────────────────────────
            if (llmResponse.Contains("Final Answer:"))
            {
                var answer = llmResponse
                    .Split("Final Answer:")[1]
                    .Trim();

                _logger.LogInformation("ReAct finished in {Steps} steps", step + 1);
                return answer;
            }

            // ── Parse and execute Action ──────────────────────────────
            string observation;

            if (llmResponse.Contains("Action: GetChunkCount"))
            {
                _logger.LogInformation("Executing tool: GetChunkCount");
                observation = await GetChunkCountAsync();
            }
            else if (llmResponse.Contains("Action: SearchDocuments["))
            {
                var query = ExtractBetween(llmResponse, "SearchDocuments[", "]");
                _logger.LogInformation("Executing tool: SearchDocuments[{Query}]", query);
                observation = await SearchDocumentsAsync(query);
            }
            else
            {
                // LLM didn't follow the format — treat whole response as answer
                _logger.LogWarning("LLM did not follow ReAct format. Returning raw response.");
                return llmResponse;
            }

            _logger.LogInformation("Observation: {Observation}", observation);

            // ── Add Thought+Action and Observation to history ─────────
            // LLM sees: its own previous output + the tool result
            messages.Add(new { role = "assistant", content = llmResponse              });
            messages.Add(new { role = "user",      content = $"Observation: {observation}" });
        }

        return "Agent reached maximum steps without a final answer.";
    }

    // ── Groq call with full conversation history ──────────────────────

    private async Task<string> CallGroqWithHistoryAsync(List<object> messages)
    {
        var body = new { model = _groqModel, messages };

        var request = new HttpRequestMessage(HttpMethod.Post, GroqUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _groqApiKey);

        var response     = await _httpClient.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Groq error {Status}: {Body}", response.StatusCode, responseText);
            return $"Error: {response.StatusCode}";
        }

        using var doc = JsonDocument.Parse(responseText);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    // ── Helper: extract text between two markers ──────────────────────

    private static string ExtractBetween(string text, string start, string end)
    {
        var startIdx = text.IndexOf(start, StringComparison.Ordinal);
        if (startIdx < 0) return text;
        startIdx += start.Length;

        var endIdx = text.IndexOf(end, startIdx, StringComparison.Ordinal);
        if (endIdx < 0) return text[startIdx..].Trim();

        return text[startIdx..endIdx].Trim();
    }

    // ── Tool: count chunks ────────────────────────────────────────────

    private async Task<string> GetChunkCountAsync()
    {
        try
        {
            await using var conn   = await _dataSource!.OpenConnectionAsync();
            await using var cmd    = new NpgsqlCommand(
                "SELECT COUNT(*), COUNT(DISTINCT filename) FROM document_chunks", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
                return $"There are {reader.GetInt64(0)} document chunks from {reader.GetInt64(1)} file(s) in the database.";

            return "Could not retrieve count.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetChunkCount error");
            return $"Database error: {ex.Message}";
        }
    }

    // ── Tool: semantic search ─────────────────────────────────────────

    private async Task<string> SearchDocumentsAsync(string query)
    {
        try
        {
            var embedding = await GenerateEmbeddingAsync(query);
            if (embedding == null) return "Could not generate embedding.";

            var vector = new Vector(embedding);
            const string sql = """
                SELECT filename, chunk_text,
                       ROUND(CAST((1 - (embedding <=> @q)) * 100 AS numeric), 1) AS similarity
                FROM document_chunks
                ORDER BY embedding <=> @q
                LIMIT 3
                """;

            await using var conn   = await _dataSource!.OpenConnectionAsync();
            await using var cmd    = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("q", vector);

            var results = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add($"[{reader.GetString(0)} — {reader.GetDouble(2)}% match]\n{reader.GetString(1)}");

            return results.Count > 0
                ? string.Join("\n\n", results)
                : "No relevant documents found.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchDocuments error");
            return $"Search error: {ex.Message}";
        }
    }

    // ── Embedding via Gemini ──────────────────────────────────────────

    private async Task<float[]?> GenerateEmbeddingAsync(string text)
    {
        try
        {
            var url  = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent?key={_geminiApiKey}";
            var body = new
            {
                model                = "models/gemini-embedding-001",
                content              = new { parts = new[] { new { text } } },
                output_dimensionality = 768
            };

            var res = await _httpClient.PostAsync(url,
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
            res.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement
                .GetProperty("embedding")
                .GetProperty("values")
                .EnumerateArray()
                .Select(v => v.GetSingle())
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedding error");
            return null;
        }
    }
}
