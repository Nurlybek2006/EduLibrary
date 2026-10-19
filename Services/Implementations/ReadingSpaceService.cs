using EduLibrary.Data;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Services.Implementations;

// A read-only presentation query: existing circulation and recommendation rules remain in their services.
public sealed class ReadingSpaceService(ApplicationDbContext db, ILibraryClock clock, IRecommendationService recommendations) : IReadingSpaceService
{
    public async Task<ReadingSpaceViewModel> GetAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return new ReadingSpaceViewModel();
        var student = await db.Students.AsNoTracking().Where(s => s.IdentityUserId == userId).AsSplitQuery()
            .Select(s => new { s.Id, s.FullName, s.Faculty, s.GroupName,
                Interests = s.Interests.OrderBy(i => i.Name).Select(i => i.Name).ToList(),
                Subjects = s.StudentSubjects.OrderBy(su => su.Subject.Name).Select(su => su.Subject.Name).ToList() }).SingleOrDefaultAsync(ct);
        if (student is null) return new ReadingSpaceViewModel();
        var ownLoans = db.Loans.AsNoTracking().Where(l => l.StudentId == student.Id);
        var loanCounts = await ownLoans.GroupBy(_ => 1).Select(g => new
            { Active = g.Count(l => l.ReturnedAt == null), Returned = g.Count(l => l.ReturnedAt != null) }).SingleOrDefaultAsync(ct);
        var model = new ReadingSpaceViewModel
        {
            HasStudentProfile = true, StudentId = student.Id, FullName = student.FullName, Faculty = student.Faculty, GroupName = student.GroupName,
            Interests = student.Interests, Subjects = student.Subjects,
            ActiveLoanCount = loanCounts?.Active ?? 0,
            ReturnedBookCount = loanCounts?.Returned ?? 0,
            OutstandingFines = await db.Fines.AsNoTracking().Where(f => f.StudentId == student.Id).SumAsync(f => (decimal?)(f.Amount - f.PaidAmount), ct) ?? 0m
        };
        model.CurrentBooks = await ownLoans.Where(l => l.ReturnedAt == null).OrderBy(l => l.DueDate).ThenBy(l => l.Id).Take(12).AsSplitQuery()
            .Select(l => new ReadingShelfBookViewModel
            {
                LoanId = l.Id, BookId = l.BookCopy.BookId, Title = l.BookCopy.Book.Title, CoverImageUrl = l.BookCopy.Book.CoverImageUrl,
                PublicationYear = l.BookCopy.Book.PublicationYear, IsArchived = l.BookCopy.Book.IsArchived,
                AuthorNames = l.BookCopy.Book.BookAuthors.OrderBy(a => a.Author.LastName).Select(a => a.Author.FirstName + " " + a.Author.LastName).ToList(),
                LoanDate = l.LoanDate, DueDate = l.DueDate
            }).ToListAsync(ct);
        foreach (var book in model.CurrentBooks) book.DaysUntilDue = (book.DueDate.Date - clock.Today).Days;
        model.History = await ownLoans.Where(l => l.ReturnedAt != null).OrderByDescending(l => l.ReturnedAt).ThenByDescending(l => l.Id).Take(6).AsSplitQuery()
            .Select(l => new ReadingShelfBookViewModel
            {
                LoanId = l.Id, BookId = l.BookCopy.BookId, Title = l.BookCopy.Book.Title, CoverImageUrl = l.BookCopy.Book.CoverImageUrl,
                PublicationYear = l.BookCopy.Book.PublicationYear, IsArchived = l.BookCopy.Book.IsArchived,
                AuthorNames = l.BookCopy.Book.BookAuthors.OrderBy(a => a.Author.LastName).Select(a => a.Author.FirstName + " " + a.Author.LastName).ToList(),
                LoanDate = l.LoanDate, DueDate = l.DueDate, ReturnedAt = l.ReturnedAt
            }).ToListAsync(ct);
        model.RecentActivity = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(5)
            .Select(n => new ReadingActivityViewModel { Title = n.Title, Message = n.Message, Type = n.Type, Link = n.Link, CreatedAt = n.CreatedAt }).ToListAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Qyzylorda");
        foreach (var item in model.RecentActivity)
        {
            item.CreatedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc), zone);
            if (!item.Link.StartsWith('/') || item.Link.StartsWith("//", StringComparison.Ordinal)
                || item.Link.StartsWith("/\\", StringComparison.Ordinal) || item.Link.Any(char.IsControl)) item.Link = "/Notifications";
        }
        var selection = await recommendations.GetAsync(userId, false, ct: ct);
        model.Recommendations = selection?.Sections.FirstOrDefault(s => s.Key == "for-you")?.Books.Take(4).ToList() ?? [];
        return model;
    }
}
