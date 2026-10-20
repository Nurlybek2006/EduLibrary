using EduLibrary.Models.Entities;
using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface IDigitalLibraryService
{
    Task<DigitalLibraryPageViewModel> GetCatalogAsync(DigitalLibraryFilterViewModel filter, string userId, CancellationToken ct = default);
    Task<DigitalLibraryPageViewModel> GetMyLibraryAsync(DigitalLibraryFilterViewModel filter, string userId, CancellationToken ct = default);
    Task<DigitalLibraryPageViewModel> GetManageAsync(DigitalLibraryFilterViewModel filter, string userId, CancellationToken ct = default);
    Task<List<DigitalAssetCardViewModel>> GetBookAssetsAsync(int bookId, string? userId, bool isStaff, CancellationToken ct = default);
    Task<DigitalAssetDetailsViewModel?> GetDetailsAsync(int id, string userId, CancellationToken ct = default);
    Task<DigitalAssetEditViewModel?> GetEditAsync(int id, string userId, CancellationToken ct = default);
    Task PopulateEditAsync(DigitalAssetEditViewModel model, string userId, CancellationToken ct = default);
    Task<int> SaveAsync(DigitalAssetEditViewModel model, string userId, CancellationToken ct = default);
    Task ArchiveAsync(int id, string rowVersion, string userId, CancellationToken ct = default);
    Task<DigitalBookAsset?> GetAuthorizedAssetAsync(int id, string userId, CancellationToken ct = default);
}
