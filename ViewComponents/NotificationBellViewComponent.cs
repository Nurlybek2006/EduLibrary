using System.Security.Claims;
using EduLibrary.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
namespace EduLibrary.ViewComponents;

public sealed class NotificationBellViewComponent(INotificationService notifications):ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId=HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if(userId==null)return Content("");
        return View(await notifications.GetBellAsync(userId,HttpContext.RequestAborted));
    }
}
