using AiScheduler.Api.DTOs;
using AiScheduler.Api.Models;

namespace AiScheduler.Api.Services;

public interface ISchedulingService
{
    // ── Dashboard ─────────────────────────────────────────────
    Task<ScheduleDashboardDto> GetActiveScheduleAsync(string? plant = null);
    Task<List<ShiftActualDto>> GetShiftActualsAsync();
    Task SaveShiftActualsAsync(List<ShiftActualDto> actuals);

    // ── Individual lookups (used by controllers) ──────────────
    Task<PurchaseOrder?> GetPurchaseOrderAsync(int id);
    Task<WorkCenter?>    GetWorkCenterAsync(int id);

    // ── Status mutations ──────────────────────────────────────
    Task<bool> UpdatePurchaseOrderStatusAsync(int id, string status, string? expectedDeliveryDate = null);
    Task<bool> UpdateWorkCenterStatusAsync(int id, string status);
    Task<bool> UpdateWorkOrderStatusAsync(int id, string status);
    Task<bool> UpdateOperationStatusAsync(int operationId, string status);

    // ── Agent Tool implementations ────────────────────────────
    Task<MaterialAvailabilityReport>  AssessMaterialAvailabilityAsync();
    Task<WorkCenterCapacityReport>    EvaluateWorkCenterCapacityAsync();
    Task<ScheduleAdjustmentResult>    ExecuteScheduleAdjustmentAsync();
    Task<InventoryManagementResult>   ManageInventoryLevelsAsync();

    // ── Chat-driven mutation tools (fuzzy match by description) ──
    Task<PurchaseOrderUpdateResult>   UpdatePurchaseOrderByDescriptionAsync(string description, string status, int delayDays = 0);
    Task<WorkCenterUpdateResult>      UpdateWorkCenterByNameAsync(string name, string status);
    Task<WorkOrderPriorityUpdateResult> UpdateWorkOrderPriorityByDescriptionAsync(string description, int priority);

    // ── Raw SQL tool ─────────────────────────────────────────
    Task<string> ExecuteRawSqlAsync(string sql);

    // ── Self-Reflection & Iteration (Tree of Thoughts) tools ──
    Task<ScheduleMetricsResult> EvaluateScheduleMetricsAsync();
    Task<string> CreateSavepointAsync(string savepointName);
    Task<string> RollbackToSavepointAsync(string savepointName);

    // ── Demo helpers ──────────────────────────────────────────
    Task<bool> ResetSimulationAsync();
    // ── External APIs (Mock) ──────────────────────────────────
    Task<string> CheckShipmentTrackingAsync(string trackingNumberOrDescription);
    Task<string> CheckWeatherForecastAsync(string location);
}
