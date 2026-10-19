using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface IRecommendationService
{
    Task<RecommendationViewModel?> GetAsync(string userId, bool isStaff, int? studentId = null, bool includePreviouslyRead = false, CancellationToken ct = default);
    Task<RecommendationPreferencesViewModel?> GetPreferencesAsync(string userId, bool isStaff, int? studentId = null, CancellationToken ct = default);
    Task<bool> SavePreferencesAsync(string userId, bool isStaff, RecommendationPreferencesViewModel model, CancellationToken ct = default);
}
