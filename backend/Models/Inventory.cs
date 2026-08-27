using System.ComponentModel.DataAnnotations;

namespace AiScheduler.Api.Models;

/// <summary>Tracks current on-hand raw material stock levels.</summary>
public class Inventory
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string PartNumber { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? PartDescription { get; set; }

    [Required, MaxLength(100)]
    public string PlantName { get; set; } = string.Empty;

    public int QuantityOnHand { get; set; }

    public int MinStockLevel { get; set; }

    public int MaxStockLevel { get; set; }

    [MaxLength(200)]
    public string? SupplierName { get; set; }

    public int LeadTimeDays { get; set; } = 7;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
