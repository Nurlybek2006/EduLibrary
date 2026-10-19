using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.ViewComponents;

public sealed class BookReviewsViewComponent(IReviewService reviews) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int bookId)
    {
        _ = int.TryParse(HttpContext.Request.Query["reviewPage"], out var page);
        var model = await reviews.GetBookReviewsAsync(bookId, UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier),
            UserClaimsPrincipal.IsInRole(RoleNames.Student), page, HttpContext.RequestAborted);
        return model is null ? Content("") : View(model);
    }
}
