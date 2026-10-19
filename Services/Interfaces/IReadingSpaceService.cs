using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface IReadingSpaceService
{
    Task<ReadingSpaceViewModel> GetAsync(string userId, CancellationToken ct = default);
}
