using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = RoleNames.Staff + "," + RoleNames.Student)]
public sealed class FinesController(ICirculationService service) : Controller
{
    private bool IsStaff => User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.Librarian);
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    public async Task<IActionResult> Index(string? search, string? status, int page = 1, CancellationToken ct = default) =>
        View(await service.GetFinesAsync(search, status, page, UserId, IsStaff, ct));

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await service.GetFineAsync(id, UserId, IsStaff, ct);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = RoleNames.Staff)]
    public async Task<IActionResult> Pay(FinePaymentViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await service.PayFineAsync(model, UserId ?? "", ct);
                TempData["Success"] = "Төлем сәтті тіркелді.";
                return RedirectToAction(nameof(Details), new { id = model.FineId });
            }
            catch (BusinessException exception) { ModelState.AddModelError("", exception.Message); }
        }
        var details = await service.GetFineAsync(model.FineId, UserId, IsStaff, ct);
        if (details is null) return NotFound();
        ModelState.Remove(nameof(model.RowVersion));
        return View("Details", details);
    }
}
