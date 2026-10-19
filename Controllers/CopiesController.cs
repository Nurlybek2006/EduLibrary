using EduLibrary.Helpers;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = "Admin,Librarian")]
public sealed class CopiesController(ICatalogService catalog, ILibraryClock clock, ICopyLookupService lookup) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await lookup.GetDetailsAsync(id, ct); return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Lookup(string? code, CancellationToken ct)
    {
        var model = new CopyLookupViewModel { Code = code, Searched = !string.IsNullOrWhiteSpace(code) };
        if (!model.Searched) return View(model);
        if (ModelState.IsValid)
        {
            try
            {
                var id = await lookup.FindByCodeAsync(code!, ct);
                if (id.HasValue) return RedirectToAction(nameof(Details), new { id = id.Value });
                ModelState.AddModelError("Code", "Бұл кодқа сәйкес кітап данасы табылмады. Штрихкодты немесе түгендеу нөмірін тексеріңіз.");
            }
            catch (BusinessException ex) { ModelState.AddModelError("Code", ex.Message); }
        }
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Label(int id, CancellationToken ct)
    {
        var model = await lookup.GetDetailsAsync(id, ct); return model is null ? NotFound() : View(model);
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Qr(int id, CancellationToken ct)
    {
        var svg = await lookup.GetQrSvgAsync(id, ct);
        if (svg is null) return NotFound();
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        return Content(svg, "image/svg+xml", System.Text.Encoding.UTF8);
    }

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
