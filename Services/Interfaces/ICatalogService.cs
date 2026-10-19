using EduLibrary.Models.Enums;
using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface ICatalogService
{
    Task<BookCatalogViewModel> GetBooksAsync(BookFilterViewModel filter, bool canViewArchived, CancellationToken ct = default);
    Task<BookDetailsViewModel?> GetBookAsync(int id, bool canViewArchived, CancellationToken ct = default);
    Task<BookEditViewModel?> GetBookForEditAsync(int id, CancellationToken ct = default);
    Task PopulateBookOptionsAsync(BookEditViewModel model, CancellationToken ct = default);
    Task<int> SaveBookAsync(BookEditViewModel model, CancellationToken ct = default);
    Task SetBookArchivedAsync(int id, bool archived, CancellationToken ct = default);
    Task<CatalogLookupViewModel> GetAuthorsAsync(string? search, CancellationToken ct = default);
    Task<AuthorEditViewModel?> GetAuthorAsync(int id, CancellationToken ct = default);
    Task SaveAuthorAsync(AuthorEditViewModel model, CancellationToken ct = default);
    Task DeleteAuthorAsync(int id, CancellationToken ct = default);
    Task<CatalogLookupViewModel> GetPublishersAsync(string? search, CancellationToken ct = default);
    Task<PublisherEditViewModel?> GetPublisherAsync(int id, CancellationToken ct = default);
    Task SavePublisherAsync(PublisherEditViewModel model, CancellationToken ct = default);
    Task DeletePublisherAsync(int id, CancellationToken ct = default);
    Task<CatalogLookupViewModel> GetCategoriesAsync(string? search, CancellationToken ct = default);
    Task<CategoryEditViewModel?> GetCategoryAsync(int id, CancellationToken ct = default);
    Task SaveCategoryAsync(CategoryEditViewModel model, CancellationToken ct = default);
    Task DeleteCategoryAsync(int id, CancellationToken ct = default);
    Task<CopiesIndexViewModel> GetCopiesAsync(string? search, int? bookId, CopyStatus? status, int page, CancellationToken ct = default);
    Task<CopyEditViewModel?> GetCopyAsync(int id, CancellationToken ct = default);
    Task PopulateCopyOptionsAsync(CopyEditViewModel model, CancellationToken ct = default);
    Task<int> SaveCopyAsync(CopyEditViewModel model, CancellationToken ct = default);
    Task DeleteCopyAsync(int id, string rowVersion, CancellationToken ct = default);
}
