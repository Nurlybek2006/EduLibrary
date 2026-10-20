using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface IAvatarStorage
{
    Task<string> SaveAsync(AvatarUploadViewModel model, CancellationToken ct = default);
    void Delete(string? avatarPath);
    bool TryGetPhysicalPath(string? id, out string path);
}
