using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface IAnalyticsService
{
    Task<AnalyticsViewModel> GetAsync(ReportFilterViewModel filter, CancellationToken ct = default);
}
