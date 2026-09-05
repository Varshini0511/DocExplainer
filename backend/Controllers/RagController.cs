using Microsoft.AspNetCore.Mvc;
using MyFirstAI.Services;
using System.Text;
using System.Text.Json;

namespace MyFirstAI.Controllers;

// ============================================================
// THE COMPLETE RAG PIPELINE IN ONE ENDPOINT
//
// POST /api/rag/ask
// Body: { "question": "How do I get a refund?" }
//
// What happens inside:
// 1. Convert question to 768 numbers (embedding)
// 2. Search pgvector for similar chunks
// 3. Build a prompt with those chunks as context
// 4. Send to Gemini
// 5. Return Gemini's answer
//
// This is the complete RAG loop — Retrieval + Augmented + Generation
// ============================================================

[ApiController]
[Route("api/rag")]
public class RagController : ControllerBase
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<RagController> _logger;

    public RagController(
        IEmbeddingService embeddingService,
        IConfiguration config,
        HttpClient httpClient,
        ILogger<RagController> logger)
    {
        _embeddingService = embeddingService;
        _config           = config;
        _httpClient       = httpClient;
        _logger           = logger;
    }

    // ----------------------------------------------------------
    // POST /api/rag/ask
    // The complete RAG pipeline in one endpoint
    // Body: { "question": "How do I get a refund?" }
    // ----------------------------------------------------------
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] RagRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "Question cannot be empty" });

        _logger.LogInformation("RAG question: {Question}", request.Question);

        // -------------------------------------------------------
        // STEP 1 — RETRIEVAL
        // Convert question to embedding and find similar chunks
        // -------------------------------------------------------
        var questionEmbedding = await _embeddingService
            .GenerateEmbeddingAsync(request.Question);

        var similarChunks = await _embeddingService
            .SearchSimilarChunksAsync(questionEmbedding, topK: 3, filename: request.Filename);

        if (!similarChunks.Any())
        {
            return Ok(new RagResponse
            {
                Question = request.Question,
                Answer   = "I could not find any relevant information in the documents to answer your question.",
                Sources  = new List<string>()
            });
        }

        // -------------------------------------------------------
        // STEP 2 — AUGMENTED
        // Build context string from retrieved chunks
        // This is what gets injected into the prompt
        // -------------------------------------------------------
        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine("CONTEXT FROM DOCUMENTS:");
        contextBuilder.AppendLine("========================");

        foreach (var chunk in similarChunks)
        {
            contextBuilder.AppendLine(chunk.ChunkText);
            contextBuilder.AppendLine("---");
        }

        var context = contextBuilder.ToString();

        _logger.LogInformation("Built context from {Count} chunks", similarChunks.Count);

        // -------------------------------------------------------
        // STEP 3 — GENERATION
        // Send context + question to Gemini and get the answer
        // -------------------------------------------------------
        var answer = await GenerateAnswerAsync(request.Question, context);

        // Return the answer plus which documents were used
        return Ok(new RagResponse
        {
            Question = request.Question,
            Answer   = answer,
            Sources  = similarChunks.Select(c => c.Filename).Distinct().ToList(),
            Chunks   = similarChunks.Select(c => new ChunkInfo
            {
                Text       = c.ChunkText,
                Filename   = c.Filename,
                Similarity = $"{Math.Round((1 - c.Distance) * 100, 1)}%"
            }).ToList()
        });
    }

    // ----------------------------------------------------------
    // POST /api/rag/ingest
    // Save multiple text chunks to the database at once
    // Use this to feed your documents into the system
    // Body: { "filename": "policy.txt", "chunks": ["text1", "text2"] }
    // ----------------------------------------------------------
    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromBody] IngestRequest request)
    {
        if (request.Chunks == null || !request.Chunks.Any())
            return BadRequest(new { error = "No chunks provided" });

        var saved = 0;

        for (int i = 0; i < request.Chunks.Count; i++)
        {
            var chunk = request.Chunks[i];

            if (string.IsNullOrWhiteSpace(chunk)) continue;

            // Generate embedding for this chunk
            var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk);

            // Save to database
            await _embeddingService.SaveChunkAsync(
                filename:   request.Filename ?? "document.txt",
                chunkText:  chunk,
                embedding:  embedding,
                chunkIndex: i
            );

            saved++;
        }

        return Ok(new
        {
            message      = $"Successfully ingested {saved} chunks",
            filename     = request.Filename,
            chunksStored = saved
        });
    }

    // -------------------------------------------------------
    // PRIVATE: Call Gemini with context + question
    // This is the GENERATION step of RAG
    // -------------------------------------------------------
    private async Task<string> GenerateAnswerAsync(string question, string context)
    {
        var apiKey = _config["Gemini:ApiKey"];
        var url    = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

        // This is the key prompt — tell Gemini to ONLY use the context
        // This prevents hallucination — Gemini cannot make things up
        var systemInstruction =
            "You are a helpful assistant that answers questions based ONLY on the " +
            "provided context. If the answer is not in the context, say " +
            "'I could not find that information in the provided documents.' " +
            "Be concise and accurate.";

        var userMessage = $"""
            {context}

            Question: {question}

            Answer based only on the context above:
            """;

        var requestBody = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemInstruction } }
            },
            contents = new[]
            {
                new
                {
                    role  = "user",
                    parts = new[] { new { text = userMessage } }
                }
            }
        };

        var json     = JsonSerializer.Serialize(requestBody);
        var content  = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(responseJson);
        var answer    = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "No answer generated.";

        return answer;
    }
}

// ============================================================
// REQUEST AND RESPONSE MODELS
// ============================================================

public class RagRequest
{
    public string  Question { get; set; } = string.Empty;
    public string? Filename { get; set; } // if set, search only this document
}

public class IngestRequest
{
    public string?       Filename { get; set; }
    public List<string>  Chunks   { get; set; } = new();
}

public class RagResponse
{
    public string       Question { get; set; } = string.Empty;
    public string       Answer   { get; set; } = string.Empty;
    public List<string> Sources  { get; set; } = new();
    public List<ChunkInfo> Chunks { get; set; } = new();
}

public class ChunkInfo
{
    public string Text       { get; set; } = string.Empty;
    public string Filename   { get; set; } = string.Empty;
    public string Similarity { get; set; } = string.Empty;
}
