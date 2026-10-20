using System.Data;
using System.Linq.Expressions;
using EduLibrary.Data;
using EduLibrary.Models.Entities;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Services.Implementations;

public sealed class NotificationService(ApplicationDbContext db, ILibraryClock clock) : INotificationService
{
    private static readonly TimeZoneInfo LocalZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Qyzylorda");
    private static readonly Expression<Func<Notification,NotificationRowViewModel>> Projection = n=>new()
    {
        Id=n.Id, Title=n.Title, Message=n.Message, Link=n.Link, Type=n.Type, CreatedAt=n.CreatedAt, IsRead=n.ReadAt!=null
    };
    public static async Task<List<NotificationRowViewModel>> LocalizeAsync(ApplicationDbContext db, string userId, List<NotificationRowViewModel> items, CancellationToken ct = default)
    {
        // Scope first, including when this shared projection helper is called by another screen.
        var rowIds = items.Select(i => i.Id).ToArray();
        var owned = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId && rowIds.Contains(n.Id)).Select(n => n.Id).ToListAsync(ct);
        items = items.Where(i => owned.Contains(i.Id)).ToList();
        static int? LinkId(string link, string prefix) => link.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(link[prefix.Length..], out var id) && id > 0 ? id : null;
        var loanIds = items.Select(i => LinkId(i.Link, "/Loans/Details/")).OfType<int>().Distinct().ToArray();
        var fineIds = items.Select(i => LinkId(i.Link, "/Fines/Details/")).OfType<int>().Distinct().ToArray();
        var reservationIds = items.Select(i => LinkId(i.Link, "/Reservations/Details/")).OfType<int>().Distinct().ToArray();
        var loans = await db.Loans.AsNoTracking().Where(l => loanIds.Contains(l.Id) && l.Student.IdentityUserId == userId)
            .Select(l => new { l.Id, Title = l.BookCopy.Book.Title, l.DueDate }).ToDictionaryAsync(l => l.Id, ct);
        var fines = await db.Fines.AsNoTracking().Where(f => fineIds.Contains(f.Id) && f.Student.IdentityUserId == userId)
            .Select(f => new { f.Id, Title = f.Loan.BookCopy.Book.Title, f.Amount }).ToDictionaryAsync(f => f.Id, ct);
        var reservations = await db.Reservations.AsNoTracking().Where(r => reservationIds.Contains(r.Id) && r.Student.IdentityUserId == userId)
            .Select(r => new { r.Id, Title = r.Book.Title, r.PickupAtUtc, r.ExpiresAtUtc }).ToDictionaryAsync(r => r.Id, ct);
        foreach (var item in items)
        {
            item.CreatedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc), LocalZone);
            var loanId = LinkId(item.Link, "/Loans/Details/");
            if (loanId.HasValue && loans.TryGetValue(loanId.Value, out var loan))
            {
                if (item.Type == "Issued") { item.Title = UiText.Get("Кітап сәтті берілді"); item.Message = UiText.Get("Notifications.IssuedBody", loan.Title, loan.DueDate); }
                else if (item.Type == "Returned") { item.Title = UiText.Get("Кітап сәтті қайтарылды"); item.Message = UiText.Get("Notifications.ReturnedBody", loan.Title); }
                else if (item.Type is "Due1" or "Due3" or "Overdue")
                {
                    item.Title = UiText.Get(item.Type == "Overdue" ? "Қайтару мерзімі өтті" : item.Type == "Due1" ? "Қайтаруға 1 күн қалды" : "Қайтаруға 3 күн қалды");
                    item.Message = UiText.Get("Notifications.ReminderBody", loan.Title, loan.DueDate);
                }
            }
            var fineId = LinkId(item.Link, "/Fines/Details/");
            if (item.Type == "Fine" && fineId.HasValue && fines.TryGetValue(fineId.Value, out var fine))
            { item.Title = UiText.Get("Айыппұл есептелді"); item.Message = UiText.Get("Notifications.FineBody", fine.Title, fine.Amount); }
            var reservationId = LinkId(item.Link, "/Reservations/Details/");
            if (reservationId.HasValue && reservations.TryGetValue(reservationId.Value, out var reservation))
            {
                var pickup = reservation.PickupAtUtc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(reservation.PickupAtUtc.Value, DateTimeKind.Utc), LocalZone) : (DateTime?)null;
                var deadline = reservation.ExpiresAtUtc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(reservation.ExpiresAtUtc.Value, DateTimeKind.Utc), LocalZone) : (DateTime?)null;
                var title = item.Type switch {
                    "ReservationCreated" => "Бронь қабылданды", "ReservationQueued" => "Кітап кезегіне қосылдыңыз", "ReservationReady" => "Кітап алып кетуге дайын",
                    "ReservationPromoted" => "Кітап сізге бөлінді", "ReservationExpired" => "Бронь мерзімі өтті", "ReservationCancelled" => "Бронь тоқтатылды",
                    "ReservationExpiring" => "Бронь мерзімі аяқталуға жақын", _ => null };
                if (title is not null)
                {
                    item.Title = UiText.Get(title);
                    var key = item.Type switch { "ReservationCreated" => "Created", "ReservationQueued" => "Queued", "ReservationReady" => "Ready", "ReservationPromoted" => "Promoted", "ReservationExpired" => "Expired", "ReservationCancelled" => "Cancelled", _ => "Expiring" };
                    item.Message = UiText.Get("Notifications.Reservation" + key + "Body", reservation.Title, pickup, deadline);
                }
            }
        }
        return items;
    }
    public async Task<NotificationPageViewModel> GetAsync(string userId,int page=1,bool unreadOnly=false,CancellationToken ct=default)
    {
        var scoped=db.Notifications.AsNoTracking().Where(n=>n.UserId==userId);
        var unread=await scoped.CountAsync(n=>n.ReadAt==null,ct);
        var query=unreadOnly ? scoped.Where(n=>n.ReadAt==null) : scoped;
        var count=await query.CountAsync(ct);
        var pages=Math.Max(1,(int)Math.Ceiling(count/20d)); page=Math.Clamp(page,1,pages);
        var items=await query.OrderByDescending(n=>n.CreatedAt).ThenByDescending(n=>n.Id).Skip((page-1)*20).Take(20).Select(Projection).ToListAsync(ct);
        return new(){Items=await LocalizeAsync(db,userId,items,ct),Page=page,TotalPages=pages,TotalCount=count,UnreadCount=unread,UnreadOnly=unreadOnly};
    }
    public async Task<NotificationBellViewModel> GetBellAsync(string userId,CancellationToken ct=default)
    {
        var query=db.Notifications.AsNoTracking().Where(n=>n.UserId==userId);
        var count=await query.CountAsync(n=>n.ReadAt==null,ct);
        var items=await query.OrderBy(n=>n.ReadAt!=null).ThenByDescending(n=>n.CreatedAt).ThenByDescending(n=>n.Id).Take(5).Select(Projection).ToListAsync(ct);
        return new(count,await LocalizeAsync(db,userId,items,ct));
    }
    public async Task<bool> MarkReadAsync(string userId,int id,CancellationToken ct=default)
    {
        var now=clock.UtcNow;
        var updated=await db.Notifications.Where(n=>n.UserId==userId && n.Id==id && n.ReadAt==null).ExecuteUpdateAsync(s=>s.SetProperty(n=>n.ReadAt,now),ct);
        return updated>0 || await db.Notifications.AnyAsync(n=>n.UserId==userId && n.Id==id,ct);
    }
    public Task<int> MarkAllReadAsync(string userId,CancellationToken ct=default)
    {
        var now=clock.UtcNow;
        return db.Notifications.Where(n=>n.UserId==userId && n.ReadAt==null).ExecuteUpdateAsync(s=>s.SetProperty(n=>n.ReadAt,now),ct);
    }
    public async Task<int> GenerateRemindersAsync(CancellationToken ct=default)
    {
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        // A transaction-scoped SQL lock coordinates all application instances.
        // The unique key remains a second independent deduplication guarantee.
        await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result = sp_getapplock @Resource=N'EduLibrary.Reminders.v1', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; IF @result < 0 THROW 51000, 'Reminder lock unavailable', 1;",ct);
        var today=clock.Today; var one=today.AddDays(1);var three=today.AddDays(3);
        var loans=await db.Loans.AsNoTracking().Where(l=>l.ReturnedAt==null && l.Student.IdentityUserId!=null && (l.DueDate==one||l.DueDate==three||l.DueDate<today))
            .Select(l=>new{l.Id,l.DueDate,UserId=l.Student.IdentityUserId!,Title=l.BookCopy.Book.Title}).ToListAsync(ct);
        var keys=loans.Select(l=>NotificationRules.EventKey(NotificationRules.ReminderType(l.DueDate,today)!,l.Id,l.DueDate)).ToArray();
        var existing=(await db.Notifications.AsNoTracking().Where(n=>keys.Contains(n.DeduplicationKey))
            .Select(n=>n.DeduplicationKey).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var count=0;
        foreach(var loan in loans)
        {
            var type=NotificationRules.ReminderType(loan.DueDate,today)!;
            var key=NotificationRules.EventKey(type,loan.Id,loan.DueDate);
            if(!existing.Add(key)) continue;
            var title=type=="Overdue" ? global::EduLibrary.Localization.UiText.Get("Қайтару мерзімі өтті") : type=="Due1" ? global::EduLibrary.Localization.UiText.Get("Қайтаруға 1 күн қалды") : global::EduLibrary.Localization.UiText.Get("Қайтаруға 3 күн қалды");
            db.Notifications.Add(new(){UserId=loan.UserId,DeduplicationKey=key,Type=type,Title=title,Message=global::EduLibrary.Localization.UiText.Format($"«{loan.Title}» кітабын қайтару мерзімі: {loan.DueDate:dd.MM.yyyy}."),Link=$"/Loans/Details/{loan.Id}",CreatedAt=clock.UtcNow}); count++;
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return count;
    }
}

public static class NotificationRules
{
    public static string? ReminderType(DateTime dueDate,DateTime today)
        => (dueDate.Date-today.Date).Days switch { 3=>"Due3",1=>"Due1",<0=>"Overdue",_=>null };
    public static string EventKey(string type,int loanId,DateTime dueDate) => $"{type}:{loanId}:{dueDate:yyyyMMdd}";
}
