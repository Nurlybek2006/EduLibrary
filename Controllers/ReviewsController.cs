using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize]
public sealed class ReviewsController(IReviewService reviews) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    [HttpGet, Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Index([FromQuery] ReviewModerationViewModel filter, CancellationToken ct) => View(await reviews.GetModerationAsync(filter, ct));

    [HttpGet, Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> Create(int bookId, CancellationToken ct)
    {
        var model = await reviews.GetCreateFormAsync(bookId, UserId, ct);
        if (model is null) return NotFound();
        return model.Id == 0 ? View("Edit", model) : RedirectToAction(nameof(Edit), new { id = model.Id });
    }

    [HttpGet, Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var model = await reviews.GetEditFormAsync(id, UserId, ct); return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> Create(ReviewEditViewModel model, CancellationToken ct)
    {
        model.Id = 0;
        return await SaveFormAsync(model, ct);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> Edit(int id, ReviewEditViewModel model, CancellationToken ct)
    {
        if (id != model.Id || id <= 0) return BadRequest();
        if (await reviews.GetEditFormAsync(id, UserId, ct) is null) return NotFound();
        return await SaveFormAsync(model, ct);
    }

    private async Task<IActionResult> SaveFormAsync(ReviewEditViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var bookId = await reviews.SaveAsync(model, UserId, ct); TempData["Success"] = "Пікіріңіз сақталды. Ойыңызбен бөліскеніңізге рақмет!";
                return RedirectToAction("Details", "Books", new { id = bookId }, "reviews");
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var original = model.Id == 0 ? await reviews.GetCreateFormAsync(model.BookId, UserId, ct) : await reviews.GetEditFormAsync(model.Id, UserId, ct);
        if (original is null) return NotFound();
        model.BookTitle = original.BookTitle; model.IsHidden = original.IsHidden;
        return View("Edit", model);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> Delete(int id, string rowVersion, CancellationToken ct)
    {
        if (await reviews.GetEditFormAsync(id, UserId, ct) is null) return NotFound();
        try
        {
            var bookId = await reviews.DeleteAsync(id, rowVersion, UserId, ct); TempData["Success"] = "Пікіріңіз жойылды.";
            return RedirectToAction("Details", "Books", new { id = bookId }, "reviews");
        }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; return RedirectToAction(nameof(Edit), new { id }); }
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Moderate(int id, bool hidden, string rowVersion, int? bookId, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();
        try { await reviews.ModerateAsync(id, hidden, rowVersion, ct); TempData["Success"] = hidden ? "Пікір жария көрсетілімнен жасырылды." : "Пікір қайта жарияланды."; }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { bookId });
    }
}
