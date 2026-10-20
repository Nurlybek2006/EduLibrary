using System.Data;
using System.Security.Cryptography;
using System.Text;
using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace EduLibrary.Services.Implementations;

public sealed class ReadingProgressService(ApplicationDbContext db, IDigitalLibraryService library, IOptions<DigitalLibraryOptions> options,
    ILibraryClock clock) : IReadingProgressService
{
    public async Task<ReaderViewModel?> OpenAsync(int assetId, string userId, CancellationToken ct = default)
    {
        var details = await library.GetDetailsAsync(assetId, userId, ct);
        if (details is null) return null;
        var progress = await WithLockAsync(assetId, userId, async asset =>
        {
            var state = await db.ReadingProgress.SingleOrDefaultAsync(p => p.UserId == userId && p.DigitalBookAssetId == assetId, ct);
            if (state is null)
            {
                state = new() { UserId = userId, DigitalBookAssetId = assetId, TotalPages = asset.PageCount, LastReadAt = clock.UtcNow };
                db.ReadingProgress.Add(state);
            }
            else if (clock.UtcNow - state.LastReadAt >= TimeSpan.FromSeconds(30)) state.LastReadAt = clock.UtcNow;
            await TouchSessionAsync(assetId, userId, false, ct);
            await db.SaveChangesAsync(ct);
            return Present(state);
        }, ct);
        return new() { Asset = details, Progress = progress, Bookmarks = await GetBookmarksAsync(assetId, userId, ct) };
    }

    public async Task<ReadingProgressViewModel?> GetAsync(int assetId, string userId, CancellationToken ct = default)
    {
        if (await library.GetAuthorizedAssetAsync(assetId, userId, ct) is null) return null;
        var progress = await db.ReadingProgress.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId && p.DigitalBookAssetId == assetId, ct);
        return progress is null ? new() { AssetId = assetId } : Present(progress);
    }

    public Task<ReadingProgressViewModel> SaveAsync(SaveReadingProgressViewModel model, string userId, CancellationToken ct = default) => WithLockAsync(model.AssetId, userId, async asset =>
    {
        ValidateLocator(asset.FileFormat, asset.PageCount, model.CurrentPage, model.TotalPages, model.EpubCfi, model.ProgressPercentage);
        var state = await db.ReadingProgress.SingleOrDefaultAsync(p => p.UserId == userId && p.DigitalBookAssetId == model.AssetId, ct);
        if (state is null)
        {
            state = new() { UserId = userId, DigitalBookAssetId = model.AssetId };
            db.ReadingProgress.Add(state);
        }
        var percent = asset.FileFormat == DigitalFileFormat.Pdf ? decimal.Round(100m * model.CurrentPage / asset.PageCount, 2) : decimal.Round(model.ProgressPercentage, 2);
        var same = state.CurrentPage == model.CurrentPage && state.TotalPages == model.TotalPages && state.EpubCfi == model.EpubCfi && state.ProgressPercentage == percent;
        if (!same || clock.UtcNow - state.LastReadAt >= TimeSpan.FromSeconds(30))
        {
            state.CurrentPage = model.CurrentPage; state.TotalPages = asset.FileFormat == DigitalFileFormat.Pdf ? asset.PageCount : model.TotalPages;
            state.EpubCfi = asset.FileFormat == DigitalFileFormat.Epub ? model.EpubCfi : null; state.ProgressPercentage = percent; state.LastReadAt = clock.UtcNow;
            if (percent >= 100m) state.CompletedAt ??= clock.UtcNow;
            await TouchSessionAsync(model.AssetId, userId, percent >= 100m, ct);
            await db.SaveChangesAsync(ct);
        }
        return Present(state);
    }, ct);

    public async Task<List<ReadingBookmarkViewModel>> GetBookmarksAsync(int assetId, string userId, CancellationToken ct = default)
    {
        if (await library.GetAuthorizedAssetAsync(assetId, userId, ct) is null) return [];
        return await db.ReadingBookmarks.AsNoTracking().Where(b => b.UserId == userId && b.DigitalBookAssetId == assetId)
            .OrderBy(b => b.PageNumber).ThenBy(b => b.CreatedAt).Select(b => new ReadingBookmarkViewModel { Id = b.Id, AssetId = b.DigitalBookAssetId, PageNumber = b.PageNumber,
                EpubCfi = b.EpubCfi, Title = b.Title, Note = b.Note, CreatedAt = b.CreatedAt }).ToListAsync(ct);
    }

    public Task<ReadingBookmarkViewModel> SaveBookmarkAsync(ReadingBookmarkEditViewModel model, string userId, CancellationToken ct = default) => WithLockAsync<ReadingBookmarkViewModel>(model.AssetId, userId, async asset =>
    {
        if (string.IsNullOrWhiteSpace(model.Title) || model.Title.Length > 200 || model.Note?.Length > 2000 || model.Id < 0)
            throw new BusinessException(UiText.Get("Reader.InvalidBookmark"));
        if (asset.FileFormat == DigitalFileFormat.Pdf && (model.PageNumber is null or < 1 || model.PageNumber > asset.PageCount || !string.IsNullOrEmpty(model.EpubCfi))
            || asset.FileFormat == DigitalFileFormat.Epub && (!DigitalFileRules.IsValidCfi(model.EpubCfi) || model.PageNumber is not null))
            throw new BusinessException(UiText.Get("Reader.InvalidLocator"));
        var bookmark = model.Id == 0 ? new ReadingBookmark { UserId = userId, DigitalBookAssetId = model.AssetId, CreatedAt = clock.UtcNow } :
            await db.ReadingBookmarks.SingleOrDefaultAsync(b => b.Id == model.Id && b.UserId == userId && b.DigitalBookAssetId == model.AssetId, ct)
                ?? throw new BusinessException(UiText.Get("Reader.BookmarkUnavailable"));
        if (model.Id == 0 && await db.ReadingBookmarks.CountAsync(b => b.UserId == userId && b.DigitalBookAssetId == model.AssetId, ct) >= options.Value.MaxBookmarksPerAsset)
            throw new BusinessException(UiText.Get("Reader.BookmarkLimit"));
        bookmark.Title = model.Title.Trim(); bookmark.Note = model.Note?.Trim() ?? ""; bookmark.PageNumber = model.PageNumber; bookmark.EpubCfi = model.EpubCfi; bookmark.UpdatedAt = clock.UtcNow;
        if (model.Id == 0) db.ReadingBookmarks.Add(bookmark);
        await db.SaveChangesAsync(ct);
        return new() { Id = bookmark.Id, AssetId = bookmark.DigitalBookAssetId, PageNumber = bookmark.PageNumber, EpubCfi = bookmark.EpubCfi, Title = bookmark.Title, Note = bookmark.Note, CreatedAt = bookmark.CreatedAt };
    }, ct);

    public async Task<bool> RemoveBookmarkAsync(int id, string userId, CancellationToken ct = default)
    {
        var assetId = await db.ReadingBookmarks.AsNoTracking().Where(b => b.Id == id && b.UserId == userId).Select(b => (int?)b.DigitalBookAssetId).SingleOrDefaultAsync(ct);
        if (assetId is null) return false;
        return await WithLockAsync(assetId.Value, userId, async _ =>
        {
            var row = await db.ReadingBookmarks.SingleOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);
            if (row is null) return false; db.ReadingBookmarks.Remove(row); await db.SaveChangesAsync(ct); return true;
        }, ct);
    }

    public static void ValidateLocator(DigitalFileFormat format, int actualPages, int currentPage, int totalPages, string? cfi, decimal percent)
    {
        if (percent is < 0 or > 100 || currentPage < 0 || totalPages is < 0 or > 100000 || totalPages > 0 && currentPage > totalPages)
            throw new BusinessException(UiText.Get("Reader.InvalidProgress"));
        if (format == DigitalFileFormat.Pdf && (actualPages < 1 || totalPages != actualPages || currentPage < 1 || currentPage > actualPages || !string.IsNullOrEmpty(cfi))
            || format == DigitalFileFormat.Epub && (!DigitalFileRules.IsValidCfi(cfi) || currentPage > 100000)) throw new BusinessException(UiText.Get("Reader.InvalidLocator"));
    }

    private async Task TouchSessionAsync(int assetId, string userId, bool completed, CancellationToken ct)
    {
        var session = await db.DigitalReadingSessions.Where(s => s.UserId == userId && s.DigitalBookAssetId == assetId).OrderByDescending(s => s.LastActivityAt).FirstOrDefaultAsync(ct);
        if (session is null || clock.UtcNow - session.LastActivityAt >= TimeSpan.FromMinutes(options.Value.SessionIdleMinutes))
        {
            session = new() { UserId = userId, DigitalBookAssetId = assetId, StartedAt = clock.UtcNow, LastActivityAt = clock.UtcNow };
            db.DigitalReadingSessions.Add(session);
        }
        else if (clock.UtcNow - session.LastActivityAt >= TimeSpan.FromSeconds(30) || completed) session.LastActivityAt = clock.UtcNow;
        if (completed) session.CompletedAt ??= clock.UtcNow;
    }
    private async Task<T> WithLockAsync<T>(int assetId, string userId, Func<DigitalBookAsset, Task<T>> action, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var resource = "EduLibrary.Reading." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId + ":" + assetId)));
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@key,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; SELECT @result;";
                var parameter = command.CreateParameter(); parameter.ParameterName = "@key"; parameter.Value = resource; command.Parameters.Add(parameter);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0) throw new BusinessException(UiText.Get("Reader.SaveConflict"));
            }
            var asset = await library.GetAuthorizedAssetAsync(assetId, userId, ct) ?? throw new BusinessException(UiText.Get("Digital.AssetUnavailable"));
            var result = await action(asset); await transaction.CommitAsync(ct); return result;
        }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); throw new BusinessException(UiText.Get("Reader.SaveConflict")); }
        catch (Exception ex) when (DatabaseConflict.IsExpected(ex)) { db.ChangeTracker.Clear(); throw new BusinessException(UiText.Get("Reader.SaveConflict")); }
    }
    private static ReadingProgressViewModel Present(ReadingProgress p) => new() { AssetId = p.DigitalBookAssetId, CurrentPage = p.CurrentPage, TotalPages = p.TotalPages,
        EpubCfi = p.EpubCfi, ProgressPercentage = p.ProgressPercentage, LastReadAt = p.LastReadAt, CompletedAt = p.CompletedAt };
}
