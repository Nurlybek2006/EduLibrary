using System.Security.Claims;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize]
public sealed class AccountController(IAccountService accounts) : Controller
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
        return !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl) : RedirectToAction("Index", "Home");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

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
        TempData["Success"] = "Аккаунтыңыз ашылды. EduLibrary жүйесіне қош келдіңіз!";
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
                    TempData["Success"] = "Профиль жаңартылды.";
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
        return View(model);
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private void AddErrors(AccountActionResult result)
    {
        foreach (var error in result.Errors) ModelState.AddModelError("", error);
    }
}
