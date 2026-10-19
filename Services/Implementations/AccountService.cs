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

public sealed class AccountService(ApplicationDbContext db, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, ILibraryClock clock, ILogger<AccountService> logger) : IAccountService
{
    public async Task<AccountActionResult> LoginAsync(LoginViewModel model, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByEmailAsync(model.Email.Trim());
        if (user is null || await db.Students.AnyAsync(s => s.IdentityUserId == user.Id && !s.IsActive, cancellationToken))
            return AccountActionResult.Failure("Пошта немесе құпиясөз дұрыс емес, не аккаунт уақытша бұғатталған.");
        var result = await signIn.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return AccountActionResult.Failure("Пошта немесе құпиясөз дұрыс емес, не аккаунт уақытша бұғатталған.");
        await signIn.SignInAsync(user, model.RememberMe);
        logger.LogInformation("User {UserId} signed in", user.Id);
        return AccountActionResult.Success();
    }

    public async Task<AccountActionResult> RegisterAsync(RegisterViewModel model, CancellationToken cancellationToken = default)
    {
        ApplicationUser? registeredUser = null;
        try
        {
            var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var number = model.StudentNumber.Trim();
                var email = model.Email.Trim();
                var normalizedEmail = email.ToUpperInvariant();
                // Existing library records are never claimed by matching an unauthenticated number.
                if (await db.Students.AnyAsync(s => s.StudentNumber == number, cancellationToken))
                    return AccountActionResult.Failure("Бұл студент нөмірі тіркелген. Аккаунт ашу үшін кітапханашыға хабарласыңыз.");
                if (await db.Students.AnyAsync(s => s.Email.ToUpper() == normalizedEmail, cancellationToken) || await users.FindByEmailAsync(email) is not null)
                    return AccountActionResult.Failure("Бұл электрондық пошта тіркелген.");
                var user = new ApplicationUser { UserName = email, Email = email, FullName = model.FullName.Trim(), PhoneNumber = model.PhoneNumber?.Trim() };
                var created = await users.CreateAsync(user, model.Password);
                if (!created.Succeeded) return FromIdentity(created);
                var role = await users.AddToRoleAsync(user, "Student");
                if (!role.Succeeded) return FromIdentity(role);
                db.Students.Add(new Student
                {
                    StudentNumber = number, FullName = user.FullName, Email = email, PhoneNumber = user.PhoneNumber ?? "",
                    Faculty = model.Faculty.Trim(), Course = model.Course, GroupName = model.GroupName.Trim(), IdentityUserId = user.Id,
                    CreatedAt = clock.UtcNow, IsActive = true
                });
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                registeredUser = user;
                return AccountActionResult.Success();
            });
            if (result.Succeeded && registeredUser is not null)
            {
                await signIn.SignInAsync(registeredUser, isPersistent: false);
                logger.LogInformation("Student account {UserId} registered", registeredUser.Id);
            }
            return result;
        }
        catch (Exception ex) when (DatabaseConflict.IsExpected(ex))
        {
            logger.LogWarning(ex, "Student registration conflicted");
            return AccountActionResult.Failure("Тіркелу орындалмады. Студент нөмірі мен поштаны тексеріп, қайта көріңіз.");
        }
    }

    public Task LogoutAsync() => signIn.SignOutAsync();

    public async Task<ProfileViewModel?> GetProfileAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return null;
        var student = await db.Students.AsNoTracking().Include(s => s.Interests).Include(s => s.StudentSubjects).AsSplitQuery().SingleOrDefaultAsync(s => s.IdentityUserId == userId, cancellationToken);
        return new ProfileViewModel
        {
            FullName = user.FullName, Email = user.Email ?? "", PhoneNumber = user.PhoneNumber,
            StudentId = student?.Id, StudentNumber = student?.StudentNumber, Faculty = student?.Faculty, Course = student?.Course, GroupName = student?.GroupName,
            Interests = student is null ? null : string.Join(", ", student.Interests.Select(i => i.Name)),
            SelectedSubjectIds = student?.StudentSubjects.Select(s => s.SubjectId).ToList() ?? [],
            Subjects = await SubjectOptionsAsync(cancellationToken)
        };
    }

    public async Task<AccountActionResult> UpdateProfileAsync(string userId, ProfileViewModel model, CancellationToken cancellationToken = default)
    {
        ApplicationUser? updatedUser = null;
        try
        {
            var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var user = await users.FindByIdAsync(userId);
                if (user is null) return AccountActionResult.Failure("Аккаунт табылмады.");
                var student = await db.Students.Include(s => s.Interests).Include(s => s.StudentSubjects).AsSplitQuery().SingleOrDefaultAsync(s => s.IdentityUserId == userId, cancellationToken);
                if (student is { IsActive: false }) return AccountActionResult.Failure("Аккаунт белсенді емес. Кітапханашыға хабарласыңыз.");
                var email = model.Email.Trim();
                var normalizedEmail = email.ToUpperInvariant();
                if (await db.Students.AnyAsync(s => s.IdentityUserId != userId && s.Email.ToUpper() == normalizedEmail, cancellationToken))
                    return AccountActionResult.Failure("Бұл электрондық пошта басқа студентке тиесілі.");
                user.FullName = model.FullName.Trim(); user.Email = email; user.UserName = email; user.PhoneNumber = model.PhoneNumber?.Trim();
                if (student is not null)
                {
                    student.FullName = user.FullName; student.Email = email; student.PhoneNumber = user.PhoneNumber ?? "";
                    // Academic fields and record identifiers from the request are deliberately ignored.
                    await StudentProfileData.SetPreferencesAsync(db, student, model.SelectedSubjectIds, model.Interests, cancellationToken);
                }
                var updated = await users.UpdateAsync(user);
                if (!updated.Succeeded) return FromIdentity(updated);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                updatedUser = user;
                return AccountActionResult.Success();
            });
            if (result.Succeeded && updatedUser is not null) await signIn.RefreshSignInAsync(updatedUser);
            return result;
        }
        catch (Exception ex) when (DatabaseConflict.IsExpected(ex))
        {
            logger.LogWarning(ex, "Profile {UserId} update conflicted", userId);
            return AccountActionResult.Failure("Профиль сақталмады. Поштаны тексеріп, қайта көріңіз.");
        }
    }

    public async Task<List<StaffListItem>> ListStaffAsync(string currentUserId)
    {
        var librarians = await users.GetUsersInRoleAsync("Librarian");
        var admins = await users.GetUsersInRoleAsync("Admin");
        var adminIds = admins.Select(u => u.Id).ToHashSet();
        return librarians.Concat(admins).DistinctBy(u => u.Id).OrderBy(u => u.FullName).Select(u => new StaffListItem
        {
            Id = u.Id, FullName = u.FullName, Email = u.Email ?? "", Role = adminIds.Contains(u.Id) ? "Әкімші" : "Кітапханашы",
            IsInactive = u.LockoutEnabled && u.LockoutEnd > DateTimeOffset.UtcNow,
            CanDeactivate = u.Id != currentUserId && !adminIds.Contains(u.Id) && !(u.LockoutEnabled && u.LockoutEnd > DateTimeOffset.UtcNow)
        }).ToList();
    }

    public async Task<AccountActionResult> CreateLibrarianAsync(CreateLibrarianViewModel model, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var email = model.Email.Trim();
                if (await users.FindByEmailAsync(email) is not null) return AccountActionResult.Failure("Бұл электрондық пошта тіркелген.");
                var user = new ApplicationUser { UserName = email, Email = email, FullName = model.FullName.Trim() };
                var result = await users.CreateAsync(user, model.Password);
                if (!result.Succeeded) return FromIdentity(result);
                result = await users.AddToRoleAsync(user, "Librarian");
                if (!result.Succeeded) return FromIdentity(result);
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Librarian {UserId} created", user.Id);
                return AccountActionResult.Success();
            });
        }
        catch (Exception ex) when (DatabaseConflict.IsExpected(ex))
        {
            logger.LogWarning(ex, "Librarian creation conflicted");
            return AccountActionResult.Failure("Қызметкер тіркелмеді. Поштаны тексеріп, қайта көріңіз.");
        }
    }

    public async Task DeactivateLibrarianAsync(string id, string currentUserId, CancellationToken cancellationToken = default)
    {
        if (id == currentUserId) throw new BusinessException("Өз аккаунтыңызды тоқтатуға болмайды.");
        try
        {
            var user = await users.FindByIdAsync(id) ?? throw new BusinessException("Қызметкер табылмады.");
            if (await users.IsInRoleAsync(user, "Admin") || !await users.IsInRoleAsync(user, "Librarian"))
                throw new BusinessException("Тек кітапханашының аккаунтын тоқтатуға болады.");
            user.LockoutEnabled = true; user.LockoutEnd = DateTimeOffset.MaxValue; user.SecurityStamp = Guid.NewGuid().ToString();
            var result = await users.UpdateAsync(user);
            if (!result.Succeeded) throw new BusinessException(string.Join(" ", result.Errors.Select(e => e.Description)));
            logger.LogInformation("Librarian {UserId} deactivated by {ActorId}", id, currentUserId);
        }
        catch (Exception ex) when (DatabaseConflict.IsExpected(ex))
        {
            logger.LogWarning(ex, "Librarian {UserId} deactivation conflicted", id);
            throw new BusinessException("Қызметкер аккаунтының күйі өзгерді. Бетті жаңартып, қайта көріңіз.");
        }
    }

    private Task<List<SelectListItem>> SubjectOptionsAsync(CancellationToken ct) => db.Subjects.AsNoTracking().OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync(ct);
    private static AccountActionResult FromIdentity(IdentityResult result) => AccountActionResult.Failure(result.Errors.Select(e => e.Description).ToArray());
}
