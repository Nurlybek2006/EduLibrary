using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Services.Implementations;

public sealed class ReviewService(ApplicationDbContext db, ILibraryClock clock, ILogger<ReviewService> logger) : IReviewService
{
    private const int PublicPageSize = 6;
    private const int AdminPageSize = 20;

    public async Task<BookReviewsViewModel?> GetBookReviewsAsync(int bookId, string? userId, bool isStudent, int page, CancellationToken ct = default)
    {
        var book = await db.Books.AsNoTracking().Where(x => x.Id == bookId).Select(x => new { x.IsArchived }).SingleOrDefaultAsync(ct);
        if (book is null) return null;
        var query = db.Reviews.AsNoTracking().Where(x => x.BookId == bookId && !x.IsDeleted && !x.IsHidden);
        var summary = await query.GroupBy(x => 1).Select(x => new { Count = x.Count(), Average = x.Average(r => (double)r.Rating) }).SingleOrDefaultAsync(ct);
        var count = summary?.Count ?? 0;
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)PublicPageSize));
        page = Math.Clamp(page, 1, totalPages);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * PublicPageSize).Take(PublicPageSize)
            .Select(x => new ReviewRowViewModel { Id = x.Id, BookId = x.BookId, Rating = x.Rating, Comment = x.Comment,
                CreatedAt = x.CreatedAt, UpdatedAt = x.UpdatedAt, IsOwn = userId != null && x.Student.IdentityUserId == userId }).ToListAsync(ct);
        foreach (var row in rows) row.ReviewerLabel = row.IsOwn ? "Сіздің пікіріңіз" : "Оқырман";
        ReviewRowViewModel? own = null;
        var canWrite = false;
        if (isStudent && !string.IsNullOrEmpty(userId))
        {
            canWrite = !book.IsArchived && await db.Students.AnyAsync(x => x.IdentityUserId == userId && x.IsActive, ct);
            var ownRecord = await db.Reviews.AsNoTracking().Where(x => x.BookId == bookId && !x.IsDeleted && x.Student.IdentityUserId == userId)
                .Select(x => new { x.Id, x.BookId, x.Rating, x.Comment, x.CreatedAt, x.UpdatedAt, x.IsHidden, x.RowVersion }).SingleOrDefaultAsync(ct);
            if (ownRecord is not null)
                own = new ReviewRowViewModel { Id = ownRecord.Id, BookId = ownRecord.BookId, Rating = ownRecord.Rating, Comment = ownRecord.Comment,
                    CreatedAt = ownRecord.CreatedAt, UpdatedAt = ownRecord.UpdatedAt, IsHidden = ownRecord.IsHidden, IsOwn = true,
                    ReviewerLabel = "Сіздің пікіріңіз", RowVersion = Convert.ToBase64String(ownRecord.RowVersion) };
        }
        return new BookReviewsViewModel { BookId = bookId, AverageRating = summary?.Average ?? 0, ReviewCount = count, Page = page,
            TotalPages = totalPages, Reviews = rows, OwnReview = own, CanWrite = canWrite, BookArchived = book.IsArchived };
    }

    public async Task<ReviewEditViewModel?> GetCreateFormAsync(int bookId, string userId, CancellationToken ct = default)
    {
        var studentId = await ActiveStudentIdAsync(userId, ct);
        if (studentId is null) return null;
        var book = await db.Books.AsNoTracking().Where(x => x.Id == bookId && !x.IsArchived).Select(x => new { x.Id, x.Title }).SingleOrDefaultAsync(ct);
        if (book is null) return null;
        var existing = await db.Reviews.AsNoTracking().Where(x => x.BookId == bookId && x.StudentId == studentId && !x.IsDeleted).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
        return existing.HasValue ? await GetEditFormAsync(existing.Value, userId, ct) : new ReviewEditViewModel { BookId = book.Id, BookTitle = book.Title };
    }

    public async Task<ReviewEditViewModel?> GetEditFormAsync(int id, string userId, CancellationToken ct = default)
    {
        var record = await db.Reviews.AsNoTracking().Where(x => x.Id == id && !x.IsDeleted && x.Student.IdentityUserId == userId && x.Student.IsActive)
            .Select(x => new { x.Id, x.BookId, x.Book.Title, x.Rating, x.Comment, x.RowVersion, x.IsHidden }).SingleOrDefaultAsync(ct);
        return record is null ? null : new ReviewEditViewModel { Id = record.Id, BookId = record.BookId, BookTitle = record.Title,
            Rating = record.Rating, Comment = record.Comment, RowVersion = Convert.ToBase64String(record.RowVersion), IsHidden = record.IsHidden };
    }

    public async Task<int> SaveAsync(ReviewEditViewModel model, string userId, CancellationToken ct = default)
    {
        ReviewPolicy.ValidateContent(model.Rating, model.Comment);
        var studentId = await ActiveStudentIdAsync(userId, ct) ?? throw new BusinessException("Пікір жазу үшін белсенді студент аккаунты қажет.");
        Review review;
        if (model.Id == 0)
        {
            if (!await db.Books.AnyAsync(x => x.Id == model.BookId && !x.IsArchived, ct)) throw new BusinessException("Кітап табылмады немесе мұрағатқа көшірілген.");
            if (await db.Reviews.AnyAsync(x => x.BookId == model.BookId && x.StudentId == studentId && !x.IsDeleted, ct))
                throw new BusinessException("Бұл кітапқа пікір жазғансыз. Бұрынғы пікіріңізді өзгертіңіз.");
            review = new Review { BookId = model.BookId, StudentId = studentId, CreatedAt = clock.UtcNow };
            db.Reviews.Add(review);
        }
        else
        {
            review = await db.Reviews.SingleOrDefaultAsync(x => x.Id == model.Id && x.StudentId == studentId && !x.IsDeleted, ct)
                ?? throw new BusinessException("Пікір табылмады немесе оны өзгертуге рұқсат жоқ.");
            if (model.BookId != review.BookId) throw new BusinessException("Пікірдің кітабын өзгертуге болмайды.");
            db.Entry(review).Property(x => x.RowVersion).OriginalValue = ReviewPolicy.ParseVersion(model.RowVersion);
            review.UpdatedAt = clock.UtcNow;
            // An author's edit must not override the administrator's moderation.
        }
        review.Rating = model.Rating; review.Comment = model.Comment?.Trim() ?? "";
        await PersistAsync(ct);
        logger.LogInformation("Review {ReviewId} saved by student {StudentId}", review.Id, studentId);
        return review.BookId;
    }

    public async Task<int> DeleteAsync(int id, string rowVersion, string userId, CancellationToken ct = default)
    {
        var review = await db.Reviews.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted && x.Student.IdentityUserId == userId && x.Student.IsActive, ct)
            ?? throw new BusinessException("Пікір табылмады немесе оны жоюға рұқсат жоқ.");
        db.Entry(review).Property(x => x.RowVersion).OriginalValue = ReviewPolicy.ParseVersion(rowVersion);
        review.IsDeleted = true; review.UpdatedAt = clock.UtcNow;
        await PersistAsync(ct);
        logger.LogInformation("Review {ReviewId} soft deleted", id);
        return review.BookId;
    }

    public async Task<ReviewModerationViewModel> GetModerationAsync(ReviewModerationViewModel filter, CancellationToken ct = default)
    {
        filter.Search = filter.Search?.Trim();
        filter.Status = filter.Status is "visible" or "hidden" or "deleted" or "all" ? filter.Status : "active";
        var query = db.Reviews.AsNoTracking();
        if (filter.BookId.HasValue) query = query.Where(x => x.BookId == filter.BookId);
        if (!string.IsNullOrEmpty(filter.Search)) query = query.Where(x => x.Book.Title.Contains(filter.Search) || x.Comment.Contains(filter.Search) || x.Student.FullName.Contains(filter.Search));
        query = filter.Status switch
        {
            "visible" => query.Where(x => !x.IsDeleted && !x.IsHidden),
            "hidden" => query.Where(x => !x.IsDeleted && x.IsHidden),
            "deleted" => query.Where(x => x.IsDeleted),
            "all" => query,
            _ => query.Where(x => !x.IsDeleted)
        };
        filter.TotalCount = await query.CountAsync(ct);
        filter.TotalPages = Math.Max(1, (int)Math.Ceiling(filter.TotalCount / (double)AdminPageSize));
        filter.Page = Math.Clamp(filter.Page, 1, filter.TotalPages);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((filter.Page - 1) * AdminPageSize).Take(AdminPageSize)
            .Select(x => new { x.Id, x.BookId, BookTitle = x.Book.Title, StudentName = x.Student.FullName, x.Rating, x.Comment, x.CreatedAt, x.UpdatedAt, x.IsHidden, x.IsDeleted, x.RowVersion }).ToListAsync(ct);
        filter.Reviews = rows.Select(x => new ReviewRowViewModel { Id = x.Id, BookId = x.BookId, BookTitle = x.BookTitle, ReviewerLabel = x.StudentName,
            Rating = x.Rating, Comment = x.Comment, CreatedAt = x.CreatedAt, UpdatedAt = x.UpdatedAt, IsHidden = x.IsHidden, IsDeleted = x.IsDeleted,
            RowVersion = Convert.ToBase64String(x.RowVersion) }).ToList();
        return filter;
    }

    public async Task ModerateAsync(int id, bool hidden, string rowVersion, CancellationToken ct = default)
    {
        var review = await db.Reviews.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct) ?? throw new BusinessException("Белсенді пікір табылмады.");
        db.Entry(review).Property(x => x.RowVersion).OriginalValue = ReviewPolicy.ParseVersion(rowVersion);
        review.IsHidden = hidden; review.UpdatedAt = clock.UtcNow;
        await PersistAsync(ct);
        logger.LogInformation("Review {ReviewId} moderation hidden={IsHidden}", id, hidden);
    }

    private Task<int?> ActiveStudentIdAsync(string userId, CancellationToken ct) => db.Students.AsNoTracking()
        .Where(x => x.IdentityUserId == userId && x.IsActive).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);

    private async Task PersistAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex)
        {
            db.ChangeTracker.Clear(); logger.LogWarning(ex, "Review concurrency conflict");
            throw new BusinessException("Пікірді басқа пайдаланушы өзгертті. Бетті жаңартып, қайта көріңіз.");
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear(); logger.LogWarning(ex, "Review uniqueness constraint rejected a change");
            throw new BusinessException("Пікір сақталмады. Бір студент бір кітапқа бір белсенді пікір жаза алады.");
        }
    }
}
