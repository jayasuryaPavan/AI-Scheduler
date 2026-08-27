using Microsoft.EntityFrameworkCore;
using AiScheduler.Api.Models;
using System.Text.Json;

namespace AiScheduler.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<PurchaseOrder>      PurchaseOrders      => Set<PurchaseOrder>();
    public DbSet<TransferOrder>      TransferOrders      => Set<TransferOrder>();
    public DbSet<Inventory>          Inventory           => Set<Inventory>();
    public DbSet<WorkCenter>         WorkCenters         => Set<WorkCenter>();
    public DbSet<WorkOrder>          WorkOrders          => Set<WorkOrder>();
    public DbSet<WorkOrderOperation> WorkOrderOperations => Set<WorkOrderOperation>();
    public DbSet<Shift>              Shifts              => Set<Shift>();
    public DbSet<ShiftActual>        ShiftActuals        => Set<ShiftActual>();
    public DbSet<ChatConversation>   ChatConversations   => Set<ChatConversation>();
    public DbSet<AgentMemory>        AgentMemories       => Set<AgentMemory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── WorkOrder ──────────────────────────────────────────────────
        modelBuilder.Entity<WorkOrder>(entity =>
        {
            entity.HasIndex(w => w.WorkOrderNumber).IsUnique();

            entity.Property(w => w.RequiredMaterials)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                    v => JsonSerializer.Deserialize<List<RequiredMaterial>>(v,
                             new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<RequiredMaterial>()
                );

            entity.HasMany(w => w.Operations)
                  .WithOne(o => o.WorkOrder)
                  .HasForeignKey(o => o.WorkOrderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── WorkCenter ────────────────────────────────────────────────
        modelBuilder.Entity<WorkCenter>(entity =>
        {
            entity.HasMany(w => w.Operations)
                  .WithOne(o => o.WorkCenter)
                  .HasForeignKey(o => o.WorkCenterId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Inventory ─────────────────────────────────────────────────
        modelBuilder.Entity<Inventory>(entity =>
        {
            entity.HasIndex(i => i.PartNumber).IsUnique();
        });

        // ── WorkOrderOperation ────────────────────────────────────────
        modelBuilder.Entity<WorkOrderOperation>(entity =>
        {
            entity.HasOne(o => o.WorkOrder)
                  .WithMany(w => w.Operations)
                  .HasForeignKey(o => o.WorkOrderId);

            entity.HasOne(o => o.WorkCenter)
                  .WithMany(w => w.Operations)
                  .HasForeignKey(o => o.WorkCenterId);
        });

        // ── Shift ─────────────────────────────────────────────────────
        modelBuilder.Entity<Shift>(entity =>
        {
            entity.HasOne(s => s.WorkCenter)
                  .WithMany(w => w.Shifts)
                  .HasForeignKey(s => s.WorkCenterId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ChatConversation (pgvector) ───────────────────────────────
        modelBuilder.Entity<ChatConversation>(entity =>
        {
            entity.HasIndex(c => new { c.SessionId, c.CreatedAt });

            entity.Property(c => c.Embedding)
                  .HasColumnType("vector(768)");

            entity.Property(c => c.ToolCalls)
                  .HasColumnType("jsonb");
        });
    }
}
