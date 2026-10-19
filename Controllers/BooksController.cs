using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

public sealed class BooksController(ICatalogService catalog, ILibraryClock clock) : Controller
{
    private bool IsStaff => User.IsInRole("Admin") || User.IsInRole("Librarian");

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Index([FromQuery] BookFilterViewModel filter, CancellationToken ct)
    {
        if (!ModelState.IsValid) { filter.Search = null; ModelState.Clear(); }
        return View(await catalog.GetBooksAsync(filter, IsStaff, ct));
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await catalog.GetBookAsync(id, IsStaff, ct);
        return model is null ? NotFound() : View(model);
    }

    [Authorize(Roles = "Admin,Librarian"), HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var model = new BookEditViewModel { PublicationYear = clock.Today.Year }; await catalog.PopulateBookOptionsAsync(model, ct); return View(model);
    }

    [Authorize(Roles = "Admin,Librarian"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BookEditViewModel model, CancellationToken ct)
    {
        model.Id = 0;
        if (ModelState.IsValid)
        {
            try
            {
                var id = await catalog.SaveBookAsync(model, ct); TempData["Success"] = "Кітап каталогқа қосылды.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await catalog.PopulateBookOptionsAsync(model, ct); return View(model);
    }

    [Authorize(Roles = "Admin,Librarian"), HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var model = await catalog.GetBookForEditAsync(id, ct); return model is null ? NotFound() : View(model);
    }

    [Authorize(Roles = "Admin,Librarian"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, BookEditViewModel model, CancellationToken ct)
    {
        if (id != model.Id || id <= 0) return BadRequest();
        if (ModelState.IsValid)
        {
            try { await catalog.SaveBookAsync(model, ct); TempData["Success"] = "Кітап деректері жаңартылды."; return RedirectToAction(nameof(Details), new { id }); }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await catalog.PopulateBookOptionsAsync(model, ct); return View(model);
    }

    [Authorize(Roles = "Admin,Librarian"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id, CancellationToken ct)
    {
        try { await catalog.SetBookArchivedAsync(id, true, ct); TempData["Success"] = "Кітап мұрағатқа көшірілді. Берілу тарихы сақталды."; }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin,Librarian"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id, CancellationToken ct)
    {
        try { await catalog.SetBookArchivedAsync(id, false, ct); TempData["Success"] = "Кітап каталогқа қалпына келтірілді."; }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }
}
