namespace FamilyAssistant.Core;

// Host for future scheduled work. Milestone 1 performs no external operations.
public sealed class SkeletonWorker(ILogger<SkeletonWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Family Assistant worker started; integrations disabled");
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }
}
