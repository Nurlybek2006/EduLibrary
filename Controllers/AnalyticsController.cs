using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = RoleNames.Staff)]
public sealed class AnalyticsController(IAnalyticsService service) : Controller
{
    public async Task<IActionResult> Index(ReportFilterViewModel filter, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest("Талдау кезеңін дұрыс енгізіңіз.");
        try { return View(await service.GetAsync(filter, ct)); }
        catch (BusinessException exception)
        {
            TempData["Error"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    public async Task<IActionResult> Data(ReportFilterViewModel filter, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest("Талдау кезеңін дұрыс енгізіңіз.");
        try { return Json(await service.GetAsync(filter, ct)); }
        catch (BusinessException exception) { return BadRequest(exception.Message); }
    }
}
