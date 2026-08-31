using Microsoft.AspNetCore.Mvc;
using AiScheduler.Api.Agent;
using AiScheduler.Api.Models;
using AiScheduler.Api.Services;

namespace AiScheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class EmailController : ControllerBase
{
    private readonly MasterProductionSchedulerAgent _agent;
    private readonly NotificationChannel _notificationChannel;
    private readonly ILogger<EmailController> _logger;

    public EmailController(
        MasterProductionSchedulerAgent agent,
        NotificationChannel notificationChannel,
        ILogger<EmailController> logger)
    {
        _agent = agent;
        _notificationChannel = notificationChannel;
        _logger = logger;
    }

    /// <summary>
    /// Inbound email webhook — simulates receiving an email from a shipper or supplier.
    /// The AI agent parses the email content and autonomously adjusts the production schedule.
    /// </summary>
    [HttpPost("inbound")]
    public async Task<IActionResult> ReceiveInboundEmail([FromBody] InboundEmailPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Body) && string.IsNullOrWhiteSpace(payload.Subject))
        {
            return BadRequest(new { error = "Email must have a subject or body." });
        }

        _logger.LogInformation(
            "📧 Inbound email received — From: {Sender}, Subject: {Subject}",
            payload.Sender, payload.Subject);

        // Construct a natural-language prompt for the agent that includes the full email context
        var agentPrompt =
            $"INBOUND EMAIL from shipper/supplier:\n" +
            $"  From:    {payload.Sender}\n" +
            $"  Subject: {payload.Subject}\n" +
            $"  Body:    {payload.Body}\n\n" +
            $"Parse this email, extract any supply-chain updates (delays, early arrivals, quantity changes, etc.), " +
            $"and adjust the production schedule accordingly.";

        var sessionId = $"email-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var result = await _agent.RunAsync(agentPrompt, sessionId);

        _logger.LogInformation("Agent response for email: {Reasoning}", result.AgentReasoning);

        // Push the result to the frontend via SSE so the dashboard lights up
        await _notificationChannel.PushNotificationAsync(result);

        return Ok(new EmailProcessingResult
        {
            Success = true,
            AgentReasoning = result.AgentReasoning,
            ToolCalls = result.ToolCalls
        });
    }
}
