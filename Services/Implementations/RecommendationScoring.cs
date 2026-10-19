using System.Text.RegularExpressions;
using EduLibrary.Helpers;
using EduLibrary.ViewModels;

namespace EduLibrary.Services.Implementations;

public static class RecommendationScoring
{
    private static readonly Regex WordSeparator = new(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> StopWords = ["the", "a", "an", "of", "to", "and", "in", "for", "with", "introduction", "негіздері", "негізі", "бойынша", "және", "мен", "үшін", "основы", "и", "в"];
    private static readonly Dictionary<string, string> Concepts = BuildConcepts();

    public static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    public static HashSet<string> Tokens(string? value) => WordSeparator.Split((value ?? "").ToLowerInvariant())
        .Where(w => w.Length > 1 && !StopWords.Contains(w)).Select(w => Concepts.GetValueOrDefault(w, w)).ToHashSet(StringComparer.Ordinal);

    public static double TextSimilarity(string preference, string metadata)
    {
        var requested = Tokens(preference);
        if (requested.Count == 0) return 0;
        var candidate = Tokens(metadata);
        return Clamp(requested.Count(candidate.Contains) / (double)requested.Count);
    }

    public static double NormalizePopularity(int count, int maximum) => maximum <= 0 || count <= 0 ? 0 : Clamp(Math.Log(1d + count) / Math.Log(1d + maximum));
    public static double NormalizeRating(double average, int count) => count <= 0 ? 0 : Clamp((average - 1) / 4) * count / (count + 5d);

    public static bool IsEligible(int bookId, StudentPreferenceProfile profile, bool excludePreviouslyRead) =>
        !profile.ActiveBookIds.Contains(bookId) && (!excludePreviouslyRead || !profile.PreviouslyReadBookIds.Contains(bookId));

    public static List<RecommendationFactor> Factors(double interests, double subjects, double history, double popularity, bool hasInterests, bool hasSubjects, bool hasHistory, RecommendationOptions options)
    {
        var weights = new[] { hasInterests ? options.InterestWeight : 0, hasSubjects ? options.SubjectWeight : 0, hasHistory ? options.HistoryWeight : 0, options.PopularityWeight };
        var sum = weights.Sum();
        // No personal signal with a disabled popularity weight still needs an honest cold-start fallback.
        if (sum <= 0) { weights[3] = 1; sum = 1; }
        return [new("Қызығушылықтар", hasInterests ? Clamp(interests) : 0, options.InterestWeight, weights[0] / sum),
            new("Пәндер", hasSubjects ? Clamp(subjects) : 0, options.SubjectWeight, weights[1] / sum),
            new("Оқу тарихы мен бағалар", hasHistory ? Clamp(history) : 0, options.HistoryWeight, weights[2] / sum),
            new("Танымалдық, рейтинг және факультет", Clamp(popularity), options.PopularityWeight, weights[3] / sum)];
    }

    public static double HistoryAffinity(IEnumerable<int> ids, IReadOnlyDictionary<int, double> learned)
    {
        if (learned.Count == 0) return 0;
        var strongest = learned.Values.Select(Math.Abs).DefaultIfEmpty(0).Max();
        if (strongest <= 0) return 0;
        return ids.Select(id => learned.GetValueOrDefault(id) / strongest).DefaultIfEmpty(0).Average();
    }

    public static RecommendationResult Score(RecommendationCandidate book, StudentPreferenceProfile profile, RecommendationHistory history, int maxLoans, int maxFacultyLoans, RecommendationOptions options)
    {
        var metadata = string.Join(" ", new[] { book.Title, book.Description }.Concat(book.Categories.Select(c => c.Name)).Concat(book.Subjects.Select(s => s.Name)));
        var interestMatches = profile.Interests.Select(i => (Name: i, Score: TextSimilarity(i, metadata))).ToList();
        var subjectMatches = profile.Subjects.Select(s => (s.Name, Score: book.Subjects.Any(b => b.Id == s.Id) ? 1 : .75 * TextSimilarity(s.Name, metadata))).ToList();
        var interestScore = interestMatches.Select(i => i.Score).DefaultIfEmpty(0).Average();
        var subjectScore = subjectMatches.Select(i => i.Score).DefaultIfEmpty(0).Average();
        var historyScore = Clamp(.55 * HistoryAffinity(book.Categories.Select(c => c.Id), history.Categories)
            + .35 * HistoryAffinity(book.Authors.Select(a => a.Id), history.Authors) + .10 * HistoryAffinity(book.Subjects.Select(s => s.Id), history.Subjects));
        var globalPopularity = NormalizePopularity(book.TotalLoans, maxLoans);
        var ratings = NormalizeRating(book.AverageRating, book.ReviewCount);
        var fieldPopularity = NormalizePopularity(book.FacultyLoans, maxFacultyLoans);
        var popularityScore = maxFacultyLoans > 0 ? .45 * globalPopularity + .35 * ratings + .20 * fieldPopularity : .60 * globalPopularity + .40 * ratings;
        var factors = Factors(interestScore, subjectScore, historyScore, popularityScore, profile.Interests.Count > 0, profile.Subjects.Count > 0, profile.HasHistory, options);
        return new RecommendationResult
        {
            BookId = book.Id, Title = book.Title, Authors = string.Join(", ", book.Authors.Select(a => a.Name)), PublicationYear = book.PublicationYear,
            CoverImageUrl = book.CoverImageUrl, Categories = book.Categories.Select(c => c.Name).ToList(), AvailableCopies = book.AvailableCopies,
            TotalLoans = book.TotalLoans, FacultyLoans = book.FacultyLoans, AverageRating = book.AverageRating, ReviewCount = book.ReviewCount,
            MatchingInterests = interestMatches.Where(i => i.Score > 0).Select(i => i.Name).ToList(), MatchingSubjects = subjectMatches.Where(i => i.Score > 0).Select(i => i.Name).ToList(),
            HistoryMatches = book.Categories.Where(c => history.Categories.GetValueOrDefault(c.Id) > 0).Select(c => c.Name)
                .Concat(book.Authors.Where(a => history.Authors.GetValueOrDefault(a.Id) > 0).Select(a => a.Name)).ToList(),
            InterestScore = interestScore, SubjectScore = subjectScore, HistoryScore = historyScore, PopularityScore = Clamp(popularityScore),
            Breakdown = factors, RelevanceScore = Math.Round(factors.Sum(f => f.Contribution), 2)
        };
    }

    public static IOrderedEnumerable<RecommendationResult> Rank(IEnumerable<RecommendationResult> books, bool prioritizeAvailable) => books
        .OrderByDescending(b => prioritizeAvailable && b.AvailableCopies > 0).ThenByDescending(b => b.RelevanceScore)
        .ThenByDescending(b => b.AvailableCopies).ThenByDescending(b => b.TotalLoans).ThenBy(b => b.BookId);

    private static Dictionary<string, string> BuildConcepts()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in new[] { "programming|бағдарламалау|программалау|программирование|coding|code", "algorithm|algorithms|алгоритм|алгоритмдер|алгоритмы|алгоритмдік", "database|databases|қоры|базасы|базы", "data|деректер|деректердің|данных", "network|networks|networking|желілер|желілері|желілердің|сети", "artificial|жасанды|искусственный", "intelligence|интеллект|интеллекті", "learning|оқыту|обучение", "machine|машиналық|машинное", "mathematics|математика|математикалық|математики", "physics|физика|физикалық", "design|дизайн|дизайны", "literature|әдебиет|әдебиеттер|литература", "history|тарих|тарихы|история" })
        {
            var words = group.Split('|');
            foreach (var word in words) result[word] = words[0];
        }
        return result;
    }
}
