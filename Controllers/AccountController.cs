using System.Security.Claims;
using System.Globalization;
using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Localization;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Controllers;

[Authorize]
public sealed class AccountController(IAccountService accounts, IAvatarStorage avatars, ApplicationDbContext db, IWebHostEnvironment environment) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await accounts.LoginAsync(model, cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }
        if (!Request.Cookies.ContainsKey(CookieRequestCultureProvider.DefaultCookieName))
        {
            var language = await db.Users.AsNoTracking().Where(u => u.Email == model.Email.Trim()).Select(u => u.PreferredLanguage).SingleOrDefaultAsync(cancellationToken);
            if (ProfilePreferences.IsLanguage(language)) SetCultureCookie(language!);
        }
        return !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl) : RedirectToAction("Index", "Home");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [AllowAnonymous, HttpGet]
    public IActionResult ForgotPassword() => View();

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await accounts.RegisterAsync(model, cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }
        TempData["Success"] = UiText.Get("Аккаунтыңыз ашылды. EduLibrary жүйесіне қош келдіңіз!");
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await accounts.LogoutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var model = await accounts.GetProfileAsync(UserId, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var result = await accounts.UpdateProfileAsync(UserId, model, cancellationToken);
                if (result.Succeeded)
                {
                    SetCultureCookie(model.PreferredLanguage);
                    TempData["Success"] = UiText.Get("Profile updated.");
                    return RedirectToAction(nameof(Profile));
                }
                AddErrors(result);
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var saved = await accounts.GetProfileAsync(UserId, cancellationToken);
        if (saved is null) return NotFound();
        model.StudentId = saved.StudentId; model.StudentNumber = saved.StudentNumber; model.Faculty = saved.Faculty;
        model.Course = saved.Course; model.GroupName = saved.GroupName; model.Subjects = saved.Subjects;
        model.AvatarPath = saved.AvatarPath; model.ActiveLoanCount = saved.ActiveLoanCount; model.ReturnedLoanCount = saved.ReturnedLoanCount;
        model.DigitalBooks = saved.DigitalBooks; model.DigitalBookCount = saved.DigitalBookCount; model.CompletedDigitalBookCount = saved.CompletedDigitalBookCount; model.RecentLoans = saved.RecentLoans;
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(3 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 3 * 1024 * 1024)]
    public async Task<IActionResult> UploadAvatar(AvatarUploadViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = UiText.Get("Choose a valid JPG, PNG, or WebP photo no larger than 2 MB.");
            return RedirectToAction(nameof(Profile));
        }
        var result = await accounts.UpdateAvatarAsync(UserId, model, cancellationToken);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? UiText.Get("Profile photo updated.") : string.Join(" ", result.Errors);
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAvatar(CancellationToken cancellationToken)
    {
        var result = await accounts.RemoveAvatarAsync(UserId, cancellationToken);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? UiText.Get("Profile photo removed.") : string.Join(" ", result.Errors);
        return RedirectToAction(nameof(Profile));
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Avatar(string id, CancellationToken cancellationToken)
    {
        if (!avatars.TryGetPhysicalPath(id, out var path) || !await db.Users.AsNoTracking().AnyAsync(u => u.AvatarPath == EduLibrary.Services.Implementations.AvatarStorage.UrlPrefix + id, cancellationToken)) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "public, max-age=86400";
        try
        {
            // A replacement may remove the old name while an existing page
            // is requesting it. Open now so that race yields a quiet 404.
            return File(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), "image/webp");
        }
        catch (IOException) { return NotFound(); }
    }

    private void SetCultureCookie(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true, IsEssential = true,
            SameSite = SameSiteMode.Lax, Secure = !environment.IsDevelopment() || Request.IsHttps, Path = "/"
        });
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private void AddErrors(AccountActionResult result)
    {
        foreach (var error in result.Errors) ModelState.AddModelError("", error);
    }
}
