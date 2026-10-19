using System.Data;
using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduLibrary.Services.Implementations;

public sealed class RecommendationService(ApplicationDbContext db, IOptions<RecommendationOptions> configuration,
    IRecommendationExplanationService explanations, ILogger<RecommendationService> logger) : IRecommendationService
{
    private RecommendationOptions Settings => configuration.Value;

    public async Task<RecommendationViewModel?> GetAsync(string userId, bool isStaff, int? studentId = null, bool includePreviouslyRead = false, CancellationToken ct = default)
    {
        var model = new RecommendationViewModel { IsStaff = isStaff };
        if (isStaff)
            model.Students = await db.Students.AsNoTracking().OrderBy(s => s.FullName).ThenBy(s => s.Id)
                .Select(s => new SelectListItem(s.FullName + (s.IsActive ? "" : " · белсенді емес"), s.Id.ToString())).ToListAsync(ct);
        if (isStaff && studentId is null) return model;
        var profile = await ScopedStudent(userId, isStaff, studentId).AsNoTracking().AsSplitQuery().Select(s => new StudentPreferenceProfile
        {
            StudentId = s.Id, DisplayName = s.FullName, Faculty = s.Faculty,
            Interests = s.Interests.OrderBy(i => i.Name).Select(i => i.Name).ToList(),
            Subjects = s.StudentSubjects.OrderBy(x => x.Subject.Name).Select(x => new RecommendationFacet(x.SubjectId, x.Subject.Name)).ToList()
        }).SingleOrDefaultAsync(ct);
        if (profile is null) return studentId.HasValue ? null : model;

        var loans = await db.Loans.AsNoTracking().Where(l => l.StudentId == profile.StudentId)
            .Select(l => new { BookId = l.BookCopy.BookId, IsActive = l.ReturnedAt == null }).ToListAsync(ct);
        var reviews = await db.Reviews.AsNoTracking().Where(r => r.StudentId == profile.StudentId && !r.IsDeleted && !r.IsHidden)
            .Select(r => new { r.BookId, r.Rating }).ToListAsync(ct);
        profile.ActiveBookIds = loans.Where(l => l.IsActive).Select(l => l.BookId).ToHashSet();
        profile.PreviouslyReadBookIds = loans.Select(l => l.BookId).Concat(reviews.Select(r => r.BookId)).ToHashSet();
        profile.HasHistory = profile.PreviouslyReadBookIds.Count > 0;
        var excludeRead = Settings.ExcludePreviouslyBorrowed && !includePreviouslyRead;
        var selectedId = profile.StudentId;
        var faculty = profile.Faculty;
        var candidatesQuery = db.Books.AsNoTracking().Where(b => !b.IsArchived
            && !b.Copies.Any(c => c.Loans.Any(l => l.StudentId == selectedId && l.ReturnedAt == null)));
        if (excludeRead)
            candidatesQuery = candidatesQuery.Where(b => !b.Copies.Any(c => c.Loans.Any(l => l.StudentId == selectedId))
                && !b.Reviews.Any(r => r.StudentId == selectedId && !r.IsHidden && !r.IsDeleted));

        // One bounded metadata projection; split collections avoid a Cartesian product and N+1 queries.
        var candidates = await candidatesQuery.OrderByDescending(b => b.Copies.Any(c => c.Status == CopyStatus.Available))
            .ThenByDescending(b => b.Copies.SelectMany(c => c.Loans).Count()).ThenBy(b => b.Id).Take(Settings.CandidateLimit + 1)
            .AsSplitQuery().Select(b => new RecommendationCandidate
            {
                Id = b.Id, Title = b.Title, Description = b.Description, CoverImageUrl = b.CoverImageUrl, PublicationYear = b.PublicationYear,
                Authors = b.BookAuthors.OrderBy(a => a.Author.LastName).ThenBy(a => a.Author.FirstName).Select(a => new RecommendationFacet(a.AuthorId, a.Author.FirstName + " " + a.Author.LastName)).ToList(),
                Categories = b.BookCategories.OrderBy(c => c.Category.Name).Select(c => new RecommendationFacet(c.CategoryId, c.Category.Name)).ToList(),
                Subjects = b.BookSubjects.OrderBy(s => s.Subject.Name).Select(s => new RecommendationFacet(s.SubjectId, s.Subject.Name)).ToList(),
                AvailableCopies = b.Copies.Count(c => c.Status == CopyStatus.Available), TotalLoans = b.Copies.SelectMany(c => c.Loans).Count(),
                FacultyLoans = b.Copies.SelectMany(c => c.Loans).Count(l => faculty != "" && l.Student.Faculty == faculty),
                AverageRating = b.Reviews.Where(r => !r.IsDeleted && !r.IsHidden).Average(r => (double?)r.Rating) ?? 0,
                ReviewCount = b.Reviews.Count(r => !r.IsDeleted && !r.IsHidden)
            }).ToListAsync(ct);
        model.CandidateLimitReached = candidates.Count > Settings.CandidateLimit;
        if (model.CandidateLimitReached) candidates.RemoveAt(candidates.Count - 1);

        var history = new RecommendationHistory();
        if (profile.HasHistory)
        {
            var knownIds = profile.PreviouslyReadBookIds.ToArray();
            var known = await db.Books.AsNoTracking().Where(b => knownIds.Contains(b.Id)).AsSplitQuery().Select(b => new
            {
                b.Id, Authors = b.BookAuthors.Select(a => a.AuthorId).ToList(), Categories = b.BookCategories.Select(c => c.CategoryId).ToList(),
                Subjects = b.BookSubjects.Select(s => s.SubjectId).ToList()
            }).ToListAsync(ct);
            var loanCounts = loans.GroupBy(l => l.BookId).ToDictionary(g => g.Key, g => g.Count());
            var ownRatings = reviews.ToDictionary(r => r.BookId, r => r.Rating);
            foreach (var book in known)
            {
                var weight = Math.Min(3, loanCounts.GetValueOrDefault(book.Id));
                var learnedWeight = weight + (ownRatings.TryGetValue(book.Id, out var rating) ? (rating - 3) * .5 : 0);
                Learn(history.Authors, book.Authors, learnedWeight);
                Learn(history.Categories, book.Categories, learnedWeight);
                Learn(history.Subjects, book.Subjects, learnedWeight);
            }
        }

        var maxLoans = candidates.Select(c => c.TotalLoans).DefaultIfEmpty(0).Max();
        var maxFaculty = candidates.Select(c => c.FacultyLoans).DefaultIfEmpty(0).Max();
        var results = candidates.Where(c => RecommendationScoring.IsEligible(c.Id, profile, excludeRead))
            .Select(c => RecommendationScoring.Score(c, profile, history, maxLoans, maxFaculty, Settings)).ToList();
        var ranked = RecommendationScoring.Rank(results, Settings.PrioritizeAvailable).ToList();
        var count = Settings.ItemsPerSection;
        var recommended = ranked.Take(count).ToList();
        var familiarCategories = history.Categories.Where(c => c.Value > 0).Select(c => c.Key).ToHashSet();
        if (familiarCategories.Count == 0)
            familiarCategories = candidates.Where(c => recommended.Any(r => r.BookId == c.Id)).SelectMany(c => c.Categories).Select(c => c.Id).ToHashSet();
        var discoverIds = candidates.Where(c => c.Categories.Count > 0 && c.Categories.All(category => !familiarCategories.Contains(category.Id))).Select(c => c.Id).ToHashSet();
        model.Sections =
        [
            new() { Key = "for-you", Title = "Сізге ұсынылады", Description = "Қызығушылық, пән, оқу тарихы және кітапхана деректерінің ортақ нәтижесі.", EmptyMessage = "Жаңа ұсыныс табылмады. Бұрын оқылған кітаптарды қосып көріңіз.", Books = recommended },
            new() { Key = "subjects", Title = "Оқыған пәндеріңізге сай", Description = "Кітаптың пәндік байланысы және мәтіндік сәйкестігі есепке алынды.", EmptyMessage = profile.Subjects.Count == 0 ? "Профильде пәндер таңдалмаған. Оқу бағытыңызды толықтырыңыз." : "Таңдалған пәндерге сәйкес жаңа кітап табылмады.", Books = RankSection(results.Where(b => b.MatchingSubjects.Count > 0), b => b.SubjectScore, count) },
            new() { Key = "interests", Title = "Қызығушылықтарыңыз бойынша", Description = "Сіз көрсеткен бағыттармен сәйкес әдебиеттер.", EmptyMessage = profile.Interests.Count == 0 ? "Қызығушылықтарыңызды қосып, ұсыныстарды нақтылаңыз." : "Қызығушылықтарыңызға сәйкес жаңа кітап әзірге жоқ.", Books = RankSection(results.Where(b => b.MatchingInterests.Count > 0), b => b.InterestScore, count) },
            new() { Key = "history", Title = "Оқыған кітаптарыңызға ұқсас", Description = "Бұрынғы авторлар, санаттар, пәндер және өз бағаларыңыз негізінде.", EmptyMessage = profile.HasHistory ? "Оқу тарихына ұқсас жаңа кітап табылмады." : "Оқу тарихы мен пікірлер әлі жоқ. Бұл бөлім алғашқы кітаптардан кейін нақтыланады.", Books = RankSection(results.Where(b => b.HistoryScore > 0), b => b.HistoryScore, count) },
            new() { Key = "field", Title = "Салаңызда танымал", Description = "Сол факультет оқырмандарының нақты кітап алу тарихы бойынша.", EmptyMessage = "Факультетіңіз бойынша жеткілікті кітап алу дерегі жоқ. Жалпы танымалдық негізгі ұсыныстарда ескерілді.", Books = RankSection(results.Where(b => b.FacultyLoans > 0), b => b.FacultyLoans, count) },
            new() { Key = "discover", Title = "Жаңа бағытты ашыңыз", Description = "Таныс санаттардан тыс әдебиеттер. Төмен сәйкестік жаңа бағытты білдіруі мүмкін.", EmptyMessage = "Каталогтағы басқа бағыттар әзірге таусылды. Жаңа әдебиеттер қосылғанда бұл бөлім жаңарады.", Books = RankSection(results.Where(b => discoverIds.Contains(b.BookId)), b => b.PopularityScore, count) }
        ];
        model.StudentId = profile.StudentId; model.StudentName = profile.DisplayName; model.Faculty = profile.Faculty;
        model.Interests = profile.Interests; model.Subjects = profile.Subjects.Select(s => s.Name).ToList();
        model.IncludePreviouslyRead = !excludeRead; model.CandidateCount = candidates.Count;
        model.IsColdStart = profile.Interests.Count == 0 && profile.Subjects.Count == 0 && !profile.HasHistory;
        model.HasMissingSignals = profile.Interests.Count == 0 || profile.Subjects.Count == 0 || !profile.HasHistory;
        await explanations.ApplyAsync(model.Sections.SelectMany(s => s.Books).DistinctBy(b => b.BookId).ToList(), ct);
        return model;
    }

    public async Task<RecommendationPreferencesViewModel?> GetPreferencesAsync(string userId, bool isStaff, int? studentId = null, CancellationToken ct = default)
    {
        if (isStaff && studentId is null) return null;
        var student = await ScopedStudent(userId, isStaff, studentId).AsNoTracking().Include(s => s.Interests).Include(s => s.StudentSubjects).AsSplitQuery().SingleOrDefaultAsync(ct);
        if (student is null) return null;
        return new RecommendationPreferencesViewModel
        {
            StudentId = student.Id, StudentName = student.FullName, Faculty = student.Faculty,
            Interests = string.Join(", ", student.Interests.OrderBy(i => i.Name).Select(i => i.Name)),
            SelectedSubjectIds = student.StudentSubjects.Select(s => s.SubjectId).ToList(),
            Subjects = await db.Subjects.AsNoTracking().OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync(ct)
        };
    }

    public async Task<bool> SavePreferencesAsync(string userId, bool isStaff, RecommendationPreferencesViewModel model, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var student = await ScopedStudent(userId, isStaff, model.StudentId).Include(s => s.Interests).Include(s => s.StudentSubjects).AsSplitQuery().SingleOrDefaultAsync(ct);
                if (student is null) return false;
                if (!isStaff && !student.IsActive) throw new BusinessException("Оқырман белсенді емес. Кітапханашыға хабарласыңыз.");
                await StudentProfileData.SetPreferencesAsync(db, student, model.SelectedSubjectIds, model.Interests, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                logger.LogInformation("Recommendation preferences updated for student {StudentId}", student.Id);
                return true;
            });
        }
        catch (Exception ex) when (DatabaseConflict.IsExpected(ex))
        {
            logger.LogWarning("Recommendation preferences update conflicted");
            throw new BusinessException("Оқу бағыты қатар өзгертілді. Бетті жаңартып, қайта көріңіз.");
        }
    }

    private IQueryable<Student> ScopedStudent(string userId, bool isStaff, int? studentId)
    {
        var query = db.Students.AsQueryable();
        if (!isStaff) query = query.Where(s => s.IdentityUserId == userId);
        if (studentId.HasValue) query = query.Where(s => s.Id == studentId.Value);
        return query;
    }

    private List<RecommendationResult> RankSection(IEnumerable<RecommendationResult> results, Func<RecommendationResult, double> signal, int count) => results
        .OrderByDescending(b => Settings.PrioritizeAvailable && b.AvailableCopies > 0).ThenByDescending(signal)
        .ThenByDescending(b => b.RelevanceScore).ThenByDescending(b => b.AvailableCopies).ThenBy(b => b.BookId).Take(count).ToList();

    private static void Learn(Dictionary<int, double> dictionary, IEnumerable<int> ids, double weight)
    {
        foreach (var id in ids) dictionary[id] = dictionary.GetValueOrDefault(id) + weight;
    }
}
