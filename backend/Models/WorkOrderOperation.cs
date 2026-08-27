using System.ComponentModel.DataAnnotations;

namespace AiScheduler.Api.Models;

/// <summary>
/// A specific routing step within a Work Order.
/// OperationSequence (10, 20, 30) enforces execution order.
/// </summary>
public class WorkOrderOperation
{
    [Key]
    public int Id { get; set; }

    public int WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;

    public int WorkCenterId { get; set; }
    public WorkCenter WorkCenter { get; set; } = null!;

    /// <summary>Routing step number — lower executes first (10 before 20).</summary>
    public int OperationSequence { get; set; }

    [MaxLength(200)]
    public string? OperationDescription { get; set; }

    public decimal SetupTimeHours { get; set; }

    public decimal CycleTimePerUnitHours { get; set; }

    /// <summary>Scheduled | In-Progress | Completed | Blocked</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = OpStatus.Scheduled;
}

public static class OpStatus
{
    public const string Scheduled  = "Scheduled";
    public const string InProgress = "In-Progress";
    public const string Completed  = "Completed";
    public const string Blocked    = "Blocked";
}
