using System.Security.Cryptography;
using EduLibrary.Helpers;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace EduLibrary.Services.Implementations;

public sealed class DigitalAssetStorage : IDigitalAssetStorage
{
    private readonly string root;
    private readonly DigitalLibraryOptions options;
    public DigitalAssetStorage(IWebHostEnvironment environment, IOptions<DigitalLibraryOptions> settings)
    {
        options = settings.Value;
        if (!DigitalLibraryOptions.IsValid(options)) throw new InvalidOperationException("Invalid digital library storage options.");
        root = Path.GetFullPath(Path.IsPathRooted(options.StorageRoot) ? options.StorageRoot : Path.Combine(environment.ContentRootPath, options.StorageRoot));
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (IsWithin(root, webRoot)) throw new InvalidOperationException("Digital content storage must be outside wwwroot.");
        Directory.CreateDirectory(root);
        if (new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Digital storage cannot be a symbolic link.");
    }
    public async Task<StoredDigitalAsset> SaveAsync(IFormFile file, DigitalFileFormat format, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(format) || file.Length <= 0 || file.Length > options.MaxFileBytes) throw new BusinessException(UiText.Get("Digital.InvalidFileSize"));
        var original = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (original.Length > 200 || original.Any(char.IsControl) || !string.Equals(Path.GetExtension(original), DigitalFileRules.Extension(format), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(file.ContentType?.Split(';')[0].Trim(), DigitalFileRules.MimeType(format), StringComparison.OrdinalIgnoreCase)) throw new BusinessException(UiText.Get("Digital.InvalidFileType"));
        await using var source = file.OpenReadStream();
        using var buffer = new MemoryStream(); var block = new byte[81920]; int read;
        while ((read = await source.ReadAsync(block, ct)) != 0)
        {
            if (buffer.Length + read > options.MaxFileBytes) throw new BusinessException(UiText.Get("Digital.InvalidFileSize"));
            buffer.Write(block, 0, read);
        }
        var bytes = buffer.ToArray();
        var pages = format == DigitalFileFormat.Pdf ? DigitalFileRules.ValidatePdf(bytes) : 0;
        if (format == DigitalFileFormat.Epub) bytes = DigitalFileRules.SanitizeEpub(bytes, options);
        if (bytes.LongLength > options.MaxFileBytes) throw new BusinessException(UiText.Get("Digital.InvalidFileSize"));
        var leaf = Guid.NewGuid().ToString("N") + DigitalFileRules.Extension(format);
        var target = Resolve(leaf);
        try
        {
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await output.WriteAsync(bytes, ct);
        }
        catch { if (System.IO.File.Exists(target)) System.IO.File.Delete(target); throw; }
        return new(leaf, original, format, bytes.LongLength, DigitalFileRules.MimeType(format), Convert.ToHexString(SHA256.HashData(bytes)), pages);
    }
    public Task<Stream?> OpenAsync(string storagePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var target = Resolve(storagePath);
        if (!System.IO.File.Exists(target)) return Task.FromResult<Stream?>(null);
        if (new FileInfo(target).Attributes.HasFlag(FileAttributes.ReparsePoint)) return Task.FromResult<Stream?>(null);
        Stream stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream?>(stream);
    }
    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var target = Resolve(storagePath);
        if (System.IO.File.Exists(target)) System.IO.File.Delete(target);
        return Task.CompletedTask;
    }
    private string Resolve(string leaf)
    {
        if (!DigitalFileRules.IsSafeLeaf(leaf)) throw new BusinessException(UiText.Get("Digital.InvalidStoragePath"));
        var resolved = Path.GetFullPath(Path.Combine(root, leaf));
        if (!IsWithin(resolved, root)) throw new BusinessException(UiText.Get("Digital.InvalidStoragePath"));
        return resolved;
    }
    private static bool IsWithin(string target, string directory) => string.Equals(target, directory, StringComparison.OrdinalIgnoreCase) || target.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
