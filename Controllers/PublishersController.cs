using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = "Admin,Librarian")]
public sealed class PublishersController(ICatalogService catalog) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, CancellationToken ct) => View(await catalog.GetPublishersAsync(search, ct));

    [HttpGet]
    public IActionResult Create() => View(new PublisherEditViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PublisherEditViewModel model, CancellationToken ct)
    {
        model.Id = 0;
        if (ModelState.IsValid)
        {
            try { await catalog.SavePublisherAsync(model, ct); TempData["Success"] = "Баспа анықтамалыққа қосылды."; return RedirectToAction(nameof(Index)); }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var model = await catalog.GetPublisherAsync(id, ct); return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PublisherEditViewModel model, CancellationToken ct)
    {
        if (id != model.Id || id <= 0) return BadRequest();
        if (ModelState.IsValid)
        {
            try { await catalog.SavePublisherAsync(model, ct); TempData["Success"] = "Баспа деректері жаңартылды."; return RedirectToAction(nameof(Index)); }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        try { await catalog.DeletePublisherAsync(id, ct); TempData["Success"] = "Баспа жойылды."; }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}
