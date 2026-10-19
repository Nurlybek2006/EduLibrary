using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.ViewComponents;

public sealed class ReservationSummaryViewComponent(IReservationService reservations) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!UserClaimsPrincipal.IsInRole(RoleNames.Student) || string.IsNullOrWhiteSpace(userId)) return Content("");
        var model = await reservations.GetListAsync(new ReservationFilterViewModel { Status = "active" }, userId, false, HttpContext.RequestAborted);
        return View(model);
    }
}
