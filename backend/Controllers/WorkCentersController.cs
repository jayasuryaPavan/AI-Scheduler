using Microsoft.AspNetCore.Mvc;
using AiScheduler.Api.Agent;
using AiScheduler.Api.Models;
using AiScheduler.Api.Services;

namespace AiScheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class WorkCentersController : ControllerBase
{
    private readonly ISchedulingService             _service;
    private readonly MasterProductionSchedulerAgent _agent;

    public WorkCentersController(
        ISchedulingService             service,
        MasterProductionSchedulerAgent agent)
    {
        _service = service;
        _agent   = agent;
    }

    /// <summary>Marks a Work Center as Down and immediately triggers the AI agent to re-route jobs.</summary>
    [HttpPut("{id:int}/breakdown")]
    public async Task<IActionResult> SimulateMachineBreakdown(int id)
    {
        var wc = await _service.GetWorkCenterAsync(id);
        if (wc is null) return NotFound(new { error = $"WorkCenter {id} not found." });

        await _service.UpdateWorkCenterStatusAsync(id, WcStatus.Down);

        var result = await _agent.RunDirectAsync(
            $"CRITICAL: Work Center '{wc.Name}' (ID: {id}) has gone DOWN unexpectedly due to mechanical failure. " +
            $"All Operations routed through this Work Center are now at risk. " +
            $"Identify all impacted Work Orders, block them, and promote viable alternatives to fill the capacity gap.");

        return Ok(result);
    }

    /// <summary>Restores a Work Center to Active and triggers re-evaluation of blocked jobs.</summary>
    [HttpPut("{id:int}/restore")]
    public async Task<IActionResult> RestoreWorkCenter(int id)
    {
        var wc = await _service.GetWorkCenterAsync(id);
        if (wc is null) return NotFound(new { error = $"WorkCenter {id} not found." });

        await _service.UpdateWorkCenterStatusAsync(id, WcStatus.Active);

        var result = await _agent.RunDirectAsync(
            $"Work Center '{wc.Name}' (ID: {id}) has been RESTORED to operational status. " +
            $"Re-assess all Blocked Work Orders — those that were blocked solely due to this Work Center " +
            $"should now be unblocked and returned to the production queue.");

        return Ok(result);
    }
}
