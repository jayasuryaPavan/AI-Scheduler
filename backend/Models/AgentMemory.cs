using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiScheduler.Api.Models;

public class AgentMemory
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string MemoryText { get; set; } = string.Empty;

    /// <summary>768-dimensional text embedding from Vertex AI text-embedding-005.</summary>
    [Column(TypeName = "vector(768)")]
    public Pgvector.Vector? Embedding { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
