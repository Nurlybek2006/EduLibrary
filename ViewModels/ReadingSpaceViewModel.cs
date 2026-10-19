namespace EduLibrary.ViewModels;

public sealed class ReadingSpaceViewModel
{
    public bool HasStudentProfile { get; set; }
    public int StudentId { get; set; }
    public string FullName { get; set; } = "";
    public string Faculty { get; set; } = "";
    public string GroupName { get; set; } = "";
    public int ActiveLoanCount { get; set; }
    public int ReturnedBookCount { get; set; }
    public decimal OutstandingFines { get; set; }
    public List<string> Interests { get; set; } = [];
    public List<string> Subjects { get; set; } = [];
    public List<ReadingShelfBookViewModel> CurrentBooks { get; set; } = [];
    public List<ReadingShelfBookViewModel> History { get; set; } = [];
    public List<ReadingActivityViewModel> RecentActivity { get; set; } = [];
    public List<RecommendationResult> Recommendations { get; set; } = [];
    public string RecommendationMode => Recommendations.Any(b => b.IsLlmEnhanced) ? "LLM-enhanced explanations" : "Local Recommendation Engine";
}

public sealed class ReadingShelfBookViewModel
{
    public int LoanId { get; set; }
    public int BookId { get; set; }
    public string Title { get; set; } = "";
    public List<string> AuthorNames { get; set; } = [];
    public string Authors => string.Join(", ", AuthorNames);
    public string? CoverImageUrl { get; set; }
    public int PublicationYear { get; set; }
    public DateTime LoanDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public int DaysUntilDue { get; set; }
    public bool IsArchived { get; set; }
    public string DeadlineLabel => DaysUntilDue < 0 ? $"{-DaysUntilDue} күн кешіккен" : DaysUntilDue == 0 ? "Бүгін қайтару керек" : $"{DaysUntilDue} күн қалды";
}

public sealed class ReadingActivityViewModel
{
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Link { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
