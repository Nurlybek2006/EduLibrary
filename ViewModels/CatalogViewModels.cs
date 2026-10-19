using System.ComponentModel.DataAnnotations;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduLibrary.ViewModels;

public sealed class BookFilterViewModel
{
    [StringLength(200, ErrorMessage = "Іздеу мәтіні 200 таңбадан аспауы тиіс.")]
    public string? Search { get; set; }
    public int? AuthorId { get; set; }
    public int? PublisherId { get; set; }
    public int? CategoryId { get; set; }
    public int? SubjectId { get; set; }
    public bool AvailableOnly { get; set; }
    public string? Language { get; set; }
    public string Sort { get; set; } = "title";
    public bool IncludeArchived { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class BookCatalogViewModel
{
    public BookFilterViewModel Filter { get; set; } = new();
    public IReadOnlyList<BookCardViewModel> Books { get; set; } = [];
    public IReadOnlyList<SelectListItem> Authors { get; set; } = [];
    public IReadOnlyList<SelectListItem> Publishers { get; set; } = [];
    public IReadOnlyList<SelectListItem> Categories { get; set; } = [];
    public IReadOnlyList<SelectListItem> Languages { get; set; } = [];
    public IReadOnlyList<SelectListItem> Subjects { get; set; } = [];
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public sealed class BookCardViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string ISBN { get; set; } = "";
    public string Authors { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Language { get; set; } = "";
    public int PublicationYear { get; set; }
    public string? CoverImageUrl { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public bool IsArchived { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public IReadOnlyList<string> Categories { get; set; } = [];
}

public sealed class BookDetailsViewModel
{
    public BookRecommendationContext? RecommendationContext { get; set; }
    public BookCardViewModel Book { get; set; } = new();
    public string Description { get; set; } = "";
    public int PageCount { get; set; }
    public IReadOnlyList<string> Categories { get; set; } = [];
    public IReadOnlyList<CopyListItemViewModel> Copies { get; set; } = [];
    public IReadOnlyList<string> Subjects { get; set; } = [];
}

public sealed class BookEditViewModel : IValidatableObject
{
    public int Id { get; set; }
    [Required(ErrorMessage = "ISBN енгізіңіз."), StringLength(20, ErrorMessage = "ISBN 20 таңбадан аспауы тиіс.")]
    [RegularExpression(@"[0-9Xx\- ]{10,20}", ErrorMessage = "ISBN тек сандардан, X, бос орын мен дефистен тұруы тиіс (10–20 таңба).")]
    [Display(Name = "ISBN")]
    public string ISBN { get; set; } = "";
    [Required(ErrorMessage = "Кітап атауын енгізіңіз."), StringLength(300, ErrorMessage = "Атауы 300 таңбадан аспауы тиіс.")]
    [Display(Name = "Кітап атауы")]
    public string Title { get; set; } = "";
    [StringLength(5000, ErrorMessage = "Сипаттама 5000 таңбадан аспауы тиіс.")]
    [Display(Name = "Сипаттамасы")]
    public string? Description { get; set; }
    [Range(1000, 2200, ErrorMessage = "Басылым жылы 1000–2200 аралығында болуы тиіс.")]
    [Display(Name = "Басылым жылы")]
    public int PublicationYear { get; set; } = DateTime.UtcNow.Year;
    [Required(ErrorMessage = "Кітап тілін енгізіңіз."), StringLength(50, ErrorMessage = "Тіл атауы 50 таңбадан аспауы тиіс.")]
    [Display(Name = "Тілі")]
    public string Language { get; set; } = "Қазақша";
    [Range(1, 50000, ErrorMessage = "Беттер саны 1–50000 аралығында болуы тиіс.")]
    [Display(Name = "Беттер саны")]
    public int PageCount { get; set; } = 1;
    [StringLength(1000, ErrorMessage = "Мұқаба сілтемесі 1000 таңбадан аспауы тиіс.")]
    [Display(Name = "Мұқаба сілтемесі (HTTPS)")]
    public string? CoverImageUrl { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Баспаны таңдаңыз.")]
    [Display(Name = "Баспа")]
    public int PublisherId { get; set; }
    [MinLength(1, ErrorMessage = "Кемінде бір авторды таңдаңыз.")]
    [Display(Name = "Авторлар")]
    public List<int> AuthorIds { get; set; } = [];
    [MinLength(1, ErrorMessage = "Кемінде бір санатты таңдаңыз.")]
    [Display(Name = "Санаттар")]
    public List<int> CategoryIds { get; set; } = [];
    [Display(Name="Пәндер")]
    public List<int> SubjectIds { get; set; } = [];
    public IReadOnlyList<SelectListItem> Authors { get; set; } = [];
    public IReadOnlyList<SelectListItem> Publishers { get; set; } = [];
    public IReadOnlyList<SelectListItem> Categories { get; set; } = [];
    public IReadOnlyList<SelectListItem> Subjects { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(CoverImageUrl) &&
            (!Uri.TryCreate(CoverImageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            yield return new ValidationResult("Мұқаба үшін жарамды HTTPS сілтемесін енгізіңіз.", [nameof(CoverImageUrl)]);
        var today = (validationContext.GetService(typeof(ILibraryClock)) as ILibraryClock)?.Today ?? DateTime.Today;
        if (PublicationYear > today.Year + 1)
            yield return new ValidationResult("Басылым жылы келесі жылдан аспауы тиіс.", [nameof(PublicationYear)]);
        var isbn = ISBN?.Replace("-", "").Replace(" ", "");
        if (isbn is not null && isbn.Length is not (10 or 13))
            yield return new ValidationResult("ISBN 10 немесе 13 таңбадан тұруы тиіс.", [nameof(ISBN)]);
    }
}

public sealed class AuthorEditViewModel
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Автордың атын енгізіңіз."), StringLength(100, ErrorMessage = "Аты 100 таңбадан аспауы тиіс.")]
    [Display(Name = "Аты")]
    public string FirstName { get; set; } = "";
    [Required(ErrorMessage = "Автордың тегін енгізіңіз."), StringLength(100, ErrorMessage = "Тегі 100 таңбадан аспауы тиіс.")]
    [Display(Name = "Тегі")]
    public string LastName { get; set; } = "";
    [StringLength(4000, ErrorMessage = "Өмірбаяны 4000 таңбадан аспауы тиіс.")]
    [Display(Name = "Өмірбаяны")]
    public string? Biography { get; set; }
}

public sealed class PublisherEditViewModel
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Баспа атауын енгізіңіз."), StringLength(200, ErrorMessage = "Атауы 200 таңбадан аспауы тиіс.")]
    [Display(Name = "Баспа атауы")]
    public string Name { get; set; } = "";
    [Required(ErrorMessage = "Елін енгізіңіз."), StringLength(100, ErrorMessage = "Ел атауы 100 таңбадан аспауы тиіс.")]
    [Display(Name = "Елі")]
    public string Country { get; set; } = "";
    [Url(ErrorMessage = "Жарамды веб-сайт сілтемесін енгізіңіз."), StringLength(500, ErrorMessage = "Сілтеме 500 таңбадан аспауы тиіс.")]
    [Display(Name = "Веб-сайт")]
    public string? Website { get; set; }
}

public sealed class CategoryEditViewModel
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Санат атауын енгізіңіз."), StringLength(100, ErrorMessage = "Атауы 100 таңбадан аспауы тиіс.")]
    [Display(Name = "Санат атауы")]
    public string Name { get; set; } = "";
    [StringLength(2000, ErrorMessage = "Сипаттама 2000 таңбадан аспауы тиіс.")]
    [Display(Name = "Сипаттамасы")]
    public string? Description { get; set; }
}

public sealed class CatalogLookupViewModel
{
    public string? Search { get; set; }
    public IReadOnlyList<CatalogLookupItemViewModel> Items { get; set; } = [];
}

public sealed class CatalogLookupItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int BookCount { get; set; }
}

public sealed class CopiesIndexViewModel
{
    public string? Search { get; set; }
    public int? BookId { get; set; }
    public CopyStatus? Status { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<CopyListItemViewModel> Copies { get; set; } = [];
}

public sealed class CopyListItemViewModel
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = "";
    public string InventoryNumber { get; set; } = "";
    public string Barcode { get; set; } = "";
    public CopyStatus Status { get; set; }
    public string StatusLabel => CatalogLabels.CopyStatusLabel(Status);
    public string ShelfLocation { get; set; } = "";
    public string Condition { get; set; } = "";
    public DateTime AcquiredAt { get; set; }
    public string RowVersion { get; set; } = "";
    public bool HasLoans { get; set; }
}

public sealed class CopyEditViewModel : IValidatableObject
{
    public int Id { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Кітапты таңдаңыз.")]
    [Display(Name = "Кітап")]
    public int BookId { get; set; }
    [Required(ErrorMessage = "Түгендеу нөмірін енгізіңіз."), StringLength(60, ErrorMessage = "Түгендеу нөмірі 60 таңбадан аспауы тиіс.")]
    [Display(Name = "Түгендеу нөмірі")]
    public string InventoryNumber { get; set; } = "";
    [StringLength(80, ErrorMessage = "Штрихкод 80 таңбадан аспауы тиіс.")]
    [Display(Name = "Штрихкод")]
    public string? Barcode { get; set; }
    [EnumDataType(typeof(CopyStatus), ErrorMessage = "Жарамды мәртебені таңдаңыз.")]
    [Display(Name = "Мәртебесі")]
    public CopyStatus Status { get; set; }
    [Required(ErrorMessage = "Сөре орнын енгізіңіз."), StringLength(100, ErrorMessage = "Сөре орны 100 таңбадан аспауы тиіс.")]
    [Display(Name = "Сөре орны")]
    public string ShelfLocation { get; set; } = "";
    [Required(ErrorMessage = "Дананың жағдайын енгізіңіз."), StringLength(500, ErrorMessage = "Жағдайы 500 таңбадан аспауы тиіс.")]
    [Display(Name = "Физикалық жағдайы")]
    public string Condition { get; set; } = "Жақсы";
    [Required(ErrorMessage = "Қабылданған күнді енгізіңіз."), DataType(DataType.Date)]
    [Display(Name = "Қабылданған күні")]
    public DateTime AcquiredAt { get; set; } = DateTime.Today;
    public string? RowVersion { get; set; }
    public bool IsOnLoan { get; set; }
    public IReadOnlyList<SelectListItem> Books { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var today = (validationContext.GetService(typeof(ILibraryClock)) as ILibraryClock)?.Today ?? DateTime.Today;
        if (AcquiredAt.Date > today)
            yield return new ValidationResult("Қабылданған күн болашақта болмауы тиіс.", [nameof(AcquiredAt)]);
        if (AcquiredAt.Year < 1900)
            yield return new ValidationResult("Қабылданған күн 1900 жылдан ерте болмауы тиіс.", [nameof(AcquiredAt)]);
    }
}

public static class CatalogLabels
{
    public static string CopyStatusLabel(CopyStatus status) => status switch
    {
        CopyStatus.Available => "Қолжетімді",
        CopyStatus.OnLoan => "Оқырманда",
        CopyStatus.Damaged => "Зақымдалған",
        CopyStatus.Lost => "Жоғалған",
        CopyStatus.Maintenance => "Жөндеуде",
        _ => "Белгісіз"
    };
    public static IEnumerable<SelectListItem> CopyStatuses(bool includeOnLoan = true) =>
        Enum.GetValues<CopyStatus>().Where(x => includeOnLoan || x != CopyStatus.OnLoan)
            .Select(x => new SelectListItem(CopyStatusLabel(x), ((int)x).ToString()));
}
