using System.Data;
using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Services.Implementations;

public sealed class StudentService(ApplicationDbContext db, UserManager<ApplicationUser> users, ILibraryClock clock, ILogger<StudentService> logger) : IStudentService
{
    private const int PageSize = 20;

    public async Task<StudentListViewModel> ListAsync(StudentListViewModel filter, CancellationToken cancellationToken = default)
    {
        var query = db.Students.AsNoTracking();
        if (!filter.IncludeInactive) query = query.Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(s => s.FullName.Contains(search) || s.StudentNumber.Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(filter.Faculty)) query = query.Where(s => s.Faculty == filter.Faculty);
        if (filter.Course.HasValue) query = query.Where(s => s.Course == filter.Course);
        if (!string.IsNullOrWhiteSpace(filter.GroupName))
        {
            var group = filter.GroupName.Trim();
            query = query.Where(s => s.GroupName.Contains(group));
        }
        filter.TotalCount = await query.CountAsync(cancellationToken);
        filter.TotalPages = Math.Max(1, (int)Math.Ceiling(filter.TotalCount / (double)PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, filter.TotalPages);
        filter.Students = await query.OrderBy(s => s.FullName).ThenBy(s => s.Id).Skip((filter.Page - 1) * PageSize).Take(PageSize)
            .Select(s => new StudentListItem { Id = s.Id, StudentNumber = s.StudentNumber, FullName = s.FullName, Email = s.Email, Faculty = s.Faculty, Course = s.Course, GroupName = s.GroupName, IsActive = s.IsActive })
            .ToListAsync(cancellationToken);
        filter.Faculties = await db.Students.AsNoTracking().Select(s => s.Faculty).Distinct().OrderBy(s => s).ToListAsync(cancellationToken);
        return filter;
    }

    public async Task<int?> GetOwnIdAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.Students.AsNoTracking().Where(s => s.IdentityUserId == userId).Select(s => (int?)s.Id).SingleOrDefaultAsync(cancellationToken);

    public async Task<StudentDetailsViewModel?> GetDetailsAsync(int id, string currentUserId, bool canManage, CancellationToken cancellationToken = default)
    {
        var student = await db.Students.AsNoTracking().Where(s => s.Id == id && (canManage || s.IdentityUserId == currentUserId))
            .Include(s => s.Interests).Include(s => s.StudentSubjects).ThenInclude(s => s.Subject)
            .AsSplitQuery().SingleOrDefaultAsync(cancellationToken);
        if (student is null) return null;
        var loans = await db.Loans.AsNoTracking().Where(l => l.StudentId == id).OrderByDescending(l => l.LoanDate).ThenByDescending(l => l.Id)
            .Select(l => new StudentLoanItem { Id = l.Id, BookTitle = l.BookCopy.Book.Title, BookId = l.BookCopy.BookId,
                CoverImageUrl = l.BookCopy.Book.CoverImageUrl, PublicationYear = l.BookCopy.Book.PublicationYear,
                BookAuthors = l.BookCopy.Book.BookAuthors.OrderBy(a => a.Author.LastName).Select(a => a.Author.FirstName + " " + a.Author.LastName).ToList(),
                InventoryNumber = l.BookCopy.InventoryNumber, LoanDate = l.LoanDate, DueDate = l.DueDate, ReturnedAt = l.ReturnedAt })
            .ToListAsync(cancellationToken);
        foreach (var loan in loans)
            loan.OverdueDays = Math.Max(0, ((loan.ReturnedAt ?? clock.Today).Date - loan.DueDate.Date).Days);
        var fines = await db.Fines.AsNoTracking().Where(f => f.StudentId == id).OrderByDescending(f => f.CreatedAt)
            .Select(f => new StudentFineItem { BookTitle = f.Loan.BookCopy.Book.Title, Reason = f.Reason, Amount = f.Amount, PaidAmount = f.PaidAmount, CreatedAt = f.CreatedAt }).ToListAsync(cancellationToken);
        return new StudentDetailsViewModel
        {
            Id = student.Id, StudentNumber = student.StudentNumber, FullName = student.FullName, Email = student.Email, PhoneNumber = student.PhoneNumber,
            Faculty = student.Faculty, Course = student.Course, GroupName = student.GroupName, IsActive = student.IsActive, CanManage = canManage,
            Interests = student.Interests.Select(i => i.Name).OrderBy(i => i).ToList(), Subjects = student.StudentSubjects.Select(s => s.Subject.Name).OrderBy(s => s).ToList(),
            ActiveLoans = loans.Where(l => l.ReturnedAt is null).ToList(), History = loans.Where(l => l.ReturnedAt is not null).ToList(),
            Fines = fines, OutstandingFines = fines.Sum(f => f.Balance)
        };
    }

    public Task<List<SelectListItem>> GetSubjectOptionsAsync(CancellationToken cancellationToken = default) =>
        db.Subjects.AsNoTracking().OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync(cancellationToken);

    public async Task<StudentEditViewModel?> GetEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var student = await db.Students.AsNoTracking().Include(s => s.Interests).Include(s => s.StudentSubjects).AsSplitQuery().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (student is null) return null;
        return new StudentEditViewModel
        {
            Id = student.Id, StudentNumber = student.StudentNumber, FullName = student.FullName, Email = student.Email, PhoneNumber = student.PhoneNumber,
            Faculty = student.Faculty, Course = student.Course, GroupName = student.GroupName, Interests = string.Join(", ", student.Interests.Select(i => i.Name)),
            SelectedSubjectIds = student.StudentSubjects.Select(s => s.SubjectId).ToList(), Subjects = await GetSubjectOptionsAsync(cancellationToken)
        };
    }

    public async Task<int> CreateAsync(StudentEditViewModel model, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await EnsureUniqueAsync(model, 0, cancellationToken);
                var student = new Student { CreatedAt = clock.UtcNow, IsActive = true };
                ApplyFields(student, model);
                await StudentProfileData.SetPreferencesAsync(db, student, model.SelectedSubjectIds, model.Interests, cancellationToken);
                db.Students.Add(student);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Student {StudentId} created", student.Id);
                return student.Id;
            });
        }
        catch (Exception ex) when (DatabaseConflict.IsExpected(ex))
        {
            logger.LogWarning(ex, "Student creation conflicted");
            throw new BusinessException("Студентті сақтау мүмкін болмады. Нөмір мен поштаның бірегей екенін тексеріңіз.");
        }
    }

    public async Task UpdateAsync(int id, StudentEditViewModel model, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var student = await db.Students.Include(s => s.IdentityUser).Include(s => s.Interests).Include(s => s.StudentSubjects).AsSplitQuery().SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
                    ?? throw new BusinessException("Студент табылмады.");
                await EnsureUniqueAsync(model, id, cancellationToken);
                ApplyFields(student, model);
                await StudentProfileData.SetPreferencesAsync(db, student, model.SelectedSubjectIds, model.Interests, cancellationToken);
                if (student.IdentityUser is { } user)
                {
                    user.FullName = student.FullName; user.Email = student.Email; user.UserName = student.Email; user.PhoneNumber = student.PhoneNumber;
                    var result = await users.UpdateAsync(user);
                    if (!result.Succeeded) throw new BusinessException(string.Join(" ", result.Errors.Select(e => e.Description)));
                }
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Student {StudentId} updated", id);
            });
        }
        catch (Exception ex) when (DatabaseConflict.IsExpected(ex))
        {
            logger.LogWarning(ex, "Student {StudentId} update conflicted", id);
            throw new BusinessException("Өзгерістер сақталмады. Нөмір мен поштаның бірегей екенін тексеріп, қайта көріңіз.");
        }
    }

    public async Task DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var student = await db.Students.Include(s => s.IdentityUser).SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
                    ?? throw new BusinessException("Студент табылмады.");
                student.IsActive = false;
                if (student.IdentityUser is { } user)
                {
                    // A student record must not be used to disable a privileged account.
                    if (await users.IsInRoleAsync(user, "Admin") || await users.IsInRoleAsync(user, "Librarian"))
                        throw new BusinessException("Қызметкер аккаунтын студент парағы арқылы тоқтатуға болмайды.");
                    user.LockoutEnabled = true;
                    user.LockoutEnd = DateTimeOffset.MaxValue;
                    user.SecurityStamp = Guid.NewGuid().ToString();
                    var result = await users.UpdateAsync(user);
                    if (!result.Succeeded) throw new BusinessException(string.Join(" ", result.Errors.Select(e => e.Description)));
                }
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Student {StudentId} deactivated; history preserved", id);
            });
        }
        catch (Exception ex) when (DatabaseConflict.IsExpected(ex))
        {
            logger.LogWarning(ex, "Student {StudentId} deactivation conflicted", id);
            throw new BusinessException("Студент деректері өзгерді. Бетті жаңартып, қайта көріңіз.");
        }
    }

    private async Task EnsureUniqueAsync(StudentEditViewModel model, int exceptId, CancellationToken ct)
    {
        var number = model.StudentNumber.Trim();
        var email = model.Email.Trim().ToUpperInvariant();
        if (await db.Students.AnyAsync(s => s.Id != exceptId && s.StudentNumber == number, ct)) throw new BusinessException("Бұл студент нөмірі тіркелген.");
        if (await db.Students.AnyAsync(s => s.Id != exceptId && s.Email.ToUpper() == email, ct)) throw new BusinessException("Бұл электрондық пошта басқа студентке тиесілі.");
    }

    private static void ApplyFields(Student student, StudentEditViewModel model)
    {
        student.StudentNumber = model.StudentNumber.Trim(); student.FullName = model.FullName.Trim(); student.Email = model.Email.Trim();
        student.PhoneNumber = model.PhoneNumber?.Trim() ?? ""; student.Faculty = model.Faculty.Trim(); student.Course = model.Course; student.GroupName = model.GroupName.Trim();
    }
}
