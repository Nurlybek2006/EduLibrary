using System.Data;
using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduLibrary.Services.Implementations;

public sealed class DigitalLibraryService(ApplicationDbContext db, IDigitalAssetStorage storage, IOptions<DigitalLibraryOptions> options,
    ILibraryClock clock, ILogger<DigitalLibraryService> logger) : IDigitalLibraryService
{
    private const int PageSize = 20;
    private async Task<(bool Staff, bool Student)> ActorAsync(string? userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId)) return (false, false);
        var roles = await (from user in db.Users join membership in db.UserRoles on user.Id equals membership.UserId
            join role in db.Roles on membership.RoleId equals role.Id
            where user.Id == userId && (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= clock.UtcNow)
            select role.Name).ToListAsync(ct);
        return (roles.Any(r => r is RoleNames.Admin or RoleNames.Librarian), roles.Contains(RoleNames.Student)
            && await db.Students.AnyAsync(s => s.IdentityUserId == userId && s.IsActive, ct));
    }
    private async Task RequireStaffAsync(string userId, CancellationToken ct)
    { if (!(await ActorAsync(userId, ct)).Staff) throw new BusinessException(UiText.Get("Digital.StaffRequired")); }
    private IQueryable<DigitalBookAsset> Visible(bool staff, bool student) => db.DigitalBookAssets.AsNoTracking().Where(a => !a.IsArchived && !a.Book.IsArchived
        && (staff || a.IsPublished && a.RightsConfirmed && a.LicenseType != DigitalLicenseType.Unknown
            && (a.AccessLevel == DigitalAccessLevel.Authenticated || student && a.AccessLevel == DigitalAccessLevel.Students)));

    public async Task<DigitalBookAsset?> GetAuthorizedAssetAsync(int id, string userId, CancellationToken ct = default)
    {
        var actor = await ActorAsync(userId, ct);
        if (!actor.Staff && !actor.Student && !await db.Users.AnyAsync(u => u.Id == userId && (!u.LockoutEnabled || u.LockoutEnd == null || u.LockoutEnd <= clock.UtcNow), ct)) return null;
        return await Visible(actor.Staff, actor.Student).SingleOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<DigitalLibraryPageViewModel> GetCatalogAsync(DigitalLibraryFilterViewModel filter, string userId, CancellationToken ct = default)
    {
        var actor = await ActorAsync(userId, ct);
        var query = Visible(actor.Staff, actor.Student).Where(a => a.IsPublished); // Drafts only in management/explicit staff preview.
        var result = await PageAsync(query, filter, userId, ct);
        result.IsStaff = actor.Staff;
        var readableIds = query.Select(a => a.Id);
        var own = db.ReadingProgress.AsNoTracking().Where(p => p.UserId == userId && readableIds.Contains(p.DigitalBookAssetId));
        var recentIds = await own.OrderByDescending(p => p.LastReadAt).Take(8).Select(p => p.DigitalBookAssetId).ToListAsync(ct);
        var continueIds = await own.Where(p => p.CompletedAt == null).OrderByDescending(p => p.LastReadAt).Take(8).Select(p => p.DigitalBookAssetId).ToListAsync(ct);
        result.RecentlyRead = await CardsAsync(query.Where(a => recentIds.Contains(a.Id)), userId, ct);
        result.RecentlyRead = result.RecentlyRead.OrderByDescending(a => a.LastReadAt).ToList();
        result.ContinueReading = (await CardsAsync(query.Where(a => continueIds.Contains(a.Id)), userId, ct)).OrderByDescending(a => a.LastReadAt).ToList();
        // Deterministic subject-based digital recommendations using the current reader's real curriculum.
        var subjectIds = await db.StudentSubjects.Where(s => s.Student.IdentityUserId == userId).Select(s => s.SubjectId).ToArrayAsync(ct);
        var recommendationIds = await query.OrderByDescending(a => a.Book.BookSubjects.Count(s => subjectIds.Contains(s.SubjectId)))
            .ThenByDescending(a => db.ReadingProgress.Count(p => p.DigitalBookAssetId == a.Id)).ThenByDescending(a => a.CreatedAt).Take(8).Select(a => a.Id).ToListAsync(ct);
        result.Recommended = (await CardsAsync(query.Where(a => recommendationIds.Contains(a.Id)), userId, ct)).OrderBy(a => recommendationIds.IndexOf(a.Id)).ToList();
        return result;
    }

    public async Task<DigitalLibraryPageViewModel> GetMyLibraryAsync(DigitalLibraryFilterViewModel filter, string userId, CancellationToken ct = default)
    {
        var actor = await ActorAsync(userId, ct);
        var query = Visible(actor.Staff, actor.Student).Where(a => a.IsPublished).Where(a => db.ReadingProgress.Any(p => p.UserId == userId && p.DigitalBookAssetId == a.Id)
            || db.ReadingBookmarks.Any(b => b.UserId == userId && b.DigitalBookAssetId == a.Id));
        var result = await PageAsync(query, filter, userId, ct);
        result.IsStaff = actor.Staff; result.IsPersonal = true;
        result.ContinueReading = result.Items.Where(a => a.LastReadAt != null && a.CompletedAt == null).OrderByDescending(a => a.LastReadAt).ToList();
        result.CompletedBooks = result.Items.Where(a => a.CompletedAt != null).OrderByDescending(a => a.CompletedAt).ToList();
        result.RecentlyRead = result.Items.OrderByDescending(a => a.LastReadAt).ToList();
        return result;
    }

    public async Task<DigitalLibraryPageViewModel> GetManageAsync(DigitalLibraryFilterViewModel filter, string userId, CancellationToken ct = default)
    {
        await RequireStaffAsync(userId, ct);
        var result = await PageAsync(db.DigitalBookAssets.AsNoTracking(), filter, userId, ct);
        result.IsStaff = true; result.IsManagement = true;
        return result;
    }
    public async Task<List<DigitalAssetCardViewModel>> GetBookAssetsAsync(int bookId, string? userId, bool isStaff, CancellationToken ct = default)
    {
        var actor = await ActorAsync(userId, ct);
        // Caller-supplied role flags never grant access to private/draft metadata.
        return await CardsAsync(Visible(actor.Staff, actor.Student).Where(a => a.IsPublished && a.BookId == bookId), userId ?? "", ct);
    }
    public async Task<DigitalAssetDetailsViewModel?> GetDetailsAsync(int id, string userId, CancellationToken ct = default)
    {
        var asset = await GetAuthorizedAssetAsync(id, userId, ct);
        if (asset is null) return null;
        var card = (await CardsAsync(db.DigitalBookAssets.AsNoTracking().Where(a => a.Id == id), userId, ct)).Single();
        var description = await db.Books.Where(b => b.Id == asset.BookId).Select(b => b.Description).SingleAsync(ct);
        return new()
        {
            Id = card.Id, BookId = card.BookId, Title = card.Title, Authors = card.Authors, CoverImageUrl = card.CoverImageUrl, PublicationYear = card.PublicationYear,
            FileFormat = card.FileFormat, Language = card.Language, PageCount = card.PageCount, FileSize = card.FileSize, LicenseName = card.LicenseName, AccessLevel = card.AccessLevel,
            AllowDownload = card.AllowDownload, IsPublished = card.IsPublished, IsArchived = card.IsArchived, ProgressPercentage = card.ProgressPercentage, CurrentPage = card.CurrentPage,
            TotalPages = card.TotalPages, LastReadAt = card.LastReadAt, CompletedAt = card.CompletedAt, BookmarkCount = card.BookmarkCount, ReaderCount = card.ReaderCount,
            Description = description, LicenseType = asset.LicenseType, LicenseUrl = asset.LicenseUrl, SourceUrl = asset.SourceUrl, RightsHolder = asset.RightsHolder,
            RightsConfirmed = asset.RightsConfirmed, CreatedAt = asset.CreatedAt, UpdatedAt = asset.UpdatedAt, CanManage = (await ActorAsync(userId, ct)).Staff, RowVersion = Convert.ToBase64String(asset.RowVersion)
        };
    }

    public async Task<DigitalAssetEditViewModel?> GetEditAsync(int id, string userId, CancellationToken ct = default)
    {
        await RequireStaffAsync(userId, ct);
        var asset = await db.DigitalBookAssets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return null;
        var result = new DigitalAssetEditViewModel { Id = asset.Id, BookId = asset.BookId, FileFormat = asset.FileFormat, Language = asset.Language, LicenseType = asset.LicenseType,
            LicenseName = asset.LicenseName, LicenseUrl = asset.LicenseUrl, SourceUrl = asset.SourceUrl, RightsHolder = asset.RightsHolder, RightsConfirmed = asset.RightsConfirmed,
            AccessLevel = asset.AccessLevel, AllowDownload = asset.AllowDownload, IsPublished = asset.IsPublished, RowVersion = Convert.ToBase64String(asset.RowVersion), OriginalFileName = asset.OriginalFileName, PageCount = asset.PageCount };
        await PopulateEditAsync(result, userId, ct); return result;
    }
    public async Task PopulateEditAsync(DigitalAssetEditViewModel model, string userId, CancellationToken ct = default)
    {
        await RequireStaffAsync(userId, ct);
        model.Books = await db.Books.AsNoTracking().Where(b => !b.IsArchived || b.Id == model.BookId).OrderBy(b => b.Title)
            .Select(b => new SelectListItem(b.Title + " · " + b.ISBN, b.Id.ToString())).ToListAsync(ct);
        model.MaxFileBytes = options.Value.MaxFileBytes;
    }

    public async Task<int> SaveAsync(DigitalAssetEditViewModel model, string userId, CancellationToken ct = default)
    {
        await RequireStaffAsync(userId, ct);
        ValidateMetadata(model);
        StoredDigitalAsset? staged = null; string? oldPath = null;
        try
        {
            // Validation/staging is outside the SQL transaction; cleanup removes a staged file on every SQL failure.
            if (model.File is not null && model.File.Length > 0) staged = await storage.SaveAsync(model.File, model.FileFormat, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (!await db.Books.AnyAsync(b => b.Id == model.BookId && !b.IsArchived, ct)) throw new BusinessException(UiText.Get("Digital.BookUnavailable"));
            var asset = model.Id == 0 ? new DigitalBookAsset { BookId = model.BookId, UploadedByUserId = userId, CreatedAt = clock.UtcNow } :
                await db.DigitalBookAssets.SingleOrDefaultAsync(a => a.Id == model.Id, ct) ?? throw new BusinessException(UiText.Get("Digital.AssetUnavailable"));
            if (model.Id != 0)
            {
                CheckVersion(asset.RowVersion, model.RowVersion);
                if (asset.BookId != model.BookId) throw new BusinessException(UiText.Get("Digital.CannotMoveAsset"));
                if (asset.IsArchived) throw new BusinessException(UiText.Get("Digital.AssetArchived"));
                if (staged is null && asset.FileFormat != model.FileFormat) throw new BusinessException(UiText.Get("Digital.ReplacementRequired"));
                if (staged is not null && asset.FileFormat != model.FileFormat && await db.ReadingProgress.AnyAsync(p => p.DigitalBookAssetId == asset.Id, ct))
                    throw new BusinessException(UiText.Get("Digital.FormatHasReaders"));
                if (staged is not null) oldPath = asset.StoragePath;
            }
            else if (staged is null) throw new BusinessException(UiText.Get("Digital.FileRequired"));
            if (staged is not null)
            {
                asset.FileName = staged.StoragePath; asset.StoragePath = staged.StoragePath; asset.OriginalFileName = staged.OriginalFileName;
                asset.FileFormat = staged.FileFormat; asset.FileSize = staged.FileSize; asset.MimeType = staged.MimeType; asset.Checksum = staged.Checksum; asset.PageCount = staged.PageCount;
                if (model.Id != 0)
                {
                    // Old locators refer to a different document. Keep session history, reset current reading state.
                    await db.ReadingBookmarks.Where(b => b.DigitalBookAssetId == asset.Id).ExecuteDeleteAsync(ct);
                    await db.ReadingProgress.Where(p => p.DigitalBookAssetId == asset.Id).ExecuteUpdateAsync(setters => setters
                        .SetProperty(p => p.CurrentPage, 0).SetProperty(p => p.TotalPages, staged.PageCount).SetProperty(p => p.EpubCfi, (string?)null)
                        .SetProperty(p => p.ProgressPercentage, 0m).SetProperty(p => p.CompletedAt, (DateTime?)null), ct);
                }
            }
            asset.Language = model.Language.Trim(); asset.LicenseType = model.LicenseType; asset.LicenseName = model.LicenseName?.Trim() ?? "";
            asset.LicenseUrl = Empty(model.LicenseUrl); asset.SourceUrl = Empty(model.SourceUrl); asset.RightsHolder = model.RightsHolder?.Trim() ?? ""; asset.RightsConfirmed = model.RightsConfirmed;
            asset.AccessLevel = model.AccessLevel; asset.AllowDownload = model.AllowDownload; asset.IsPublished = model.IsPublished; asset.UpdatedAt = clock.UtcNow;
            if (model.Id == 0) db.DigitalBookAssets.Add(asset);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            staged = null; // SQL now owns the staged file; never remove it if later best-effort cleanup fails.
            if (oldPath is not null) { try { await storage.DeleteAsync(oldPath, CancellationToken.None); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { logger.LogWarning("An obsolete protected digital file awaits cleanup."); } }
            return asset.Id;
        }
        catch (DbUpdateException)
        { db.ChangeTracker.Clear(); throw new BusinessException(UiText.Get("Digital.SaveConflict")); }
        catch (Exception ex) when (DatabaseConflict.IsExpected(ex))
        { db.ChangeTracker.Clear(); throw new BusinessException(UiText.Get("Digital.SaveConflict")); }
        finally { if (staged is not null) await storage.DeleteAsync(staged.StoragePath, CancellationToken.None); }
    }

    public async Task ArchiveAsync(int id, string rowVersion, string userId, CancellationToken ct = default)
    {
        await RequireStaffAsync(userId, ct);
        try
        {
            var asset = await db.DigitalBookAssets.SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new BusinessException(UiText.Get("Digital.AssetUnavailable"));
            CheckVersion(asset.RowVersion, rowVersion); asset.IsArchived = true; asset.IsPublished = false; asset.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) { throw new BusinessException(UiText.Get("Digital.SaveConflict")); }
    }
    public static void CheckVersion(byte[] current, string? encoded)
    {
        if (encoded is null || encoded.Length != 12) throw new BusinessException(UiText.Get("Digital.StaleVersion"));
        try { if (Convert.FromBase64String(encoded).AsSpan().SequenceEqual(current)) return; } catch (FormatException) { }
        throw new BusinessException(UiText.Get("Digital.StaleVersion"));
    }
    public static void ValidateMetadata(DigitalAssetEditViewModel model)
    {
        if (!Enum.IsDefined(model.FileFormat) || !Enum.IsDefined(model.AccessLevel) || !Enum.IsDefined(model.LicenseType) || string.IsNullOrWhiteSpace(model.Language) || model.Language.Length > 40
            || model.LicenseName is { Length: > 200 } || model.RightsHolder is { Length: > 200 }
            || !DigitalFileRules.IsSafeWebUrl(model.LicenseUrl) || !DigitalFileRules.IsSafeSourceUrl(model.SourceUrl)) throw new BusinessException(UiText.Get("Digital.InvalidMetadata"));
        if (model.IsPublished && (!model.RightsConfirmed || model.LicenseType == DigitalLicenseType.Unknown || string.IsNullOrWhiteSpace(model.LicenseName)
            || string.IsNullOrWhiteSpace(model.RightsHolder) || string.IsNullOrWhiteSpace(model.SourceUrl) || string.IsNullOrWhiteSpace(model.LicenseUrl)))
            throw new BusinessException(UiText.Get("Digital.LicenseRequired"));
    }
    private static string? Empty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private async Task<DigitalLibraryPageViewModel> PageAsync(IQueryable<DigitalBookAsset> source, DigitalLibraryFilterViewModel filter, string userId, CancellationToken ct)
    {
        filter.Search = filter.Search?.Trim(); if (filter.Search?.Length > 200) filter.Search = filter.Search[..200];
        filter.Page = Math.Clamp(filter.Page, 1, 1_000_000); filter.Sort = filter.Sort is "recent" or "popular" or "title" or "progress" ? filter.Sort : "recent";
        var query = source;
        if (!string.IsNullOrWhiteSpace(filter.Search)) query = query.Where(a => a.Book.Title.Contains(filter.Search) || a.Book.BookAuthors.Any(x => (x.Author.FirstName + " " + x.Author.LastName).Contains(filter.Search)));
        if (filter.Format is "Pdf" or "PDF") query = query.Where(a => a.FileFormat == DigitalFileFormat.Pdf);
        if (filter.Format is "Epub" or "EPUB") query = query.Where(a => a.FileFormat == DigitalFileFormat.Epub);
        if (!string.IsNullOrWhiteSpace(filter.Language)) query = query.Where(a => a.Language == filter.Language);
        if (filter.CategoryId.HasValue) query = query.Where(a => a.Book.BookCategories.Any(c => c.CategoryId == filter.CategoryId));
        if (filter.SubjectId.HasValue) query = query.Where(a => a.Book.BookSubjects.Any(s => s.SubjectId == filter.SubjectId));
        if (filter.AuthorId.HasValue) query = query.Where(a => a.Book.BookAuthors.Any(s => s.AuthorId == filter.AuthorId));
        var count = await query.CountAsync(ct); var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)PageSize)); filter.Page = Math.Min(filter.Page, totalPages);
        var ordered = filter.Sort switch
        {
            "title" => query.OrderBy(a => a.Book.Title).ThenBy(a => a.Id),
            "popular" => query.OrderByDescending(a => db.ReadingProgress.Count(p => p.DigitalBookAssetId == a.Id)).ThenBy(a => a.Id),
            "progress" => query.OrderByDescending(a => db.ReadingProgress.Where(p => p.UserId == userId && p.DigitalBookAssetId == a.Id).Select(p => (DateTime?)p.LastReadAt).FirstOrDefault()).ThenBy(a => a.Id),
            _ => query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
        };
        var result = new DigitalLibraryPageViewModel { Filter = filter, TotalCount = count, TotalPages = totalPages, Items = await CardsAsync(ordered.Skip((filter.Page - 1) * PageSize).Take(PageSize), userId, ct) };
        result.Categories = await db.Categories.Where(c => db.BookCategories.Any(b => b.CategoryId == c.Id && source.Any(a => a.BookId == b.BookId))).OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(ct);
        result.Subjects = await db.Subjects.Where(s => db.BookSubjects.Any(b => b.SubjectId == s.Id && source.Any(a => a.BookId == b.BookId))).OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync(ct);
        result.Authors = await db.Authors.Where(a => db.BookAuthors.Any(b => b.AuthorId == a.Id && source.Any(x => x.BookId == b.BookId))).OrderBy(a => a.LastName).Select(a => new SelectListItem(a.FirstName + " " + a.LastName, a.Id.ToString())).ToListAsync(ct);
        result.Languages = await source.Select(a => a.Language).Distinct().OrderBy(x => x).Select(x => new SelectListItem(x, x)).ToListAsync(ct);
        return result;
    }

    private async Task<List<DigitalAssetCardViewModel>> CardsAsync(IQueryable<DigitalBookAsset> query, string userId, CancellationToken ct)
    {
        var rows = await query.Select(a => new DigitalAssetCardViewModel {
            Id = a.Id, BookId = a.BookId, Title = a.Book.Title, CoverImageUrl = a.Book.CoverImageUrl, PublicationYear = a.Book.PublicationYear,
            FileFormat = a.FileFormat, Language = a.Language, PageCount = a.PageCount, FileSize = a.FileSize, LicenseName = a.LicenseName, AccessLevel = a.AccessLevel,
            AllowDownload = a.AllowDownload, IsPublished = a.IsPublished, IsArchived = a.IsArchived,
            ProgressPercentage = db.ReadingProgress.Where(p => p.UserId == userId && p.DigitalBookAssetId == a.Id).Select(p => (decimal?)p.ProgressPercentage).FirstOrDefault(),
            CurrentPage = db.ReadingProgress.Where(p => p.UserId == userId && p.DigitalBookAssetId == a.Id).Select(p => p.CurrentPage).FirstOrDefault(),
            TotalPages = db.ReadingProgress.Where(p => p.UserId == userId && p.DigitalBookAssetId == a.Id).Select(p => p.TotalPages).FirstOrDefault(),
            LastReadAt = db.ReadingProgress.Where(p => p.UserId == userId && p.DigitalBookAssetId == a.Id).Select(p => (DateTime?)p.LastReadAt).FirstOrDefault(),
            CompletedAt = db.ReadingProgress.Where(p => p.UserId == userId && p.DigitalBookAssetId == a.Id).Select(p => p.CompletedAt).FirstOrDefault(),
            BookmarkCount = db.ReadingBookmarks.Count(b => b.UserId == userId && b.DigitalBookAssetId == a.Id), ReaderCount = db.ReadingProgress.Count(p => p.DigitalBookAssetId == a.Id)
        }).ToListAsync(ct);
        var books = rows.Select(r => r.BookId).Distinct().ToArray();
        var names = await db.BookAuthors.Where(a => books.Contains(a.BookId)).OrderBy(a => a.Author.LastName).Select(a => new { a.BookId, Name = a.Author.FirstName + " " + a.Author.LastName }).ToListAsync(ct);
        var lookup = names.ToLookup(a => a.BookId, a => a.Name);
        foreach (var row in rows) row.Authors = string.Join(", ", lookup[row.BookId]);
        return rows;
    }
}
