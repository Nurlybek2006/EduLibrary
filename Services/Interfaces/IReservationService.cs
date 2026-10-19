using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface IReservationService
{
    Task<ReservationListViewModel> GetListAsync(ReservationFilterViewModel filter, string userId, bool isStaff, CancellationToken ct = default);
    Task<ReservationDetailsViewModel?> GetDetailsAsync(int id, string userId, bool isStaff, CancellationToken ct = default);
    Task<ReservationCreateViewModel?> GetCreateFormAsync(int bookId, string userId, CancellationToken ct = default);
    Task<List<PickupSlotViewModel>> GetSlotsAsync(DateTime pickupDate, string pickupLocationId, CancellationToken ct = default);
    Task<int> CreateAsync(ReservationCreateViewModel model, string userId, CancellationToken ct = default);
    Task<ReservationReadyViewModel?> GetPrepareFormAsync(int id, string staffUserId, CancellationToken ct = default);
    Task MarkReadyAsync(ReservationReadyViewModel model, string staffUserId, CancellationToken ct = default);
    Task<int> FulfillAsync(ReservationFulfillViewModel model, string staffUserId, CancellationToken ct = default);
    Task CancelAsync(int id, string rowVersion, string userId, bool isStaff, CancellationToken ct = default);
    Task<int?> FindByCodeAsync(string code, string staffUserId, CancellationToken ct = default);
    Task<int> ProcessAsync(CancellationToken ct = default);
}
