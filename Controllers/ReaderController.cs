using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace EduLibrary.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReaderController(IDigitalLibraryService library, IReadingProgressService progress, IDigitalAssetStorage storage) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    [HttpGet] public Task<IActionResult> Pdf(int id, CancellationToken ct) => Open(id, DigitalFileFormat.Pdf, ct);
    [HttpGet] public Task<IActionResult> Epub(int id, CancellationToken ct) => Open(id, DigitalFileFormat.Epub, ct);
    private async Task<IActionResult> Open(int id, DigitalFileFormat format, CancellationToken ct)
    {
        var asset = await library.GetAuthorizedAssetAsync(id, UserId, ct);
        if (asset is null || asset.FileFormat != format) return NotFound();
        var model = await progress.OpenAsync(id, UserId, ct);
        return model is null ? NotFound() : View(format == DigitalFileFormat.Pdf ? "Pdf" : "Epub", model);
    }
    [HttpGet, HttpHead, ActionName("File")]
    public async Task<IActionResult> ContentFile(int id, CancellationToken ct)
    {
        var asset = await library.GetAuthorizedAssetAsync(id, UserId, ct);
        if (asset is null) return NotFound();
        var stream = await storage.OpenAsync(asset.StoragePath, ct);
        if (stream is null) return NotFound();
        Response.Headers[HeaderNames.CacheControl] = "private, no-store";
        Response.Headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; script-src 'none'; object-src 'none'; base-uri 'none'; sandbox";
        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        return base.File(stream, asset.MimeType, enableRangeProcessing: true);
    }
    [HttpGet]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var asset = await library.GetAuthorizedAssetAsync(id, UserId, ct);
        if (asset is null) return NotFound();
        if (!asset.AllowDownload) return Forbid();
        var stream = await storage.OpenAsync(asset.StoragePath, ct);
        return stream is null ? NotFound() : base.File(stream, asset.MimeType, asset.OriginalFileName, enableRangeProcessing: true);
    }
    [HttpGet]
    public async Task<IActionResult> State(int id, CancellationToken ct)
    {
        var state = await progress.GetAsync(id, UserId, ct);
        return state is null ? NotFound() : Json(new { progress = state, bookmarks = await progress.GetBookmarksAsync(id, UserId, ct) });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Progress(SaveReadingProgressViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = UiText.Get("Reader.InvalidProgress") });
        try { return Json(new { success = true, progress = await progress.SaveAsync(model, UserId, ct) }); }
        catch (BusinessException error) { return BadRequest(new { message = error.Message }); }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Bookmark(ReadingBookmarkEditViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = UiText.Get("Reader.InvalidBookmark") });
        try { return Json(new { success = true, bookmark = await progress.SaveBookmarkAsync(model, UserId, ct) }); }
        catch (BusinessException error) { return BadRequest(new { message = error.Message }); }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveBookmark(int id, CancellationToken ct)
    {
        try { return await progress.RemoveBookmarkAsync(id, UserId, ct) ? Json(new { success = true }) : NotFound(); }
        catch (BusinessException error) { return BadRequest(new { message = error.Message }); }
    }
}
