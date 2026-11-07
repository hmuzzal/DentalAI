using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Services;
using DentalAI.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace DentalAI.Api.Infrastructure;

/// <summary>
/// Runs the reminder scheduler (every minute) and the message sender (every few seconds) inside the API process.
/// Each run gets its own DI scope. Errors are logged and the loop carries on. Turned off with
/// Notifications:BackgroundJobsEnabled = false (integration tests run the jobs directly instead).
/// </summary>
public sealed class NotificationWorker(
    IServiceScopeFactory scopes,
    IOptions<NotificationOptions> options,
    IDateTimeProvider clock,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var dispatchEvery = TimeSpan.FromSeconds(Math.Max(2, options.Value.DispatchIntervalSeconds));
        var remindEvery = TimeSpan.FromSeconds(Math.Max(10, options.Value.ReminderIntervalSeconds));
        var nextReminderRun = DateTime.MinValue;

        // Let the app finish starting (migrations, seeding) first.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        using var timer = new PeriodicTimer(dispatchEvery);
        do
        {
            var now = clock.UtcNow;
            if (now >= nextReminderRun)
            {
                await RunAsync<ReminderScheduler>((job, ct) => job.RunOnceAsync(now, ct), stoppingToken);
                nextReminderRun = now + remindEvery;
            }
            await RunAsync<MessageDispatcher>((job, ct) => job.RunOnceAsync(clock.UtcNow, ct), stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync<TJob>(Func<TJob, CancellationToken, Task<int>> run, CancellationToken cancellationToken) where TJob : notnull
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await run(scope.ServiceProvider.GetRequiredService<TJob>(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (TimeoutException)
        {
            // Another server holds the dispatch lock: it's sending right now. Try again next tick.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{Job} failed; it will run again shortly", typeof(TJob).Name);
        }
    }
}
