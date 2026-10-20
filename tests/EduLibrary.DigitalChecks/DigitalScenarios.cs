using System.IO.Compression;
using System.Security.Cryptography;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

internal static class DigitalScenarios
{
    public static async Task RunAsync(DigitalFixture f)
    {
        var invalid = f.Metadata(); invalid.RightsConfirmed = false;
        await f.Reject(async () => { await f.UploadAsync(invalid); }, "unverified rights cannot publish a digital file");
        f.Check(!Directory.EnumerateFiles(f.OwnFolder).Any(), "metadata rejection leaves no staged protected file");
        var id = await f.UploadAsync(f.Metadata());
        DigitalBookAsset asset;
        await using (var db = f.NewDb())
        {
            asset = await db.DigitalBookAssets.AsNoTracking().SingleAsync(a => a.Id == id);
            f.Check(asset.PageCount == 8 && asset.FileSize > 0 && asset.Checksum.Length == 64 && DigitalFileRules.IsSafeLeaf(asset.StoragePath), "uploaded PDF has real page count checksum size and safe random storage name");
            f.Check(await db.Loans.CountAsync() == 0 && await db.BookCopies.AllAsync(c => c.Status == CopyStatus.Available), "digital upload has no physical circulation side effects");
            f.Check((await f.Library(db).GetCatalogAsync(new(), DigitalFixture.Reader)).Items.Any(a => a.Id == id), "published digital asset is queried from SQL catalog");
            f.Check((await f.Library(db).GetCatalogAsync(new() { Format = "EPUB", Search = "Original" }, DigitalFixture.Reader)).TotalCount == 0, "digital format and title filters run against SQL assets");
            f.Check(await f.Library(db).GetAuthorizedAssetAsync(id, "missing-user") is null, "unknown identity cannot access protected file metadata");
        }
        await using (var stream = await f.Storage.OpenAsync(asset.StoragePath))
        {
            f.Check(stream is not null && stream.CanSeek, "protected PDF storage supports seekable HTTP range streaming");
            using var memory = new MemoryStream(); await stream!.CopyToAsync(memory);
            f.Check(Convert.ToHexString(SHA256.HashData(memory.ToArray())) == asset.Checksum, "protected stored bytes match persisted checksum");
        }
        await f.Reject(async () => { await f.Storage.OpenAsync("../../appsettings.Local.json"); }, "protected storage rejects path traversal");
        await using (var db = f.NewDb())
        {
            var opened = await f.Progress(db).OpenAsync(id, DigitalFixture.Reader);
            f.Check(opened is not null && opened.Progress.CurrentPage == 0 && opened.Asset.Id == id, "reader opens actual authorized document and initial SQL progress");
        }
        await using (var db = f.NewDb())
        {
            var saved = await f.Progress(db).SaveAsync(new() { AssetId = id, CurrentPage = 3, TotalPages = 8, ProgressPercentage = 99 }, DigitalFixture.Reader);
            f.Check(saved.ProgressPercentage == 37.5m && saved.CurrentPage == 3, "PDF progress percentage is server-derived from actual pages");
        }
        await using (var db = f.NewDb())
        {
            var reopened = await f.Progress(db).OpenAsync(id, DigitalFixture.Reader);
            f.Check(reopened?.Progress.CurrentPage == 3, "a fresh device/context resumes persisted SQL page");
            f.Check(await db.ReadingProgress.CountAsync(p => p.UserId == DigitalFixture.Reader && p.DigitalBookAssetId == id) == 1 && await db.DigitalReadingSessions.CountAsync() == 1, "repeated reader opens reuse progress and active session");
            f.Check((await f.Progress(db).GetAsync(id, DigitalFixture.OtherReader))?.CurrentPage == 0, "another reader receives no owner's progress");
        }
        await f.Reject(async () => { await using var db = f.NewDb(); await f.Progress(db).SaveAsync(new() { AssetId = id, CurrentPage = 9, TotalPages = 9 }, DigitalFixture.Reader); }, "PDF save rejects forged page count and out-of-range page");
        await f.Reject(async () => { await using var db = f.NewDb(); await f.Progress(db).SaveAsync(new() { AssetId = id, CurrentPage = 1, TotalPages = 8 }, "missing-user"); }, "progress write requires real authorized identity");
        ReadingBookmarkViewModel bookmark;
        await using (var db = f.NewDb()) bookmark = await f.Progress(db).SaveBookmarkAsync(new() { AssetId = id, PageNumber = 3, Title = "Review later", Note = "Explain the example" }, DigitalFixture.Reader);
        await using (var db = f.NewDb()) f.Check((await f.Progress(db).GetBookmarksAsync(id, DigitalFixture.Reader)).Single().Note == "Explain the example", "bookmark page title and note persist in SQL");
        await f.Reject(async () => { await using var db = f.NewDb(); await f.Progress(db).SaveBookmarkAsync(new() { Id = bookmark.Id, AssetId = id, PageNumber = 3, Title = "Foreign edit" }, DigitalFixture.OtherReader); }, "foreign reader cannot rename another bookmark");
        await using (var db = f.NewDb()) f.Check(!await f.Progress(db).RemoveBookmarkAsync(bookmark.Id, DigitalFixture.OtherReader) && (await f.Progress(db).GetBookmarksAsync(id, DigitalFixture.OtherReader)).Count == 0, "foreign bookmark list and delete are scoped to current identity");
        await using (var db = f.NewDb())
        {
            var renamed = await f.Progress(db).SaveBookmarkAsync(new() { Id = bookmark.Id, AssetId = id, PageNumber = 4, Title = "Renamed", Note = "Updated note" }, DigitalFixture.Reader);
            f.Check(renamed.Title == "Renamed" && renamed.PageNumber == 4, "owner can rename annotate and relocate a bookmark");
        }
        await f.Reject(async () => { await using var db = f.NewDb(); await f.Progress(db).SaveBookmarkAsync(new() { AssetId = id, PageNumber = 99, Title = "Invalid" }, DigitalFixture.Reader); }, "bookmark cannot point beyond actual PDF pages");

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task SavePage(int page) { await start.Task; await using var db = f.NewDb(); await f.Progress(db).SaveAsync(new() { AssetId = id, CurrentPage = page, TotalPages = 8 }, DigitalFixture.Reader); }
        var a = SavePage(5); var b = SavePage(6); start.SetResult(); await Task.WhenAll(a, b);
        await using (var db = f.NewDb())
        {
            var progress = await db.ReadingProgress.SingleAsync(p => p.UserId == DigitalFixture.Reader && p.DigitalBookAssetId == id);
            f.Check(progress.CurrentPage is 5 or 6 && await db.ReadingProgress.CountAsync(p => p.DigitalBookAssetId == id) == 1, "parallel saves serialize without duplicate progress or lost ownership");
            await f.Progress(db).SaveAsync(new() { AssetId = id, CurrentPage = 8, TotalPages = 8 }, DigitalFixture.Reader);
            f.Check((await f.Progress(db).GetAsync(id, DigitalFixture.Reader))?.CompletedAt is not null && await db.DigitalReadingSessions.AnyAsync(s => s.CompletedAt != null), "finishing a PDF persists completion in progress and session history");
        }
        f.Clock.UtcNow = f.Clock.UtcNow.AddMinutes(31);
        await using (var db = f.NewDb()) { await f.Progress(db).OpenAsync(id, DigitalFixture.Reader); f.Check(await db.DigitalReadingSessions.CountAsync() == 2, "returning after inactivity creates a new real reading session"); }

        var epubId = await f.UploadAsync(f.Metadata(0, DigitalFileFormat.Epub));
        await using (var db = f.NewDb())
        {
            var epub = await db.DigitalBookAssets.SingleAsync(a => a.Id == epubId);
            await using var stream = await f.Storage.OpenAsync(epub.StoragePath); using var zip = new ZipArchive(stream!);
            f.Check(zip.GetEntry("mimetype") is not null && !zip.Entries.Any(e => e.Name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)), "stored EPUB is a real reconstructed passive ZIP package");
            var state = await f.Progress(db).SaveAsync(new() { AssetId = epubId, EpubCfi = "epubcfi(/6/2[chapter-1]!/4/2:18)", ProgressPercentage = 58.25m }, DigitalFixture.Reader);
            f.Check(state.EpubCfi == "epubcfi(/6/2[chapter-1]!/4/2:18)" && state.ProgressPercentage == 58.25m, "EPUB CFI and percentage persist exactly for cross-device continuation");
        }
        await using (var db = f.NewDb())
        {
            var bm = await f.Progress(db).SaveBookmarkAsync(new() { AssetId = epubId, EpubCfi = "epubcfi(/6/2!/4/2:18)", Title = "Chapter marker" }, DigitalFixture.Reader);
            f.Check(bm.PageNumber is null && bm.EpubCfi is not null && await f.Progress(db).RemoveBookmarkAsync(bm.Id, DigitalFixture.Reader), "EPUB bookmarks use CFI and owner can remove them");
        }
        await f.Reject(async () => { await using var db = f.NewDb(); await f.Progress(db).SaveAsync(new() { AssetId = epubId, EpubCfi = "javascript:alert(1)", ProgressPercentage = 50 }, DigitalFixture.Reader); }, "EPUB progress rejects active URLs instead of CFI");
        await using (var db = f.NewDb())
        {
            var shelf = await f.Library(db).GetMyLibraryAsync(new(), DigitalFixture.Reader);
            var other = await f.Library(db).GetMyLibraryAsync(new(), DigitalFixture.OtherReader);
            f.Check(shelf.Items.Count == 2 && shelf.CompletedBooks.Any(c => c.Id == id) && other.TotalCount == 0, "personal digital shelf contains real own progress and completed books only");
        }

        var draft = f.Metadata(1); draft.IsPublished = false; draft.RightsConfirmed = false; draft.LicenseType = DigitalLicenseType.Unknown;
        draft.LicenseName = null; draft.RightsHolder = null; draft.LicenseUrl = null; draft.SourceUrl = null;
        var draftId = await f.UploadAsync(draft);
        await using (var db = f.NewDb())
        {
            var persistedDraft = await db.DigitalBookAssets.AsNoTracking().SingleAsync(a => a.Id == draftId);
            f.Check(persistedDraft.LicenseName == "" && persistedDraft.RightsHolder == "" && !persistedDraft.IsPublished, "incomplete unknown-license draft normalizes optional fields without publishing");
            f.Check(await f.Library(db).GetDetailsAsync(draftId, DigitalFixture.Reader) is null && await f.Library(db).GetAuthorizedAssetAsync(draftId, DigitalFixture.Reader) is null, "draft files cannot be accessed by students");
            f.Check(await f.Library(db).GetDetailsAsync(draftId, DigitalFixture.Staff) is not null && (await f.Library(db).GetManageAsync(new(), DigitalFixture.Staff)).TotalCount == 3, "real staff can manage and preview drafts");
        }
        await f.Reject(async () => { await using var db = f.NewDb(); await f.Library(db).GetManageAsync(new(), DigitalFixture.Reader); }, "student identity cannot enter digital management service");
        await f.Reject(async () => { await using var db = f.NewDb(); var incomplete = (await f.Library(db).GetEditAsync(draftId, DigitalFixture.Staff))!; incomplete.IsPublished = true; await f.Library(db).SaveAsync(incomplete, DigitalFixture.Staff); }, "incomplete draft still cannot be published without complete rights provenance");
        await using (var db = f.NewDb())
        {
            var edit = (await f.Library(db).GetEditAsync(draftId, DigitalFixture.Staff))!;
            edit.IsPublished = true; edit.RightsConfirmed = true; edit.LicenseType = DigitalLicenseType.Original; edit.AccessLevel = DigitalAccessLevel.Staff;
            edit.LicenseName = "Creative Commons Attribution 4.0"; edit.RightsHolder = "EduLibrary Demo Content";
            edit.LicenseUrl = "https://creativecommons.org/licenses/by/4.0/"; edit.SourceUrl = "/digital-demo/provenance.html";
            await f.Library(db).SaveAsync(edit, DigitalFixture.Staff);
        }
        await using (var db = f.NewDb()) f.Check(await f.Library(db).GetAuthorizedAssetAsync(draftId, DigitalFixture.Reader) is null && (await f.Library(db).GetBookAssetsAsync(f.Books[1], DigitalFixture.Reader, true)).Count == 0, "forged staff flag cannot expose staff-only digital assets");

        DigitalAssetEditViewModel stale;
        await using (var db = f.NewDb()) stale = (await f.Library(db).GetEditAsync(id, DigitalFixture.Staff))!;
        var current = f.Metadata(); current.Id = id; current.RowVersion = stale.RowVersion;
        await f.UploadAsync(current);
        var fileCount = Directory.EnumerateFiles(f.OwnFolder).Count();
        await f.Reject(async () => { await f.UploadAsync(stale); }, "stale metadata/file replacement is rejected by rowversion");
        f.Check(Directory.EnumerateFiles(f.OwnFolder).Count() == fileCount, "failed replacement cleans its staged file without deleting the committed file");
        f.Check(await f.Storage.OpenAsync(asset.StoragePath) is null, "successful replacement deletes obsolete private file only after commit");
        await using (var db = f.NewDb())
        {
            var reset = await db.ReadingProgress.SingleAsync(p => p.UserId == DigitalFixture.Reader && p.DigitalBookAssetId == id);
            f.Check(reset.CurrentPage == 0 && reset.CompletedAt is null && !await db.ReadingBookmarks.AnyAsync(bm => bm.DigitalBookAssetId == id) && await db.DigitalReadingSessions.CountAsync(s => s.DigitalBookAssetId == id) == 2, "replacing a document resets invalid locators while retaining reading session history");
            var edit = (await f.Library(db).GetEditAsync(epubId, DigitalFixture.Staff))!;
            await f.Library(db).ArchiveAsync(epubId, edit.RowVersion ?? throw new InvalidOperationException("Edit form lacks a concurrency token."), DigitalFixture.Staff);
        }
        await using (var db = f.NewDb())
        {
            f.Check(await f.Library(db).GetAuthorizedAssetAsync(epubId, DigitalFixture.Reader) is null && (await f.Progress(db).GetAsync(epubId, DigitalFixture.Reader)) is null, "archive immediately revokes protected reading and progress access");
            f.Check(await db.ReadingProgress.AnyAsync(p => p.DigitalBookAssetId == epubId) && await db.DigitalBookAssets.AnyAsync(x => x.Id == epubId && x.IsArchived), "archive preserves asset metadata and SQL reading history");
            f.Check(await db.Loans.CountAsync() == 0 && await db.BookCopies.AllAsync(c => c.Status == CopyStatus.Available), "all digital reading and management workflows leave physical loans/copies unchanged");
        }
    }
}
