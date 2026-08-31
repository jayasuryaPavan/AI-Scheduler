using AiScheduler.Api.Data;
using AiScheduler.Api.Models;
using Google.Cloud.AIPlatform.V1;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Data.Common;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore.Storage;
using ProtobufValue = Google.Protobuf.WellKnownTypes.Value;

namespace AiScheduler.Api.Services;

public class AgentMemoryHit
{
    public string MemoryText { get; set; } = string.Empty;
    public double Score { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public interface IAgentMemoryService
{
    /// <summary>Store a user preference or rule with its embedding.</summary>
    Task StorePreferenceAsync(string memoryText);

    /// <summary>Retrieve the top-K most similar past preferences using vector search.</summary>
    Task<List<AgentMemoryHit>> RetrieveRelevantPreferencesAsync(string currentContext, int topK = 5);
}

public class AgentMemoryService : IAgentMemoryService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AgentMemoryService> _logger;
    private readonly PredictionServiceClient _predictionClient;
    private readonly string _modelName;

    private const int EmbeddingDimension = 768;

    public AgentMemoryService(
        AppDbContext db,
        ILogger<AgentMemoryService> logger,
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
    }

    public async Task StorePreferenceAsync(string memoryText)
    {
        try
        {
            float[]? embedding = await GenerateEmbeddingAsync(memoryText);

            var entry = new AgentMemory
            {
                MemoryText = memoryText,
                Embedding  = embedding != null ? new Pgvector.Vector(embedding) : null,
                CreatedAt  = DateTimeOffset.UtcNow
            };

            _db.AgentMemories.Add(entry);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Stored agent memory: '{Memory}'", memoryText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store agent memory");
        }
    }

    public async Task<List<AgentMemoryHit>> RetrieveRelevantPreferencesAsync(string currentContext, int topK = 5)
    {
        try
        {
            float[]? queryEmbedding = await GenerateEmbeddingAsync(currentContext);
            if (queryEmbedding is null)
                return new List<AgentMemoryHit>();

            var embeddingStr = $"[{string.Join(",", queryEmbedding)}]";
            var hits = new List<AgentMemoryHit>();

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            if (_db.Database.CurrentTransaction != null)
                cmd.Transaction = _db.Database.CurrentTransaction.GetDbTransaction();

            if (_db.Database.CurrentTransaction != null)
                await _db.Database.CurrentTransaction.CreateSavepointAsync("agent_mem_sp");
                
            cmd.CommandText = """
                SELECT "MemoryText", "CreatedAt",
                       1 - ("Embedding" <=> @embedding::vector) AS score
                FROM "AgentMemories"
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
                hits.Add(new AgentMemoryHit
                {
                    MemoryText = reader.GetString(0),
                    CreatedAt  = reader.GetDateTime(1), // Actually it's DateTimeOffset in the DB, but reader might return DateTime
                    Score      = reader.GetDouble(2)
                });
            }

            if (_db.Database.CurrentTransaction != null)
                await _db.Database.CurrentTransaction.ReleaseSavepointAsync("agent_mem_sp");

            return hits;
        }
        catch (Exception ex)
        {
            if (_db.Database.CurrentTransaction != null)
                await _db.Database.CurrentTransaction.RollbackToSavepointAsync("agent_mem_sp");

            _logger.LogWarning(ex, "Failed to retrieve relevant agent memories");
            return new List<AgentMemoryHit>();
        }
    }

    private async Task<float[]?> GenerateEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var instance = new Struct();
        instance.Fields["content"] = ProtobufValue.ForString(text);

        var request = new PredictRequest
        {
            Endpoint = _modelName,
            Instances = { ProtobufValue.ForStruct(instance) }
        };

        var response = await _predictionClient.PredictAsync(request);

        var predictions = response.Predictions;
        if (predictions.Count == 0) return null;

        var embeddingsList = predictions[0]
            .StructValue.Fields["embeddings"]
            .StructValue.Fields["values"]
            .ListValue.Values;

        float[] result = new float[embeddingsList.Count];
        for (int i = 0; i < embeddingsList.Count; i++)
        {
            result[i] = (float)embeddingsList[i].NumberValue;
        }

        return result;
    }
}
