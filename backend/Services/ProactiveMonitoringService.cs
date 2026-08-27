using Microsoft.EntityFrameworkCore;
using AiScheduler.Api.Agent;
using AiScheduler.Api.Data;

namespace AiScheduler.Api.Services;

public class ProactiveMonitoringService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly NotificationChannel _notificationChannel;
    private readonly ILogger<ProactiveMonitoringService> _logger;
    private readonly HashSet<string> _notifiedAnomalies = new();

    public ProactiveMonitoringService(
        IServiceProvider serviceProvider,
        NotificationChannel notificationChannel,
        ILogger<ProactiveMonitoringService> logger)
    {
        _serviceProvider = serviceProvider;
        _notificationChannel = notificationChannel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Proactive Monitoring Service started.");

        // Loop every 30 seconds
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForAnomaliesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during proactive monitoring check.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task CheckForAnomaliesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Check for Down WorkCenters
        var downWorkCenters = await db.WorkCenters
            .Where(wc => wc.Status == "Down")
            .ToListAsync(stoppingToken);

        foreach (var wc in downWorkCenters)
        {
            var anomalyKey = $"WC_{wc.Id}_Down";
            if (!_notifiedAnomalies.Contains(anomalyKey))
            {
                _logger.LogInformation("Detected anomaly: Work Center '{Name}' is Down.", wc.Name);
                await TriggerAgentAsync(scope, $"PROACTIVE ALERT: Work Center '{wc.Name}' is currently Down. Evaluate the schedule and propose adjustments.");
                _notifiedAnomalies.Add(anomalyKey);
            }
        }

        // 2. Check for Delayed PurchaseOrders
        var delayedPos = await db.PurchaseOrders
            .Where(po => po.Status == "Delayed")
            .ToListAsync(stoppingToken);

        foreach (var po in delayedPos)
        {
            var anomalyKey = $"PO_{po.Id}_Delayed";
            if (!_notifiedAnomalies.Contains(anomalyKey))
            {
                _logger.LogInformation("Detected anomaly: Purchase Order '{PartNumber}' is Delayed.", po.PartNumber);
                await TriggerAgentAsync(scope, $"PROACTIVE ALERT: Purchase Order for '{po.PartDescription}' ({po.PartNumber}) is Delayed. Evaluate the schedule and propose adjustments.");
                _notifiedAnomalies.Add(anomalyKey);
            }
        }
    }

    private async Task TriggerAgentAsync(IServiceScope scope, string triggerMessage)
    {
        var agent = scope.ServiceProvider.GetRequiredService<MasterProductionSchedulerAgent>();
        
        // We use a global system session ID for proactive alerts, or we don't pass one so it just creates a generic response
        var result = await agent.RunAsync(triggerMessage, "proactive_system_session");

        // Push the result to the SSE channel so frontend clients see it pop up
        await _notificationChannel.PushNotificationAsync(result);
    }
}
