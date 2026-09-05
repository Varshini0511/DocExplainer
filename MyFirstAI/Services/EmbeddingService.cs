using Npgsql;
using Pgvector;
using System.Text.Json;

namespace MyFirstAI.Services;

// ============================================================
// WHAT THIS DOES:
// 1. Takes a piece of text (a chunk of your document)
// 2. Sends it to Gemini embedding model
// 3. Gets back 768 numbers (the embedding, reduced from 3072)
// 4. Saves those numbers to your pgvector Postgres table
//
// This is the heart of RAG — without this, no semantic search
// ============================================================

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text);
    Task SaveChunkAsync(string filename, string chunkText, float[] embedding, int chunkIndex);
    Task<List<DocumentChunk>> SearchSimilarChunksAsync(float[] queryEmbedding, int topK = 5, string? filename = null);
}

public class EmbeddingService : IEmbeddingService
{
    private readonly string _apiKey;
    private readonly NpgsqlDataSource _dataSource;
    private readonly HttpClient _httpClient;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        IConfiguration config,
        HttpClient httpClient,
        ILogger<EmbeddingService> logger)
    {
        _apiKey = config["Gemini:ApiKey"]
            ?? throw new InvalidOperationException("Gemini API key not found");

        // Connection string for your Docker pgvector database
        // Port 5433 = the Docker container we created
        var connectionString = config["ConnectionStrings:DefaultConnection"]
            ?? "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres123";

        // Npgsql 8.x: register vector type on the data source, not the connection
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();

        _httpClient = httpClient;
        _logger = logger;
    }

    // ----------------------------------------------------------
    // STEP 1: Generate embedding
    // Send text to Gemini → get back 768 numbers (reduced from 3072 via output_dimensionality)
    // pgvector HNSW/IVFFlat indexes support max 2000 dimensions, so we cap at 768
    // ----------------------------------------------------------
    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        _logger.LogInformation("Generating embedding for text of length: {Length}", text.Length);

        // Gemini embedding API endpoint
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent?key={_apiKey}";

        // Build the request body
        // output_dimensionality reduces 3072 → 768 using MRL (Matryoshka Representation Learning)
        var requestBody = new
        {
            model = "models/gemini-embedding-001",
            content = new
            {
                parts = new[]
                {
                    new { text = text }
                }
            },
            output_dimensionality = 768
        };

        var json    = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Call Gemini embedding API
        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();

        // Parse the 3072 numbers from the response
        using var doc   = JsonDocument.Parse(responseJson);
        var valuesArray = doc.RootElement
            .GetProperty("embedding")
            .GetProperty("values")
            .EnumerateArray()
            .Select(v => v.GetSingle())
            .ToArray();

        _logger.LogInformation("Embedding generated: {Count} dimensions", valuesArray.Length);

        return valuesArray;
    }

    // ----------------------------------------------------------
    // STEP 2: Save chunk to database
    // Store the text AND its 768 numbers in pgvector table
    // ----------------------------------------------------------
    public async Task SaveChunkAsync(
        string filename,
        string chunkText,
        float[] embedding,
        int chunkIndex)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();

        var vector = new Vector(embedding);

        var sql = """
            INSERT INTO document_chunks (filename, chunk_text, embedding, chunk_index)
            VALUES (@filename, @chunkText, @embedding, @chunkIndex)
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("filename",   filename);
        cmd.Parameters.AddWithValue("chunkText",  chunkText);
        cmd.Parameters.AddWithValue("embedding",  vector);
        cmd.Parameters.AddWithValue("chunkIndex", chunkIndex);

        await cmd.ExecuteNonQueryAsync();

        _logger.LogInformation("Saved chunk {Index} from {File}", chunkIndex, filename);
    }

    // ----------------------------------------------------------
    // STEP 3: Search for similar chunks
    // Given a question's embedding, find the 5 most similar chunks
    // This is the RETRIEVAL part of RAG
    // ----------------------------------------------------------
    public async Task<List<DocumentChunk>> SearchSimilarChunksAsync(
        float[] queryEmbedding,
        int topK = 5,
        string? filename = null)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();

        var vector = new Vector(queryEmbedding);

        // Filter by filename when provided so only that document is searched
        var sql = filename is not null
            ? """
              SELECT filename, chunk_text, chunk_index,
                     embedding <=> @queryEmbedding AS distance
              FROM document_chunks
              WHERE filename = @filename
              ORDER BY embedding <=> @queryEmbedding
              LIMIT @topK
              """
            : """
              SELECT filename, chunk_text, chunk_index,
                     embedding <=> @queryEmbedding AS distance
              FROM document_chunks
              ORDER BY embedding <=> @queryEmbedding
              LIMIT @topK
              """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("queryEmbedding", vector);
        cmd.Parameters.AddWithValue("topK",           topK);
        if (filename is not null)
            cmd.Parameters.AddWithValue("filename", filename);

        var chunks = new List<DocumentChunk>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            chunks.Add(new DocumentChunk
            {
                Filename   = reader.GetString(0),
                ChunkText  = reader.GetString(1),
                ChunkIndex = reader.GetInt32(2),
                Distance   = reader.GetDouble(3)
            });
        }

        _logger.LogInformation("Found {Count} similar chunks", chunks.Count);
        return chunks;
    }
}

// Simple model to hold a retrieved chunk
public class DocumentChunk
{
    public string Filename   { get; set; } = string.Empty;
    public string ChunkText  { get; set; } = string.Empty;
    public int    ChunkIndex { get; set; }
    public double Distance   { get; set; } // 0 = identical, 1 = completely different
}
