using Microsoft.AspNetCore.Mvc;
using AiScheduler.Api.Agent;
using AiScheduler.Api.Models;
using AiScheduler.Api.Services;

namespace AiScheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PurchaseOrdersController : ControllerBase
{
    private readonly ISchedulingService             _service;
    private readonly MasterProductionSchedulerAgent _agent;

    public PurchaseOrdersController(
        ISchedulingService             service,
        MasterProductionSchedulerAgent agent)
    {
        _service = service;
        _agent   = agent;
    }

    /// <summary>Marks a PO as Delayed and immediately triggers the AI agent to re-optimize.</summary>
    [HttpPut("{id:int}/delay")]
    public async Task<IActionResult> SimulatePODelay(int id)
    {
        var po = await _service.GetPurchaseOrderAsync(id);
        if (po is null) return NotFound(new { error = $"PurchaseOrder {id} not found." });

        await _service.UpdatePurchaseOrderStatusAsync(id, PoStatus.Delayed);

        var result = await _agent.RunDirectAsync(
            $"ALERT: Purchase Order PO-{id} for part '{po.PartNumber}' ({po.PartDescription}) " +
            $"has been flagged as DELAYED. Expected delivery of {po.Quantity} units on {po.ExpectedDeliveryDate:MMM dd} " +
            $"will NOT occur. Assess the impact on all active Work Orders and re-optimize the schedule.");

        return Ok(result);
    }

    /// <summary>Marks a PO as Received and triggers the agent to unblock any resolved Work Orders.</summary>
    [HttpPut("{id:int}/receive")]
    public async Task<IActionResult> MarkPOReceived(int id)
    {
        var po = await _service.GetPurchaseOrderAsync(id);
        if (po is null) return NotFound(new { error = $"PurchaseOrder {id} not found." });

        await _service.UpdatePurchaseOrderStatusAsync(id, PoStatus.Received);

        var result = await _agent.RunDirectAsync(
            $"GOOD NEWS: Purchase Order PO-{id} for part '{po.PartNumber}' ({po.Quantity} units) " +
            $"has been RECEIVED. Check if any previously-Blocked Work Orders can now be unblocked and promoted.");

        return Ok(result);
    }
}
