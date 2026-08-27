using AiScheduler.Api.Data;
using AiScheduler.Api.Models;
using Google.Cloud.AIPlatform.V1;
using Google.Protobuf.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;

namespace AiScheduler.Api.Services;

/// <summary>
/// A hit from the vector similarity search — a past conversation turn
/// with its cosine similarity score.
/// </summary>
public class ChatMemoryHit
{
    public string Role      { get; set; } = string.Empty;
    public string Content   { get; set; } = string.Empty;
    public string ToolCalls { get; set; } = "[]";
    public double Score     { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Chat history entry for loading full conversation on the frontend.
/// </summary>
public class ChatHistoryEntry
{
    public string  Role      { get; set; } = string.Empty;
    public string  Content   { get; set; } = string.Empty;
    public List<string> Tools { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public bool RequiresApproval { get; set; }
    public string? ProposalId { get; set; }
    public List<string> SimulatedImpact { get; set; } = new();
    public string ApprovalStatus { get; set; } = string.Empty;
}

public interface IChatMemoryService
{
    /// <summary>Store a conversation turn with its embedding.</summary>
    Task StoreConversationTurnAsync(string sessionId, string role, string content, List<string>? toolCalls = null, bool requiresApproval = false, string? proposalId = null, List<string>? simulatedImpact = null, string approvalStatus = "");

    /// <summary>Retrieve the top-K most similar past conversation turns using vector search.</summary>
    Task<List<ChatMemoryHit>> RetrieveSimilarAsync(string query, int topK = 10);

    /// <summary>Load full chat history for a session (for UI display).</summary>
    Task<List<ChatHistoryEntry>> GetSessionHistoryAsync(string sessionId);

    /// <summary>Clear all chat history for a session.</summary>
    Task ClearSessionHistoryAsync(string sessionId);
}

public class ChatMemoryService : IChatMemoryService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ChatMemoryService> _logger;
    private readonly PredictionServiceClient _predictionClient;
    private readonly string _modelName;

    private const int EmbeddingDimension = 768;

    public ChatMemoryService(
        AppDbContext db,
        ILogger<ChatMemoryService> logger,
        IConfiguration configuration)
    {
        _db     = db;
        _logger = logger;

        var projectId = configuration["Gemini:ProjectId"]
                        ?? throw new InvalidOperationException("Gemini:ProjectId is not configured.");
        var location  = configuration["Gemini:Location"] ?? "us-central1";

        _modelName = $"projects/{projectId}/locations/{location}/publishers/google/models/text-embedding-005";

        _predictionClient = new PredictionServiceClientBuilder
        {
            Endpoint = $"{location}-aiplatform.googleapis.com"
        }.Build();

        _logger.LogInformation("ChatMemoryService initialized — embedding model: {Model}", _modelName);
    }

    public async Task StoreConversationTurnAsync(string sessionId, string role, string content, List<string>? toolCalls = null, bool requiresApproval = false, string? proposalId = null, List<string>? simulatedImpact = null, string approvalStatus = "")
    {
        try
        {
            float[]? embedding = await GenerateEmbeddingAsync(content);

            var entry = new ChatConversation
            {
                SessionId = sessionId,
                Role      = role,
                Content   = content,
                ToolCalls = JsonSerializer.Serialize(toolCalls ?? new List<string>()),
                Embedding = embedding != null ? new Pgvector.Vector(embedding) : null,
                RequiresApproval = requiresApproval,
                ProposalId = proposalId,
                SimulatedImpact = JsonSerializer.Serialize(simulatedImpact ?? new List<string>()),
                ApprovalStatus = approvalStatus,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _db.ChatConversations.Add(entry);
            await _db.SaveChangesAsync();

            _logger.LogDebug("Stored {Role} message for session {Session} ({Dims}-dim embedding)",
                role, sessionId, embedding?.Length ?? 0);
        }
        catch (Exception ex)
        {
            // Don't fail the agent run if chat storage fails
            _logger.LogWarning(ex, "Failed to store chat conversation turn");
        }
    }

    public async Task<List<ChatMemoryHit>> RetrieveSimilarAsync(string query, int topK = 10)
    {
        try
        {
            float[]? queryEmbedding = await GenerateEmbeddingAsync(query);
            if (queryEmbedding is null)
                return new List<ChatMemoryHit>();

            // Use raw ADO.NET for pgvector cosine similarity search
            var embeddingStr = $"[{string.Join(",", queryEmbedding)}]";
            var hits = new List<ChatMemoryHit>();

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            if (_db.Database.CurrentTransaction != null)
                cmd.Transaction = _db.Database.CurrentTransaction.GetDbTransaction();
                
            cmd.CommandText = """
                SELECT "Role", "Content", "ToolCalls", "CreatedAt",
                       1 - ("Embedding" <=> @embedding::vector) AS score
                FROM "ChatConversations"
                WHERE "Embedding" IS NOT NULL
                ORDER BY "Embedding" <=> @embedding::vector
                LIMIT @topk
                """;

            var p1 = cmd.CreateParameter();
            p1.ParameterName = "@embedding";
            p1.Value = embeddingStr;
            cmd.Parameters.Add(p1);

            var p2 = cmd.CreateParameter();
            p2.ParameterName = "@topk";
            p2.Value = topK;
            cmd.Parameters.Add(p2);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                hits.Add(new ChatMemoryHit
                {
                    Role      = reader.GetString(0),
                    Content   = reader.GetString(1),
                    ToolCalls = reader.GetString(2),
                    CreatedAt = reader.GetFieldValue<DateTimeOffset>(3),
                    Score     = reader.GetDouble(4)
                });
            }

            _logger.LogDebug("Retrieved {Count} similar messages from chat memory", hits.Count);
            return hits;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve similar chat messages — returning empty");
            return new List<ChatMemoryHit>();
        }
    }

    public async Task<List<ChatHistoryEntry>> GetSessionHistoryAsync(string sessionId)
    {
        var conversations = await _db.ChatConversations
            .Where(c => c.SessionId == sessionId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        return conversations.Select(c => new ChatHistoryEntry
        {
            Role      = c.Role,
            Content   = c.Content,
            Tools     = string.IsNullOrWhiteSpace(c.ToolCalls) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(c.ToolCalls) ?? new(),
            CreatedAt = c.CreatedAt,
            RequiresApproval = c.RequiresApproval,
            ProposalId = c.ProposalId,
            SimulatedImpact = string.IsNullOrWhiteSpace(c.SimulatedImpact) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(c.SimulatedImpact) ?? new(),
            ApprovalStatus = c.ApprovalStatus
        }).ToList();
    }

    public async Task ClearSessionHistoryAsync(string sessionId)
    {
        var entries = await _db.ChatConversations
            .Where(c => c.SessionId == sessionId)
            .ToListAsync();

        _db.ChatConversations.RemoveRange(entries);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Cleared {Count} chat history entries for session {Session}",
            entries.Count, sessionId);
    }

    // ─────────────────────────────────────────────────────────
    // Vertex AI Embeddings
    // ─────────────────────────────────────────────────────────

    private async Task<float[]?> GenerateEmbeddingAsync(string text)
    {
        try
        {
            // Truncate very long texts to avoid exceeding token limits
            if (text.Length > 2048)
                text = text[..2048];

            var request = new PredictRequest
            {
                Endpoint = _modelName,
            };

            var instance = Google.Protobuf.WellKnownTypes.Value.ForStruct(
                new Google.Protobuf.WellKnownTypes.Struct
                {
                    Fields =
                    {
                        ["content"] = Google.Protobuf.WellKnownTypes.Value.ForString(text)
                    }
                });
            request.Instances.Add(instance);

            var response = await _predictionClient.PredictAsync(request);

            var prediction = response.Predictions.FirstOrDefault();
            if (prediction == null)
            {
                _logger.LogWarning("Embedding API returned no predictions");
                return null;
            }

            // Extract the embedding values from the response
            var embeddingValues = prediction.StructValue.Fields["embeddings"]
                .StructValue.Fields["values"]
                .ListValue.Values
                .Select(v => (float)v.NumberValue)
                .ToArray();

            return embeddingValues;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate embedding for text");
            return null;
        }
    }
}
