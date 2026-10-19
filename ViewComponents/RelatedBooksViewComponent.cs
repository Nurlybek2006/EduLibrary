using EduLibrary.Data;
using EduLibrary.Models.Enums;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.ViewComponents;

public sealed class RelatedBooksViewComponent(ApplicationDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int bookId)
    {
        var ct = HttpContext.RequestAborted;
        var categories = await db.BookCategories.AsNoTracking().Where(x => x.BookId == bookId).Select(x => x.CategoryId).ToListAsync(ct);
        var authors = await db.BookAuthors.AsNoTracking().Where(x => x.BookId == bookId).Select(x => x.AuthorId).ToListAsync(ct);
        var books = await db.Books.AsNoTracking().Where(x => x.Id != bookId && !x.IsArchived &&
                (x.BookCategories.Any(c => categories.Contains(c.CategoryId)) || x.BookAuthors.Any(a => authors.Contains(a.AuthorId))))
            .OrderByDescending(x => x.BookCategories.Count(c => categories.Contains(c.CategoryId)))
            .ThenByDescending(x => x.BookAuthors.Count(a => authors.Contains(a.AuthorId)))
            .ThenByDescending(x => x.Copies.Any(c => c.Status == CopyStatus.Available)).ThenBy(x => x.Title).Take(4)
            .AsSplitQuery().Select(x => new
            {
                x.Id, x.Title, x.CoverImageUrl, x.PublicationYear,
                AuthorNames = x.BookAuthors.OrderBy(a => a.Author.LastName).Select(a => a.Author.FirstName + " " + a.Author.LastName).ToList(),
                Category = x.BookCategories.OrderBy(c => c.Category.Name).Select(c => c.Category.Name).FirstOrDefault(),
                Average = x.Reviews.Where(r => !r.IsHidden && !r.IsDeleted).Average(r => (double?)r.Rating) ?? 0,
                ReviewCount = x.Reviews.Count(r => !r.IsHidden && !r.IsDeleted),
                Available = x.Copies.Count(c => c.Status == CopyStatus.Available), Total = x.Copies.Count
            }).ToListAsync(ct);
        return View(books.Select(x => new BookCardPresentationViewModel
        {
            BookId = x.Id, Title = x.Title, Authors = string.Join(", ", x.AuthorNames), CoverImageUrl = x.CoverImageUrl,
            PublicationYear = x.PublicationYear, Category = x.Category, AverageRating = x.Average, ReviewCount = x.ReviewCount,
            AvailableCopies = x.Available, TotalCopies = x.Total
        }).ToList());
    }
}
