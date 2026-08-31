using Microsoft.AspNetCore.Mvc;
using AiScheduler.Api.Agent;
using AiScheduler.Api.Models;

namespace AiScheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SlackController : ControllerBase
{
    private readonly MasterProductionSchedulerAgent _agent;
    private readonly ILogger<SlackController> _logger;

    public SlackController(MasterProductionSchedulerAgent agent, ILogger<SlackController> logger)
    {
        _agent = agent;
        _logger = logger;
    }

    /// <summary>
    /// Webhook endpoint to receive events from Slack.
    /// </summary>
    [HttpPost("event")]
    public async Task<IActionResult> ReceiveEvent([FromBody] SlackEventPayload payload)
    {
        // Respond to Slack url_verification challenge
        if (payload.Type == "url_verification" && !string.IsNullOrEmpty(payload.Challenge))
        {
            return Ok(new { challenge = payload.Challenge });
        }

        // Process message events
        if (payload.Type == "event_callback" && payload.Event != null)
        {
            if (payload.Event.Type == "message" && !string.IsNullOrEmpty(payload.Event.Text))
            {
                var userMessage = payload.Event.Text;
                
                // Prevent bot loops (in a real app you'd check if the sender is a bot)
                if (userMessage.StartsWith("[AI]"))
                    return Ok();

                _logger.LogInformation("Received Slack message from {User} in {Channel}: {Text}", 
                    payload.Event.User, payload.Event.Channel, userMessage);

                // For a hackathon, we can use a hardcoded or unique session ID for the Slack channel
                var sessionId = $"slack-{payload.Event.Channel}";

                // Forward the message to the Master Production Scheduler Agent
                var agentResponse = await _agent.RunAsync(userMessage, sessionId);

                // Here we would typically use a Slack SDK to post a message back to the channel asynchronously
                // But for simulation, we can just log the response and return it.
                _logger.LogInformation("--- SIMULATED SLACK REPLY to {Channel} ---", payload.Event.Channel);
                _logger.LogInformation("{Reply}", agentResponse.AgentReasoning);
                _logger.LogInformation("-------------------------------------------");

                // Returning it in the HTTP response so testing via Postman is easy
                return Ok(new SlackResponsePayload
                {
                    Text = agentResponse.AgentReasoning
                });
            }
        }

        return Ok();
    }
}
