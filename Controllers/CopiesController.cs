using EduLibrary.Helpers;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = "Admin,Librarian")]
public sealed class CopiesController(ICatalogService catalog, ILibraryClock clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int? bookId, CopyStatus? status, int page = 1, CancellationToken ct = default) =>
        View(await catalog.GetCopiesAsync(search, bookId, status, page, ct));

    [HttpGet]
    public async Task<IActionResult> Create(int? bookId, CancellationToken ct)
    {
        var model = new CopyEditViewModel { BookId = bookId ?? 0, AcquiredAt = clock.Today }; await catalog.PopulateCopyOptionsAsync(model, ct); return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CopyEditViewModel model, CancellationToken ct)
    {
        model.Id = 0;
        if (ModelState.IsValid)
        {
            try { await catalog.SaveCopyAsync(model, ct); TempData["Success"] = "Кітаптың жаңа данасы тіркелді."; return RedirectToAction(nameof(Index), new { bookId = model.BookId }); }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await catalog.PopulateCopyOptionsAsync(model, ct); return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var model = await catalog.GetCopyAsync(id, ct); return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CopyEditViewModel model, CancellationToken ct)
    {
        if (id != model.Id || id <= 0) return BadRequest();
        if (ModelState.IsValid)
        {
            try { await catalog.SaveCopyAsync(model, ct); TempData["Success"] = "Дана деректері жаңартылды."; return RedirectToAction(nameof(Index), new { bookId = model.BookId }); }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await catalog.PopulateCopyOptionsAsync(model, ct); return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string rowVersion, CancellationToken ct)
    {
        try { await catalog.DeleteCopyAsync(id, rowVersion, ct); TempData["Success"] = "Кітап данасы жойылды."; }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}
