using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface ICopyLookupService
{
    Task<CopyDetailsViewModel?> GetDetailsAsync(int id, CancellationToken ct = default);
    Task<int?> FindByCodeAsync(string code, CancellationToken ct = default);
    Task<string?> GetQrSvgAsync(int id, CancellationToken ct = default);
}
