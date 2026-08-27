using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiScheduler.Api.Models;

/// <summary>
/// Stores a single chat conversation turn (user or assistant) with its
/// text embedding for vector similarity search via pgvector.
/// </summary>
public class ChatConversation
{
    [Key]
    public int Id { get; set; }

    /// <summary>Persistent user/session identifier for grouping conversations.</summary>
    [Required, MaxLength(100)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>'user' or 'assistant'</summary>
    [Required, MaxLength(20)]
    public string Role { get; set; } = string.Empty;

    /// <summary>The raw message text.</summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>JSON array of tool names used by the agent (empty for user messages).</summary>
    [Column(TypeName = "jsonb")]
    public string ToolCalls { get; set; } = "[]";

    /// <summary>768-dimensional text embedding from Vertex AI text-embedding-005.</summary>
    [Column(TypeName = "vector(768)")]
    public Pgvector.Vector? Embedding { get; set; }

    public bool RequiresApproval { get; set; } = false;

    [MaxLength(20)]
    public string? ProposalId { get; set; }

    [Column(TypeName = "jsonb")]
    public string SimulatedImpact { get; set; } = "[]";

    public string ApprovalStatus { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
