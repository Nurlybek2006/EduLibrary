using System.Data;
using System.Globalization;
using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Localization;
using EduLibrary.Models.Entities;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Services.Implementations;

public sealed class AccountService(ApplicationDbContext db, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, ILibraryClock clock, ILogger<AccountService> logger, IAvatarStorage? avatars = null) : IAccountService
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
                var user = new ApplicationUser { UserName = email, Email = email, FullName = model.FullName.Trim(), PhoneNumber = model.PhoneNumber?.Trim(),
                    PreferredLanguage = ProfilePreferences.IsLanguage(CultureInfo.CurrentUICulture.Name) ? CultureInfo.CurrentUICulture.Name : "kk-KZ" };
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
        var progress = db.ReadingProgress.AsNoTracking().Where(p => p.UserId == userId && p.DigitalBookAsset.IsPublished && !p.DigitalBookAsset.IsArchived
            && !p.DigitalBookAsset.Book.IsArchived && p.DigitalBookAsset.RightsConfirmed
            && (p.DigitalBookAsset.AccessLevel == Models.Enums.DigitalAccessLevel.Authenticated || p.DigitalBookAsset.AccessLevel == Models.Enums.DigitalAccessLevel.Students));
        var profile = new ProfileViewModel
        {
            AvatarPath = user.AvatarPath, PreferredLanguage = ProfilePreferences.IsLanguage(user.PreferredLanguage) ? user.PreferredLanguage : "kk-KZ",
            PreferredTheme = ProfilePreferences.IsTheme(user.PreferredTheme) ? user.PreferredTheme : "system",
            ActiveLoanCount = student is null ? 0 : await db.Loans.CountAsync(l => l.StudentId == student.Id && l.ReturnedAt == null, cancellationToken),
            ReturnedLoanCount = student is null ? 0 : await db.Loans.CountAsync(l => l.StudentId == student.Id && l.ReturnedAt != null, cancellationToken),
            FullName = user.FullName, Email = user.Email ?? "", PhoneNumber = user.PhoneNumber,
            StudentId = student?.Id, StudentNumber = student?.StudentNumber, Faculty = student?.Faculty, Course = student?.Course, GroupName = student?.GroupName,
            Interests = student is null ? null : string.Join(", ", student.Interests.Select(i => i.Name)),
            SelectedSubjectIds = student?.StudentSubjects.Select(s => s.SubjectId).ToList() ?? [],
            Subjects = await SubjectOptionsAsync(cancellationToken)
        };
        if (student is not null)
        {
            profile.RecentLoans = await db.Loans.AsNoTracking().Where(l => l.StudentId == student.Id).OrderByDescending(l => l.LoanDate).ThenByDescending(l => l.Id).Take(4)
                .Select(l => new ProfileLoanViewModel { Id = l.Id, Title = l.BookCopy.Book.Title, LoanDate = l.LoanDate, DueDate = l.DueDate, ReturnedAt = l.ReturnedAt }).ToListAsync(cancellationToken);
            profile.DigitalBookCount = await progress.CountAsync(cancellationToken);
            profile.CompletedDigitalBookCount = await progress.CountAsync(p => p.CompletedAt != null, cancellationToken);
            profile.DigitalBooks = await progress.OrderByDescending(p => p.LastReadAt).Take(3).Select(p => new DigitalAssetCardViewModel
            {
                Id = p.DigitalBookAssetId, BookId = p.DigitalBookAsset.BookId, Title = p.DigitalBookAsset.Book.Title,
                FileFormat = p.DigitalBookAsset.FileFormat, ProgressPercentage = p.ProgressPercentage, CurrentPage = p.CurrentPage, TotalPages = p.TotalPages,
                LastReadAt = p.LastReadAt, CompletedAt = p.CompletedAt
            }).ToListAsync(cancellationToken);
        }
        return profile;
    }

    public async Task<AccountActionResult> UpdateProfileAsync(string userId, ProfileViewModel model, CancellationToken cancellationToken = default)
    {
        if (!ProfilePreferences.IsLanguage(model.PreferredLanguage)) return AccountActionResult.Failure("Choose a supported language.");
        if (!ProfilePreferences.IsTheme(model.PreferredTheme)) return AccountActionResult.Failure("Choose a supported theme.");
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
                user.PreferredLanguage = model.PreferredLanguage; user.PreferredTheme = model.PreferredTheme;
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

    public async Task<AccountActionResult> UpdateAvatarAsync(string userId, AvatarUploadViewModel model, CancellationToken cancellationToken = default)
    {
        if (avatars is null) return AccountActionResult.Failure("Photo uploads are temporarily unavailable.");
        var saved = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.AvatarPath, u.ConcurrencyStamp, u.LockoutEnabled, u.LockoutEnd }).SingleOrDefaultAsync(cancellationToken);
        if (saved is null) return AccountActionResult.Failure("Account not found.");
        if (saved.LockoutEnabled && saved.LockoutEnd > clock.UtcNow) return AccountActionResult.Failure("This account is inactive.");
        if (await db.Students.AnyAsync(s => s.IdentityUserId == userId && !s.IsActive, cancellationToken)) return AccountActionResult.Failure("This account is inactive.");
        string? newPath = null;
        var committed = false;
        try
        {
            newPath = await avatars.SaveAsync(model, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // A single conditional UPDATE prevents concurrent replacement/removal
            // from deleting the winning image or overwriting another profile edit.
            var updated = await db.Users.Where(u => u.Id == userId && u.ConcurrencyStamp == saved.ConcurrencyStamp && u.AvatarPath == saved.AvatarPath)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.AvatarPath, newPath).SetProperty(u => u.ConcurrencyStamp, Guid.NewGuid().ToString()), cancellationToken);
            if (updated != 1)
            {
                avatars.Delete(newPath);
                return AccountActionResult.Failure("Your profile changed. Refresh the page and try again.");
            }
            await transaction.CommitAsync(CancellationToken.None);
            committed = true;
            avatars.Delete(saved.AvatarPath);
            return AccountActionResult.Success();
        }
        catch (BusinessException ex)
        {
            if (!committed && newPath is not null) avatars.Delete(newPath);
            return AccountActionResult.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException || DatabaseConflict.IsExpected(ex))
        {
            if (!committed && newPath is not null) avatars.Delete(newPath);
            logger.LogWarning("An avatar update could not be completed.");
            return AccountActionResult.Failure("The photo could not be saved. Please try again.");
        }
        catch
        {
            if (!committed && newPath is not null) avatars.Delete(newPath);
            throw;
        }
    }

    public async Task<AccountActionResult> RemoveAvatarAsync(string userId, CancellationToken cancellationToken = default)
    {
        var saved = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.AvatarPath, u.ConcurrencyStamp }).SingleOrDefaultAsync(cancellationToken);
        if (saved is null) return AccountActionResult.Failure("Account not found.");
        if (saved.AvatarPath is null) return AccountActionResult.Success();
        try
        {
            var updated = await db.Users.Where(u => u.Id == userId && u.ConcurrencyStamp == saved.ConcurrencyStamp && u.AvatarPath == saved.AvatarPath)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.AvatarPath, (string?)null).SetProperty(u => u.ConcurrencyStamp, Guid.NewGuid().ToString()), cancellationToken);
            if (updated != 1) return AccountActionResult.Failure("Your profile changed. Refresh the page and try again.");
            avatars?.Delete(saved.AvatarPath);
            return AccountActionResult.Success();
        }
        catch (Exception ex) when (ex is DbUpdateException || DatabaseConflict.IsExpected(ex))
        {
            logger.LogWarning("An avatar removal conflicted with another update.");
            return AccountActionResult.Failure("The photo could not be removed. Please try again.");
        }
    }

    public async Task<List<StaffListItem>> ListStaffAsync(string currentUserId)
    {
        var librarians = await users.GetUsersInRoleAsync("Librarian");
        var admins = await users.GetUsersInRoleAsync("Admin");
        var adminIds = admins.Select(u => u.Id).ToHashSet();
        return librarians.Concat(admins).DistinctBy(u => u.Id).OrderBy(u => u.FullName).Select(u => new StaffListItem
        {
            Id = u.Id, AvatarPath = u.AvatarPath, FullName = u.FullName, Email = u.Email ?? "", Role = UiText.Get(adminIds.Contains(u.Id) ? "Әкімші" : "Кітапханашы"),
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
        if (id == currentUserId) throw new BusinessException(UiText.Get("Өз аккаунтыңызды тоқтатуға болмайды."));
        try
        {
            var user = await users.FindByIdAsync(id) ?? throw new BusinessException(UiText.Get("Қызметкер табылмады."));
            if (await users.IsInRoleAsync(user, "Admin") || !await users.IsInRoleAsync(user, "Librarian"))
                throw new BusinessException(UiText.Get("Тек кітапханашының аккаунтын тоқтатуға болады."));
            user.LockoutEnabled = true; user.LockoutEnd = DateTimeOffset.MaxValue; user.SecurityStamp = Guid.NewGuid().ToString();
            var result = await users.UpdateAsync(user);
            if (!result.Succeeded) throw new BusinessException(string.Join(" ", result.Errors.Select(e => e.Description)));
            logger.LogInformation("Librarian {UserId} deactivated by {ActorId}", id, currentUserId);
        }
        catch (Exception ex) when (DatabaseConflict.IsExpected(ex))
        {
            logger.LogWarning(ex, "Librarian {UserId} deactivation conflicted", id);
            throw new BusinessException(UiText.Get("Қызметкер аккаунтының күйі өзгерді. Бетті жаңартып, қайта көріңіз."));
        }
    }

    private Task<List<SelectListItem>> SubjectOptionsAsync(CancellationToken ct) => db.Subjects.AsNoTracking().OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync(ct);
    private static AccountActionResult FromIdentity(IdentityResult result) => AccountActionResult.Failure(result.Errors.Select(e => e.Description).ToArray());
}
