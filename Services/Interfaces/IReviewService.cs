using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface IReviewService
{
    Task<BookReviewsViewModel?> GetBookReviewsAsync(int bookId, string? userId, bool isStudent, int page, CancellationToken ct = default);
    Task<ReviewEditViewModel?> GetCreateFormAsync(int bookId, string userId, CancellationToken ct = default);
    Task<ReviewEditViewModel?> GetEditFormAsync(int id, string userId, CancellationToken ct = default);
    Task<int> SaveAsync(ReviewEditViewModel model, string userId, CancellationToken ct = default);
    Task<int> DeleteAsync(int id, string rowVersion, string userId, CancellationToken ct = default);
    Task<ReviewModerationViewModel> GetModerationAsync(ReviewModerationViewModel filter, CancellationToken ct = default);
    Task ModerateAsync(int id, bool hidden, string rowVersion, CancellationToken ct = default);
}
