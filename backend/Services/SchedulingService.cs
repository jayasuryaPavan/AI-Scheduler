using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using AiScheduler.Api.Data;
using AiScheduler.Api.DTOs;
using AiScheduler.Api.Models;

namespace AiScheduler.Api.Services;

public class SchedulingService : ISchedulingService
{
    private readonly AppDbContext             _db;
    private readonly ILogger<SchedulingService> _logger;

    public SchedulingService(AppDbContext db, ILogger<SchedulingService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────
    // Dashboard
    // ─────────────────────────────────────────────────────────

    public async Task<ScheduleDashboardDto> GetActiveScheduleAsync(string? plant = null)
    {
        var workOrders = await _db.WorkOrders
            .Include(w => w.Operations)
                .ThenInclude(o => o.WorkCenter)
            .OrderBy(w => w.Priority)
            .ToListAsync();

        // If plant filter is active, only return WOs that have at least one operation at a work center in that plant
        if (!string.IsNullOrEmpty(plant))
        {
            workOrders = workOrders
                .Where(w => w.Operations.Any(o => o.WorkCenter?.PlantName == plant))
                .ToList();
        }

        var workCentersQuery = _db.WorkCenters.Include(w => w.Shifts).OrderBy(w => w.Id);
        var workCenters = !string.IsNullOrEmpty(plant)
            ? await workCentersQuery.Where(w => w.PlantName == plant).ToListAsync()
            : await workCentersQuery.ToListAsync();

        var purchaseOrders = await _db.PurchaseOrders
            .OrderByDescending(p => p.Status == PoStatus.Delayed)
            .ThenBy(p => p.ExpectedDeliveryDate)
            .ToListAsync();

        var transferOrders = await _db.TransferOrders
            .OrderByDescending(t => t.Status == ToStatus.Pending)
            .ThenBy(t => t.ExpectedDeliveryDate)
            .ToListAsync();

        // If plant filter active, only show TOs destined for that plant
        if (!string.IsNullOrEmpty(plant))
        {
            transferOrders = transferOrders.Where(t => t.DestinationPlant == plant || t.SourcePlant == plant).ToList();
        }

        var workOrderDtos = workOrders.Select(MapWorkOrder).ToList();
        
        string? lastSku = null;
        foreach (var woDto in workOrderDtos.OrderBy(w => w.Priority))
        {
            if (woDto.Status == WoStatus.Scheduled || woDto.Status == WoStatus.InProgress)
            {
                bool waived = lastSku == woDto.FinishedGoodSku;
                foreach (var op in woDto.Operations)
                {
                    op.SetupWaived = waived;
                    op.TotalJobHours = (op.CycleTimePerUnitHours * woDto.Quantity) + (waived ? 0 : op.SetupTimeHours);
                }
                lastSku = woDto.FinishedGoodSku;
            }
            else
            {
                foreach (var op in woDto.Operations)
                {
                    op.SetupWaived = false;
                    op.TotalJobHours = (op.CycleTimePerUnitHours * woDto.Quantity) + op.SetupTimeHours;
                }
            }
        }

        return new ScheduleDashboardDto
        {
            WorkOrders     = workOrderDtos,
            WorkCenters    = workCenters.Select(MapWorkCenter).ToList(),
            PurchaseOrders = purchaseOrders.Select(MapPurchaseOrder).ToList(),
            TransferOrders = transferOrders.Select(MapTransferOrder).ToList(),
            GeneratedAt    = DateTimeOffset.UtcNow
        };
    }

    // ─────────────────────────────────────────────────────────
    // Lookups
    // ─────────────────────────────────────────────────────────

    public async Task<List<ShiftActualDto>> GetShiftActualsAsync()
    {
        var actuals = await _db.ShiftActuals.ToListAsync();
        return actuals.Select(a => new ShiftActualDto
        {
            WorkOrderOperationId = a.WorkOrderOperationId,
            ShiftName            = a.ShiftName,
            TimeFinished         = a.TimeFinished,
            AssociatesWorked     = a.AssociatesWorked
        }).ToList();
    }

    public async Task SaveShiftActualsAsync(List<ShiftActualDto> dtos)
    {
        foreach (var dto in dtos)
        {
            var actual = await _db.ShiftActuals
                .FirstOrDefaultAsync(a => a.WorkOrderOperationId == dto.WorkOrderOperationId && a.ShiftName == dto.ShiftName);
            
            if (actual == null)
            {
                actual = new ShiftActual
                {
                    WorkOrderOperationId = dto.WorkOrderOperationId,
                    ShiftName = dto.ShiftName,
                    TimeFinished = dto.TimeFinished,
                    AssociatesWorked = dto.AssociatesWorked
                };
                _db.ShiftActuals.Add(actual);
            }
            else
            {
                actual.TimeFinished = dto.TimeFinished;
                actual.AssociatesWorked = dto.AssociatesWorked;
                actual.RecordedAt = DateTimeOffset.UtcNow;
            }
        }
        await _db.SaveChangesAsync();
    }

    public Task<PurchaseOrder?> GetPurchaseOrderAsync(int id) =>
        _db.PurchaseOrders.FindAsync(id).AsTask();

    public Task<WorkCenter?> GetWorkCenterAsync(int id) =>
        _db.WorkCenters.FindAsync(id).AsTask();

    // ─────────────────────────────────────────────────────────
    // Status mutations
    // ─────────────────────────────────────────────────────────

    public async Task<bool> UpdatePurchaseOrderStatusAsync(int id, string status, string? expectedDeliveryDate = null)
    {
        var po = await _db.PurchaseOrders.FindAsync(id);
        if (po is null) return false;
        po.Status = status;
        if (!string.IsNullOrEmpty(expectedDeliveryDate) && DateOnly.TryParse(expectedDeliveryDate, out var date))
        {
            po.ExpectedDeliveryDate = date;
        }
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateWorkCenterStatusAsync(int id, string status)
    {
        var wc = await _db.WorkCenters.FindAsync(id);
        if (wc is null) return false;
        wc.Status = status;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateWorkOrderStatusAsync(int id, string status)
    {
        var wo = await _db.WorkOrders.FindAsync(id);
        if (wo is null) return false;
        wo.Status = status;
        wo.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateOperationStatusAsync(int operationId, string status)
    {
        var op = await _db.WorkOrderOperations
            .Include(o => o.WorkOrder)
                .ThenInclude(w => w.Operations)
            .FirstOrDefaultAsync(o => o.Id == operationId);
        if (op is null) return false;

        op.Status = status;

        // Sequential cascade: when this op is completed, mark the next sequential op as Scheduled (ready)
        if (status == OpStatus.Completed)
        {
            var allOps = op.WorkOrder.Operations.OrderBy(o => o.OperationSequence).ToList();
            var nextOp = allOps.FirstOrDefault(o => o.OperationSequence > op.OperationSequence && o.Status == OpStatus.Blocked);
            if (nextOp != null)
            {
                nextOp.Status = OpStatus.Scheduled;
            }

            // If all operations are completed, mark the work order as completed
            if (allOps.All(o => o.Id == op.Id || o.Status == OpStatus.Completed))
            {
                op.WorkOrder.Status = WoStatus.Completed;
                op.WorkOrder.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        // When starting an operation, mark the work order as In-Progress
        else if (status == OpStatus.InProgress)
        {
            if (op.WorkOrder.Status == WoStatus.Scheduled)
            {
                op.WorkOrder.Status = WoStatus.InProgress;
                op.WorkOrder.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        return true;
    }

    // ─────────────────────────────────────────────────────────
    // Agent Tool 1: Assess Material Availability
    // ─────────────────────────────────────────────────────────

    public async Task<MaterialAvailabilityReport> AssessMaterialAvailabilityAsync()
    {
        var activeWOs = await _db.WorkOrders
            .Where(w => w.Status == WoStatus.Scheduled || w.Status == WoStatus.InProgress || w.Status == WoStatus.Blocked)
            .ToListAsync();

        var inventory   = await _db.Inventory.ToListAsync();
        var receivedPos = await _db.PurchaseOrders
            .Where(p => p.Status == PoStatus.Received)
            .ToListAsync();
        var delayedPos  = await _db.PurchaseOrders
            .Where(p => p.Status == PoStatus.Delayed)
            .ToListAsync();

        // Build effective stock: on-hand + received PO quantities
        var stock = inventory.ToDictionary(i => i.PartNumber, i => i.QuantityOnHand);
        foreach (var po in receivedPos)
        {
            stock[po.PartNumber] = stock.GetValueOrDefault(po.PartNumber, 0) + po.Quantity;
        }

        var report = new MaterialAvailabilityReport { Timestamp = DateTimeOffset.UtcNow };

        foreach (var wo in activeWOs)
        {
            var woStatus = new WorkOrderMaterialStatus
            {
                WorkOrderId     = wo.Id,
                WorkOrderNumber = wo.WorkOrderNumber,
                FinishedGoodSku = wo.FinishedGoodSku
            };

            bool allOk = true;
            foreach (var req in wo.RequiredMaterials)
            {
                int avail       = stock.GetValueOrDefault(req.PartNumber, 0);
                bool satisfied  = avail >= req.Quantity;
                if (!satisfied) allOk = false;

                var delayedPo = delayedPos.FirstOrDefault(p => p.PartNumber == req.PartNumber);

                woStatus.MaterialChecks.Add(new MaterialCheck
                {
                    PartNumber  = req.PartNumber,
                    Required    = req.Quantity,
                    Available   = avail,
                    IsSatisfied = satisfied,
                    BlockedBy   = delayedPo is not null && !satisfied
                                    ? $"PO-{delayedPo.Id} is DELAYED (ETA {delayedPo.ExpectedDeliveryDate:MMM dd})"
                                    : null
                });
            }

            woStatus.AllMaterialsAvailable = allOk;
            if (!allOk) report.TotalBlocked++;
            else report.TotalActive++;

            report.WorkOrderStatuses.Add(woStatus);
        }

        _logger.LogInformation("Material assessment complete: {Active} OK, {Blocked} short",
            report.TotalActive, report.TotalBlocked);

        return report;
    }

    // ─────────────────────────────────────────────────────────
    // Agent Tool 2: Evaluate Work Center Capacity
    // ─────────────────────────────────────────────────────────

    public async Task<WorkCenterCapacityReport> EvaluateWorkCenterCapacityAsync()
    {
        var workCenters   = await _db.WorkCenters.Include(w => w.Shifts).ToListAsync();
        var scheduledOps  = await _db.WorkOrderOperations
            .Include(o => o.WorkOrder)
            .Where(o => o.Status == OpStatus.Scheduled || o.Status == OpStatus.InProgress)
            .ToListAsync();

        var report = new WorkCenterCapacityReport { Timestamp = DateTimeOffset.UtcNow };

        foreach (var wc in workCenters)
        {
            var wcOps          = scheduledOps.Where(o => o.WorkCenterId == wc.Id).ToList();
            
            // Re-calculate TotalScheduledHours including setup time logic
            decimal scheduledHours = 0;
            string? lastSku = null;
            
            foreach (var op in wcOps.OrderBy(o => o.WorkOrder.DueDate).ThenBy(o => o.WorkOrderId))
            {
                decimal opHours = op.CycleTimePerUnitHours * op.WorkOrder.Quantity;
                if (lastSku != op.WorkOrder.FinishedGoodSku)
                {
                    opHours += op.SetupTimeHours;
                }
                scheduledHours += opHours;
                lastSku = op.WorkOrder.FinishedGoodSku;
            }
            
            decimal dailyCapacity = wc.Shifts.Any() ? wc.Shifts.Sum(s => s.CapacityHours) : wc.DailyCapacityHours;

            var util           = dailyCapacity > 0
                                   ? Math.Round(scheduledHours / dailyCapacity * 100m, 1)
                                   : 0m;

            bool isDown = wc.Status == WcStatus.Down;
            if (isDown) report.HasDownCenters = true;

            var status = new WorkCenterCapacityStatus
            {
                WorkCenterId       = wc.Id,
                Name               = wc.Name,
                Status             = wc.Status,
                DailyCapacityHours = dailyCapacity,
                ScheduledHours     = scheduledHours,
                UtilizationPercent = util,
                IsOverCapacity     = scheduledHours > dailyCapacity,
                IsDown             = isDown
            };
            
            // Distribute scheduled hours across shifts
            decimal remainingHoursToSchedule = scheduledHours;
            foreach (var shift in wc.Shifts.OrderBy(s => s.StartTime))
            {
                decimal hoursInShift = Math.Min(shift.CapacityHours, remainingHoursToSchedule);
                remainingHoursToSchedule -= hoursInShift;
                if (remainingHoursToSchedule < 0) remainingHoursToSchedule = 0;
                
                status.Shifts.Add(new ShiftDto
                {
                    Id = shift.Id,
                    ShiftName = shift.ShiftName,
                    StartTime = shift.StartTime,
                    EndTime = shift.EndTime,
                    CapacityHours = shift.CapacityHours,
                    ScheduledHours = hoursInShift,
                    RemainingHours = shift.CapacityHours - hoursInShift
                });
            }

            report.WorkCenterStatuses.Add(status);
        }

        _logger.LogInformation("Capacity assessment: {Count} work centers, hasDown={HasDown}",
            report.WorkCenterStatuses.Count, report.HasDownCenters);

        return report;
    }

    // ─────────────────────────────────────────────────────────
    // Agent Tool 3: Execute Schedule Adjustment
    // ─────────────────────────────────────────────────────────

    public async Task<ScheduleAdjustmentResult> ExecuteScheduleAdjustmentAsync()
    {
        var result = new ScheduleAdjustmentResult { AdjustedAt = DateTimeOffset.UtcNow };

        var delayedPos    = await _db.PurchaseOrders.Where(p => p.Status == PoStatus.Delayed).ToListAsync();
        var downWCs       = await _db.WorkCenters.Where(w => w.Status == WcStatus.Down).ToListAsync();
        var downWcIds     = downWCs.Select(w => w.Id).ToHashSet();

        var workOrders    = await _db.WorkOrders
            .Include(w => w.Operations)
            .Where(w => w.Status != WoStatus.Completed && w.Status != WoStatus.InProgress)
            .ToListAsync();

        var inventory     = await _db.Inventory.ToListAsync();
        var receivedPos   = await _db.PurchaseOrders.Where(p => p.Status == PoStatus.Received).ToListAsync();

        // Effective stock map
        var stock = inventory.ToDictionary(i => i.PartNumber, i => i.QuantityOnHand);
        foreach (var po in receivedPos)
            stock[po.PartNumber] = stock.GetValueOrDefault(po.PartNumber, 0) + po.Quantity;

        // ── Pass 1: Determine which WOs must be blocked or can be unblocked ──
        foreach (var wo in workOrders)
        {
            bool shouldBlock     = false;
            string blockReason   = string.Empty;

            // Check BOM against stock
            foreach (var mat in wo.RequiredMaterials)
            {
                int avail = stock.GetValueOrDefault(mat.PartNumber, 0);
                if (avail < mat.Quantity)
                {
                    var dp = delayedPos.FirstOrDefault(p => p.PartNumber == mat.PartNumber);
                    if (dp is not null)
                    {
                        shouldBlock  = true;
                        blockReason  = $"Material '{mat.PartNumber}' required: {mat.Quantity}, available: {avail} — PO-{dp.Id} is DELAYED";
                        break;
                    }
                }
            }

            // Check if any required (not-yet-complete) operation targets a down Work Center
            if (!shouldBlock)
            {
                var affectedOp = wo.Operations
                    .Where(o => o.Status == OpStatus.Scheduled)
                    .FirstOrDefault(o => downWcIds.Contains(o.WorkCenterId));

                if (affectedOp is not null)
                {
                    var wcName   = downWCs.First(w => w.Id == affectedOp.WorkCenterId).Name;
                    shouldBlock  = true;
                    blockReason  = $"Operation #{affectedOp.OperationSequence} requires '{wcName}' which is DOWN";
                }
            }

            // Transition: Scheduled → Blocked
            if (shouldBlock && wo.Status == WoStatus.Scheduled)
            {
                wo.Status      = WoStatus.Blocked;
                var newNote = $"[AGENT {DateTimeOffset.UtcNow:u}] BLOCKED — {blockReason}";
                wo.AgentNotes = string.IsNullOrEmpty(wo.AgentNotes) ? newNote : wo.AgentNotes + "\n" + newNote;
                
                wo.UpdatedAt   = DateTimeOffset.UtcNow;

                foreach (var op in wo.Operations.Where(o => o.Status == OpStatus.Scheduled))
                    op.Status = OpStatus.Blocked;

                result.BlockedWorkOrders.Add(wo.WorkOrderNumber);
                result.Actions.Add($"BLOCK   {wo.WorkOrderNumber}: {blockReason}");
            }
            // Transition: Blocked → Scheduled (conditions resolved)
            else if (!shouldBlock && wo.Status == WoStatus.Blocked)
            {
                wo.Status      = WoStatus.Scheduled;
                var newNote = $"[AGENT {DateTimeOffset.UtcNow:u}] UNBLOCKED — all constraints resolved";
                wo.AgentNotes = string.IsNullOrEmpty(wo.AgentNotes) ? newNote : wo.AgentNotes + "\n" + newNote;
                
                wo.UpdatedAt   = DateTimeOffset.UtcNow;

                foreach (var op in wo.Operations.Where(o => o.Status == OpStatus.Blocked))
                    op.Status = OpStatus.Scheduled;

                result.PromotedWorkOrders.Add(wo.WorkOrderNumber);
                result.Actions.Add($"PROMOTE {wo.WorkOrderNumber}: constraints resolved — returned to queue");
            }
        }

        // ── Pass 2: Re-sequence priority of schedulable Work Orders by due date ──
        var schedulable = workOrders
            .Where(w => w.Status == WoStatus.Scheduled)
            .OrderBy(w => w.DueDate)
            .ThenBy(w => w.Id)
            .ToList();

        decimal setupTimeSaved = 0m;
        string? lastSku = null;

        for (int i = 0; i < schedulable.Count; i++)
        {
            var wo          = schedulable[i];
            int newPriority = i + 1;
            if (wo.Priority != newPriority)
            {
                wo.Priority   = newPriority;
                wo.UpdatedAt  = DateTimeOffset.UtcNow;
                result.Actions.Add($"REPRIO  {wo.WorkOrderNumber}: promoted to queue position {newPriority} (due {wo.DueDate:MMM dd})");
            }
            
            if (lastSku == wo.FinishedGoodSku)
            {
                // Setup time waived for consecutive same SKU jobs
                decimal savedForWo = wo.Operations.Sum(o => o.SetupTimeHours);
                if (savedForWo > 0)
                {
                    setupTimeSaved += savedForWo;
                    result.Actions.Add($"OPTIMIZE {wo.WorkOrderNumber}: Back-to-back SKU {wo.FinishedGoodSku}, waived {savedForWo}h setup time");
                }
            }
            lastSku = wo.FinishedGoodSku;
        }

        await _db.SaveChangesAsync();

        result.Success = true;
        result.SetupTimeSavedHours = setupTimeSaved;
        result.Summary = result.Actions.Count > 0
            ? $"Adjusted {result.BlockedWorkOrders.Count} blocked, promoted {result.PromotedWorkOrders.Count}. {schedulable.Count} WOs now schedulable. Saved {setupTimeSaved}h setup time."
            : "Schedule is already optimal — no adjustments required.";

        _logger.LogInformation("Schedule adjustment: {Blocked} blocked, {Promoted} promoted",
            result.BlockedWorkOrders.Count, result.PromotedWorkOrders.Count);

        return result;
    }

    // ─────────────────────────────────────────────────────────
    // Agent Tool 4: Manage Inventory Levels
    // ─────────────────────────────────────────────────────────

    public async Task<InventoryManagementResult> ManageInventoryLevelsAsync()
    {
        var inventory = await _db.Inventory.ToListAsync();
        var pendingPos = await _db.PurchaseOrders
            .Where(p => p.Status == PoStatus.Pending || p.Status == PoStatus.Delayed)
            .ToListAsync();

        var result = new InventoryManagementResult { Timestamp = DateTimeOffset.UtcNow };

        foreach (var item in inventory)
        {
            // Calculate effective inventory (on-hand + inbound pending)
            int inboundQuantity = pendingPos.Where(p => p.PartNumber == item.PartNumber).Sum(p => p.Quantity);
            int effectiveQuantity = item.QuantityOnHand + inboundQuantity;

            if (effectiveQuantity < item.MinStockLevel)
            {
                int reorderQty = item.MaxStockLevel - effectiveQuantity;
                
                // Draft new Purchase Order
                var newPo = new PurchaseOrder
                {
                    PartNumber = item.PartNumber,
                    PartDescription = item.PartDescription,
                    Quantity = reorderQty,
                    ExpectedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(item.LeadTimeDays)),
                    SupplierLeadTimeDays = item.LeadTimeDays,
                    Status = PoStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _db.PurchaseOrders.Add(newPo);
                await _db.SaveChangesAsync(); // Save immediately to get the generated ID

                result.ReorderedItems.Add(new InventoryStatusDto
                {
                    PartNumber = item.PartNumber,
                    QuantityOnHand = item.QuantityOnHand,
                    MinStockLevel = item.MinStockLevel,
                    MaxStockLevel = item.MaxStockLevel,
                    IsBelowMin = true,
                    ReorderQuantity = reorderQty,
                    GeneratedPurchaseOrderId = newPo.Id
                });
                result.TotalPOsGenerated++;
            }
            else
            {
                result.ReorderedItems.Add(new InventoryStatusDto
                {
                    PartNumber = item.PartNumber,
                    QuantityOnHand = item.QuantityOnHand,
                    MinStockLevel = item.MinStockLevel,
                    MaxStockLevel = item.MaxStockLevel,
                    IsBelowMin = false,
                    ReorderQuantity = 0,
                    GeneratedPurchaseOrderId = null
                });
            }
        }

        if (result.TotalPOsGenerated > 0)
        {
            _logger.LogInformation("Generated {Count} Purchase Orders for low inventory items", result.TotalPOsGenerated);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────
    // Agent Tool 5: Update Purchase Order by Description (Chat)
    // ─────────────────────────────────────────────────────────

    public async Task<PurchaseOrderUpdateResult> UpdatePurchaseOrderByDescriptionAsync(
        string description, string status, int delayDays = 0)
    {
        var searchTerm = description.Trim().ToLower();
        var matches = await _db.PurchaseOrders
            .Where(p => (p.PartDescription != null && p.PartDescription.ToLower().Contains(searchTerm))
                     || p.PartNumber.ToLower().Contains(searchTerm))
            .ToListAsync();

        if (matches.Count == 0)
        {
            return new PurchaseOrderUpdateResult
            {
                Success = false,
                Message = $"No Purchase Order found matching '{description}'. Please provide a more specific part description or part number."
            };
        }

        // Use the first match (most common scenario for chat)
        var po = matches.First();
        var previousStatus = po.Status;
        po.Status = status;

        if (delayDays > 0)
        {
            po.ExpectedDeliveryDate = po.ExpectedDeliveryDate.AddDays(delayDays);
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Chat update: PO-{Id} '{Desc}' status {Old} → {New}, delay +{Days}d",
            po.Id, po.PartDescription, previousStatus, status, delayDays);

        return new PurchaseOrderUpdateResult
        {
            Success         = true,
            MatchedPoId     = po.Id,
            PartNumber      = po.PartNumber,
            PartDescription = po.PartDescription,
            PreviousStatus  = previousStatus,
            NewStatus       = status,
            NewDeliveryDate = delayDays > 0 ? po.ExpectedDeliveryDate.ToString("yyyy-MM-dd") : null,
            Message         = $"Updated PO-{po.Id:D3} ({po.PartDescription ?? po.PartNumber}) from {previousStatus} to {status}."
                            + (delayDays > 0 ? $" Delivery date pushed to {po.ExpectedDeliveryDate:MMM dd}." : "")
        };
    }

    // ─────────────────────────────────────────────────────────
    // Agent Tool 6: Update Work Center by Name (Chat)
    // ─────────────────────────────────────────────────────────

    public async Task<WorkCenterUpdateResult> UpdateWorkCenterByNameAsync(
        string name, string status)
    {
        var searchTerm = name.Trim().ToLower();
        var matches = await _db.WorkCenters
            .Where(w => w.Name.ToLower().Contains(searchTerm))
            .ToListAsync();

        if (matches.Count == 0)
        {
            return new WorkCenterUpdateResult
            {
                Success = false,
                Message = $"No Work Center found matching '{name}'. Available work centers can be checked with the evaluate_work_center_capacity tool."
            };
        }

        var wc = matches.First();
        var previousStatus = wc.Status;
        wc.Status = status;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Chat update: WC-{Id} '{Name}' status {Old} → {New}",
            wc.Id, wc.Name, previousStatus, status);

        return new WorkCenterUpdateResult
        {
            Success        = true,
            MatchedWcId    = wc.Id,
            Name           = wc.Name,
            PreviousStatus = previousStatus,
            NewStatus      = status,
            Message        = $"Updated Work Center '{wc.Name}' from {previousStatus} to {status}."
        };
    }


    // ─────────────────────────────────────────────────────────
    // Demo Reset
    // ─────────────────────────────────────────────────────────

    public async Task<bool> ResetSimulationAsync()
    {
        // Reset PO-3 to Pending (demo: was simulated as Delayed)
        var po = await _db.PurchaseOrders.FindAsync(3);
        if (po is not null) po.Status = PoStatus.Pending;

        // Reset Work Center 1 to Active (demo: was simulated as Down)
        var wc = await _db.WorkCenters.FindAsync(1);
        if (wc is not null) wc.Status = WcStatus.Active;

        // Unblock any blocked Work Orders and restore natural priority
        var blockedWOs = await _db.WorkOrders
            .Include(w => w.Operations)
            .Where(w => w.Status == WoStatus.Blocked)
            .OrderBy(w => w.Id)
            .ToListAsync();

        // Find the highest priority of non-blocked, non-completed orders
        int nextPriority = await _db.WorkOrders
            .Where(w => w.Status == WoStatus.Scheduled || w.Status == WoStatus.InProgress)
            .CountAsync() + 1;

        foreach (var wo in blockedWOs)
        {
            wo.Status     = WoStatus.Scheduled;
            wo.AgentNotes = null;
            wo.Priority   = nextPriority++;
            wo.UpdatedAt  = DateTimeOffset.UtcNow;

            foreach (var op in wo.Operations.Where(o => o.Status == OpStatus.Blocked))
                op.Status = OpStatus.Scheduled;
        }

        await _db.SaveChangesAsync();
        return true;
    }

    // ─────────────────────────────────────────────────────────
    // Mapping helpers
    // ─────────────────────────────────────────────────────────

    private static WorkOrderDto MapWorkOrder(WorkOrder wo) => new()
    {
        Id                = wo.Id,
        WorkOrderNumber   = wo.WorkOrderNumber,
        FinishedGoodSku   = wo.FinishedGoodSku,
        Quantity          = wo.Quantity,
        DueDate           = wo.DueDate,
        Priority          = wo.Priority,
        Status            = wo.Status,
        AgentNotes        = wo.AgentNotes,
        RequiredMaterials = wo.RequiredMaterials.Select(m => new RequiredMaterialDto
        {
            PartNumber = m.PartNumber,
            Quantity   = m.Quantity
        }).ToList(),
        Operations = wo.Operations
            .OrderBy(o => o.OperationSequence)
            .Select(o => new OperationDto
            {
                Id                   = o.Id,
                OperationSequence    = o.OperationSequence,
                OperationDescription = o.OperationDescription ?? string.Empty,
                WorkCenterId         = o.WorkCenterId,
                WorkCenterName       = o.WorkCenter?.Name ?? string.Empty,
                PlantName            = o.WorkCenter?.PlantName ?? string.Empty,
                RequiredAssociates   = o.WorkCenter?.RequiredAssociatesPerShift ?? 1,
                SetupTimeHours       = o.SetupTimeHours,
                CycleTimePerUnitHours = o.CycleTimePerUnitHours,
                Status               = o.Status
            }).ToList()
    };

    private static WorkCenterDto MapWorkCenter(WorkCenter wc) => new()
    {
        Id                 = wc.Id,
        Name               = wc.Name,
        PlantName          = wc.PlantName,
        DailyCapacityHours = wc.Shifts.Any() ? wc.Shifts.Sum(s => s.CapacityHours) : wc.DailyCapacityHours,
        Status             = wc.Status,
        Shifts             = wc.Shifts.Select(s => new ShiftDto
        {
            Id = s.Id,
            ShiftName = s.ShiftName,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            CapacityHours = s.CapacityHours
        }).ToList()
    };

    private static PurchaseOrderDto MapPurchaseOrder(PurchaseOrder po) => new()
    {
        Id                   = po.Id,
        PartNumber           = po.PartNumber,
        PartDescription      = po.PartDescription,
        Quantity             = po.Quantity,
        ExpectedDeliveryDate = po.ExpectedDeliveryDate,
        SupplierLeadTimeDays = po.SupplierLeadTimeDays,
        Status               = po.Status
    };

    private static TransferOrderDto MapTransferOrder(TransferOrder to) => new()
    {
        Id                   = to.Id,
        PartNumber           = to.PartNumber,
        PartDescription      = to.PartDescription,
        Quantity             = to.Quantity,
        SourcePlant          = to.SourcePlant,
        DestinationPlant     = to.DestinationPlant,
        ExpectedDeliveryDate = to.ExpectedDeliveryDate,
        Status               = to.Status
    };

    // ─────────────────────────────────────────────────────────
    // Agent Tool 7: Update Work Order Priority by Description (Chat)
    // ─────────────────────────────────────────────────────────

    public async Task<WorkOrderPriorityUpdateResult> UpdateWorkOrderPriorityByDescriptionAsync(
        string description, int priority)
    {
        var normalizedSearch = description.Replace("-", "").Replace(" ", "").Trim().ToLower();
        var matches = await _db.WorkOrders
            .Where(w => w.FinishedGoodSku.Replace("-", "").Replace(" ", "").ToLower().Contains(normalizedSearch) || 
                        w.WorkOrderNumber.Replace("-", "").Replace(" ", "").ToLower().Contains(normalizedSearch))
            .ToListAsync();

        if (matches.Count == 0)
        {
            return new WorkOrderPriorityUpdateResult
            {
                Success = false,
                Message = $"No Work Order found matching '{description}'. Please provide a more specific product name or order number."
            };
        }

        var wo = matches.First();
        var previousPriority = wo.Priority;
        
        // The schedule adjustment algorithm sorts exclusively by DueDate. 
        // To ensure this manual prioritization is respected, we adjust the DueDate.
        // We use extremely old dates to ensure they sort ahead of any natural overdue jobs.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (priority == 1)
        {
            wo.DueDate = DateOnly.MinValue; // Absolute first
        }
        else if (priority == 2)
        {
            wo.DueDate = DateOnly.MinValue.AddDays(1); // Absolute second
        }
        else if (priority == 3)
        {
            wo.DueDate = DateOnly.MinValue.AddDays(2); // Absolute third
        }
        else
        {
            // For lower priorities, we just push it to today + the priority number
            wo.DueDate = today.AddDays(priority); 
        }
        
        wo.Priority = priority;
        var newNote = $"[AGENT {DateTimeOffset.UtcNow:u}] Priority manually updated to {priority}";
        wo.AgentNotes = string.IsNullOrEmpty(wo.AgentNotes) ? newNote : wo.AgentNotes + "\n" + newNote;
        
        await _db.SaveChangesAsync();

        _logger.LogInformation("Chat update: WO-{Id} '{Name}' priority {Old} → {New}",
            wo.Id, wo.FinishedGoodSku, previousPriority, priority);

        return new WorkOrderPriorityUpdateResult
        {
            Success         = true,
            MatchedWoId     = wo.Id,
            FinishedGoodSku = wo.FinishedGoodSku,
            PreviousPriority= previousPriority,
            NewPriority     = priority,
            Message         = $"Updated Work Order {wo.WorkOrderNumber} ({wo.FinishedGoodSku}) priority from {previousPriority} to {priority}."
        };
    }

    // ─────────────────────────────────────────────────────────
    // Raw SQL Execution
    // ─────────────────────────────────────────────────────────

    public async Task<string> ExecuteRawSqlAsync(string sql)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (_db.Database.CurrentTransaction != null)
            cmd.Transaction = _db.Database.CurrentTransaction.GetDbTransaction();

        try 
        {
            if (sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = await cmd.ExecuteReaderAsync();
                var results = new List<Dictionary<string, object>>();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var val = reader.GetValue(i);
                        row[reader.GetName(i)] = val == DBNull.Value ? null : val;
                    }
                    results.Add(row);
                }
                return System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                int affected = await cmd.ExecuteNonQueryAsync();
                return $"{affected} rows affected.";
            }
        }
        catch (Exception ex)
        {
            return $"Error executing SQL: {ex.Message}";
        }
    }

    // ─────────────────────────────────────────────────────────
    // Self-Reflection & Iteration (Tree of Thoughts) Tools
    // ─────────────────────────────────────────────────────────

    public async Task<ScheduleMetricsResult> EvaluateScheduleMetricsAsync()
    {
        var result = new ScheduleMetricsResult();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 1. Calculate Late Orders
        var workOrders = await _db.WorkOrders.ToListAsync();
        foreach (var wo in workOrders)
        {
            if (wo.Status != "Scheduled") continue;

            // In our simple model, if it's Scheduled and its DueDate is past, it's late.
            // A more complex model would project completion dates based on current capacities.
            if (wo.DueDate < today)
            {
                result.TotalLateOrders++;
                result.TotalDaysLate += today.DayNumber - wo.DueDate.DayNumber;
            }
        }

        // 2. Machine Status & Idle Time
        var workCenters = await _db.WorkCenters.Include(wc => wc.Operations).ThenInclude(o => o.WorkOrder).Include(wc => wc.Shifts).ToListAsync();
        foreach (var wc in workCenters)
        {
            if (wc.Status == "Down")
            {
                result.DownWorkCenters++;
            }
            else
            {
                decimal capacity = wc.Shifts.Any() ? wc.Shifts.Sum(s => s.CapacityHours) : wc.DailyCapacityHours;
                decimal scheduled = wc.Operations.Where(o => o.Status != "Completed").Sum(o => (o.CycleTimePerUnitHours * (o.WorkOrder?.Quantity ?? 0)) + o.SetupTimeHours);
                if (capacity > scheduled)
                {
                    result.TotalIdleTimeHours += (capacity - scheduled);
                }
            }
        }

        // 3. Material Shortages
        var pos = await _db.PurchaseOrders.ToListAsync();
        result.MaterialShortages = pos.Count(p => p.Status == "Delayed");

        result.ScoreSummary = $"Late Orders: {result.TotalLateOrders} ({result.TotalDaysLate} days total). " +
                              $"Idle Time: {result.TotalIdleTimeHours}h. " +
                              $"Down Centers: {result.DownWorkCenters}. " +
                              $"Material Shortages (Delayed POs): {result.MaterialShortages}.";
        
        return result;
    }

    public async Task<string> CreateSavepointAsync(string savepointName)
    {
        if (_db.Database.CurrentTransaction == null)
            return "No active transaction to create a savepoint in.";
            
        await _db.Database.CurrentTransaction.CreateSavepointAsync(savepointName);
        return $"Savepoint '{savepointName}' created successfully.";
    }

    public async Task<string> RollbackToSavepointAsync(string savepointName)
    {
        if (_db.Database.CurrentTransaction == null)
            return "No active transaction to rollback.";

        await _db.Database.CurrentTransaction.RollbackToSavepointAsync(savepointName);
        return $"Rolled back to savepoint '{savepointName}' successfully.";
    }

    // ── External APIs (Mock) ──────────────────────────────────
    
    public async Task<string> CheckShipmentTrackingAsync(string trackingNumberOrDescription)
    {
        await Task.Delay(500); // Simulate network latency
        
        // Mock responses based on keywords
        if (trackingNumberOrDescription.Contains("Storm", StringComparison.OrdinalIgnoreCase) || 
            trackingNumberOrDescription.Contains("Miami", StringComparison.OrdinalIgnoreCase))
        {
            return "Shipment is delayed by 3 days due to severe weather conditions at the routing hub.";
        }
        
        if (trackingNumberOrDescription.Contains("Priority", StringComparison.OrdinalIgnoreCase))
        {
            return "Shipment is out for delivery today.";
        }
        
        return "Shipment is on time and expected to arrive on the scheduled delivery date.";
    }

    public async Task<string> CheckWeatherForecastAsync(string location)
    {
        await Task.Delay(500);
        
        if (location.Contains("Miami", StringComparison.OrdinalIgnoreCase) || 
            location.Contains("Florida", StringComparison.OrdinalIgnoreCase))
        {
            return "Hurricane warning in effect. Major transit delays expected for the next 48-72 hours.";
        }
        
        if (location.Contains("Chicago", StringComparison.OrdinalIgnoreCase))
        {
            return "Heavy snowstorm warning. Logistics routes may be impacted for the next 24 hours.";
        }
        
        return "Clear skies. No weather disruptions expected.";
    }

    public async Task<string> SendSupplierEmailAsync(string supplierName, string partNumber, int quantity, string urgency, string messageBody)
    {
        await Task.Delay(500); // Simulate network latency
        
        _logger.LogInformation("--- SIMULATED EMAIL DISPATCH ---");
        _logger.LogInformation("To: {Supplier}", supplierName);
        _logger.LogInformation("Subject: URGENT ({Urgency}): Request for {Quantity}x {PartNumber}", urgency, quantity, partNumber);
        _logger.LogInformation("Body: \n{Message}", messageBody);
        _logger.LogInformation("----------------------------------");
        
        return $"Email successfully sent to {supplierName} requesting {quantity} units of {partNumber}.";
    }
}
