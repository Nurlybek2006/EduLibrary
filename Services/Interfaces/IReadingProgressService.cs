using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface IReadingProgressService
{
    Task<ReaderViewModel?> OpenAsync(int assetId, string userId, CancellationToken ct = default);
    Task<ReadingProgressViewModel?> GetAsync(int assetId, string userId, CancellationToken ct = default);
    Task<ReadingProgressViewModel> SaveAsync(SaveReadingProgressViewModel model, string userId, CancellationToken ct = default);
    Task<List<ReadingBookmarkViewModel>> GetBookmarksAsync(int assetId, string userId, CancellationToken ct = default);
    Task<ReadingBookmarkViewModel> SaveBookmarkAsync(ReadingBookmarkEditViewModel model, string userId, CancellationToken ct = default);
    Task<bool> RemoveBookmarkAsync(int id, string userId, CancellationToken ct = default);
}
