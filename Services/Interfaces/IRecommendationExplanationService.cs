using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface IRecommendationExplanationService
{
    Task ApplyAsync(IReadOnlyList<RecommendationResult> recommendations, CancellationToken ct = default);
}

public interface IAIExplanationProvider
{
    Task<IReadOnlyDictionary<string, string>> ExplainAsync(IReadOnlyList<RecommendationExplanationInput> books, CancellationToken ct = default);
}
