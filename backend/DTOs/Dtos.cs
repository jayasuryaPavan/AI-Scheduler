namespace AiScheduler.Api.DTOs;

// ─────────────────────────────────────────────────────────────
// Dashboard DTOs — serialized to the Vue frontend
// ─────────────────────────────────────────────────────────────

public class ScheduleDashboardDto
{
    public List<WorkOrderDto>       WorkOrders      { get; set; } = new();
    public List<WorkCenterDto>      WorkCenters     { get; set; } = new();
    public List<PurchaseOrderDto>   PurchaseOrders  { get; set; } = new();
    public List<TransferOrderDto>   TransferOrders  { get; set; } = new();
    public DateTimeOffset           GeneratedAt     { get; set; }
}

public class WorkOrderDto
{
    public int                       Id                { get; set; }
    public string                    WorkOrderNumber   { get; set; } = string.Empty;
    public string                    FinishedGoodSku   { get; set; } = string.Empty;
    public int                       Quantity          { get; set; }
    public DateOnly                  DueDate           { get; set; }
    public int                       Priority          { get; set; }
    public string                    Status            { get; set; } = string.Empty;
    public string?                   AgentNotes        { get; set; }
    public List<RequiredMaterialDto> RequiredMaterials { get; set; } = new();
    public List<OperationDto>        Operations        { get; set; } = new();
}

public class RequiredMaterialDto
{
    public string PartNumber { get; set; } = string.Empty;
    public int    Quantity   { get; set; }
}

public class OperationDto
{
    public int     Id                   { get; set; }
    public int     OperationSequence    { get; set; }
    public string  OperationDescription { get; set; } = string.Empty;
    public int     WorkCenterId         { get; set; }
    public string  WorkCenterName       { get; set; } = string.Empty;
    public string  PlantName            { get; set; } = string.Empty;
    public decimal SetupTimeHours       { get; set; }
    public decimal CycleTimePerUnitHours { get; set; }
    public decimal TotalJobHours        { get; set; }
    public bool    SetupWaived          { get; set; }
    public string  Status               { get; set; } = string.Empty;
    public int     RequiredAssociates   { get; set; }
}

public class WorkCenterDto
{
    public int     Id                 { get; set; }
    public string  Name               { get; set; } = string.Empty;
    public string  PlantName          { get; set; } = string.Empty;
    public decimal DailyCapacityHours { get; set; }
    public string  Status             { get; set; } = string.Empty;
    public List<ShiftDto> Shifts      { get; set; } = new();
}

public class ShiftDto
{
    public int     Id             { get; set; }
    public string  ShiftName      { get; set; } = string.Empty;
    public TimeOnly StartTime     { get; set; }
    public TimeOnly EndTime       { get; set; }
    public decimal CapacityHours  { get; set; }
    public decimal ScheduledHours { get; set; }
    public decimal RemainingHours { get; set; }
}

public class ShiftActualDto
{
    public int     WorkOrderOperationId { get; set; }
    public string  ShiftName            { get; set; } = string.Empty;
    public string? TimeFinished         { get; set; }
    public int?    AssociatesWorked     { get; set; }
}

public class PurchaseOrderDto
{
    public int     Id                   { get; set; }
    public string  PartNumber           { get; set; } = string.Empty;
    public string? PartDescription      { get; set; }
    public int     Quantity             { get; set; }
    public DateOnly ExpectedDeliveryDate { get; set; }
    public int     SupplierLeadTimeDays { get; set; }
    public string  Status               { get; set; } = string.Empty;
}

public class TransferOrderDto
{
    public int     Id                   { get; set; }
    public string  PartNumber           { get; set; } = string.Empty;
    public string? PartDescription      { get; set; }
    public int     Quantity             { get; set; }
    public string  SourcePlant          { get; set; } = string.Empty;
    public string  DestinationPlant     { get; set; } = string.Empty;
    public DateOnly ExpectedDeliveryDate { get; set; }
    public string  Status               { get; set; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────
// Agent Tool Result DTOs — returned as Gemini function responses
// ─────────────────────────────────────────────────────────────

public class InventoryManagementResult
{
    public List<InventoryStatusDto> ReorderedItems { get; set; } = new();
    public int                      TotalPOsGenerated { get; set; }
    public DateTimeOffset           Timestamp         { get; set; }
}

public class InventoryStatusDto
{
    public string PartNumber              { get; set; } = string.Empty;
    public int    QuantityOnHand          { get; set; }
    public int    MinStockLevel           { get; set; }
    public int    MaxStockLevel           { get; set; }
    public bool   IsBelowMin              { get; set; }
    public int    ReorderQuantity         { get; set; }
    public int?   GeneratedPurchaseOrderId { get; set; }
}

public class MaterialAvailabilityReport
{
    public List<WorkOrderMaterialStatus> WorkOrderStatuses { get; set; } = new();
    public int                           TotalActive       { get; set; }
    public int                           TotalBlocked      { get; set; }
    public DateTimeOffset                Timestamp         { get; set; }
}

public class WorkOrderMaterialStatus
{
    public int                  WorkOrderId            { get; set; }
    public string               WorkOrderNumber        { get; set; } = string.Empty;
    public string               FinishedGoodSku        { get; set; } = string.Empty;
    public bool                 AllMaterialsAvailable  { get; set; }
    public List<MaterialCheck>  MaterialChecks         { get; set; } = new();
}

public class MaterialCheck
{
    public string PartNumber  { get; set; } = string.Empty;
    public int    Required    { get; set; }
    public int    Available   { get; set; }
    public bool   IsSatisfied { get; set; }
    public string? BlockedBy  { get; set; }
}

public class WorkCenterCapacityReport
{
    public List<WorkCenterCapacityStatus> WorkCenterStatuses { get; set; } = new();
    public bool                           HasDownCenters     { get; set; }
    public DateTimeOffset                 Timestamp          { get; set; }
}

public class WorkCenterCapacityStatus
{
    public int     WorkCenterId       { get; set; }
    public string  Name               { get; set; } = string.Empty;
    public string  Status             { get; set; } = string.Empty;
    public decimal DailyCapacityHours { get; set; }
    public decimal ScheduledHours     { get; set; }
    public decimal UtilizationPercent { get; set; }
    public bool    IsOverCapacity     { get; set; }
    public bool    IsDown             { get; set; }
    public List<ShiftDto> Shifts      { get; set; } = new();
}

public class ScheduleAdjustmentResult
{
    public bool          Success             { get; set; }
    public List<string>  BlockedWorkOrders   { get; set; } = new();
    public List<string>  PromotedWorkOrders  { get; set; } = new();
    public List<string>  Actions             { get; set; } = new();
    public string        Summary             { get; set; } = string.Empty;
    public decimal       SetupTimeSavedHours { get; set; }
    public DateTimeOffset AdjustedAt         { get; set; }
}

// ─────────────────────────────────────────────────────────────
// Agent Run Result — returned to the Vue frontend
// ─────────────────────────────────────────────────────────────

public class AgentRunResult
{
    public bool           Success          { get; set; }
    public string         AgentReasoning   { get; set; } = string.Empty;
    public List<string>   ToolCalls        { get; set; } = new();
    public string?        Error            { get; set; }
    public DateTimeOffset StartedAt        { get; set; }
    public DateTimeOffset CompletedAt      { get; set; }

    // ── Human-in-the-Loop approval fields ─────────────────────
    public bool           RequiresApproval { get; set; }
    public string?        ProposalId       { get; set; }
    public List<string>?  SimulatedImpact  { get; set; }
}

// ─────────────────────────────────────────────────────────────
// Chat-driven mutation result DTOs
// ─────────────────────────────────────────────────────────────

public class PurchaseOrderUpdateResult
{
    public bool    Success         { get; set; }
    public int?    MatchedPoId     { get; set; }
    public string  PartNumber      { get; set; } = string.Empty;
    public string? PartDescription { get; set; }
    public string  PreviousStatus  { get; set; } = string.Empty;
    public string  NewStatus       { get; set; } = string.Empty;
    public string? NewDeliveryDate { get; set; }
    public string  Message         { get; set; } = string.Empty;
}

public class WorkCenterUpdateResult
{
    public bool   Success          { get; set; }
    public int?   MatchedWcId      { get; set; }
    public string Name             { get; set; } = string.Empty;
    public string? PreviousStatus { get; set; }
    public string  NewStatus      { get; set; } = string.Empty;
    public string  Message        { get; set; } = string.Empty;
}

public class WorkOrderPriorityUpdateResult
{
    public bool    Success         { get; set; }
    public int?    MatchedWoId     { get; set; }
    public string? FinishedGoodSku { get; set; }
    public int?    PreviousPriority{ get; set; }
    public int     NewPriority     { get; set; }
    public string  Message         { get; set; } = string.Empty;
}

public class ScheduleMetricsResult
{
    public int TotalLateOrders { get; set; }
    public int TotalDaysLate { get; set; }
    public decimal TotalIdleTimeHours { get; set; }
    public int DownWorkCenters { get; set; }
    public int MaterialShortages { get; set; }
    public string ScoreSummary { get; set; } = string.Empty;
}
