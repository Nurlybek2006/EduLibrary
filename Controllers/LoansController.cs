using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = RoleNames.Staff + "," + RoleNames.Student)]
public sealed class LoansController(ICirculationService service) : Controller
{
    private bool IsStaff => User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.Librarian);
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    public async Task<IActionResult> Index(LoanFilterViewModel filter, CancellationToken ct) =>
        View(await service.GetLoansAsync(filter, UserId, IsStaff, ct));

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await service.GetLoanAsync(id, UserId, IsStaff, ct);
        return model is null ? NotFound() : View(model);
    }

    [Authorize(Roles = RoleNames.Staff)]
    public async Task<IActionResult> Create(int? copyId, int? studentId, CancellationToken ct) =>
        View(await service.GetIssueFormAsync(copyId, studentId, ct));

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = RoleNames.Staff)]
    public async Task<IActionResult> Create(IssueLoanViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await service.IssueAsync(model, UserId ?? "", ct);
                TempData["Success"] = "Кітап студентке сәтті берілді.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (BusinessException exception) { ModelState.AddModelError("", exception.Message); }
        }
        await service.PopulateIssueFormAsync(model, ct);
        return View(model);
    }
}
