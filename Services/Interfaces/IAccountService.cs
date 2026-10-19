using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface IAccountService
{
    Task<AccountActionResult> LoginAsync(LoginViewModel model, CancellationToken cancellationToken = default);
    Task<AccountActionResult> RegisterAsync(RegisterViewModel model, CancellationToken cancellationToken = default);
    Task LogoutAsync();
    Task<ProfileViewModel?> GetProfileAsync(string userId, CancellationToken cancellationToken = default);
    Task<AccountActionResult> UpdateProfileAsync(string userId, ProfileViewModel model, CancellationToken cancellationToken = default);
    Task<List<StaffListItem>> ListStaffAsync(string currentUserId);
    Task<AccountActionResult> CreateLibrarianAsync(CreateLibrarianViewModel model, CancellationToken cancellationToken = default);
    Task DeactivateLibrarianAsync(string id, string currentUserId, CancellationToken cancellationToken = default);
}
