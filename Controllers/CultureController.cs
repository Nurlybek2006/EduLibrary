using EduLibrary.Data;
using EduLibrary.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EduLibrary.Controllers;

public sealed class CultureController(ApplicationDbContext db, IWebHostEnvironment environment) : Controller
{
    [HttpPost]
    public async Task<IActionResult> Set(string culture, string? returnUrl, CancellationToken ct)
    {
        if (!UiText.IsSupported(culture)) return BadRequest();
        Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)), new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = true,
                SameSite = SameSiteMode.Lax, Secure = !environment.IsDevelopment() || Request.IsHttps,
                Path = "/"
            });
        if (User.Identity?.IsAuthenticated == true)
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.PreferredLanguage, culture), ct);
        }
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/Books");
    }
}
