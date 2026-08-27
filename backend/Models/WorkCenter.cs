using System.ComponentModel.DataAnnotations;

namespace AiScheduler.Api.Models;

/// <summary>Represents a machine or labor station with daily capacity.</summary>
public class WorkCenter
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string PlantName { get; set; } = string.Empty;

    public decimal DailyCapacityHours { get; set; }

    public int RequiredAssociatesPerShift { get; set; } = 1;

    /// <summary>Active | Down</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = WcStatus.Active;

    public ICollection<WorkOrderOperation> Operations { get; set; } = new List<WorkOrderOperation>();

    public ICollection<Shift> Shifts { get; set; } = new List<Shift>();
}

public static class WcStatus
{
    public const string Active = "Active";
    public const string Down   = "Down";
}
