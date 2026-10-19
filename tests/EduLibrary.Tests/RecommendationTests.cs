using System.Net;
using System.Text.Json;
using EduLibrary.Helpers;
using EduLibrary.Services.Implementations;
using EduLibrary.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EduLibrary.Tests;

public sealed class RecommendationTests
{
    [Theory]
    [InlineData("Python", "Python Crash Course", 1)]
    [InlineData("PYTHON", "python", 1)]
    [InlineData("алгоритмдер", "Introduction to Algorithms", 1)]
    [InlineData("Бағдарламалау негіздері", "Clean Code", 1)]
    [InlineData("физика", "Database System Concepts", 0)]
    [InlineData("", "Python", 0)]
    public void InterestMatchingIsDeterministicAndBilingual(string interest, string metadata, double expected) =>
        Assert.Equal(expected, RecommendationScoring.TextSimilarity(interest, metadata), 8);

    [Fact]
    public void FourConfiguredWeightsProduceExpectedWeightedScore()
    {
        var factors = RecommendationScoring.Factors(1, .5, .2, .8, true, true, true, new RecommendationOptions());
        Assert.Equal(63.5, factors.Sum(f => f.Contribution), 8);
        Assert.Equal(1, factors.Sum(f => f.EffectiveWeight), 8);
        Assert.Equal(new[] { .30, .25, .25, .20 }, factors.Select(f => f.EffectiveWeight));
    }

    [Fact]
    public void MissingPersonalDataRedistributesWeightsWithoutInventingMatches()
    {
        var factors = RecommendationScoring.Factors(1, 1, 1, .4, false, false, false, new RecommendationOptions());
        Assert.All(factors.Take(3), f => { Assert.Equal(0, f.Value); Assert.Equal(0, f.EffectiveWeight); });
        Assert.Equal(1, factors[3].EffectiveWeight);
        Assert.Equal(40, factors.Sum(f => f.Contribution), 8);
    }

    [Fact]
    public void ColdStartWorksWhenConfiguredPopularityWeightIsZero()
    {
        var factors = RecommendationScoring.Factors(0, 0, 0, .5, false, false, false, new RecommendationOptions { PopularityWeight = 0 });
        Assert.Equal(50, factors.Sum(f => f.Contribution));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-3)]
    public void InvalidFactorsCannotEscapeNormalizedRange(double value)
    {
        var factors = RecommendationScoring.Factors(value, value, value, value, true, true, true, new RecommendationOptions());
        Assert.All(factors, factor => Assert.InRange(factor.Value, 0, 1));
    }

    [Fact]
    public void ActiveBooksAreAlwaysExcludedEvenWhenPreviouslyReadAreAllowed()
    {
        var profile = new StudentPreferenceProfile { ActiveBookIds = [10], PreviouslyReadBookIds = [10, 20] };
        Assert.False(RecommendationScoring.IsEligible(10, profile, false));
        Assert.False(RecommendationScoring.IsEligible(20, profile, true));
        Assert.True(RecommendationScoring.IsEligible(20, profile, false));
        Assert.True(RecommendationScoring.IsEligible(30, profile, true));
    }

    [Fact]
    public void ExplicitSubjectRelationshipHasFullRelevance()
    {
        var book = new RecommendationCandidate { Id = 1, Title = "Учебник", Subjects = [new(7, "Деректер қоры")] };
        var profile = new StudentPreferenceProfile { Subjects = [new(7, "Деректер қоры")] };
        var result = RecommendationScoring.Score(book, profile, new(), 0, 0, new());
        Assert.Equal(1, result.SubjectScore);
        Assert.Contains("Деректер қоры", result.MatchingSubjects);
    }

    [Fact]
    public void LexicalSubjectMatchIsWeakerThanExplicitCurriculumLink()
    {
        var book = new RecommendationCandidate { Id = 1, Title = "Python", Categories = [new(2, "Бағдарламалау")] };
        var profile = new StudentPreferenceProfile { Subjects = [new(7, "Бағдарламалау негіздері")] };
        var result = RecommendationScoring.Score(book, profile, new(), 0, 0, new());
        Assert.Equal(.75, result.SubjectScore);
    }

    [Fact]
    public void RatingsUseConfidenceInsteadOfTreatingOneReviewAsCertain()
    {
        Assert.Equal(0, RecommendationScoring.NormalizeRating(0, 0));
        Assert.Equal(0, RecommendationScoring.NormalizeRating(1, 10));
        Assert.True(RecommendationScoring.NormalizeRating(5, 50) > RecommendationScoring.NormalizeRating(5, 1));
        Assert.InRange(RecommendationScoring.NormalizeRating(5, 50), 0, 1);
    }

    [Fact]
    public void PopularityNormalizationHandlesAnEmptyLibrary()
    {
        Assert.Equal(0, RecommendationScoring.NormalizePopularity(0, 0));
        Assert.Equal(1, RecommendationScoring.NormalizePopularity(10, 10));
        Assert.InRange(RecommendationScoring.NormalizePopularity(3, 10), 0, 1);
    }

    [Fact]
    public void NegativeReviewEvidenceReducesLearnedCategoryAffinity()
    {
        var positive = RecommendationScoring.HistoryAffinity([1, 2], new Dictionary<int, double> { [1] = 2, [2] = 1 });
        var mixed = RecommendationScoring.HistoryAffinity([1, 2], new Dictionary<int, double> { [1] = 2, [2] = -1 });
        Assert.True(positive > mixed);
    }

    [Fact]
    public void AvailabilityHasPriorityAndTiesAreStableByBookId()
    {
        RecommendationResult[] books = [new() { BookId = 3, AvailableCopies = 0, RelevanceScore = 99 }, new() { BookId = 2, AvailableCopies = 1, RelevanceScore = 20 }, new() { BookId = 1, AvailableCopies = 1, RelevanceScore = 20 }];
        Assert.Equal(new[] { 1, 2, 3 }, RecommendationScoring.Rank(books, true).Select(b => b.BookId));
        Assert.Equal(3, RecommendationScoring.Rank(books, false).First().BookId);
    }

    [Fact]
    public void RepeatedScoringHasIdenticalBreakdown()
    {
        var book = new RecommendationCandidate { Id = 4, Title = "Python", TotalLoans = 4, AvailableCopies = 2, ReviewCount = 2, AverageRating = 5 };
        var profile = new StudentPreferenceProfile { Interests = ["Python"] };
        var first = RecommendationScoring.Score(book, profile, new(), 8, 0, new());
        var second = RecommendationScoring.Score(book, profile, new(), 8, 0, new());
        Assert.Equal(first.RelevanceScore, second.RelevanceScore);
        Assert.Equal(first.Breakdown, second.Breakdown);
        Assert.InRange(first.RelevanceScore, 0, 100);
    }

    [Fact]
    public void RemoteConfigurationCannotPreventLocalEngineStartup()
    {
        Assert.True(RecommendationOptions.IsValid(new() { Llm = new() { Enabled = true, BaseUrl = "not a URL" } }));
        Assert.False(RecommendationOptions.IsValid(new() { InterestWeight = -1 }));
        Assert.False(RecommendationOptions.IsValid(new() { InterestWeight = 0, SubjectWeight = 0, HistoryWeight = 0, PopularityWeight = 0 }));
    }

    [Theory]
    [InlineData("http://api.example.com/v1")]
    [InlineData("https://localhost/v1")]
    [InlineData("https://127.0.0.1/v1")]
    [InlineData("https://[::ffff:127.0.0.1]/v1")]
    [InlineData("https://192.168.1.20/v1")]
    [InlineData("https://user:secret@api.example.com/v1")]
    [InlineData("https://api.example.com/v1?key=secret")]
    public void InsecureProviderEndpointsAreRejected(string url) => Assert.False(RecommendationOptions.TryGetEndpoint(url, out _));

    [Fact]
    public void CompatibleEndpointUsesChatCompletions()
    {
        Assert.True(RecommendationOptions.TryGetEndpoint("https://api.example.com/v1/", out var endpoint));
        Assert.Equal("https://api.example.com/v1/chat/completions", endpoint!.AbsoluteUri);
    }

    [Fact]
    public void RawPersonalInterestTextIsNotSentToExternalProvider()
    {
        var book = new RecommendationResult { BookId = 981, Title = "Python", MatchingInterests = ["Аян Сәрсенов Python ayan@example.com +77011112233 ST20260001"], MatchingSubjects = ["Деректер қоры"] };
        var input = RecommendationExplanationService.ToProviderInput(book, "item0");
        Assert.Equal(new[] { "python" }, input.MatchingInterests);
        var json = JsonSerializer.Serialize(input);
        Assert.Contains("python", json);
        Assert.DoesNotContain("Аян", json);
        Assert.DoesNotContain("example.com", json);
        Assert.DoesNotContain("77011112233", json);
        Assert.DoesNotContain("ST20260001", json);
        Assert.DoesNotContain("981", json);
    }

    [Fact]
    public void LocalExplanationDoesNotInventInterestEvidence()
    {
        var text = new TemplateExplanationProvider().Explain(new RecommendationResult { AvailableCopies = 0 });
        Assert.Contains("Жеке сәйкестік дәлелі әзірге аз", text);
        Assert.Contains("бос дана жоқ", text);
        Assert.DoesNotContain("Қызығушылықтарыңызбен сәйкестік бар", text);
    }

    [Theory]
    [InlineData(false, "https://api.example.com/v1", "key")]
    [InlineData(true, "invalid", "key")]
    [InlineData(true, "https://api.example.com/v1", "")]
    public async Task DisabledMissingKeyOrInvalidUrlStaysLocalWithoutHttp(bool enabled, string baseUrl, string apiKey)
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var settings = Options.Create(new RecommendationOptions { Llm = new() { Enabled = enabled, BaseUrl = baseUrl, Model = "configured-model", ApiKey = apiKey } });
        using var http = new HttpClient(handler);
        var provider = new OpenAICompatibleExplanationProvider(http, settings, NullLogger<OpenAICompatibleExplanationProvider>.Instance);
        var service = new RecommendationExplanationService(new(), provider, settings);
        var book = new RecommendationResult { Title = "Python", AvailableCopies = 1 };
        await service.ApplyAsync([book]);
        Assert.Equal(0, handler.Calls);
        Assert.False(book.IsLlmEnhanced);
        Assert.NotEmpty(book.Explanation);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"choices\":[null]}")]
    [InlineData("{\"choices\":[{\"message\":[]} ]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}")]
    public void WrongProviderShapesAreIgnored(string body) => Assert.Empty(OpenAICompatibleExplanationProvider.ParseResponse(body, new HashSet<string> { "item0" }));

    [Fact]
    public void ModelCannotAddBooksOrExecutableMarkupThroughExplanations()
    {
        var body = Envelope(new { items = new[] { new { token = "item0", explanation = "Python бағытыңызға сәйкес оқу әдебиеті." }, new { token = "other-book", explanation = "Басқа кітапқа қатысты ойдан шығарылған ұсыныс." }, new { token = "item1", explanation = "<script>alert('unsafe')</script>" } } });
        var results = OpenAICompatibleExplanationProvider.ParseResponse(body, new HashSet<string> { "item0", "item1" });
        Assert.Single(results);
        Assert.Contains("item0", results.Keys);
    }

    [Fact]
    public async Task HttpFailureKeepsDeterministicLocalExplanation()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.ServiceUnavailable, "private provider diagnostic"));
        var settings = Options.Create(new RecommendationOptions { Llm = new() { Enabled = true, BaseUrl = "https://api.example.com/v1", ApiKey = "unit-test-only", Model = "configured-model" } });
        var provider = new OpenAICompatibleExplanationProvider(http, settings, NullLogger<OpenAICompatibleExplanationProvider>.Instance);
        var service = new RecommendationExplanationService(new(), provider, settings);
        var book = new RecommendationResult { AvailableCopies = 2, MatchingInterests = ["Python"] };
        await service.ApplyAsync([book]);
        Assert.False(book.IsLlmEnhanced);
        Assert.Equal(new TemplateExplanationProvider().Explain(book), book.Explanation);
    }

    [Fact]
    public async Task ProviderTimeoutFallsBackWithoutChangingSelectionOrScore()
    {
        using var http = new HttpClient(new TimeoutHandler());
        var settings = Options.Create(new RecommendationOptions { Llm = new() { Enabled = true, ApiKey = "unit-test-only", Model = "configured-model" } });
        var provider = new OpenAICompatibleExplanationProvider(http, settings, NullLogger<OpenAICompatibleExplanationProvider>.Instance);
        var book = new RecommendationResult { BookId = 23, RelevanceScore = 74, AvailableCopies = 1 };
        await new RecommendationExplanationService(new(), provider, settings).ApplyAsync([book]);
        Assert.Equal(23, book.BookId);
        Assert.Equal(74, book.RelevanceScore);
        Assert.False(book.IsLlmEnhanced);
        Assert.NotEmpty(book.Explanation);
    }

    [Fact]
    public async Task ActualModeChangesOnlyAfterAnAcceptedExplanation()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, Envelope(new { items = new[] { new { token = "item0", explanation = "Бұл әдебиет Python тақырыбына қызығушылығыңызбен байланысты." } } })));
        var settings = Options.Create(new RecommendationOptions { Llm = new() { Enabled = true, BaseUrl = "https://api.example.com/v1", ApiKey = "unit-test-only", Model = "configured-model" } });
        var provider = new OpenAICompatibleExplanationProvider(http, settings, NullLogger<OpenAICompatibleExplanationProvider>.Instance);
        var book = new RecommendationResult { Title = "Python" };
        var model = new RecommendationViewModel { Sections = [new() { Books = [book] }] };
        Assert.Equal("Local Recommendation Engine", model.ActualMode);
        await new RecommendationExplanationService(new(), provider, settings).ApplyAsync([book]);
        Assert.True(book.IsLlmEnhanced);
        Assert.Equal("LLM-enhanced explanations", model.ActualMode);
    }

    private static string Envelope(object content) => JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = JsonSerializer.Serialize(content) } } } });

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("Simulated provider timeout"));
    }
}
