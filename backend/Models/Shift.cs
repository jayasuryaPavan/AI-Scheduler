using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiScheduler.Api.Models;

/// <summary>Represents a specific time block of capacity for a Work Center.</summary>
public class Shift
{
    [Key]
    public int Id { get; set; }

    public int WorkCenterId { get; set; }
    public WorkCenter WorkCenter { get; set; } = null!;

    [Required, MaxLength(50)]
    public string ShiftName { get; set; } = string.Empty;

    public TimeOnly StartTime { get; set; }
    
    public TimeOnly EndTime { get; set; }

    public decimal CapacityHours { get; set; }
}
