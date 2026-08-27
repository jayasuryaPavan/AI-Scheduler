using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiScheduler.Api.Models;

/// <summary>Tracks recorded production and associates by shift supervisors.</summary>
[Table("ShiftActuals")]
public class ShiftActual
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int WorkOrderOperationId { get; set; }

    [ForeignKey("WorkOrderOperationId")]
    public WorkOrderOperation Operation { get; set; } = null!;

    [Required, MaxLength(50)]
    public string ShiftName { get; set; } = string.Empty;

    [MaxLength(10)]
    public string? TimeFinished { get; set; }

    public int? AssociatesWorked { get; set; }

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
