using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgendaMedica.Api.Infrastructure.Appointments;

public sealed class AppointmentNotificationBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<AppointmentNotificationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), timeProvider);
        while (!stoppingToken.IsCancellationRequested)
        {
            bool processed;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<AppointmentNotificationProcessor>();
                processed = await processor.ProcessNextAsync(stoppingToken);
            }
            catch (Exception exception) when (
                exception is DbException or DbUpdateException or TimeoutException)
            {
                logger.LogError(
                    "Appointment notification worker could not access its outbox ({FailureType}).",
                    exception.GetType().Name);
                await timer.WaitForNextTickAsync(stoppingToken);
                continue;
            }

            if (!processed)
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            else
            {
                logger.LogInformation("Processed an appointment notification outbox item.");
            }
        }
    }
}
