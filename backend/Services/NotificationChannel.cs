using System.Threading.Channels;
using AiScheduler.Api.DTOs;

namespace AiScheduler.Api.Services;

public class NotificationChannel
{
    private readonly Channel<AgentRunResult> _channel;

    public NotificationChannel()
    {
        // Unbounded channel since we don't expect millions of notifications.
        _channel = Channel.CreateUnbounded<AgentRunResult>();
    }

    public async Task PushNotificationAsync(AgentRunResult notification, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(notification, cancellationToken);
    }

    public IAsyncEnumerable<AgentRunResult> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
