using System.Text.Json.Serialization;

namespace AiScheduler.Api.Models;

public class InboundEmailPayload
{
    [JsonPropertyName("sender")]
    public string Sender { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
}

public class EmailProcessingResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("agentReasoning")]
    public string AgentReasoning { get; set; } = string.Empty;

    [JsonPropertyName("toolCalls")]
    public List<string>? ToolCalls { get; set; }
}
