using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DigitalLibraryController(IDigitalLibraryService library) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    [HttpGet] public async Task<IActionResult> Index([FromQuery] DigitalLibraryFilterViewModel filter, CancellationToken ct)
    { if (!ModelState.IsValid) { filter = new(); ModelState.Clear(); } return View(await library.GetCatalogAsync(filter, UserId, ct)); }
    [HttpGet] public async Task<IActionResult> My([FromQuery] DigitalLibraryFilterViewModel filter, CancellationToken ct)
    { if (!ModelState.IsValid) { filter = new(); ModelState.Clear(); } return View(await library.GetMyLibraryAsync(filter, UserId, ct)); }
    [HttpGet] public async Task<IActionResult> Details(int id, CancellationToken ct)
    { var result = await library.GetDetailsAsync(id, UserId, ct); return result is null ? NotFound() : View(result); }
    [HttpGet, Authorize(Roles = RoleNames.Staff)]
    public async Task<IActionResult> Manage([FromQuery] DigitalLibraryFilterViewModel filter, CancellationToken ct)
    { if (!ModelState.IsValid) { filter = new(); ModelState.Clear(); } return View(await library.GetManageAsync(filter, UserId, ct)); }
    [HttpGet, Authorize(Roles = RoleNames.Staff)]
    public async Task<IActionResult> Upload(int? bookId, CancellationToken ct)
    { var model = new DigitalAssetEditViewModel { BookId = bookId ?? 0 }; await library.PopulateEditAsync(model, UserId, ct); return View(model); }
    [HttpPost, Authorize(Roles = RoleNames.Staff), ValidateAntiForgeryToken]
    [RequestSizeLimit(110 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 105 * 1024 * 1024)]
    public async Task<IActionResult> Upload(DigitalAssetEditViewModel model, CancellationToken ct)
    {
        model.Id = 0;
        if (ModelState.IsValid)
            try { var id = await library.SaveAsync(model, UserId, ct); TempData["Success"] = UiText.Get("Digital.AssetSaved"); return RedirectToAction(nameof(Details), new { id }); }
            catch (BusinessException error) { ModelState.AddModelError("", error.Message); }
        await library.PopulateEditAsync(model, UserId, ct); return View(model);
    }
    [HttpGet, Authorize(Roles = RoleNames.Staff)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    { var model = await library.GetEditAsync(id, UserId, ct); return model is null ? NotFound() : View(model); }
    [HttpPost, Authorize(Roles = RoleNames.Staff), ValidateAntiForgeryToken]
    [RequestSizeLimit(110 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 105 * 1024 * 1024)]
    public async Task<IActionResult> Edit(int id, DigitalAssetEditViewModel model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        if (ModelState.IsValid)
            try { await library.SaveAsync(model, UserId, ct); TempData["Success"] = UiText.Get("Digital.AssetSaved"); return RedirectToAction(nameof(Details), new { id }); }
            catch (BusinessException error) { ModelState.AddModelError("", error.Message); }
        await library.PopulateEditAsync(model, UserId, ct); return View(model);
    }
    [HttpPost, Authorize(Roles = RoleNames.Staff), ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id, string rowVersion, CancellationToken ct)
    {
        try { await library.ArchiveAsync(id, rowVersion, UserId, ct); TempData["Success"] = UiText.Get("Digital.AssetArchivedSuccess"); }
        catch (BusinessException error) { TempData["Error"] = error.Message; }
        return RedirectToAction(nameof(Manage));
    }
}
