using System.ComponentModel.DataAnnotations;

namespace EduLibrary.ViewModels;

public sealed class ReviewEditViewModel
{
    public int Id { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Кітап таңдалмады.")]
    public int BookId { get; set; }
    public string BookTitle { get; set; } = "";
    [Range(1, 5, ErrorMessage = "Баға 1–5 жұлдыз аралығында болуы тиіс.")]
    [Display(Name = "Бағаңыз")]
    public int Rating { get; set; } = 5;
    [StringLength(3000, ErrorMessage = "Пікір 3000 таңбадан аспауы тиіс.")]
    [Display(Name = "Пікіріңіз")]
    public string? Comment { get; set; }
    public string? RowVersion { get; set; }
    public bool IsHidden { get; set; }
}

public sealed class ReviewRowViewModel
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = "";
    public string ReviewerLabel { get; set; } = "Оқырман";
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsOwn { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class BookReviewsViewModel
{
    public int BookId { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public bool CanWrite { get; set; }
    public bool BookArchived { get; set; }
    public ReviewRowViewModel? OwnReview { get; set; }
    public IReadOnlyList<ReviewRowViewModel> Reviews { get; set; } = [];
}

public sealed class ReviewModerationViewModel
{
    public string? Search { get; set; }
    public int? BookId { get; set; }
    public string Status { get; set; } = "active";
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public IReadOnlyList<ReviewRowViewModel> Reviews { get; set; } = [];
}
