using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduLibrary.ViewModels;

public sealed record BookRecommendationContext(double Score,DateTime CalculatedAt);

public sealed class RecommendationViewModel
{
    public bool IsStaff { get; set; }
    public int? StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string Faculty { get; set; } = "";
    public bool IncludePreviouslyRead { get; set; }
    public bool IsColdStart { get; set; }
    public bool HasMissingSignals { get; set; }
    public bool CandidateLimitReached { get; set; }
    public int CandidateCount { get; set; }
    public List<SelectListItem> Students { get; set; } = [];
    public List<string> Interests { get; set; } = [];
    public List<string> Subjects { get; set; } = [];
    public List<RecommendationSection> Sections { get; set; } = [];
    public string ActualMode => Sections.SelectMany(s => s.Books).Any(b => b.IsLlmEnhanced) ? "LLM-enhanced explanations" : "Local Recommendation Engine";
}

public sealed class RecommendationSection
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string EmptyMessage { get; set; } = "";
    public List<RecommendationResult> Books { get; set; } = [];
}

public sealed class RecommendationResult
{
    public string? DetailsToken { get; set; }
    public int BookId { get; set; }
    public string Title { get; set; } = "";
    public string Authors { get; set; } = "";
    public int PublicationYear { get; set; }
    public string? CoverImageUrl { get; set; }
    public List<string> Categories { get; set; } = [];
    public List<string> MatchingInterests { get; set; } = [];
    public List<string> MatchingSubjects { get; set; } = [];
    public List<string> HistoryMatches { get; set; } = [];
    public int AvailableCopies { get; set; }
    public int TotalLoans { get; set; }
    public int FacultyLoans { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public double RelevanceScore { get; set; }
    public double InterestScore { get; set; }
    public double SubjectScore { get; set; }
    public double HistoryScore { get; set; }
    public double PopularityScore { get; set; }
    public List<RecommendationFactor> Breakdown { get; set; } = [];
    public string Explanation { get; set; } = "";
    public bool IsLlmEnhanced { get; set; }
}

public sealed record RecommendationFactor(string Label, double Value, double ConfiguredWeight, double EffectiveWeight)
{
    public double Contribution => Value * EffectiveWeight * 100;
}

public sealed class RecommendationPreferencesViewModel
{
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string Faculty { get; set; } = "";
    [StringLength(1000, ErrorMessage = "Қызығушылықтар тізімі 1000 таңбадан аспасын."), Display(Name = "Академиялық қызығушылықтар")]
    public string? Interests { get; set; }
    [Display(Name = "Оқитын пәндер")]
    public List<int> SelectedSubjectIds { get; set; } = [];
    public List<SelectListItem> Subjects { get; set; } = [];
}

// These internal scoring inputs are never serialized as a provider request.
public sealed class StudentPreferenceProfile
{
    public int StudentId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Faculty { get; set; } = "";
    public List<string> Interests { get; set; } = [];
    public List<RecommendationFacet> Subjects { get; set; } = [];
    public HashSet<int> ActiveBookIds { get; set; } = [];
    public HashSet<int> PreviouslyReadBookIds { get; set; } = [];
    public bool HasHistory { get; set; }
}

public sealed record RecommendationFacet(int Id, string Name);

public sealed class RecommendationCandidate
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int PublicationYear { get; set; }
    public string? CoverImageUrl { get; set; }
    public List<RecommendationFacet> Authors { get; set; } = [];
    public List<RecommendationFacet> Categories { get; set; } = [];
    public List<RecommendationFacet> Subjects { get; set; } = [];
    public int AvailableCopies { get; set; }
    public int TotalLoans { get; set; }
    public int FacultyLoans { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
}

public sealed class RecommendationHistory
{
    public Dictionary<int, double> Authors { get; set; } = [];
    public Dictionary<int, double> Categories { get; set; } = [];
    public Dictionary<int, double> Subjects { get; set; } = [];
}

// The external provider contract contains no reader identifiers or academic cohort.
public sealed record RecommendationExplanationInput(string Token, string Title, string Authors, IReadOnlyList<string> Categories, IReadOnlyList<string> MatchingInterests, IReadOnlyList<string> MatchingSubjects);
