using Microsoft.AspNetCore.Mvc;
using AiScheduler.Api.Services;

namespace AiScheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ScheduleController : ControllerBase
{
    private readonly ISchedulingService _service;

    public ScheduleController(ISchedulingService service) => _service = service;

    /// <summary>Returns the full dashboard snapshot, optionally filtered by plant.</summary>
    [HttpGet]
    public async Task<IActionResult> GetActiveSchedule([FromQuery] string? plant = null)
    {
        var schedule = await _service.GetActiveScheduleAsync(plant);
        return Ok(schedule);
    }

    /// <summary>Returns shift actuals logged by supervisors.</summary>
    [HttpGet("shift-actuals")]
    public async Task<IActionResult> GetShiftActuals()
    {
        var actuals = await _service.GetShiftActualsAsync();
        return Ok(actuals);
    }

    /// <summary>Saves shift actuals logged by supervisors.</summary>
    [HttpPost("shift-actuals")]
    public async Task<IActionResult> SaveShiftActuals([FromBody] List<AiScheduler.Api.DTOs.ShiftActualDto> actuals)
    {
        await _service.SaveShiftActualsAsync(actuals);
        return Ok(new { message = "Shift actuals saved successfully." });
    }

    /// <summary>Resets the simulation to initial seed state (for demo re-runs).</summary>
    [HttpPost("reset")]
    public async Task<IActionResult> ResetSimulation()
    {
        await _service.ResetSimulationAsync();
        return Ok(new { message = "Simulation reset to initial state." });
    }

    /// <summary>Manually updates a Work Order's status (e.g. from MES UI).</summary>
    [HttpPut("workorders/{id}/status")]
    public async Task<IActionResult> UpdateWorkOrderStatus(int id, [FromBody] string status)
    {
        var success = await _service.UpdateWorkOrderStatusAsync(id, status);
        if (!success) return NotFound();
        return Ok(new { message = "Work Order status updated." });
    }

    /// <summary>Updates an operation's status with sequential cascade logic.</summary>
    [HttpPut("operations/{id}/status")]
    public async Task<IActionResult> UpdateOperationStatus(int id, [FromBody] string status)
    {
        try
        {
            var success = await _service.UpdateOperationStatusAsync(id, status);
            if (!success) return NotFound();
            return Ok(new { message = "Operation status updated." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public record PurchaseOrderUpdateRequest(string Status, string? ExpectedDeliveryDate);

    /// <summary>Updates a Purchase Order's status and expected delivery date.</summary>
    [HttpPut("purchaseorders/{id}/status")]
    public async Task<IActionResult> UpdatePurchaseOrderStatus(int id, [FromBody] PurchaseOrderUpdateRequest request)
    {
        var success = await _service.UpdatePurchaseOrderStatusAsync(id, request.Status, request.ExpectedDeliveryDate);
        if (!success) return NotFound();
        return Ok(new { message = "Purchase Order updated." });
    }
}
