using System.Data;
using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EduLibrary.Services.Implementations;

/// <summary>Coordinates circulation and holds inside the caller's SQL transaction.</summary>
public static class ReservationCoordinator
{
    public const string LockResource = "EduLibrary.Reservations.v1";

    public static async Task LockAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Reservation coordination requires a transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = 'EduLibrary.Reservations.v1', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; SELECT @result;";
        command.CommandTimeout = 20;
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        if (result < 0) throw new BusinessException(global::EduLibrary.Localization.UiText.Get("Бронь кезегін қазір жаңарту мүмкін болмады. Қайта көріңіз."));
    }

    public static async Task<int> ReconcileAsync(ApplicationDbContext db, ILibraryClock clock, ReservationOptions options, int? bookId = null, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Reservation reconciliation requires a transaction.");
        var now = clock.UtcNow;
        var query = db.Reservations.Where(r => r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.ReadyForPickup || r.Status == ReservationStatus.Queued);
        if (bookId.HasValue) query = query.Where(r => r.BookId == bookId.Value);
        var active = await query.Include(r => r.Student).Include(r => r.Book).Include(r => r.BookCopy)
            .OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Id).ToListAsync(ct);
        var changed = 0;
        foreach (var reservation in active)
        {
            if (!reservation.Student.IsActive || reservation.Book.IsArchived || reservation.Student.IdentityUserId is null)
            {
                Release(reservation);
                reservation.Status = ReservationStatus.Cancelled;
                reservation.CancelledAtUtc = now;
                Notify(db, reservation, "ReservationCancelled", global::EduLibrary.Localization.UiText.Get("Бронь тоқтатылды"), global::EduLibrary.Localization.UiText.Get("Кітап немесе оқырман профилі қолжетімсіз болғандықтан бронь тоқтатылды."), now);
                changed++;
            }
            else if (reservation.Status != ReservationStatus.Queued && reservation.ExpiresAtUtc <= now)
            {
                Release(reservation);
                reservation.Status = ReservationStatus.Expired;
                reservation.ExpiredAtUtc = now;
                Notify(db, reservation, "ReservationExpired", global::EduLibrary.Localization.UiText.Get("Бронь мерзімі өтті"), global::EduLibrary.Localization.UiText.Get("Алып кету мерзімі аяқталды. Қажет болса, қайта бронь жасаңыз."), now);
                changed++;
            }
        }
        // Release the filtered unique copy keys before allocating them to another reader.
        if (changed > 0) await db.SaveChangesAsync(ct);

        var held = await db.Reservations.AsNoTracking()
            .Where(r => (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.ReadyForPickup) && r.ExpiresAtUtc > now && r.PickupAtUtc != null)
            .Select(r => new { r.PickupLocationId, At = r.PickupAtUtc!.Value }).ToListAsync(ct);
        var capacity = held.GroupBy(r => (r.PickupLocationId, r.At)).ToDictionary(g => g.Key, g => g.Count());
        var slots = ReservationSchedule.EnumerateSlots(now, options).ToArray();
        foreach (var bookQueue in active.Where(r => r.Status == ReservationStatus.Queued).GroupBy(r => r.BookId))
        {
            var copies = await db.BookCopies.Where(c => c.BookId == bookQueue.Key && c.Status == CopyStatus.Available
                    && !c.Loans.Any(l => l.ReturnedAt == null))
                .OrderBy(c => c.Id).ToListAsync(ct);
            var nextCopy = 0;
            foreach (var reservation in bookQueue.OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Id))
            {
                if (nextCopy == copies.Count) break;
                var location = options.PickupLocations.FirstOrDefault(l => l.Id == reservation.PickupLocationId);
                if (location is null)
                {
                    reservation.Status = ReservationStatus.Cancelled;
                    reservation.CancelledAtUtc = now;
                    Notify(db, reservation, "ReservationCancelled", global::EduLibrary.Localization.UiText.Get("Бронь тоқтатылды"), global::EduLibrary.Localization.UiText.Get("Таңдалған алып кету орны енді қолжетімсіз. Жаңа орынмен қайта бронь жасаңыз."), now);
                    changed++;
                    continue;
                }
                DateTime? appointment = null;
                if (reservation.RequestedPickupAtUtc is DateTime requested && slots.Contains(requested)
                    && capacity.GetValueOrDefault((location.Id, requested)) < options.SlotCapacity) appointment = requested;
                appointment ??= slots.Cast<DateTime?>().FirstOrDefault(s => capacity.GetValueOrDefault((location.Id, s!.Value)) < options.SlotCapacity);
                // Strict FIFO: leave the oldest reader queued if no appointment can be allocated.
                if (appointment is null) break;
                var copy = copies[nextCopy++];
                copy.Status = CopyStatus.Reserved;
                reservation.BookCopy = copy;
                reservation.BookCopyId = copy.Id;
                reservation.PickupAtUtc = appointment;
                reservation.ExpiresAtUtc = ReservationSchedule.HoldDeadline(now, appointment.Value, options.PendingHoldHours, options);
                reservation.Status = ReservationStatus.Pending;
                capacity[(location.Id, appointment.Value)] = capacity.GetValueOrDefault((location.Id, appointment.Value)) + 1;
                Notify(db, reservation, "ReservationPromoted", global::EduLibrary.Localization.UiText.Get("Кітап сізге бөлінді"), global::EduLibrary.Localization.UiText.Format($"Кезегіңіз келді. Қызметкер кітапты дайындайды. Алып кету: {ReservationSchedule.ToLocal(appointment.Value):dd.MM.yyyy HH:mm}, {location.Name}."), now);
                changed++;
            }
        }

        var soon = active.Where(r => r.Status == ReservationStatus.ReadyForPickup && r.ExpiresAtUtc > now && r.ExpiresAtUtc <= now.AddHours(options.ExpiringSoonHours)).ToList();
        if (soon.Count > 0)
        {
            var keys = soon.Select(r => $"ReservationExpiring:{r.Id}").ToArray();
            var existing = await db.Notifications.Where(n => keys.Contains(n.DeduplicationKey)).Select(n => n.DeduplicationKey).ToListAsync(ct);
            foreach (var reservation in soon.Where(r => !existing.Contains($"ReservationExpiring:{r.Id}")))
            {
                Notify(db, reservation, "ReservationExpiring", global::EduLibrary.Localization.UiText.Get("Бронь мерзімі аяқталуға жақын"), global::EduLibrary.Localization.UiText.Format($"Дайын кітапты {ReservationSchedule.ToLocal(reservation.ExpiresAtUtc!.Value):dd.MM.yyyy HH:mm} дейін алып кетіңіз."), now);
                changed++;
            }
        }
        await db.SaveChangesAsync(ct);
        return changed;
    }

    internal static void Release(Reservation reservation)
    {
        if (reservation.BookCopy?.Status == CopyStatus.Reserved) reservation.BookCopy.Status = CopyStatus.Available;
    }

    internal static void Notify(ApplicationDbContext db, Reservation reservation, string type, string title, string message, DateTime now)
    {
        if (reservation.Id <= 0) throw new InvalidOperationException("A reservation must be saved before notifying its reader.");
        if (reservation.Student.IdentityUserId is not string userId) return;
        db.Notifications.Add(new Notification
        {
            UserId = userId, DeduplicationKey = $"{type}:{reservation.Id}", Type = type,
            Title = title, Message = Clip($"«{reservation.Book.Title}»: {message}"), Link = $"/Reservations/Details/{reservation.Id}", CreatedAt = now
        });
    }

    private static string Clip(string text) => text.Length <= 1000 ? text : text[..997] + "...";
}
