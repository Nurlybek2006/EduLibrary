using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace EduLibrary.Services.Implementations;

public sealed class ReservationBackgroundService(IServiceScopeFactory scopes, IOptions<ReservationOptions> options, ILogger<ReservationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IReservationService>().ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Failure is retried on the next bounded interval; never log private reservation payloads.
                logger.LogWarning("Reservation reconciliation could not complete; it will retry at the next interval.");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(options.Value.ReconcileIntervalMinutes), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
