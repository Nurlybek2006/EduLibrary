using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface IDashboardService
{
    Task<DashboardViewModel> GetAsync(CancellationToken ct = default);
}
