using EduLibrary.Services.Interfaces;

namespace EduLibrary.Services.Implementations;

public sealed class ReminderBackgroundService(IServiceScopeFactory scopeFactory,ILogger<ReminderBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(30));
        do
        {
            try
            {
                await using var scope=scopeFactory.CreateAsyncScope();
                var count=await scope.ServiceProvider.GetRequiredService<INotificationService>().GenerateRemindersAsync(stoppingToken);
                if(count>0) logger.LogInformation("Created {Count} library reminders",count);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Library reminder generation failed; next scheduled run will retry");}
        }while(await timer.WaitForNextTickAsync(stoppingToken));
    }
}
