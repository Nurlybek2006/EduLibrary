using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = "Admin")]
public sealed class StaffController(IAccountService accounts) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(await accounts.ListStaffAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ""));

    [HttpGet]
    public IActionResult Create() => View(new CreateLibrarianViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateLibrarianViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await accounts.CreateLibrarianAsync(model, cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error);
            return View(model);
        }
        TempData["Success"] = "Кітапханашы аккаунты ашылды.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(string id, CancellationToken cancellationToken)
    {
        try
        {
            await accounts.DeactivateLibrarianAsync(id, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", cancellationToken);
            TempData["Success"] = "Кітапханашы аккаунты тоқтатылды.";
        }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}
