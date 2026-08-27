using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using AiScheduler.Api.Agent;
using AiScheduler.Api.Services;

namespace AiScheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AgentController : ControllerBase
{
    private readonly MasterProductionSchedulerAgent _agent;
    private readonly IApprovalStore                 _approvalStore;
    private readonly IChatMemoryService             _chatMemory;
    private readonly NotificationChannel            _notificationChannel;

    public AgentController(
        MasterProductionSchedulerAgent agent,
        IApprovalStore approvalStore,
        IChatMemoryService chatMemory,
        NotificationChannel notificationChannel)
    {
        _agent               = agent;
        _approvalStore       = approvalStore;
        _chatMemory          = chatMemory;
        _notificationChannel = notificationChannel;
    }

    /// <summary>Triggers the AI agent in SANDBOX mode — changes are NOT committed until approved.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> RunAgent([FromBody] AgentRunRequest? request)
    {
        var result = await _agent.RunAsync(request?.Trigger, request?.SessionId);
        return Ok(result);
    }

    /// <summary>Approves a pending proposal and commits its changes to the live database.</summary>
    [HttpPost("approve/{proposalId}")]
    public async Task<IActionResult> ApproveProposal(string proposalId)
    {
        var proposal = _approvalStore.Get(proposalId);
        if (proposal is null)
            return NotFound(new { error = $"Proposal '{proposalId}' not found or already applied." });

        var result = await _agent.ReplayAsync(proposalId);
        return Ok(result);
    }

    /// <summary>Rejects a pending proposal and discards it.</summary>
    [HttpPost("reject/{proposalId}")]
    public IActionResult RejectProposal(string proposalId)
    {
        var removed = _approvalStore.Remove(proposalId);
        if (!removed)
            return NotFound(new { error = $"Proposal '{proposalId}' not found or already handled." });

        return Ok(new { success = true, message = "Proposal rejected and discarded." });
    }

    /// <summary>Returns full chat history for a session.</summary>
    [HttpGet("history/{sessionId}")]
    public async Task<IActionResult> GetChatHistory(string sessionId)
    {
        var history = await _chatMemory.GetSessionHistoryAsync(sessionId);
        return Ok(history);
    }

    /// <summary>Clears all chat history for a session.</summary>
    [HttpDelete("history/{sessionId}")]
    public async Task<IActionResult> ClearChatHistory(string sessionId)
    {
        await _chatMemory.ClearSessionHistoryAsync(sessionId);
        return Ok(new { success = true, message = "Chat history cleared." });
    }

    /// <summary>Server-Sent Events endpoint to stream proactive notifications.</summary>
    [HttpGet("stream")]
    public async Task StreamNotifications(CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            await foreach (var notification in _notificationChannel.ReadAllAsync(ct))
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };
                var json = JsonSerializer.Serialize(notification, options);
                
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected normally
        }
    }
}

/// <param name="Trigger">Optional human-readable description of why the agent is being triggered.</param>
/// <param name="SessionId">Persistent user session ID for chat memory.</param>
public record AgentRunRequest(string? Trigger, string? SessionId);
