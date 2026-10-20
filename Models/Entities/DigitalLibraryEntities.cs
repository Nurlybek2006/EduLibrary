using EduLibrary.Models.Enums;

namespace EduLibrary.Models.Entities;

public sealed class DigitalBookAsset
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string FileName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    // An opaque, server-generated leaf name, never an absolute or public URL.
    public string StoragePath { get; set; } = "";
    public DigitalFileFormat FileFormat { get; set; }
    public long FileSize { get; set; }
    public string MimeType { get; set; } = "";
    public string Checksum { get; set; } = "";
    public string Language { get; set; } = "";
    public int PageCount { get; set; }
    public DigitalLicenseType LicenseType { get; set; }
    public string LicenseName { get; set; } = "";
    public string? LicenseUrl { get; set; }
    public string? SourceUrl { get; set; }
    public string RightsHolder { get; set; } = "";
    public bool RightsConfirmed { get; set; }
    public DigitalAccessLevel AccessLevel { get; set; }
    public bool AllowDownload { get; set; }
    public bool IsPublished { get; set; }
    public bool IsArchived { get; set; }
    public string UploadedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Book Book { get; set; } = null!;
    public ApplicationUser UploadedByUser { get; set; } = null!;
}

public sealed class ReadingProgress
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int DigitalBookAssetId { get; set; }
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public string? EpubCfi { get; set; }
    public decimal ProgressPercentage { get; set; }
    public DateTime LastReadAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ApplicationUser User { get; set; } = null!;
    public DigitalBookAsset DigitalBookAsset { get; set; } = null!;
}

public sealed class ReadingBookmark
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int DigitalBookAssetId { get; set; }
    public int? PageNumber { get; set; }
    public string? EpubCfi { get; set; }
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ApplicationUser User { get; set; } = null!;
    public DigitalBookAsset DigitalBookAsset { get; set; } = null!;
}

public sealed class DigitalReadingSession
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int DigitalBookAssetId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public DigitalBookAsset DigitalBookAsset { get; set; } = null!;
}
