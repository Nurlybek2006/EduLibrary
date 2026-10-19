using EduLibrary.ViewModels;
namespace EduLibrary.Services.Interfaces;

public interface INotificationService
{
    Task<NotificationPageViewModel> GetAsync(string userId, int page = 1, bool unreadOnly = false, CancellationToken ct = default);
    Task<NotificationBellViewModel> GetBellAsync(string userId, CancellationToken ct = default);
    Task<bool> MarkReadAsync(string userId, int id, CancellationToken ct = default);
    Task<int> MarkAllReadAsync(string userId, CancellationToken ct = default);
    Task<int> GenerateRemindersAsync(CancellationToken ct = default);
}
