using System.Globalization;
using System.Security.Claims;
using EduLibrary.Data;
using EduLibrary.Services.Implementations;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.ViewComponents;

public sealed class AvatarViewComponent(ApplicationDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(string? userId = null, string? name = null, string? avatarPath = null, string size = "small", bool resolved = false)
    {
        if (userId is null && name is null && avatarPath is null) userId = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null && avatarPath is null && !resolved)
        {
            // The scoped cache avoids repeated SQL lookups for the current user
            // when both navigation and profile use the same component.
            var key = "EduLibrary.Avatar:" + userId;
            if (!HttpContext.Items.TryGetValue(key, out var value))
            {
                value = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new AvatarIdentity(u.FullName, u.AvatarPath)).SingleOrDefaultAsync(HttpContext.RequestAborted);
                HttpContext.Items[key] = value;
            }
            if (value is AvatarIdentity identity) { name ??= identity.Name; avatarPath = identity.Path; }
        }
        return View(new AvatarViewModel
        {
            Name = name ?? "EduLibrary", Initials = Initials(name), ImageUrl = AvatarStorage.IsSafeUrl(avatarPath) ? avatarPath : null,
            Size = size is "tiny" or "small" or "medium" or "large" ? size : "small"
        });
    }

    public static string Initials(string? name)
    {
        var words = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return "EL";
        static string First(string word) => StringInfo.GetNextTextElement(word);
        return (First(words[0]) + (words.Length > 1 ? First(words[^1]) : "")).ToUpperInvariant();
    }

    private sealed record AvatarIdentity(string Name, string? Path);
}
