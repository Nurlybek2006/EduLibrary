using System.ComponentModel.DataAnnotations;
using EduLibrary.Models.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduLibrary.ViewModels;

public sealed class DigitalLibraryFilterViewModel
{
    [StringLength(200, ErrorMessage = "Validation.StringLength")] public string? Search { get; set; }
    public string? Format { get; set; }
    public string? Language { get; set; }
    public int? CategoryId { get; set; }
    public int? SubjectId { get; set; }
    public int? AuthorId { get; set; }
    public string Sort { get; set; } = "recent";
    public int Page { get; set; } = 1;
}

public sealed class DigitalLibraryPageViewModel
{
    public DigitalLibraryFilterViewModel Filter { get; set; } = new();
    public List<DigitalAssetCardViewModel> Items { get; set; } = [];
    public List<DigitalAssetCardViewModel> ContinueReading { get; set; } = [];
    public List<DigitalAssetCardViewModel> RecentlyRead { get; set; } = [];
    public List<DigitalAssetCardViewModel> CompletedBooks { get; set; } = [];
    public List<DigitalAssetCardViewModel> Recommended { get; set; } = [];
    public List<SelectListItem> Categories { get; set; } = [];
    public List<SelectListItem> Subjects { get; set; } = [];
    public List<SelectListItem> Authors { get; set; } = [];
    public List<SelectListItem> Languages { get; set; } = [];
    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;
    public bool IsStaff { get; set; }
    public bool IsManagement { get; set; }
    public bool IsPersonal { get; set; }
}

public class DigitalAssetCardViewModel
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string Title { get; set; } = "";
    public string Authors { get; set; } = "";
    public string? CoverImageUrl { get; set; }
    public int PublicationYear { get; set; }
    public DigitalFileFormat FileFormat { get; set; }
    public string Format => FileFormat == DigitalFileFormat.Pdf ? "PDF" : "EPUB";
    public string ReaderUrl => $"/Reader/{(FileFormat == DigitalFileFormat.Pdf ? "Pdf" : "Epub")}/{Id}";
    public string Language { get; set; } = "";
    public int PageCount { get; set; }
    public long FileSize { get; set; }
    public string LicenseName { get; set; } = "";
    public DigitalAccessLevel AccessLevel { get; set; }
    public bool AllowDownload { get; set; }
    public bool IsPublished { get; set; }
    public bool IsArchived { get; set; }
    public decimal? ProgressPercentage { get; set; }
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public DateTime? LastReadAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int BookmarkCount { get; set; }
    public int ReaderCount { get; set; }
}

public sealed class DigitalAssetDetailsViewModel : DigitalAssetCardViewModel
{
    public string Description { get; set; } = "";
    public DigitalLicenseType LicenseType { get; set; }
    public string? LicenseUrl { get; set; }
    public string? SourceUrl { get; set; }
    public string RightsHolder { get; set; } = "";
    public bool RightsConfirmed { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool CanManage { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class DigitalAssetEditViewModel
{
    public int Id { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Validation.SelectItem")] public int BookId { get; set; }
    public IFormFile? File { get; set; }
    public DigitalFileFormat FileFormat { get; set; }
    [Required(ErrorMessage = "Validation.Required"), StringLength(40, ErrorMessage = "Validation.StringLength")] public string Language { get; set; } = "Қазақша";
    public DigitalLicenseType LicenseType { get; set; }
    [StringLength(200, ErrorMessage = "Validation.StringLength")] public string? LicenseName { get; set; }
    [StringLength(1000, ErrorMessage = "Validation.StringLength")] public string? LicenseUrl { get; set; }
    [StringLength(1000, ErrorMessage = "Validation.StringLength")] public string? SourceUrl { get; set; }
    [StringLength(200, ErrorMessage = "Validation.StringLength")] public string? RightsHolder { get; set; }
    public bool RightsConfirmed { get; set; }
    public DigitalAccessLevel AccessLevel { get; set; }
    public bool AllowDownload { get; set; }
    public bool IsPublished { get; set; }
    public string? RowVersion { get; set; }
    public string OriginalFileName { get; set; } = "";
    public int PageCount { get; set; }
    public long MaxFileBytes { get; set; }
    public List<SelectListItem> Books { get; set; } = [];
}

public sealed class ReaderViewModel
{
    public DigitalAssetDetailsViewModel Asset { get; set; } = new();
    public ReadingProgressViewModel Progress { get; set; } = new();
    public List<ReadingBookmarkViewModel> Bookmarks { get; set; } = [];
    public string FileUrl => $"/Reader/File/{Asset.Id}";
}

public sealed class ReadingProgressViewModel
{
    public int AssetId { get; set; }
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public string? EpubCfi { get; set; }
    public decimal ProgressPercentage { get; set; }
    public DateTime? LastReadAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class SaveReadingProgressViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Validation.SelectItem")] public int AssetId { get; set; }
    [Range(0, 100000, ErrorMessage = "Validation.Range")] public int CurrentPage { get; set; }
    [Range(0, 100000, ErrorMessage = "Validation.Range")] public int TotalPages { get; set; }
    [StringLength(1024, ErrorMessage = "Validation.StringLength")] public string? EpubCfi { get; set; }
    [Range(0, 100, ErrorMessage = "Validation.Range")] public decimal ProgressPercentage { get; set; }
}

public sealed class ReadingBookmarkEditViewModel
{
    public int Id { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Validation.SelectItem")] public int AssetId { get; set; }
    [Range(1, 100000, ErrorMessage = "Validation.Range")] public int? PageNumber { get; set; }
    [StringLength(1024, ErrorMessage = "Validation.StringLength")] public string? EpubCfi { get; set; }
    [Required(ErrorMessage = "Validation.Required"), StringLength(200, ErrorMessage = "Validation.StringLength")] public string Title { get; set; } = "";
    [StringLength(2000, ErrorMessage = "Validation.StringLength")] public string? Note { get; set; }
}

public sealed class ReadingBookmarkViewModel
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public int? PageNumber { get; set; }
    public string? EpubCfi { get; set; }
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
