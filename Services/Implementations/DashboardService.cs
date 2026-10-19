using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduLibrary.Services.Implementations;

public sealed class DashboardService(ApplicationDbContext db, ILibraryClock clock, IOptions<LibraryOptions> options) : IDashboardService
{
    public async Task<DashboardViewModel> GetAsync(CancellationToken ct = default)
    {
        var today = clock.Today;
        // A DbContext runs one SQL command at a time. Keep these independent aggregates sequential.
        var model = new DashboardViewModel
        {
            Today = today,
            TotalBooks = await db.Books.CountAsync(x => !x.IsArchived, ct),
            TotalCopies = await db.BookCopies.CountAsync(ct),
            AvailableCopies = await db.BookCopies.CountAsync(x => x.Status == CopyStatus.Available && !x.Book.IsArchived, ct),
            ActiveLoans = await db.Loans.CountAsync(x => x.ReturnedAt == null, ct),
            OverdueLoans = await db.Loans.CountAsync(x => x.ReturnedAt == null && x.DueDate < today, ct),
            TotalStudents = await db.Students.CountAsync(ct),
            ActiveReaders = await db.Loans.Where(x => x.ReturnedAt == null).Select(x => x.StudentId).Distinct().CountAsync(ct),
            OutstandingFines = await db.Fines.SumAsync(x => (decimal?)(x.Amount - x.PaidAmount), ct) ?? 0m
        };
        var recentLoans = await db.Loans.AsNoTracking().OrderByDescending(x => x.LoanDate).ThenByDescending(x => x.Id).Take(6).Select(CirculationService.LoanProjection).ToListAsync(ct);
        CirculationService.CalculateOverdue(recentLoans, today, options.Value.FinePerOverdueDay);
        model.RecentLoans = recentLoans;
        var overdue = await db.Loans.AsNoTracking().Where(x => x.ReturnedAt == null && x.DueDate < today).OrderBy(x => x.DueDate).ThenBy(x => x.Id).Take(5).Select(CirculationService.LoanProjection).ToListAsync(ct);
        CirculationService.CalculateOverdue(overdue, today, options.Value.FinePerOverdueDay);
        model.OverdueAlerts = overdue;
        var returns = await db.Returns.AsNoTracking().OrderByDescending(x => x.ReturnDate).ThenByDescending(x => x.Id).Take(5)
            .Select(x => new ReturnRowViewModel { LoanId = x.LoanId, StudentName = x.Loan.Student.FullName, BookTitle = x.Loan.BookCopy.Book.Title, InventoryNumber = x.Loan.BookCopy.InventoryNumber, ReturnDate = x.ReturnDate, ReturnCondition = x.ReturnCondition, FineAmount = x.Loan.Fines.Sum(f => (decimal?)f.Amount) ?? 0m }).ToListAsync(ct);
        foreach (var item in returns) item.ReturnCondition = CirculationService.ConditionLabel(item.ReturnCondition) ?? "";
        model.RecentReturns = returns;
        model.PopularBooks = await db.Books.AsNoTracking().Where(x => !x.IsArchived)
            .Select(x => new PopularBookViewModel { Id = x.Id, Title = x.Title, LoanCount = x.Copies.SelectMany(c => c.Loans).Count(), AvailableCopies = x.Copies.Count(c => c.Status == CopyStatus.Available) })
            .OrderByDescending(x => x.LoanCount).ThenBy(x => x.Title).Take(5).ToListAsync(ct);
        return model;
    }
}
