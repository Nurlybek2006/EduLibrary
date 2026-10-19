using System.Security.Claims;
using EduLibrary.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize]
public sealed class NotificationsController(INotificationService notifications) : Controller
{
    private string UserId=>User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<IActionResult> Index(int page=1,bool unreadOnly=false,CancellationToken ct=default)
        => View(await notifications.GetAsync(UserId,page,unreadOnly,ct));
    [HttpPost]
    public async Task<IActionResult> Read(int id,CancellationToken ct)
    {
        if(!await notifications.MarkReadAsync(UserId,id,ct))return NotFound();
        return RedirectToAction(nameof(Index));
    }
    [HttpPost]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(UserId,ct);TempData["Success"]="Барлық хабарландыру оқылды деп белгіленді.";
        return RedirectToAction(nameof(Index));
    }
}
