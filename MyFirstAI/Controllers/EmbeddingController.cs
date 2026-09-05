using Microsoft.AspNetCore.Mvc;
using MyFirstAI.Services;

namespace MyFirstAI.Controllers;

// ============================================================
// TEST CONTROLLER
// Use Swagger to test each step of the RAG pipeline
// before building the full thing
// ============================================================

[ApiController]
[Route("api/embedding")]
public class EmbeddingController : ControllerBase
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<EmbeddingController> _logger;

    public EmbeddingController(
        IEmbeddingService embeddingService,
        ILogger<EmbeddingController> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    // ----------------------------------------------------------
    // POST /api/embedding/generate
    // Test: send any text, get back the 768 numbers
    // Body: { "text": "What is the refund policy?" }
    // ----------------------------------------------------------
    [HttpPost("generate")]
    public async Task<IActionResult> GenerateEmbedding([FromBody] EmbedRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text cannot be empty" });

        var embedding = await _embeddingService.GenerateEmbeddingAsync(request.Text);

        return Ok(new
        {
            text            = request.Text,
            dimensions      = embedding.Length,       // Should be 768
            first5Numbers   = embedding.Take(5),      // Just show first 5 so response is not huge
            message         = "Embedding generated successfully"
        });
    }

    // ----------------------------------------------------------
    // POST /api/embedding/save
    // Test: save a text chunk and its embedding to the database
    // Body: { "text": "Customers can return items within 30 days", "filename": "policy.txt" }
    // ----------------------------------------------------------
    [HttpPost("save")]
    public async Task<IActionResult> SaveChunk([FromBody] SaveChunkRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text cannot be empty" });

        // Generate embedding for the text
        var embedding = await _embeddingService.GenerateEmbeddingAsync(request.Text);

        // Save both the text and its embedding to Postgres
        await _embeddingService.SaveChunkAsync(
            filename:   request.Filename ?? "test.txt",
            chunkText:  request.Text,
            embedding:  embedding,
            chunkIndex: 0
        );

        return Ok(new
        {
            message    = "Chunk saved to database successfully",
            filename   = request.Filename,
            textLength = request.Text.Length,
            dimensions = embedding.Length
        });
    }

    // ----------------------------------------------------------
    // POST /api/embedding/search
    // Test: search for chunks similar to your question
    // Body: { "question": "How do I get a refund?" }
    // ----------------------------------------------------------
    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "Question cannot be empty" });

        // Convert question to embedding
        var questionEmbedding = await _embeddingService.GenerateEmbeddingAsync(request.Question);

        // Find similar chunks in the database
        var similarChunks = await _embeddingService.SearchSimilarChunksAsync(questionEmbedding);

        return Ok(new
        {
            question = request.Question,
            results  = similarChunks.Select(c => new
            {
                c.Filename,
                c.ChunkText,
                c.ChunkIndex,
                distance = Math.Round(c.Distance, 4),
                similarity = $"{Math.Round((1 - c.Distance) * 100, 1)}%"
            })
        });
    }
}

// Request models
public class EmbedRequest     { public string Text     { get; set; } = string.Empty; }
public class SaveChunkRequest { public string Text     { get; set; } = string.Empty;
                                public string? Filename { get; set; } }
public class SearchRequest    { public string Question { get; set; } = string.Empty; }
