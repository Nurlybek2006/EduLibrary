using EduLibrary.Models.Enums;

namespace EduLibrary.Services.Interfaces;

public sealed record StoredDigitalAsset(string StoragePath, string OriginalFileName, DigitalFileFormat FileFormat,
    long FileSize, string MimeType, string Checksum, int PageCount);

public interface IDigitalAssetStorage
{
    Task<StoredDigitalAsset> SaveAsync(IFormFile file, DigitalFileFormat format, CancellationToken ct = default);
    Task<Stream?> OpenAsync(string storagePath, CancellationToken ct = default);
    Task DeleteAsync(string storagePath, CancellationToken ct = default);
}
