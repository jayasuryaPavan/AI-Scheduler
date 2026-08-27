using System.ComponentModel.DataAnnotations;

namespace AiScheduler.Api.Models;

/// <summary>Tracks inbound raw material Purchase Orders.</summary>
public class PurchaseOrder
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string PartNumber { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? PartDescription { get; set; }

    public int Quantity { get; set; }

    public DateOnly ExpectedDeliveryDate { get; set; }

    public int SupplierLeadTimeDays { get; set; }

    /// <summary>Pending | Received | Delayed</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = PoStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class PoStatus
{
    public const string Pending     = "Pending";
    public const string Received    = "Received";
    public const string Delayed     = "Delayed";
    public const string InTransit   = "In-Transit";
    public const string OutOfStock  = "Out of Stock";
}
