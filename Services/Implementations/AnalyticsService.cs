using EduLibrary.Data;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.EntityFrameworkCore;
using static EduLibrary.Services.Implementations.ReportingRules;

namespace EduLibrary.Services.Implementations;

public sealed class AnalyticsService(ApplicationDbContext db, ILibraryClock clock) : IAnalyticsService
{
    public async Task<AnalyticsViewModel> GetAsync(ReportFilterViewModel filter, CancellationToken ct = default)
    {
        var period = ResolvePeriod(filter, clock.Today);
        var today = clock.Today;
        var loans = db.Loans.AsNoTracking().Where(x => x.LoanDate >= period.Start && x.LoanDate < period.EndExclusive);
        var returns = db.Returns.AsNoTracking().Where(x => x.ReturnDate >= period.Start && x.ReturnDate < period.EndExclusive);
        var reviews = db.Reviews.AsNoTracking().Where(x => !x.IsDeleted && !x.IsHidden && x.CreatedAt >= period.UtcStart && x.CreatedAt < period.UtcEnd);
        // Inventory and current liability are snapshots. Activity and payments are
        // period flows; the UI names both explicitly instead of mixing semantics.
        var statistics = new List<ReportMetricViewModel>
        {
            new("Кітап атаулары", Number(await db.Books.CountAsync(x => !x.IsArchived, ct)), "Қазіргі белсенді каталог"),
            new("Физикалық даналар", Number(await db.BookCopies.CountAsync(ct)), "Қазіргі толық қор"),
            new("Қолжетімді даналар", Number(await db.BookCopies.CountAsync(x => x.Status == CopyStatus.Available && !x.Book.IsArchived, ct)), "Қазіргі қолжетімді қор"),
            new("Белсенді берулер", Number(await db.Loans.CountAsync(x => x.ReturnedAt == null, ct)), "Қазіргі мәртебе"),
            new("Мерзімі өткен", Number(await db.Loans.CountAsync(x => x.ReturnedAt == null && x.DueDate < today, ct)), "Бүгінге қайтарылмаған"),
            new("Тіркелген студенттер", Number(await db.Students.CountAsync(ct)), "Қазіргі оқырмандар базасы"),
            new("Белсенді оқырмандар", Number(await loans.Select(x => x.StudentId).Distinct().CountAsync(ct)), "Таңдалған кезеңде кітап алғандар"),
            new("Төленбеген айыппұлдар", Money(await db.Fines.SumAsync(x => (decimal?)(x.Amount - x.PaidAmount), ct) ?? 0m), "Қазіргі бекітілген берешек"),
            new("Төленген айыппұлдар", Money(await db.FinePayments.Where(x => x.PaidAt >= period.UtcStart && x.PaidAt < period.UtcEnd).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m), "Кезеңде тіркелген төлемдер"),
            new("Әдебиеттердің орташа бағасы", Rating(await reviews.Select(x => (double?)x.Rating).AverageAsync(ct)), "Кезеңде жарияланған ашық пікірлер")
        };
        var monthly = (period.End - period.Start).Days > 90;
        var loanTimeline = await TimelineAsync(loans.Select(x => new CalendarEvent { Date = x.LoanDate, ReaderId = x.StudentId }), period, monthly, false, ct);
        var returnTimeline = await TimelineAsync(returns.Select(x => new CalendarEvent { Date = x.ReturnDate, ReaderId = x.Loan.StudentId }), period, monthly, false, ct);
        var readerTimeline = await TimelineAsync(loans.Select(x => new CalendarEvent { Date = x.LoanDate, ReaderId = x.StudentId }), period, monthly, true, ct);
        var delayed = db.Loans.AsNoTracking().Where(x => x.DueDate >= period.Start && x.DueDate < period.EndExclusive && (x.ReturnedAt ?? today) > x.DueDate);
        var overdueTimeline = await TimelineAsync(delayed.Select(x => new CalendarEvent { Date = x.DueDate, ReaderId = x.StudentId }), period, monthly, false, ct);
        var popular = await loans.GroupBy(x => new { x.BookCopy.BookId, x.BookCopy.Book.Title })
            .Select(g => new { g.Key.BookId, g.Key.Title, Count = g.Count() }).OrderByDescending(x => x.Count).ThenBy(x => x.BookId).Take(10).ToListAsync(ct);
        var categories = await db.BookCategories.AsNoTracking()
            .Where(x => x.Book.Copies.Any(c => c.Loans.Any(l => l.LoanDate >= period.Start && l.LoanDate < period.EndExclusive)))
            .GroupBy(x => x.Category.Name).Select(g => new { Name = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ThenBy(x => x.Name).ToListAsync(ct);
        var ratings = await reviews.GroupBy(x => new { x.BookId, x.Book.Title })
            .Select(g => new { g.Key.BookId, g.Key.Title, Average = g.Average(x => (double)x.Rating) })
            .OrderByDescending(x => x.Average).ThenBy(x => x.BookId).Take(10).ToListAsync(ct);
        var interval = monthly ? "Айлар бойынша" : "Күндер бойынша";
        return new AnalyticsViewModel
        {
            Filter = filter, StartDate = period.Start, EndDate = period.End, Today = today, Statistics = statistics,
            Charts =
            [
                new() { Id = "loans", Title = "Кітап беру динамикасы", Description = interval + ": кезеңде берілген кітаптар.", Labels = loanTimeline.Labels, Values = loanTimeline.Values },
                new() { Id = "returns", Title = "Кітап қайтару динамикасы", Description = interval + ": қайтарылған күні кезеңге түскен кітаптар.", Labels = returnTimeline.Labels, Values = returnTimeline.Values },
                new() { Id = "popular", Title = "Ең танымал 10 кітап", Description = "Таңдалған кезеңдегі беру саны.", Type = "bar", Horizontal = true, Labels = popular.Select(x => x.Title).ToArray(), Values = popular.Select(x => (double)x.Count).ToArray() },
                new() { Id = "categories", Title = "Кітаптардың санаттар бойынша таралуы", Description = "Кезеңде берілген бірегей кітап атаулары. Көп санатты кітап әр санатында саналады.", Type = "doughnut", Labels = categories.Select(x => x.Name).ToArray(), Values = categories.Select(x => (double)x.Count).ToArray() },
                new() { Id = "overdue", Title = "Кешіктіру динамикасы", Description = interval + ": қайтару мерзімі кезеңге түскен, кешіккен жазбалар. Бұл өткен күндегі қор қалдығы емес.", Type = "bar", Labels = overdueTimeline.Labels, Values = overdueTimeline.Values },
                new() { Id = "readers", Title = "Оқырман белсенділігі", Description = interval + ": кітап алған бірегей студенттер. Бір оқырман әр кезең ұяшығында бір рет саналады.", Fill = true, Labels = readerTimeline.Labels, Values = readerTimeline.Values },
                new() { Id = "ratings", Title = "Әдебиеттер рейтингі", Description = "Кезеңде жарияланған ашық пікірлердің орташа бағасы: 1–5. Пікірсіз кітаптар көрсетілмейді.", Type = "bar", IsRating = true, Labels = ratings.Select(x => x.Title).ToArray(), Values = ratings.Select(x => x.Average).ToArray() }
            ]
        };
    }

    private static async Task<(string[] Labels, double[] Values)> TimelineAsync(IQueryable<CalendarEvent> events, ReportingPeriod period, bool monthly, bool readers, CancellationToken ct)
    {
        var grouped = await events.GroupBy(x => new { x.Date.Year, x.Date.Month, Day = monthly ? 1 : x.Date.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = readers ? g.Select(x => x.ReaderId).Distinct().Count() : g.Count() }).ToListAsync(ct);
        var counts = grouped.ToDictionary(x => new DateTime(x.Year, x.Month, x.Day), x => (double)x.Count);
        var labels = new List<string>();
        var values = new List<double>();
        for (var date = monthly ? new DateTime(period.Start.Year, period.Start.Month, 1) : period.Start;
            date < period.EndExclusive; date = monthly ? date.AddMonths(1) : date.AddDays(1))
        {
            labels.Add(date.ToString(monthly ? "MM.yyyy" : "dd.MM.yyyy"));
            values.Add(counts.GetValueOrDefault(date));
        }
        return (labels.ToArray(), values.ToArray());
    }

    private sealed class CalendarEvent { public DateTime Date { get; set; } public int ReaderId { get; set; } }
}
