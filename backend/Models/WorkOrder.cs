using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiScheduler.Api.Models;

/// <summary>
/// The parent production ticket (Job).
/// RequiredMaterials is a JSONB-backed Bill of Materials list.
/// </summary>
public class WorkOrder
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string WorkOrderNumber { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string FinishedGoodSku { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public DateOnly DueDate { get; set; }

    /// <summary>Lower number = higher priority.</summary>
    public int Priority { get; set; } = 99;

    /// <summary>Scheduled | Blocked | In-Progress | Completed</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = WoStatus.Scheduled;

    /// <summary>JSONB Bill of Materials: [{partNumber, quantity}]</summary>
    [Column(TypeName = "jsonb")]
    public List<RequiredMaterial> RequiredMaterials { get; set; } = new();

    /// <summary>Agent-written explanation of any status change.</summary>
    public string? AgentNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<WorkOrderOperation> Operations { get; set; } = new List<WorkOrderOperation>();
}

/// <summary>A single BOM line-item stored as part of the JSONB array.</summary>
public class RequiredMaterial
{
    public string PartNumber { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public static class WoStatus
{
    public const string Scheduled  = "Scheduled";
    public const string Blocked    = "Blocked";
    public const string InProgress = "In-Progress";
    public const string Completed  = "Completed";
}
