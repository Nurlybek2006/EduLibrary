using System.Security.Claims;
using EduLibrary.Services.Implementations;
using EduLibrary.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = "Student")]
public sealed class ReadingSpaceController(IReadingSpaceService readingSpace, RecommendationContextService recommendationContext) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var model = await readingSpace.GetAsync(userId, ct);
        foreach (var book in model.Recommendations) book.DetailsToken = recommendationContext.Create(userId, book.BookId, book.RelevanceScore);
        return View(model);
    }
}
