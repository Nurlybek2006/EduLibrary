using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = RoleNames.Staff)]
public sealed class DashboardController(IDashboardService service) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await service.GetAsync(ct));
}
