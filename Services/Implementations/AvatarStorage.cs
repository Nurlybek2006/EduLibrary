using System.Security.Cryptography;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;

namespace EduLibrary.Services.Implementations;

public sealed class AvatarStorage(IWebHostEnvironment environment, ILogger<AvatarStorage> logger) : IAvatarStorage
{
    private readonly string root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "avatars"));
    public const string UrlPrefix = "/Account/Avatar/";

    public async Task<string> SaveAsync(AvatarUploadViewModel model, CancellationToken ct = default)
    {
        var image = await AvatarImageProcessor.ProcessAsync(model, ct);
        Directory.CreateDirectory(root);
        var name = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + ".webp";
        var path = Path.Combine(root, name);
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous);
            await file.WriteAsync(image, ct);
        }
        catch
        {
            Delete(UrlPrefix + name);
            throw;
        }
        return UrlPrefix + name;
    }

    public bool TryGetPhysicalPath(string? id, out string path)
    {
        path = "";
        if (!IsSafeFileName(id)) return false;
        var candidate = Path.GetFullPath(Path.Combine(root, id!));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate)) return false;
        path = candidate;
        return true;
    }

    public void Delete(string? avatarPath)
    {
        if (avatarPath is null || !avatarPath.StartsWith(UrlPrefix, StringComparison.Ordinal)) return;
        var name = avatarPath[UrlPrefix.Length..];
        if (!TryGetPhysicalPath(name, out var path)) return;
        try { File.Delete(path); }
        catch (IOException) { logger.LogWarning("An unused avatar could not be removed; the stored profile remains valid."); }
        catch (UnauthorizedAccessException) { logger.LogWarning("An unused avatar could not be removed; the stored profile remains valid."); }
    }

    public static bool IsSafeFileName(string? name) => name is { Length: 37 } && name.EndsWith(".webp", StringComparison.Ordinal)
        && name.AsSpan(0, 32).IndexOfAnyExcept("0123456789abcdef") < 0;
    public static bool IsSafeUrl(string? url) => url is not null && url.StartsWith(UrlPrefix, StringComparison.Ordinal) && IsSafeFileName(url[UrlPrefix.Length..]);
}
