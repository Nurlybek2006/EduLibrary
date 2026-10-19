using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = "Admin,Librarian,Student")]
public sealed class RecommendationsController(IRecommendationService recommendations,EduLibrary.Services.Implementations.RecommendationContextService context) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? studentId, bool includePreviouslyRead = false, CancellationToken ct = default)
    {
        var model = await recommendations.GetAsync(UserId, IsStaff, studentId, includePreviouslyRead, ct);
        if(model is not null)foreach(var book in model.Sections.SelectMany(s=>s.Books).DistinctBy(b=>b.BookId))book.DetailsToken=context.Create(UserId,book.BookId,book.RelevanceScore);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Preferences(int? studentId, CancellationToken ct = default)
    {
        var model = await recommendations.GetPreferencesAsync(UserId, IsStaff, studentId, ct);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Preferences(RecommendationPreferencesViewModel model, CancellationToken ct = default)
    {
        if (ModelState.IsValid)
        {
            try
            {
                if (!await recommendations.SavePreferencesAsync(UserId, IsStaff, model, ct)) return NotFound();
                TempData["Success"] = "Оқу бағытыңыз сақталды. Ұсыныстар жаңа деректермен есептелді.";
                return RedirectToAction(nameof(Index), new { studentId = model.StudentId });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var saved = await recommendations.GetPreferencesAsync(UserId, IsStaff, model.StudentId, ct);
        if (saved is null) return NotFound();
        model.StudentName = saved.StudentName; model.Faculty = saved.Faculty; model.Subjects = saved.Subjects;
        return View(model);
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private bool IsStaff => User.IsInRole("Admin") || User.IsInRole("Librarian");
}
