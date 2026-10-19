using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = RoleNames.Staff)]
public sealed class ReturnsController(ICirculationService service) : Controller
{
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken ct = default) =>
        View(await service.GetReturnsAsync(search, page, ct));

    public async Task<IActionResult> Create(int id, CancellationToken ct)
    {
        var model = await service.GetReturnFormAsync(id, ct);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReturnLoanViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await service.ReturnAsync(model, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", ct);
                TempData["Success"] = "Кітап қабылданды. Қайтару жазбасы мен айыппұл есебі сақталды.";
                return RedirectToAction("Details", "Loans", new { id = model.LoanId });
            }
            catch (BusinessException exception) { ModelState.AddModelError("", exception.Message); }
        }
        var original = await service.GetReturnFormAsync(model.LoanId, ct);
        if (original is null)
        {
            TempData["Error"] = "Бұл кітап бұрын қайтарылған немесе жазба табылмады.";
            return RedirectToAction("Details", "Loans", new { id = model.LoanId });
        }
        original.ReturnDate = model.ReturnDate;
        original.ReturnCondition = model.ReturnCondition;
        original.Notes = model.Notes;
        // Refresh the token after a conflict so a reviewed retry can succeed.
        ModelState.Remove(nameof(model.RowVersion));
        return View(original);
    }
}
