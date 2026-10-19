using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.ViewModels;
using Microsoft.EntityFrameworkCore;

internal static class ReservationScenarios
{
    public static async Task RunAsync(ReservationFixture f)
    {
        await CreationAndPrivacy(f);
        await QueueAndCancellation(f);
        await ConcurrentAllocation(f);
        await SchedulingAndEligibility(f);
        await ReturnPromotes(f);
        await Pickup(f);
        await DoublePickup(f);
        await NotificationRollbacks(f);
        await ExpiryAndReminders(f);
    }

    private static ReservationCreateViewModel Form(ReservationFixture f, string book, string time = "10:00", bool queue = false)
    {
        var date = f.Clock.Today.AddDays(1);
        while (!f.Settings.OpenDays.Contains((int)date.DayOfWeek)) date = date.AddDays(1);
        return new() { BookId = f.Books[book], JoinQueue = queue, PickupDate = date, PickupTime = time, PickupLocationId = "main" };
    }

    private static async Task<int> Create(ReservationFixture f, string book, int reader, ReservationCreateViewModel? form = null, ReservationOptions? options = null)
    {
        await using var db = f.NewDb();
        return await f.Service(db, options).CreateAsync(form ?? Form(f, book), f.Reader(reader));
    }
    private static async Task<Reservation> Row(ReservationFixture f, int id)
    {
        await using var db = f.NewDb();
        return await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == id);
    }
    private static string Version(Reservation row) => Convert.ToBase64String(row.RowVersion);
    private static async Task Cancel(ReservationFixture f, int id, int reader)
    {
        var row = await Row(f, id);
        await using var db = f.NewDb();
        await f.Service(db).CancelAsync(id, Version(row), f.Reader(reader), false);
    }
    private static async Task Ready(ReservationFixture f, int id)
    {
        var row = await Row(f, id);
        await using var db = f.NewDb();
        await f.Service(db).MarkReadyAsync(new() { Id = id, BookCopyId = row.BookCopyId!.Value, RowVersion = Version(row) }, ReservationFixture.StaffUserId);
    }
    private static async Task<int> Fulfill(ReservationFixture f, int id, string? code = null, string? version = null, string? actor = null)
    {
        var row = await Row(f, id);
        await using var db = f.NewDb();
        return await f.Service(db).FulfillAsync(new() { Id = id, PickupCode = code ?? row.PickupCode, RowVersion = version ?? Version(row), DueDate = f.Clock.Today.AddDays(14) }, actor ?? ReservationFixture.StaffUserId);
    }
    private static async Task<int> NotificationCount(ReservationFixture f, string key)
    {
        await using var db = f.NewDb();
        return await db.Notifications.CountAsync(n => n.DeduplicationKey == key);
    }
    private static async Task Process(ReservationFixture f)
    {
        await using var db = f.NewDb();
        await f.Service(db).ProcessAsync();
    }

    private static async Task CreationAndPrivacy(ReservationFixture f)
    {
        var id = await Create(f, "available", 0);
        var row = await Row(f, id);
        await using var db = f.NewDb();
        f.Check(row.Status == ReservationStatus.Pending && row.BookCopyId.HasValue && row.LoanId is null, "available reservation is Pending and has a copy without creating a loan");
        f.Check(await db.BookCopies.AnyAsync(c => c.Id == row.BookCopyId && c.Status == CopyStatus.Reserved) && !await db.Loans.AnyAsync(), "creation reserves physical copy while leaving circulation empty");
        f.Check(ReservationPickupCode.Normalize(row.PickupCode) == row.PickupCode && row.ExpiresAtUtc > row.PickupAtUtc, "reservation persists opaque pickup code and appointment-aware deadline");
        f.Check(await NotificationCount(f, $"ReservationCreated:{id}") == 1, "creation notification is persisted once");
        await f.Reject(async () => { await Create(f, "available", 0); }, "duplicate active reservation is rejected even when another copy is free");
        f.Check(await f.Service(db).GetDetailsAsync(id, f.Reader(1), false) is null, "foreign reservation details are hidden");
        var own = await f.Service(db).GetDetailsAsync(id, f.Reader(0), false);
        f.Check(own?.PickupCode == row.PickupCode && own.CanCancel, "owner can retrieve own pickup code and cancellation state");
        var filtered = await f.Service(db).GetListAsync(new() { StudentId = f.Students[0], Status = "all" }, f.Reader(1), false);
        f.Check(filtered.Items.All(r => r.Id != id), "student list cannot be widened by foreign student filter");
        await f.Reject(async () => { await using var other = f.NewDb(); await f.Service(other).CancelAsync(id, Version(row), f.Reader(1), false); }, "foreign student cannot cancel reservation");
        f.Check(await f.Service(db).GetDetailsAsync(id, ReservationFixture.StaffUserId, true) is not null, "staff can inspect reservation for physical preparation");
        await Cancel(f, id, 0);
        var cancelled = await Row(f, id);
        f.Check(cancelled.Status == ReservationStatus.Cancelled && cancelled.CancelledAtUtc.HasValue, "owner cancellation persists history");
        f.Check(await db.BookCopies.AsNoTracking().AnyAsync(c => c.Id == row.BookCopyId && c.Status == CopyStatus.Available), "cancel releases held copy without issuing a loan");
    }

    private static async Task QueueAndCancellation(ReservationFixture f)
    {
        await f.SeedActiveLoanAsync("unavailable", 12);
        var queue = await Create(f, "unavailable", 1, Form(f, "unavailable", queue: true));
        var queued = await Row(f, queue);
        await using (var db = f.NewDb())
        {
            f.Check(queued.Status == ReservationStatus.Queued && queued.BookCopyId is null && queued.LoanId is null, "unavailable title joins FIFO queue without assigning borrowed copy");
            f.Check((await f.Service(db).GetDetailsAsync(queue, f.Reader(1), false))?.QueuePosition == 1, "queued reader sees queue position");
        }
        await f.Reject(async () => { await Create(f, "unavailable", 1); }, "duplicate queue entry is rejected");
        await Cancel(f, queue, 1);
        await using (var db = f.NewDb()) f.Check(await db.BookCopies.AnyAsync(c => c.Id == f.Copies["unavailable"][0] && c.Status == CopyStatus.OnLoan), "queue cancellation leaves active loan copy unchanged");

        var first = await Create(f, "fifo", 1);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await Create(f, "fifo", 2);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var third = await Create(f, "fifo", 3);
        f.Check((await Row(f, second)).Status == ReservationStatus.Queued && (await Row(f, third)).Status == ReservationStatus.Queued, "single-copy title queues later readers");
        await Cancel(f, first, 1);
        f.Check((await Row(f, second)).Status == ReservationStatus.Pending && (await Row(f, second)).BookCopyId == f.Copies["fifo"][0] && (await Row(f, third)).Status == ReservationStatus.Queued, "cancellation promotes oldest reader atomically and preserves FIFO");
        await Process(f); await Process(f);
        f.Check(await NotificationCount(f, $"ReservationPromoted:{second}") == 1 && await NotificationCount(f, $"ReservationCancelled:{first}") == 1, "promotion and cancellation notifications remain idempotent");
        await using (var db = f.NewDb()) f.Check((await f.Service(db).GetDetailsAsync(third, f.Reader(3), false))?.QueuePosition == 1, "remaining queue position is recalculated after promotion");

        var staleId = await Create(f, "stale", 4);
        var stale = await Row(f, staleId);
        await Ready(f, staleId);
        await f.Reject(async () => { await using var db = f.NewDb(); await f.Service(db).CancelAsync(staleId, Version(stale), f.Reader(4), false); }, "stale rowversion cannot cancel a newly prepared reservation");
        f.Check((await Row(f, staleId)).Status == ReservationStatus.ReadyForPickup, "stale operation leaves current state intact");
        await Cancel(f, staleId, 4);
    }

    private static async Task ConcurrentAllocation(ReservationFixture f)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int?> Attempt(int reader)
        {
            await start.Task;
            try { return await Create(f, "concurrent", reader); }
            catch (BusinessException) { return null; }
        }
        var a = Attempt(4); var b = Attempt(5); start.SetResult();
        var ids = await Task.WhenAll(a, b);
        await using var db = f.NewDb();
        var rows = await db.Reservations.Where(r => r.BookId == f.Books["concurrent"]).ToListAsync();
        f.Check(ids.Any(id => id.HasValue) && rows.Count(r => r.Status == ReservationStatus.Pending) == 1 && rows.Count(r => r.BookCopyId != null) == 1, "parallel last-copy booking assigns the physical copy to exactly one reader");
        f.Check(rows.Count <= 2 && rows.All(r => r.Status is ReservationStatus.Pending or ReservationStatus.Queued) && !await db.Loans.AnyAsync(l => l.BookCopy.BookId == f.Books["concurrent"]), "parallel allocation has no duplicate loans or invalid intermediate state");
    }

    private static async Task SchedulingAndEligibility(ReservationFixture f)
    {
        await f.Reject(async () => { await Create(f, "archived", 6); }, "archived book cannot be reserved");
        await f.Reject(async () => { await Create(f, "cancel", 13); }, "inactive reader cannot reserve");
        await f.SeedActiveLoanAsync("borrowed", 6);
        await f.Reject(async () => { await Create(f, "borrowed", 6); }, "reader cannot reserve a title already on own active loan");
        var unavailable = await Create(f, "unavailable", 6, Form(f, "unavailable", queue: true));
        f.Check((await Row(f, unavailable)).Status == ReservationStatus.Queued && (await Row(f, unavailable)).BookCopyId is null, "explicit queue request never assigns a borrowed physical copy");

        foreach (var time in new[] { "08:30", "18:00", "09:07", "25:00" })
            await f.Reject(async () => { await Create(f, "slots", 7, Form(f, "slots", time)); }, "invalid pickup time " + time + " is rejected");
        var past = Form(f, "slots"); past.PickupDate = f.Clock.Today.AddDays(-1);
        await f.Reject(async () => { await Create(f, "slots", 7, past); }, "past pickup date is rejected");
        var closed = Form(f, "slots"); closed.PickupDate = new DateTime(2026, 11, 1);
        await f.Reject(async () => { await Create(f, "slots", 7, closed); }, "closed weekday pickup is rejected");
        var distant = Form(f, "slots"); distant.PickupDate = f.Clock.Today.AddDays(65);
        await f.Reject(async () => { await Create(f, "slots", 7, distant); }, "pickup beyond booking horizon is rejected");
        var soon = Form(f, "slots", "09:00"); soon.PickupDate = f.Clock.Today;
        await f.Reject(async () => { await Create(f, "slots", 7, soon); }, "pickup inside minimum lead time is rejected");
        var location = Form(f, "slots"); location.PickupLocationId = "foreign-location";
        await f.Reject(async () => { await Create(f, "slots", 7, location); }, "unconfigured pickup location is rejected");

        var capacity = new ReservationOptions { SlotCapacity = 1 };
        var full = await Create(f, "slots", 7, Form(f, "slots", "14:00"), capacity);
        await f.Reject(async () => { await Create(f, "slots", 8, Form(f, "slots", "14:00"), capacity); }, "full pickup appointment cannot exceed configured capacity");
        await using (var db = f.NewDb())
        {
            var slots = await f.Service(db, capacity).GetSlotsAsync(Form(f, "slots").PickupDate!.Value, "main");
            f.Check(slots.All(s => s.Value != "14:00" || s.AvailableCapacity == 0), "slot availability reflects committed capacity usage");
        }
        await Cancel(f, full, 7);
        var replacement = await Create(f, "slots", 8, Form(f, "slots", "14:00"), capacity);
        f.Check((await Row(f, replacement)).Status == ReservationStatus.Pending, "cancellation frees pickup appointment capacity");
        await Cancel(f, replacement, 8);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> TryCapacity(int reader) { await gate.Task; try { await Create(f, "slots", reader, Form(f, "slots", "16:30"), capacity); return true; } catch (BusinessException) { return false; } }
        var a = TryCapacity(7); var b = TryCapacity(8); gate.SetResult();
        var attempts = await Task.WhenAll(a, b);
        f.Check(attempts.Count(x => x) == 1, "parallel booking enforces shared pickup slot capacity");
    }

    private static async Task ReturnPromotes(ReservationFixture f)
    {
        var loan = await f.SeedActiveLoanAsync("return-promotion", 12);
        var first = await Create(f, "return-promotion", 9);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await Create(f, "return-promotion", 10);
        await using var db = f.NewDb();
        var form = (await f.Circulation(db).GetReturnFormAsync(loan))!;
        form.ReturnDate = f.Clock.Today; form.ReturnCondition = "Good";
        await f.Circulation(db).ReturnAsync(form, ReservationFixture.StaffUserId);
        f.Check((await Row(f, first)).Status == ReservationStatus.Pending && (await Row(f, second)).Status == ReservationStatus.Queued, "physical return promotes oldest queued reader inside circulation transaction");
        f.Check(await db.BookCopies.AsNoTracking().AnyAsync(c => c.Id == f.Copies["return-promotion"][0] && c.Status == CopyStatus.Reserved), "returned copy is reserved for queue instead of exposed as available");
        f.Check(await db.Loans.CountAsync(l => l.BookCopy.BookId == f.Books["return-promotion"]) == 1 && await NotificationCount(f, $"ReservationPromoted:{first}") == 1, "queue allocation on return creates no premature loan and one notification");
    }

    private static async Task Pickup(ReservationFixture f)
    {
        var id = await Create(f, "pickup", 0);
        var pending = await Row(f, id);
        await f.Reject(async () => { await using var db = f.NewDb(); await f.Service(db).MarkReadyAsync(new() { Id = id, BookCopyId = pending.BookCopyId!.Value, RowVersion = Version(pending) }, f.Reader(0)); }, "student actor cannot mark a reservation ready");
        await f.Reject(async () => { await using var db = f.NewDb(); await f.Service(db).MarkReadyAsync(new() { Id = id, BookCopyId = f.Copies["available"][0], RowVersion = Version(pending) }, ReservationFixture.StaffUserId); }, "preparation rejects a physical copy from another book");
        await f.Reject(async () => { await Fulfill(f, id); }, "Pending reservation cannot create a loan before staff preparation");
        await Ready(f, id);
        var ready = await Row(f, id);
        f.Check(ready.Status == ReservationStatus.ReadyForPickup && ready.ReadyAtUtc.HasValue && ready.PreparedByUserId == ReservationFixture.StaffUserId, "staff preparation records ReadyForPickup and preparer audit");
        f.Check(await NotificationCount(f, $"ReservationReady:{id}") == 1, "ready notification is emitted exactly once");
        await f.Reject(async () => { await Fulfill(f, id, actor: f.Reader(0)); }, "student actor cannot fulfill own reservation");
        await f.Reject(async () => { await using var db = f.NewDb(); await f.Service(db).FindByCodeAsync(ready.PickupCode, f.Reader(0)); }, "student actor cannot use staff code lookup");
        await f.Reject(async () => { await Fulfill(f, id, "ELR-" + new string('0', 32)); }, "wrong pickup code cannot create a loan");
        var foreign = await Create(f, "cancel", 1);
        await f.Reject(async () => { await Fulfill(f, id, (await Row(f, foreign)).PickupCode); }, "code from another reservation cannot fulfill current reservation");
        await using (var db = f.NewDb()) f.Check(await f.Service(db).FindByCodeAsync(" " + ready.PickupCode.ToLowerInvariant() + " ", ReservationFixture.StaffUserId) == id, "staff lookup normalizes the presented code and resolves only its reservation");
        await f.Reject(async () => { await using var db = f.NewDb(); await f.Service(db).FulfillAsync(new() { Id = id, PickupCode = ready.PickupCode, RowVersion = Version(ready), DueDate = f.Clock.Today.AddDays(-1) }, ReservationFixture.StaffUserId); }, "pickup rejects a due date before actual handover date");
        var loanId = await Fulfill(f, id);
        var fulfilled = await Row(f, id);
        await using (var db = f.NewDb())
        {
            var loan = await db.Loans.SingleAsync(l => l.Id == loanId);
            f.Check(fulfilled.Status == ReservationStatus.Fulfilled && fulfilled.LoanId == loanId && fulfilled.FulfilledAtUtc.HasValue, "correct physical pickup fulfills reservation and links one loan");
            f.Check(loan.StudentId == f.Students[0] && loan.BookCopyId == ready.BookCopyId && loan.LoanDate == f.Clock.Today && loan.DueDate == f.Clock.Today.AddDays(14), "loan calendar starts at actual handover with chosen due date");
            f.Check(await db.BookCopies.AnyAsync(c => c.Id == ready.BookCopyId && c.Status == CopyStatus.OnLoan), "physical handover transitions held copy to OnLoan");
            f.Check((await f.Circulation(db).GetLoansAsync(new() { Status = "all" }, f.Reader(0), false)).Items.Any(l => l.Id == loanId), "fulfilled pickup appears in owner's loan history");
        }
        f.Check(await NotificationCount(f, $"Issued:{loanId}") == 1, "handover records the existing issued workflow notification atomically");
        await f.Reject(async () => { await Fulfill(f, id); }, "fulfilled reservation and reused code cannot create a second loan");
        await f.Reject(async () => { await Cancel(f, id, 0); }, "fulfilled history cannot be cancelled");
        await Cancel(f, foreign, 1);
    }

    private static async Task DoublePickup(ReservationFixture f)
    {
        var id = await Create(f, "double-pickup", 1); await Ready(f, id);
        var row = await Row(f, id);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int?> Attempt(string actor) { await gate.Task; try { return await Fulfill(f, id, row.PickupCode, Version(row), actor); } catch (BusinessException) { return null; } }
        var a = Attempt(ReservationFixture.StaffUserId); var b = Attempt(ReservationFixture.OtherStaffUserId); gate.SetResult();
        var loans = await Task.WhenAll(a, b);
        await using var db = f.NewDb();
        f.Check(loans.Count(x => x.HasValue) == 1 && await db.Loans.CountAsync(l => l.BookCopy.BookId == f.Books["double-pickup"]) == 1, "concurrent physical handovers produce exactly one loan");
        f.Check((await Row(f, id)).Status == ReservationStatus.Fulfilled, "losing concurrent handover leaves successful reservation intact");
    }

    private static async Task WithRejectedNotification(ReservationFixture f, string prefix, Func<Task> action)
    {
        // Literal DDL only, inside our already-owned random database.
        var statement = prefix switch
        {
            "ReservationCreated" => "ALTER TABLE [Notifications] WITH NOCHECK ADD CONSTRAINT [CK_ReservationChecks_RejectEvent] CHECK ([DeduplicationKey] NOT LIKE N'ReservationCreated:%')",
            "ReservationReady" => "ALTER TABLE [Notifications] WITH NOCHECK ADD CONSTRAINT [CK_ReservationChecks_RejectEvent] CHECK ([DeduplicationKey] NOT LIKE N'ReservationReady:%')",
            "Issued" => "ALTER TABLE [Notifications] WITH NOCHECK ADD CONSTRAINT [CK_ReservationChecks_RejectEvent] CHECK ([DeduplicationKey] NOT LIKE N'Issued:%')",
            "ReservationCancelled" => "ALTER TABLE [Notifications] WITH NOCHECK ADD CONSTRAINT [CK_ReservationChecks_RejectEvent] CHECK ([DeduplicationKey] NOT LIKE N'ReservationCancelled:%')",
            "ReservationExpired" => "ALTER TABLE [Notifications] WITH NOCHECK ADD CONSTRAINT [CK_ReservationChecks_RejectEvent] CHECK ([DeduplicationKey] NOT LIKE N'ReservationExpired:%')",
            _ => throw new ArgumentException(nameof(prefix))
        };
        await using var db = f.NewDb();
        await db.Database.ExecuteSqlRawAsync(statement);
        try { await action(); }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Notifications] DROP CONSTRAINT [CK_ReservationChecks_RejectEvent]"); }
    }

    private static async Task RejectConstraint(ReservationFixture f, Func<Task> action, string label)
    {
        try { await action(); }
        catch (BusinessException)
        { f.Check(true, label); return; }
        throw new InvalidOperationException("FAILED: expected test-only notification constraint failure: " + label);
    }

    private static async Task NotificationRollbacks(ReservationFixture f)
    {
        await WithRejectedNotification(f, "ReservationCreated", async () => await RejectConstraint(f, async () => { await Create(f, "create-rollback", 2); }, "notification failure rejects entire creation transaction"));
        await using (var db = f.NewDb()) f.Check(!await db.Reservations.AnyAsync(r => r.BookId == f.Books["create-rollback"]) && await db.BookCopies.AnyAsync(c => c.Id == f.Copies["create-rollback"][0] && c.Status == CopyStatus.Available), "failed creation rolls back reservation and copy allocation");
        var id = await Create(f, "ready-rollback", 2);
        await WithRejectedNotification(f, "ReservationReady", async () => await RejectConstraint(f, async () => await Ready(f, id), "notification failure rejects entire ready transaction"));
        f.Check((await Row(f, id)).Status == ReservationStatus.Pending && (await Row(f, id)).ReadyAtUtc is null, "failed preparation rolls back status and readiness audit");
        await Ready(f, id);
        await WithRejectedNotification(f, "Issued", async () => await RejectConstraint(f, async () => { await Fulfill(f, id); }, "issued notification failure rejects entire handover transaction"));
        await using (var db = f.NewDb()) f.Check((await Row(f, id)).Status == ReservationStatus.ReadyForPickup && (await Row(f, id)).LoanId is null && !await db.Loans.AnyAsync(l => l.BookCopy.BookId == f.Books["ready-rollback"]) && await db.BookCopies.AnyAsync(c => c.Id == f.Copies["ready-rollback"][0] && c.Status == CopyStatus.Reserved), "failed handover rolls back loan reservation and physical copy together");
        var loan = await Fulfill(f, id);
        f.Check(await NotificationCount(f, $"ReservationReady:{id}") == 1 && await NotificationCount(f, $"Issued:{loan}") == 1, "retry after rollback emits each workflow notification once");

        var cancelled = await Create(f, "create-rollback", 2);
        var queued = await Create(f, "create-rollback", 3);
        await WithRejectedNotification(f, "ReservationCancelled", async () => await RejectConstraint(f, async () => await Cancel(f, cancelled, 2), "cancel notification failure rejects release and promotion transaction"));
        f.Check((await Row(f, cancelled)).Status == ReservationStatus.Pending && (await Row(f, queued)).Status == ReservationStatus.Queued, "failed cancellation preserves hold and FIFO queue");
        await Cancel(f, cancelled, 2);
        f.Check((await Row(f, queued)).Status == ReservationStatus.Pending && await NotificationCount(f, $"ReservationPromoted:{queued}") == 1, "cancel retry atomically releases and promotes exactly once");
    }

    private static async Task ExpiryAndReminders(ReservationFixture f)
    {
        var id = await Create(f, "expiry", 7); await Ready(f, id);
        var next = await Create(f, "expiry", 8);
        var rollback = await Create(f, "expiry-rollback", 9); await Ready(f, rollback);
        var rollbackNext = await Create(f, "expiry-rollback", 10);
        var ready = await Row(f, id);
        f.Clock.UtcNow = ready.ExpiresAtUtc!.Value.AddHours(-f.Settings.ExpiringSoonHours).AddMinutes(1);
        await Task.WhenAll(Process(f), Process(f));
        f.Check((await Row(f, id)).Status == ReservationStatus.ReadyForPickup && await NotificationCount(f, $"ReservationExpiring:{id}") == 1, "concurrent workers send one expiring reminder without releasing ready copy");
        f.Clock.UtcNow = new[] { ready.ExpiresAtUtc.Value, (await Row(f, rollback)).ExpiresAtUtc!.Value }.Max().AddSeconds(1);
        await WithRejectedNotification(f, "ReservationExpired", async () => await RejectConstraint(f, async () => await Process(f), "expiry notification failure rolls back reconciliation transaction"));
        f.Check((await Row(f, rollback)).Status == ReservationStatus.ReadyForPickup && (await Row(f, rollbackNext)).Status == ReservationStatus.Queued, "failed expiry leaves ready hold and queue unmodified");
        await Task.WhenAll(Process(f), Process(f));
        var promoted = await Row(f, next);
        f.Check((await Row(f, id)).Status == ReservationStatus.Expired && (await Row(f, id)).ExpiredAtUtc.HasValue && promoted.Status == ReservationStatus.Pending, "ready expiry releases copy and promotes oldest queued reader atomically");
        f.Check(promoted.BookCopyId == ready.BookCopyId && promoted.PickupAtUtc > f.Clock.UtcNow && ReservationSchedule.IsConfiguredSlot(ReservationSchedule.ToLocal(promoted.PickupAtUtc!.Value), f.Settings), "promotion replaces past requested appointment with a valid future slot");
        f.Check(await NotificationCount(f, $"ReservationExpired:{id}") == 1 && await NotificationCount(f, $"ReservationPromoted:{next}") == 1 && await NotificationCount(f, $"ReservationExpiring:{id}") == 1, "concurrent expiry and repeated workers deduplicate all reminder and promotion events");
        await Process(f);
        f.Check((await Row(f, rollback)).Status == ReservationStatus.Expired && (await Row(f, rollbackNext)).Status == ReservationStatus.Pending && await NotificationCount(f, $"ReservationExpired:{rollback}") == 1, "expiry retry commits once after notification constraint is removed");
        await f.Reject(async () => { await Fulfill(f, id); }, "expired pickup code cannot create a loan");
    }
}
