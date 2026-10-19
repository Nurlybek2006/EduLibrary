using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize]
public sealed class StudentsController(IStudentService students) : Controller
{
    [Authorize(Roles = "Admin,Librarian"), HttpGet]
    public async Task<IActionResult> Index([FromQuery] StudentListViewModel filter, CancellationToken cancellationToken) => View(await students.ListAsync(filter, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var model = await students.GetDetailsAsync(id, UserId, CanManage, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> MyHistory(CancellationToken cancellationToken)
    {
        var id = await students.GetOwnIdAsync(UserId, cancellationToken);
        return id.HasValue ? RedirectToAction(nameof(Details), new { id = id.Value }) : RedirectToAction("Profile", "Account");
    }

    [Authorize(Roles = "Admin,Librarian"), HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) => View(new StudentEditViewModel { Subjects = await students.GetSubjectOptionsAsync(cancellationToken) });

    [Authorize(Roles = "Admin,Librarian"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StudentEditViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await students.CreateAsync(model, cancellationToken);
                TempData["Success"] = "Студент қосылды.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        model.Subjects = await students.GetSubjectOptionsAsync(cancellationToken);
        return View(model);
    }

    [Authorize(Roles = "Admin,Librarian"), HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var model = await students.GetEditAsync(id, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [Authorize(Roles = "Admin,Librarian"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, StudentEditViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id) return BadRequest();
        if (ModelState.IsValid)
        {
            try
            {
                await students.UpdateAsync(id, model, cancellationToken);
                TempData["Success"] = "Студент деректері жаңартылды.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        model.Subjects = await students.GetSubjectOptionsAsync(cancellationToken);
        return View(model);
    }

    [Authorize(Roles = "Admin,Librarian"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        try
        {
            await students.DeactivateAsync(id, cancellationToken);
            TempData["Success"] = "Студенттің белсенділігі тоқтатылды. Кітап алу тарихы сақталды.";
        }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private bool CanManage => User.IsInRole("Admin") || User.IsInRole("Librarian");
}
