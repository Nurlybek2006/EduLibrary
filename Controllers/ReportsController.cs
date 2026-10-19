using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = RoleNames.Staff)]
public sealed class ReportsController(IReportingService service) : Controller
{
    public async Task<IActionResult> Index(ReportFilterViewModel filter, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest("Есеп сүзгілерін дұрыс енгізіңіз.");
        try { return View(await service.GetAsync(filter, ct)); }
        catch (BusinessException exception)
        {
            TempData["Error"] = exception.Message;
            return RedirectToAction(nameof(Index), new { Type = filter.Type });
        }
    }

    public async Task<IActionResult> Export(ReportFilterViewModel filter, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest("Есеп сүзгілерін дұрыс енгізіңіз.");
        try { return File(await service.ExportCsvAsync(filter, ct), "text/csv; charset=utf-8", $"EduLibrary-{filter.Type}-{filter.StartDate:yyyyMMdd}-{filter.EndDate:yyyyMMdd}.csv"); }
        catch (BusinessException exception) { return BadRequest(exception.Message); }
    }

    public async Task<IActionResult> Print(ReportFilterViewModel filter, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest("Есеп сүзгілерін дұрыс енгізіңіз.");
        try
        {
            var report = await service.GetPrintAsync(filter, ct);
            ViewData["Print"] = true;
            return View("Index", report);
        }
        catch (BusinessException exception) { return BadRequest(exception.Message); }
    }
}
