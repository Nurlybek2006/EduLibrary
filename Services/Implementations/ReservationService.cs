using System.Data;
using System.Globalization;
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

public sealed class ReservationService(ApplicationDbContext db, IOptions<ReservationOptions> options,
    IOptions<LibraryOptions> libraryOptions, ILibraryClock clock, ILogger<ReservationService> logger) : IReservationService
{
    private const int PageSize = 15;
    private ReservationOptions Settings => options.Value;

    private async Task<bool> IsStaffAsync(string userId, CancellationToken ct) => await (
        from user in db.Users
        join membership in db.UserRoles on user.Id equals membership.UserId
        join role in db.Roles on membership.RoleId equals role.Id
        where user.Id == userId && (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= clock.UtcNow)
            && (role.Name == RoleNames.Admin || role.Name == RoleNames.Librarian)
        select user.Id).AnyAsync(ct);

    private async Task RequireStaffAsync(string userId, CancellationToken ct)
    {
        if (!await IsStaffAsync(userId, ct)) throw new BusinessException("Бұл әрекетті тек белсенді кітапхана қызметкері орындай алады.");
    }

    private async Task<Student> RequireStudentAsync(string userId, CancellationToken ct)
    {
        var student = await db.Students.SingleOrDefaultAsync(s => s.IdentityUserId == userId && s.IsActive, ct);
        var permitted = await (from user in db.Users
            join membership in db.UserRoles on user.Id equals membership.UserId
            join role in db.Roles on membership.RoleId equals role.Id
            where user.Id == userId && role.Name == RoleNames.Student
                && (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= clock.UtcNow)
            select user.Id).AnyAsync(ct);
        return student is not null && permitted ? student : throw new BusinessException("Бронь жасау үшін белсенді студент профилі қажет.");
    }

    private IQueryable<Reservation> WithPresentation() => db.Reservations.AsNoTracking()
        .Include(r => r.Book).ThenInclude(b => b.BookAuthors).ThenInclude(a => a.Author)
        .Include(r => r.Student).Include(r => r.BookCopy);

    public async Task<ReservationListViewModel> GetListAsync(ReservationFilterViewModel filter, string userId, bool isStaff, CancellationToken ct = default)
    {
        if (isStaff) await RequireStaffAsync(userId, ct);
        else filter.StudentId = null;
        filter.Search = filter.Search?.Trim();
        filter.Page = Math.Clamp(filter.Page, 1, 1_000_000);
        filter.Status = filter.Status is "active" or "all" or "Pending" or "ReadyForPickup" or "Queued" or "Fulfilled" or "Cancelled" or "Expired" ? filter.Status : "active";
        var query = WithPresentation();
        if (!isStaff) query = query.Where(r => r.Student.IdentityUserId == userId);
        else if (filter.StudentId.HasValue) query = query.Where(r => r.StudentId == filter.StudentId.Value);
        if (filter.BookId.HasValue) query = query.Where(r => r.BookId == filter.BookId.Value);
        var counts = await query.GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(filter.Search)) query = query.Where(r => r.Book.Title.Contains(filter.Search)
            || r.Student.FullName.Contains(filter.Search) || r.Student.StudentNumber.Contains(filter.Search));
        if (filter.Status == "active") query = query.Where(r => r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.ReadyForPickup || r.Status == ReservationStatus.Queued);
        else if (Enum.TryParse<ReservationStatus>(filter.Status, out var state)) query = query.Where(r => r.Status == state);
        var total = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        filter.Page = Math.Min(filter.Page, totalPages);
        var rows = await query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id).Skip((filter.Page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        var positions = await QueuePositionsAsync(rows, ct);
        return new()
        {
            IsStaff = isStaff, Filter = filter, TotalCount = total, TotalPages = totalPages,
            PendingCount = counts.FirstOrDefault(c => c.Status == ReservationStatus.Pending)?.Count ?? 0,
            ReadyCount = counts.FirstOrDefault(c => c.Status == ReservationStatus.ReadyForPickup)?.Count ?? 0,
            QueuedCount = counts.FirstOrDefault(c => c.Status == ReservationStatus.Queued)?.Count ?? 0,
            Items = rows.Select(r => FillRow(new ReservationRowViewModel(), r, positions.GetValueOrDefault(r.Id))).ToList()
        };
    }

    public async Task<ReservationDetailsViewModel?> GetDetailsAsync(int id, string userId, bool isStaff, CancellationToken ct = default)
    {
        if (isStaff) await RequireStaffAsync(userId, ct);
        var reservation = await WithPresentation().SingleOrDefaultAsync(r => r.Id == id && (isStaff || r.Student.IdentityUserId == userId), ct);
        if (reservation is null) return null;
        var positions = await QueuePositionsAsync([reservation], ct);
        var result = FillRow(new ReservationDetailsViewModel(), reservation, positions.GetValueOrDefault(reservation.Id));
        var location = Settings.PickupLocations.FirstOrDefault(l => l.Id == reservation.PickupLocationId);
        var active = IsActive(reservation.Status);
        result.IsStaff = isStaff;
        result.CanCancel = active;
        result.CanPrepare = isStaff && reservation.Status == ReservationStatus.Pending && reservation.ExpiresAtUtc > clock.UtcNow;
        result.CanFulfill = isStaff && reservation.Status == ReservationStatus.ReadyForPickup && reservation.ExpiresAtUtc > clock.UtcNow;
        result.RowVersion = Convert.ToBase64String(reservation.RowVersion);
        result.PickupCode = reservation.PickupCode;
        result.PickupLocationAddress = location?.Address ?? "";
        result.InventoryNumber = reservation.BookCopy?.InventoryNumber;
        result.ShelfLocation = reservation.BookCopy?.ShelfLocation;
        result.LoanId = reservation.LoanId;
        result.ReadyAt = Local(reservation.ReadyAtUtc);
        result.FulfillForm = new() { Id = reservation.Id, RowVersion = result.RowVersion, DueDate = clock.Today.AddDays(libraryOptions.Value.DefaultLoanDays) };
        return result;
    }

    public async Task<ReservationCreateViewModel?> GetCreateFormAsync(int bookId, string userId, CancellationToken ct = default)
    {
        await RequireStudentAsync(userId, ct);
        var book = await db.Books.AsNoTracking().Include(b => b.BookAuthors).ThenInclude(a => a.Author).SingleOrDefaultAsync(b => b.Id == bookId && !b.IsArchived, ct);
        if (book is null) return null;
        var available = await db.BookCopies.CountAsync(c => c.BookId == bookId && c.Status == CopyStatus.Available && !c.Loans.Any(l => l.ReturnedAt == null), ct);
        var queued = await db.Reservations.AnyAsync(r => r.BookId == bookId && r.Status == ReservationStatus.Queued, ct);
        var dates = ReservationSchedule.EnumerateSlots(clock.UtcNow, Settings).Select(ReservationSchedule.ToLocal).Select(s => s.Date).Distinct().ToList();
        var model = new ReservationCreateViewModel
        {
            BookId = book.Id, BookTitle = book.Title, Authors = Authors(book), CoverImageUrl = book.CoverImageUrl, PublicationYear = book.PublicationYear,
            AvailableCopies = available, JoinQueue = available == 0 || queued,
            PickupLocationId = Settings.PickupLocations[0].Id,
            Locations = Settings.PickupLocations.Select(l => new SelectListItem(l.Name, l.Id)).ToList(),
            PickupDates = dates.Select(d => new SelectListItem(d.ToString("dd.MM.yyyy"), d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).ToList(),
            PickupDate = dates.Count > 0 ? dates[0] : null
        };
        if (dates.Count > 0) model.Slots = await GetSlotsAsync(dates[0], model.PickupLocationId, ct);
        return model;
    }

    public async Task<List<PickupSlotViewModel>> GetSlotsAsync(DateTime pickupDate, string pickupLocationId, CancellationToken ct = default)
    {
        if (!Settings.PickupLocations.Any(l => l.Id == pickupLocationId)) return [];
        var slots = ReservationSchedule.EnumerateSlots(clock.UtcNow, Settings).Where(s => ReservationSchedule.ToLocal(s).Date == pickupDate.Date).ToArray();
        if (slots.Length == 0) return [];
        var held = await db.Reservations.AsNoTracking().Where(r => r.PickupLocationId == pickupLocationId && r.PickupAtUtc != null
            && slots.Contains(r.PickupAtUtc.Value) && r.ExpiresAtUtc > clock.UtcNow
            && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.ReadyForPickup))
            .GroupBy(r => r.PickupAtUtc!.Value).Select(g => new { At = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.At, g => g.Count, ct);
        return slots.Select(s => new PickupSlotViewModel
        {
            Value = ReservationSchedule.ToLocal(s).ToString("HH:mm"),
            Label = $"{ReservationSchedule.ToLocal(s):HH:mm}–{ReservationSchedule.ToLocal(s).AddMinutes(Settings.SlotMinutes):HH:mm}",
            AvailableCapacity = Math.Max(0, Settings.SlotCapacity - held.GetValueOrDefault(s))
        }).Where(s => s.AvailableCapacity > 0).ToList();
    }

    public Task<int> CreateAsync(ReservationCreateViewModel model, string userId, CancellationToken ct = default) => InTransactionAsync(async () =>
    {
        var student = await RequireStudentAsync(userId, ct);
        var book = await db.Books.SingleOrDefaultAsync(b => b.Id == model.BookId && !b.IsArchived, ct) ?? throw new BusinessException("Кітап қолжетімсіз.");
        if (!Settings.PickupLocations.Any(l => l.Id == model.PickupLocationId)) throw new BusinessException("Алып кету орнын дұрыс таңдаңыз.");
        if (await db.Reservations.AnyAsync(r => r.StudentId == student.Id && r.BookId == book.Id && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.ReadyForPickup || r.Status == ReservationStatus.Queued), ct))
            throw new BusinessException("Бұл кітапқа белсенді броніңіз немесе кезегіңіз бар.");
        if (await db.Loans.AnyAsync(l => l.StudentId == student.Id && l.BookCopy.BookId == book.Id && l.ReturnedAt == null, ct))
            throw new BusinessException("Бұл кітап қазір сіздің қолыңызда. Алдымен оны қайтарыңыз.");
        DateTime? requested = null;
        if (!model.JoinQueue)
        {
            if (!model.PickupDate.HasValue || !ReservationSchedule.TryGetSlot(model.PickupDate.Value, model.PickupTime, clock.UtcNow, Settings, out var slot))
                throw new BusinessException("Кітапхананың қолжетімді күні мен уақытын таңдаңыз.");
            requested = slot;
            if (await CapacityAsync(model.PickupLocationId, slot, null, ct) >= Settings.SlotCapacity)
                throw new BusinessException("Бұл уақыт аралығы толды. Басқа уақытты таңдаңыз.");
        }
        var queueExists = await db.Reservations.AnyAsync(r => r.BookId == book.Id && r.Status == ReservationStatus.Queued, ct);
        var copy = !model.JoinQueue && !queueExists ? await db.BookCopies.OrderBy(c => c.Id).FirstOrDefaultAsync(c => c.BookId == book.Id && c.Status == CopyStatus.Available && !c.Loans.Any(l => l.ReturnedAt == null), ct) : null;
        var reservation = new Reservation
        {
            BookId = book.Id, Book = book, StudentId = student.Id, Student = student, CreatedAtUtc = clock.UtcNow,
            PickupCode = ReservationPickupCode.Generate(), PickupLocationId = model.PickupLocationId, RequestedPickupAtUtc = requested,
            Status = copy is null ? ReservationStatus.Queued : ReservationStatus.Pending
        };
        if (copy is not null)
        {
            copy.Status = CopyStatus.Reserved;
            reservation.BookCopy = copy;
            reservation.BookCopyId = copy.Id;
            reservation.PickupAtUtc = requested;
            reservation.ExpiresAtUtc = ReservationSchedule.HoldDeadline(clock.UtcNow, requested!.Value, Settings.PendingHoldHours, Settings);
        }
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync(ct);
        ReservationCoordinator.Notify(db, reservation, copy is null ? "ReservationQueued" : "ReservationCreated", copy is null ? "Кітап кезегіне қосылдыңыз" : "Бронь қабылданды",
            copy is null ? "Бос дана шыққанда кезек ретімен сізге бөлінеді." : "Дана сізге сақталды. Қызметкер дайындағаннан кейін хабарлама келеді.", clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await ReservationCoordinator.ReconcileAsync(db, clock, Settings, book.Id, ct);
        return reservation.Id;
    }, ct);

    public async Task<ReservationReadyViewModel?> GetPrepareFormAsync(int id, string staffUserId, CancellationToken ct = default)
    {
        await RequireStaffAsync(staffUserId, ct);
        var reservation = await db.Reservations.AsNoTracking().Include(r => r.Book).Include(r => r.Student)
            .SingleOrDefaultAsync(r => r.Id == id && r.Status == ReservationStatus.Pending && r.ExpiresAtUtc > clock.UtcNow, ct);
        if (reservation is null) return null;
        var copies = await db.BookCopies.AsNoTracking().Where(c => c.BookId == reservation.BookId && (c.Id == reservation.BookCopyId || c.Status == CopyStatus.Available)
            && !c.Loans.Any(l => l.ReturnedAt == null)).OrderBy(c => c.Id).Select(c => new { c.Id, c.InventoryNumber, c.ShelfLocation }).ToListAsync(ct);
        return new() { Id = id, BookTitle = reservation.Book.Title, StudentName = reservation.Student.FullName, BookCopyId = reservation.BookCopyId!.Value,
            RowVersion = Convert.ToBase64String(reservation.RowVersion), CopyOptions = copies.Select(c => new SelectListItem($"{c.InventoryNumber} · {c.ShelfLocation}", c.Id.ToString())).ToList() };
    }

    public async Task MarkReadyAsync(ReservationReadyViewModel model, string staffUserId, CancellationToken ct = default)
    {
        await InTransactionAsync(async () =>
        {
            await RequireStaffAsync(staffUserId, ct);
            var reservation = await TrackedAsync(model.Id, ct);
            CheckVersion(reservation, model.RowVersion);
            if (reservation.Status != ReservationStatus.Pending || reservation.ExpiresAtUtc <= clock.UtcNow) throw new BusinessException("Бұл броньді қазір дайындау мүмкін емес. Бетті жаңартыңыз.");
            if (!reservation.Student.IsActive || reservation.Book.IsArchived) throw new BusinessException("Кітап немесе студент профилі қолжетімсіз.");
            var copy = await db.BookCopies.SingleOrDefaultAsync(c => c.Id == model.BookCopyId && c.BookId == reservation.BookId
                && (c.Status == CopyStatus.Available || (c.Id == reservation.BookCopyId && c.Status == CopyStatus.Reserved)) && !c.Loans.Any(l => l.ReturnedAt == null), ct)
                ?? throw new BusinessException("Таңдалған дана қолжетімсіз немесе басқа кітапқа тиесілі.");
            if (reservation.BookCopyId != copy.Id)
            {
                ReservationCoordinator.Release(reservation);
                reservation.BookCopy = copy;
                reservation.BookCopyId = copy.Id;
                copy.Status = CopyStatus.Reserved;
            }
            var appointment = reservation.PickupAtUtc;
            if (appointment is null || appointment.Value <= clock.UtcNow
                || !ReservationSchedule.IsConfiguredSlot(ReservationSchedule.ToLocal(appointment.Value), Settings)
                || await CapacityAsync(reservation.PickupLocationId, appointment.Value, reservation.Id, ct) >= Settings.SlotCapacity)
                appointment = await NextSlotAsync(reservation.PickupLocationId, reservation.Id, ct);
            if (appointment is null) throw new BusinessException("Алып кетуге бос уақыт жоқ. Күнтізбені немесе орын сыйымдылығын тексеріңіз.");
            reservation.PickupAtUtc = appointment;
            reservation.Status = ReservationStatus.ReadyForPickup;
            reservation.ReadyAtUtc = clock.UtcNow;
            reservation.PreparedByUserId = staffUserId;
            reservation.ExpiresAtUtc = ReservationSchedule.HoldDeadline(clock.UtcNow, appointment.Value, Settings.ReadyHoldHours, Settings);
            var location = Settings.PickupLocations.FirstOrDefault(l => l.Id == reservation.PickupLocationId) ?? throw new BusinessException("Алып кету орны қолжетімсіз.");
            ReservationCoordinator.Notify(db, reservation, "ReservationReady", "Кітап алып кетуге дайын", $"{location.Name}, {ReservationSchedule.ToLocal(appointment.Value):dd.MM.yyyy HH:mm}. Бронь кодын қызметкерге көрсетіңіз. Сақтау мерзімі: {ReservationSchedule.ToLocal(reservation.ExpiresAtUtc.Value):dd.MM.yyyy HH:mm}.", clock.UtcNow);
            return 0;
        }, ct);
    }

    public Task<int> FulfillAsync(ReservationFulfillViewModel model, string staffUserId, CancellationToken ct = default) => InTransactionAsync(async () =>
    {
        await RequireStaffAsync(staffUserId, ct);
        var reservation = await TrackedAsync(model.Id, ct);
        CheckVersion(reservation, model.RowVersion);
        if (reservation.Status != ReservationStatus.ReadyForPickup || reservation.ExpiresAtUtc <= clock.UtcNow)
            throw new BusinessException("Бронь алып кетуге дайын емес немесе мерзімі өткен.");
        if (!ReservationPickupCode.Matches(model.PickupCode, reservation.PickupCode)) throw new BusinessException("Бронь коды қате. Студент көрсеткен кодты қайта тексеріңіз.");
        if (!reservation.Student.IsActive || reservation.Book.IsArchived || reservation.BookCopy?.Status != CopyStatus.Reserved)
            throw new BusinessException("Кітапты беру мүмкін емес: студент немесе бөлінген дана қолжетімсіз.");
        if (model.DueDate.Date < clock.Today || model.DueDate.Date > clock.Today.AddDays(365)) throw new BusinessException("Қайтару мерзімі бүгіннен бастап бір жыл аралығында болуы керек.");
        if (await db.Loans.AnyAsync(l => l.ReturnedAt == null && (l.BookCopyId == reservation.BookCopyId || (l.StudentId == reservation.StudentId && l.BookCopy.BookId == reservation.BookId)), ct))
            throw new BusinessException("Бұл дана немесе студенттің осы кітапқа белсенді берілімі бар.");
        var loan = new Loan { BookCopyId = reservation.BookCopyId!.Value, StudentId = reservation.StudentId,
            LoanDate = clock.Today, DueDate = model.DueDate.Date, IssuedByUserId = staffUserId };
        db.Loans.Add(loan);
        await db.SaveChangesAsync(ct);
        reservation.Status = ReservationStatus.Fulfilled;
        reservation.LoanId = loan.Id;
        reservation.Loan = loan;
        reservation.FulfilledAtUtc = clock.UtcNow;
        reservation.BookCopy!.Status = CopyStatus.OnLoan;
        await db.SaveChangesAsync(ct);
        await WorkflowNotificationWriter.AppendAsync(db, clock, [loan], [], [], ct);
        return loan.Id;
    }, ct);

    public async Task CancelAsync(int id, string rowVersion, string userId, bool isStaff, CancellationToken ct = default)
    {
        await InTransactionAsync(async () =>
        {
            if (isStaff) await RequireStaffAsync(userId, ct);
            var reservation = await TrackedAsync(id, ct);
            if (!isStaff && reservation.Student.IdentityUserId != userId) throw new BusinessException("Бұл броньге қол жеткізуге рұқсат жоқ.");
            CheckVersion(reservation, rowVersion);
            if (!IsActive(reservation.Status)) throw new BusinessException("Аяқталған броньді тоқтату мүмкін емес.");
            ReservationCoordinator.Release(reservation);
            reservation.Status = ReservationStatus.Cancelled;
            reservation.CancelledAtUtc = clock.UtcNow;
            reservation.CancelledByUserId = userId;
            ReservationCoordinator.Notify(db, reservation, "ReservationCancelled", "Бронь тоқтатылды", "Бронь тоқтатылды; бөлінген дана кезектегі келесі оқырманға ұсынылады.", clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await ReservationCoordinator.ReconcileAsync(db, clock, Settings, reservation.BookId, ct);
            return 0;
        }, ct);
    }

    public async Task<int?> FindByCodeAsync(string code, string staffUserId, CancellationToken ct = default)
    {
        await RequireStaffAsync(staffUserId, ct);
        var normalized = ReservationPickupCode.Normalize(code);
        if (normalized is null) return null;
        var candidate = await db.Reservations.AsNoTracking().Where(r => r.PickupCode == normalized)
            .Select(r => new { r.Id, r.PickupCode }).SingleOrDefaultAsync(ct);
        return candidate is not null && ReservationPickupCode.Matches(normalized, candidate.PickupCode) ? candidate.Id : null;
    }

    public Task<int> ProcessAsync(CancellationToken ct = default) => InTransactionAsync(() => Task.FromResult(0), ct, reconcileOnly: true);

    private async Task<int> InTransactionAsync(Func<Task<int>> action, CancellationToken ct, bool reconcileOnly = false)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await ReservationCoordinator.LockAsync(db, ct);
            var changes = await ReservationCoordinator.ReconcileAsync(db, clock, Settings, ct: ct);
            var result = reconcileOnly ? changes : await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (ex is DbUpdateException || DatabaseConflict.IsExpected(ex))
        {
            db.ChangeTracker.Clear();
            // Do not log payloads, pickup codes, student identities, or SQL parameters.
            logger.LogWarning("Reservation update conflicted with another transaction.");
            throw new BusinessException("Бронь басқа әрекетпен жаңартылды. Бетті жаңартып, қайта көріңіз.");
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private Task<Reservation> TrackedAsync(int id, CancellationToken ct) => FindTrackedAsync(id, ct);
    private async Task<Reservation> FindTrackedAsync(int id, CancellationToken ct) => await db.Reservations.Include(r => r.Student).Include(r => r.Book).Include(r => r.BookCopy)
        .SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new BusinessException("Бронь табылмады.");

    private static void CheckVersion(Reservation reservation, string? version)
    {
        if (version is null || version.Length != 12) throw new BusinessException("Бетті жаңартып, қайта көріңіз.");
        byte[] supplied;
        try { supplied = Convert.FromBase64String(version); }
        catch (FormatException) { throw new BusinessException("Бетті жаңартып, қайта көріңіз."); }
        if (supplied.Length != 8 || !supplied.AsSpan().SequenceEqual(reservation.RowVersion)) throw new BusinessException("Бронь өзгертілген. Бетті жаңартып, қайта көріңіз.");
    }

    private Task<int> CapacityAsync(string locationId, DateTime slot, int? excludingId, CancellationToken ct) => db.Reservations.CountAsync(r => (!excludingId.HasValue || r.Id != excludingId.Value)
        && r.PickupLocationId == locationId && r.PickupAtUtc == slot && r.ExpiresAtUtc > clock.UtcNow
        && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.ReadyForPickup), ct);

    private async Task<DateTime?> NextSlotAsync(string locationId, int excludingId, CancellationToken ct)
    {
        var occupied = await db.Reservations.AsNoTracking().Where(r => r.Id != excludingId && r.PickupLocationId == locationId && r.PickupAtUtc != null && r.ExpiresAtUtc > clock.UtcNow
            && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.ReadyForPickup))
            .GroupBy(r => r.PickupAtUtc!.Value).Select(g => new { At = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.At, g => g.Count, ct);
        return ReservationSchedule.EnumerateSlots(clock.UtcNow, Settings).Cast<DateTime?>().FirstOrDefault(s => occupied.GetValueOrDefault(s!.Value) < Settings.SlotCapacity);
    }

    private async Task<Dictionary<int, int>> QueuePositionsAsync(IReadOnlyList<Reservation> rows, CancellationToken ct)
    {
        var books = rows.Where(r => r.Status == ReservationStatus.Queued).Select(r => r.BookId).Distinct().ToArray();
        if (books.Length == 0) return [];
        var queued = await db.Reservations.AsNoTracking().Where(r => books.Contains(r.BookId) && r.Status == ReservationStatus.Queued)
            .OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Id).Select(r => new { r.Id, r.BookId }).ToListAsync(ct);
        return queued.GroupBy(r => r.BookId).SelectMany(g => g.Select((r, i) => new { r.Id, Position = i + 1 })).ToDictionary(r => r.Id, r => r.Position);
    }

    private T FillRow<T>(T row, Reservation reservation, int position) where T : ReservationRowViewModel
    {
        row.Id = reservation.Id; row.BookId = reservation.BookId; row.BookTitle = reservation.Book.Title; row.Authors = Authors(reservation.Book);
        row.CoverImageUrl = reservation.Book.CoverImageUrl; row.PublicationYear = reservation.Book.PublicationYear;
        row.StudentName = reservation.Student.FullName; row.StudentNumber = reservation.Student.StudentNumber; row.Status = reservation.Status;
        row.CreatedAt = ReservationSchedule.ToLocal(reservation.CreatedAtUtc); row.PickupAt = Local(reservation.PickupAtUtc); row.ExpiresAt = Local(reservation.ExpiresAtUtc);
        row.QueuePosition = position > 0 ? position : null;
        row.PickupLocationName = Settings.PickupLocations.FirstOrDefault(l => l.Id == reservation.PickupLocationId)?.Name ?? "Қолжетімсіз орын";
        return row;
    }

    private static bool IsActive(ReservationStatus status) => status is ReservationStatus.Pending or ReservationStatus.ReadyForPickup or ReservationStatus.Queued;
    private static DateTime? Local(DateTime? date) => date.HasValue ? ReservationSchedule.ToLocal(date.Value) : null;
    private static string Authors(Book book) => string.Join(", ", book.BookAuthors.OrderBy(a => a.Author.LastName).Select(a => $"{a.Author.FirstName} {a.Author.LastName}"));
}
