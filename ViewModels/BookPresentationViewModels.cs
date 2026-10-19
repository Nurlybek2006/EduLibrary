namespace EduLibrary.ViewModels;

public sealed class BookCoverViewModel
{
    public int BookId { get; set; }
    public string Title { get; set; } = "";
    public string Authors { get; set; } = "";
    public string? CoverImageUrl { get; set; }
    public int PublicationYear { get; set; }
    public string? Category { get; set; }
    public string Size { get; set; } = "standard";
    public bool EagerLoad { get; set; }
}

public sealed class BookCardPresentationViewModel
{
    public int BookId { get; set; }
    public string Title { get; set; } = "";
    public string Authors { get; set; } = "";
    public string? CoverImageUrl { get; set; }
    public int PublicationYear { get; set; }
    public string? Category { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public int AvailableCopies { get; set; }
    public int TotalCopies { get; set; }
    public bool IsArchived { get; set; }
    public string? Language { get; set; }
    public string? DetailsUrl { get; set; }
}
