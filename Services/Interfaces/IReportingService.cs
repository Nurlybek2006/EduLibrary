using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface IReportingService
{
    Task<ReportPageViewModel> GetAsync(ReportFilterViewModel filter, CancellationToken ct = default);
    Task<ReportPageViewModel> GetPrintAsync(ReportFilterViewModel filter, CancellationToken ct = default);
    Task<byte[]> ExportCsvAsync(ReportFilterViewModel filter, CancellationToken ct = default);
}
