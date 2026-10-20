using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.ViewComponents;

public sealed class BookDigitalAvailabilityViewComponent(IDigitalLibraryService digital) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int bookId)
    {
        var assets = await digital.GetBookAssetsAsync(bookId, UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier),
            UserClaimsPrincipal.IsInRole(RoleNames.Admin) || UserClaimsPrincipal.IsInRole(RoleNames.Librarian),HttpContext.RequestAborted);
        return View(new DigitalAvailabilityViewModel { Assets=assets });
    }
}
