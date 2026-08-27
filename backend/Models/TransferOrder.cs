using System.ComponentModel.DataAnnotations;

namespace AiScheduler.Api.Models;

/// <summary>Tracks internal material movement between plants.</summary>
public class TransferOrder
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string PartNumber { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? PartDescription { get; set; }

    public int Quantity { get; set; }

    [Required, MaxLength(100)]
    public string SourcePlant { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string DestinationPlant { get; set; } = string.Empty;

    public DateOnly ExpectedDeliveryDate { get; set; }

    /// <summary>Pending | In-Transit | Received</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = ToStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ToStatus
{
    public const string Pending   = "Pending";
    public const string InTransit = "In-Transit";
    public const string Received  = "Received";
}
