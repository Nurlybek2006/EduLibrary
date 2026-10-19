using System.Data;
using System.Linq.Expressions;
using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduLibrary.Services.Implementations;

public sealed class CirculationService(
    ApplicationDbContext db,
    IOptions<LibraryOptions> options,
    ILibraryClock clock,
    ILogger<CirculationService> logger) : ICirculationService
{
    private const int PageSize = 15;
    private decimal DailyRate => options.Value.FinePerOverdueDay;

    internal static readonly Expression<Func<Loan, LoanRowViewModel>> LoanProjection = loan => new LoanRowViewModel
    {
        Id = loan.Id,
        StudentId = loan.StudentId,
        StudentName = loan.Student.FullName,
        StudentNumber = loan.Student.StudentNumber,
        BookTitle = loan.BookCopy.Book.Title,
        InventoryNumber = loan.BookCopy.InventoryNumber,
        LoanDate = loan.LoanDate,
        DueDate = loan.DueDate,
        ReturnedAt = loan.ReturnedAt
    };

    internal static void CalculateOverdue(IEnumerable<LoanRowViewModel> loans, DateTime today, decimal dailyRate)
    {
        foreach (var loan in loans)
        {
            loan.OverdueDays = Math.Max(0, ((loan.ReturnedAt ?? today).Date - loan.DueDate.Date).Days);
            loan.EstimatedFine = loan.OverdueDays * dailyRate;
        }
    }

    private IQueryable<Loan> ScopedLoans(string? userId, bool isStaff)
    {
        var query = db.Loans.AsNoTracking();
        return isStaff ? query : query.Where(x => userId != null && x.Student.IdentityUserId == userId);
    }

    private IQueryable<Fine> ScopedFines(string? userId, bool isStaff)
    {
        var query = db.Fines.AsNoTracking();
        return isStaff ? query : query.Where(x => userId != null && x.Student.IdentityUserId == userId);
    }

    public async Task<LoanListViewModel> GetLoansAsync(LoanFilterViewModel filter, string? userId, bool isStaff, CancellationToken ct = default)
    {
        filter.Page = Math.Clamp(filter.Page, 1, 1_000_000);
        filter.Search = filter.Search?.Trim();
        filter.Status = filter.Status is "active" or "overdue" or "returned" or "all" ? filter.Status : "active";
        var today = clock.Today;
        var query = ScopedLoans(userId, isStaff);
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(x => x.BookCopy.Book.Title.Contains(filter.Search) || x.BookCopy.InventoryNumber.Contains(filter.Search) || x.Student.FullName.Contains(filter.Search) || x.Student.StudentNumber.Contains(filter.Search));
        query = filter.Status switch
        {
            "active" => query.Where(x => x.ReturnedAt == null),
            "overdue" => query.Where(x => x.ReturnedAt == null && x.DueDate < today),
            "returned" => query.Where(x => x.ReturnedAt != null),
            _ => query
        };
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.LoanDate).ThenByDescending(x => x.Id)
            .Skip((filter.Page - 1) * PageSize).Take(PageSize).Select(LoanProjection).ToListAsync(ct);
        CalculateOverdue(items, today, DailyRate);
        return new LoanListViewModel { Filter = filter, Items = items, TotalCount = total, IsStaff = isStaff };
    }

    public async Task<LoanDetailsViewModel?> GetLoanAsync(int id, string? userId, bool isStaff, CancellationToken ct = default)
    {
        var query = ScopedLoans(userId, isStaff).Where(x => x.Id == id);
        var loan = await query.Select(LoanProjection).SingleOrDefaultAsync(ct);
        if (loan is null) return null;
        CalculateOverdue([loan], clock.Today, DailyRate);
        var extra = await query.Select(x => new
        {
            x.Notes,
            ReturnCondition = x.Return == null ? null : x.Return.ReturnCondition,
            ReturnNotes = x.Return == null ? null : x.Return.Notes,
            Fine = x.Fines.Sum(f => (decimal?)f.Amount) ?? 0m
        }).SingleAsync(ct);
        return new LoanDetailsViewModel { Loan = loan, Notes = extra.Notes, ReturnCondition = ConditionLabel(extra.ReturnCondition), ReturnNotes = extra.ReturnNotes, FinalFine = extra.Fine, IsStaff = isStaff };
    }

    public async Task<IssueLoanViewModel> GetIssueFormAsync(int? copyId = null, int? studentId = null, CancellationToken ct = default)
    {
        var model = new IssueLoanViewModel { BookCopyId = copyId ?? 0, StudentId = studentId ?? 0, LoanDate = clock.Today, DueDate = clock.Today.AddDays(options.Value.DefaultLoanDays) };
        await PopulateIssueFormAsync(model, ct);
        return model;
    }

    public async Task PopulateIssueFormAsync(IssueLoanViewModel model, CancellationToken ct = default)
    {
        model.DefaultLoanDays = options.Value.DefaultLoanDays;
        model.Students = await db.Students.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FullName)
            .Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.StudentNumber + " · " + x.FullName }).ToListAsync(ct);
        model.Copies = await db.BookCopies.AsNoTracking()
            .Where(x => x.Status == CopyStatus.Available && !x.Book.IsArchived && !x.Loans.Any(l => l.ReturnedAt == null))
            .OrderBy(x => x.Book.Title).ThenBy(x => x.InventoryNumber)
            .Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Book.Title + " · " + x.InventoryNumber + " · " + x.ShelfLocation }).ToListAsync(ct);
    }

    public async Task<int> IssueAsync(IssueLoanViewModel model, string issuedByUserId, CancellationToken ct = default)
    {
        EnsureOperator(issuedByUserId);
        if (model.StudentId <= 0 || model.BookCopyId <= 0) throw new BusinessException("Студент пен кітап данасын таңдаңыз.");
        if (model.LoanDate == default || model.DueDate == default) throw new BusinessException("Берілген күн мен қайтару мерзімін енгізіңіз.");
        if (model.LoanDate.Date > clock.Today) throw new BusinessException("Берілген күн болашақта болмауы тиіс.");
        if (model.DueDate.Date < model.LoanDate.Date) throw new BusinessException("Қайтару мерзімі берілген күннен ерте болмауы тиіс.");
        ValidateNotes(model.Notes);
        var loan = await InTransactionAsync(async () =>
        {
            var student = await db.Students.SingleOrDefaultAsync(x => x.Id == model.StudentId, ct);
            if (student is null || !student.IsActive) throw new BusinessException("Белсенді студентті таңдаңыз.");
            var copy = await db.BookCopies.Include(x => x.Book).SingleOrDefaultAsync(x => x.Id == model.BookCopyId, ct);
            if (copy is null || copy.Book.IsArchived) throw new BusinessException("Кітап данасы табылмады немесе кітап мұрағатталған.");
            if (copy.Status != CopyStatus.Available || await db.Loans.AnyAsync(x => x.BookCopyId == copy.Id && x.ReturnedAt == null, ct))
                throw new BusinessException("Бұл дана қазір қолжетімсіз. Басқа дананы таңдаңыз.");
            var record = new Loan
            {
                StudentId = student.Id, BookCopyId = copy.Id,
                LoanDate = model.LoanDate.Date, DueDate = model.DueDate.Date,
                IssuedByUserId = issuedByUserId, Notes = model.Notes?.Trim()
            };
            copy.Status = CopyStatus.OnLoan;
            db.Loans.Add(record);
            return record;
        }, "Дана басқа қызметкермен өзгертілді немесе берілді. Тізімді жаңартып, қайталап көріңіз.", ct);
        logger.LogInformation("Loan {LoanId} issued to student {StudentId} by {UserId}", loan.Id, model.StudentId, issuedByUserId);
        return loan.Id;
    }

    public async Task<ReturnLoanViewModel?> GetReturnFormAsync(int loanId, CancellationToken ct = default)
    {
        var loan = await db.Loans.AsNoTracking().Where(x => x.Id == loanId && x.ReturnedAt == null)
            .Select(x => new { x.Id, x.RowVersion, x.LoanDate, x.DueDate, Student = x.Student.FullName, Title = x.BookCopy.Book.Title, x.BookCopy.InventoryNumber }).SingleOrDefaultAsync(ct);
        if (loan is null) return null;
        var days = Math.Max(0, (clock.Today - loan.DueDate.Date).Days);
        return new ReturnLoanViewModel
        {
            LoanId = loan.Id, BookTitle = loan.Title, StudentName = loan.Student, InventoryNumber = loan.InventoryNumber,
            LoanDate = loan.LoanDate, DueDate = loan.DueDate, ReturnDate = clock.Today,
            RowVersion = Convert.ToBase64String(loan.RowVersion), FinePerOverdueDay = DailyRate, OverdueDays = days, EstimatedFine = days * DailyRate
        };
    }

    public async Task ReturnAsync(ReturnLoanViewModel model, string receivedByUserId, CancellationToken ct = default)
    {
        EnsureOperator(receivedByUserId);
        if (model.ReturnDate == default || model.ReturnDate.Date > clock.Today) throw new BusinessException("Қайтару күні бүгіннен кеш болмауы тиіс.");
        if (model.ReturnCondition is not ("Good" or "Damaged")) throw new BusinessException("Дананың жағдайын дұрыс таңдаңыз.");
        ValidateNotes(model.Notes);
        await InTransactionAsync(async () =>
        {
            var loan = await db.Loans.Include(x => x.BookCopy).SingleOrDefaultAsync(x => x.Id == model.LoanId, ct);
            if (loan is null) throw new BusinessException("Кітап беру жазбасы табылмады.");
            if (loan.ReturnedAt.HasValue || await db.Returns.AnyAsync(x => x.LoanId == loan.Id, ct)) throw new BusinessException("Бұл кітап бұрын қайтарылған.");
            if (model.ReturnDate.Date < loan.LoanDate.Date) throw new BusinessException("Қайтару күні кітап берілген күннен ерте болмауы тиіс.");
            if (loan.BookCopy.Status != CopyStatus.OnLoan) throw new BusinessException("Дананың мәртебесі өзгерген. Деректерді тексеріңіз.");
            SetOriginalVersion(loan, model.RowVersion);
            loan.ReturnedAt = model.ReturnDate.Date;
            loan.BookCopy.Status = model.ReturnCondition == "Good" ? CopyStatus.Available : CopyStatus.Damaged;
            loan.BookCopy.Condition = model.ReturnCondition == "Good" ? "Жақсы" : "Зақымдалған";
            db.Returns.Add(new Return { LoanId = loan.Id, ReturnDate = model.ReturnDate.Date, ReceivedByUserId = receivedByUserId, ReturnCondition = model.ReturnCondition, Notes = model.Notes?.Trim() });
            var days = Math.Max(0, (model.ReturnDate.Date - loan.DueDate.Date).Days);
            if (days > 0 && DailyRate > 0)
            {
                if (await db.Fines.AnyAsync(x => x.LoanId == loan.Id, ct)) throw new BusinessException("Бұл жазбаға айыппұл бұрын есептелген. Деректерді тексеріңіз.");
                db.Fines.Add(new Fine { LoanId = loan.Id, StudentId = loan.StudentId, Amount = days * DailyRate, PaidAmount = 0, Status = FineStatus.Unpaid, Reason = $"Кітапты {days} күн кешіктіріп қайтару", CreatedAt = clock.UtcNow });
            }
            return true;
        }, "Бұл жазбаны басқа қызметкер өзгертті. Бетті жаңартып, қайталап көріңіз.", ct);
        logger.LogInformation("Loan {LoanId} returned by {UserId}", model.LoanId, receivedByUserId);
    }

    public async Task<ReturnListViewModel> GetReturnsAsync(string? search, int page = 1, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, 1_000_000);
        search = search?.Trim();
        var query = db.Returns.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Loan.Student.FullName.Contains(search) || x.Loan.Student.StudentNumber.Contains(search) || x.Loan.BookCopy.Book.Title.Contains(search) || x.Loan.BookCopy.InventoryNumber.Contains(search));
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.ReturnDate).ThenByDescending(x => x.Id).Skip((page - 1) * PageSize).Take(PageSize)
            .Select(x => new ReturnRowViewModel { LoanId = x.LoanId, StudentName = x.Loan.Student.FullName, BookTitle = x.Loan.BookCopy.Book.Title, InventoryNumber = x.Loan.BookCopy.InventoryNumber, ReturnDate = x.ReturnDate, ReturnCondition = x.ReturnCondition, FineAmount = x.Loan.Fines.Sum(f => (decimal?)f.Amount) ?? 0m }).ToListAsync(ct);
        foreach (var item in items) item.ReturnCondition = ConditionLabel(item.ReturnCondition) ?? "";
        return new ReturnListViewModel { Search = search, Page = page, TotalCount = count, Items = items };
    }

    private static readonly Expression<Func<Fine, FineRowViewModel>> FineProjection = fine => new FineRowViewModel
    {
        Id = fine.Id, LoanId = fine.LoanId, StudentName = fine.Student.FullName, StudentNumber = fine.Student.StudentNumber,
        BookTitle = fine.Loan.BookCopy.Book.Title, Amount = fine.Amount, PaidAmount = fine.PaidAmount,
        CreatedAt = fine.CreatedAt, PaidAt = fine.PaidAt, Reason = fine.Reason
    };

    public async Task<FineListViewModel> GetFinesAsync(string? search, string? status, int page, string? userId, bool isStaff, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, 1_000_000);
        search = search?.Trim();
        status = status is "all" or "paid" or "partial" or "outstanding" ? status : "outstanding";
        var scoped = ScopedFines(userId, isStaff);
        var outstanding = await scoped.SumAsync(x => (decimal?)(x.Amount - x.PaidAmount), ct) ?? 0m;
        var query = scoped;
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Student.FullName.Contains(search) || x.Student.StudentNumber.Contains(search) || x.Loan.BookCopy.Book.Title.Contains(search));
        query = status switch
        {
            "outstanding" => query.Where(x => x.PaidAmount < x.Amount),
            "paid" => query.Where(x => x.PaidAmount == x.Amount),
            "partial" => query.Where(x => x.PaidAmount > 0 && x.PaidAmount < x.Amount),
            _ => query
        };
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * PageSize).Take(PageSize).Select(FineProjection).ToListAsync(ct);
        return new FineListViewModel { Search = search, Status = status, Page = page, TotalCount = count, Items = items, OutstandingTotal = outstanding, IsStaff = isStaff };
    }

    public async Task<FineDetailsViewModel?> GetFineAsync(int id, string? userId, bool isStaff, CancellationToken ct = default)
    {
        var query = ScopedFines(userId, isStaff).Where(x => x.Id == id);
        var fine = await query.Select(FineProjection).SingleOrDefaultAsync(ct);
        if (fine is null) return null;
        var version = await query.Select(x => x.RowVersion).SingleAsync(ct);
        var payments = await db.FinePayments.AsNoTracking().Where(x => x.FineId == id)
            .OrderByDescending(x => x.PaidAt).ThenByDescending(x => x.Id)
            .Select(x => new FinePaymentRowViewModel { Amount = x.Amount, PaidAt = x.PaidAt }).ToListAsync(ct);
        return new FineDetailsViewModel { Fine = fine, IsStaff = isStaff, RowVersion = Convert.ToBase64String(version), Payments = payments };
    }

    public async Task PayFineAsync(FinePaymentViewModel model, string receivedByUserId, CancellationToken ct = default)
    {
        EnsureOperator(receivedByUserId);
        if (model.Amount <= 0 || decimal.Round(model.Amount, 2) != model.Amount) throw new BusinessException("Төлем сомасы оң сан болуы және екі ондық таңбадан аспауы тиіс.");
        await InTransactionAsync(async () =>
        {
            var fine = await db.Fines.SingleOrDefaultAsync(x => x.Id == model.FineId, ct);
            if (fine is null) throw new BusinessException("Айыппұл табылмады.");
            if (model.Amount > fine.Amount - fine.PaidAmount) throw new BusinessException("Төлем сомасы қалған берешектен аспауы тиіс.");
            SetOriginalVersion(fine, model.RowVersion);
            fine.PaidAmount += model.Amount;
            fine.Status = fine.PaidAmount == fine.Amount ? FineStatus.Paid : FineStatus.PartiallyPaid;
            fine.PaidAt = fine.Status == FineStatus.Paid ? clock.UtcNow : null;
            db.FinePayments.Add(new FinePayment { FineId = fine.Id, Amount = model.Amount, PaidAt = clock.UtcNow, ReceivedByUserId = receivedByUserId });
            return true;
        }, "Айыппұл басқа қызметкермен өзгертілді. Қалдықты жаңартып, қайта көріңіз.", ct);
        logger.LogInformation("Fine {FineId} payment {Amount} recorded by {UserId}", model.FineId, model.Amount, receivedByUserId);
    }

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> action, string conflictMessage, CancellationToken ct)
    {
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                // Hold eligibility reads through commit: a student cannot be
                // deactivated, nor the selected book archived, between validation
                // and issuing its physical copy.
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await action();
                  var issued = db.ChangeTracker.Entries<Loan>().Where(e=>e.State==EntityState.Added).Select(e=>e.Entity).ToList();
                  var returned = db.ChangeTracker.Entries<Return>().Where(e=>e.State==EntityState.Added).Select(e=>e.Entity).ToList();
                  var fines = db.ChangeTracker.Entries<Fine>().Where(e=>e.State==EntityState.Added).Select(e=>e.Entity).ToList();
                await db.SaveChangesAsync(ct);
                  await WorkflowNotificationWriter.AppendAsync(db,clock,issued,returned,fines,ct);
                  if(db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            });
        }
        catch (DbUpdateConcurrencyException exception)
        {
            db.ChangeTracker.Clear();
            logger.LogWarning(exception, "Circulation concurrency conflict");
            throw new BusinessException(conflictMessage);
        }
        catch (Exception exception) when (IsDeadlock(exception))
        {
            db.ChangeTracker.Clear();
            logger.LogWarning(exception, "Circulation operation was a SQL deadlock victim");
            throw new BusinessException(conflictMessage);
        }
        catch (DbUpdateException exception)
        {
            db.ChangeTracker.Clear();
            logger.LogWarning(exception, "Circulation database constraint prevented mutation");
            throw new BusinessException(conflictMessage);
        }
    }

    private static bool IsDeadlock(Exception exception)
    {
        // EF's non-retrying SQL execution strategy can wrap error 1205 inside an
        // InvalidOperationException advising EnableRetryOnFailure.
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 }) return true;
        return false;
    }

    private void SetOriginalVersion<T>(T entity, string? rowVersion) where T : class
    {
        if (string.IsNullOrEmpty(rowVersion)) return;
        try { db.Entry(entity).Property("RowVersion").OriginalValue = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new BusinessException("Жазбаның нұсқасы дұрыс емес. Бетті жаңартыңыз."); }
    }

    private static void EnsureOperator(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new BusinessException("Жүйеге қайта кіріңіз.");
    }

    private static void ValidateNotes(string? notes)
    {
        if (notes?.Length > 2000) throw new BusinessException("Ескертпе 2000 таңбадан аспауы тиіс.");
    }

    internal static string? ConditionLabel(string? condition) => condition switch { "Good" => "Жақсы", "Damaged" => "Зақымдалған", _ => condition };
}
