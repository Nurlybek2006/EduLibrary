using System.Globalization;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.Extensions.Options;

namespace EduLibrary.Services.Implementations;

public sealed class TemplateExplanationProvider
{
    public string Explain(RecommendationResult book)
    {
        var reasons = new List<string>();
        if (book.MatchingInterests.Count > 0) reasons.Add(UiText.Get("Recommendations.Interests",string.Join(", ", book.MatchingInterests.Take(3))));
        if (book.MatchingSubjects.Count > 0) reasons.Add(UiText.Get("Recommendations.Subjects",string.Join(", ", book.MatchingSubjects.Take(2))));
        if (book.HistoryMatches.Count > 0 && book.HistoryScore > 0) reasons.Add(UiText.Get("Recommendations.History",string.Join(", ", book.HistoryMatches.Take(2))));
        if (reasons.Count == 0) reasons.Add(UiText.Get("Recommendations.ColdStart"));
        if (book.FacultyLoans > 0) reasons.Add(UiText.Get("Recommendations.FacultyLoans",book.FacultyLoans));
        else if (book.TotalLoans > 0) reasons.Add(UiText.Get("Recommendations.TotalLoans",book.TotalLoans));
        if (book.ReviewCount > 0) reasons.Add(UiText.Get("Recommendations.Rating",book.ReviewCount,book.AverageRating.ToString("0.0", CultureInfo.CurrentCulture)));
        reasons.Add(book.AvailableCopies > 0 ? UiText.Get("Recommendations.Available",book.AvailableCopies) : UiText.Get("Recommendations.Unavailable"));
        return string.Join(" ", reasons);
    }
}

public sealed class RecommendationExplanationService(TemplateExplanationProvider templates, OpenAICompatibleExplanationProvider llm,
    IOptions<RecommendationOptions> configuration) : IRecommendationExplanationService
{
    private static readonly HashSet<string> SafeAcademicTokens = ["programming", "algorithm", "database", "data", "network", "artificial", "intelligence", "learning", "machine", "mathematics", "physics", "design", "literature", "history", "python", "java", "javascript", "typescript", "sql", "csharp", "economics", "экономика", "biology", "биология", "chemistry", "химия", "философия", "philosophy", "statistics", "статистика", "linear", "algebra", "алгебра", "security", "қауіпсіздік"];

    public async Task ApplyAsync(IReadOnlyList<RecommendationResult> recommendations, CancellationToken ct = default)
    {
        foreach (var book in recommendations) { book.Explanation = templates.Explain(book); book.IsLlmEnhanced = false; }
        if (!configuration.Value.Llm.Enabled || recommendations.Count == 0) return;
        var chosen = recommendations.Take(Math.Clamp(configuration.Value.Llm.MaxExplanations, 1, 24)).ToList();
        var inputs = chosen.Select((book, index) => ToProviderInput(book, "item" + index)).ToList();
        var enhanced = await llm.ExplainAsync(inputs, ct);
        for (var i = 0; i < chosen.Count; i++)
            if (enhanced.TryGetValue(inputs[i].Token, out var explanation)) { chosen[i].Explanation = explanation; chosen[i].IsLlmEnhanced = true; }
    }

    public static RecommendationExplanationInput ToProviderInput(RecommendationResult book, string token)
    {
        // Raw free-text reader interests are never transmitted. Only recognized academic
        // concept tokens survive; names, contacts, identifiers and faculty are absent.
        var interests = book.MatchingInterests.SelectMany(RecommendationScoring.Tokens).Where(SafeAcademicTokens.Contains).Distinct().Take(10).ToArray();
        return new RecommendationExplanationInput(token, Limit(book.Title, 300), Limit(book.Authors, 250),
            book.Categories.Where(IsSafeTerm).Take(8).Select(s => Limit(s, 100)).ToArray(), interests,
            book.MatchingSubjects.Where(IsSafeTerm).Take(8).Select(s => Limit(s, 100)).ToArray());
    }

    private static bool IsSafeTerm(string term) => !term.Contains('@') && !term.Contains("http", StringComparison.OrdinalIgnoreCase)
        && !System.Text.RegularExpressions.Regex.IsMatch(term, @"\d{4,}", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}
