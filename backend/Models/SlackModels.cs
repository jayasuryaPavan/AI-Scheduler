using System.Text.Json.Serialization;

namespace AiScheduler.Api.Models;

public class SlackEventPayload
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("event")]
    public SlackEventDetail? Event { get; set; }

    [JsonPropertyName("challenge")]
    public string? Challenge { get; set; }
}

public class SlackEventDetail
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public string User { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = string.Empty;
}

public class SlackResponsePayload
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
