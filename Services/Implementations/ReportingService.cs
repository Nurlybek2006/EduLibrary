using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static EduLibrary.Services.Implementations.ReportingRules;

namespace EduLibrary.Services.Implementations;

public sealed class ReportingService(ApplicationDbContext db, ILibraryClock clock, IOptions<LibraryOptions> options) : IReportingService
{
    private const int PageSize = 20;

    public Task<ReportPageViewModel> GetAsync(ReportFilterViewModel filter, CancellationToken ct = default) => BuildAsync(filter, false, ct);
    public Task<ReportPageViewModel> GetPrintAsync(ReportFilterViewModel filter, CancellationToken ct = default) => BuildAsync(filter, true, ct);

    public async Task<byte[]> ExportCsvAsync(ReportFilterViewModel filter, CancellationToken ct = default)
    {
        var report = await BuildAsync(filter, true, ct);
        var rows = new List<IEnumerable<string?>>
        {
            new[] { report.Title }, new[] { "Кезең", $"{report.StartDate:dd.MM.yyyy} – {report.EndDate:dd.MM.yyyy}" },
            new[] { report.Description }, new[] { "Іздеу", filter.Search ?? "", "Факультет", filter.Faculty ?? "", "Мәртебе", filter.Status },
            report.Columns.Select(x => x.Label)
        };
        rows.AddRange(report.Rows.Select(x => x.Cells.Select(c => c.Value)));
        rows.Add([]);
        rows.AddRange(report.Summary.Select(x => new[] { x.Label, x.Value, x.Hint }));
        return CreateCsv(rows);
    }

    private async Task<ReportPageViewModel> BuildAsync(ReportFilterViewModel filter, bool export, CancellationToken ct)
    {
        if (filter.Type == "readeractivity") filter.Type = "readers";
        filter.Type = filter.Type is "popular" or "overdue" or "readers" or "ratings" ? filter.Type : "loans";
        filter.Status = filter.Status is "active" or "returned" or "overdue" ? filter.Status : "all";
        if (filter.Type == "ratings") filter.Status = "all";
        filter.Direction = filter.Direction == "asc" ? "asc" : "desc";
        filter.GroupBy = filter.GroupBy == "month" ? "month" : "day";
        filter.Search = filter.Search?.Trim();
        filter.Faculty = filter.Faculty?.Trim();
        if (filter.Search?.Length > 200 || filter.Faculty?.Length > 150) throw new BusinessException("Іздеу немесе факультет атауы тым ұзын.");
        var period = ResolvePeriod(filter, clock.Today);
        var report = new ReportPageViewModel { Filter = filter, StartDate = period.Start, EndDate = period.End, GeneratedAt = clock.UtcNow };
        var loans = FilterLoans(filter, period);
        switch (filter.Type)
        {
            case "popular": await PopularAsync(report, loans, export, ct); break;
            case "overdue": await OverdueAsync(report, loans, export, ct); break;
            case "readers": await ReadersAsync(report, loans, export, ct); break;
            case "ratings": await RatingsAsync(report, period, export, ct); break;
            default: await LoansAsync(report, loans, export, ct); break;
        }
        if (!export)
        {
            report.Categories = await db.Categories.AsNoTracking().OrderBy(x => x.Name).Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync(ct);
            report.Faculties = await db.Students.AsNoTracking().Where(x => x.Faculty != "").Select(x => x.Faculty).Distinct().OrderBy(x => x).Select(x => new SelectListItem(x, x)).ToListAsync(ct);
        }
        return report;
    }

    private IQueryable<Loan> FilterLoans(ReportFilterViewModel filter, ReportingPeriod period)
    {
        var today = clock.Today;
        var query = db.Loans.AsNoTracking().Where(x => x.LoanDate >= period.Start && x.LoanDate < period.EndExclusive);
        if (!string.IsNullOrEmpty(filter.Search))
        {
            var search = filter.Search;
            query = query.Where(x => x.Student.FullName.Contains(search) || x.Student.StudentNumber.Contains(search)
                || x.BookCopy.Book.Title.Contains(search) || x.BookCopy.Book.ISBN.Contains(search)
                || x.BookCopy.Book.BookAuthors.Any(a => (a.Author.FirstName + " " + a.Author.LastName).Contains(search)));
        }
        if (filter.CategoryId.HasValue) query = query.Where(x => x.BookCopy.Book.BookCategories.Any(c => c.CategoryId == filter.CategoryId));
        if (!string.IsNullOrEmpty(filter.Faculty)) query = query.Where(x => x.Student.Faculty == filter.Faculty);
        return filter.Status switch
        {
            "active" => query.Where(x => x.ReturnedAt == null), "returned" => query.Where(x => x.ReturnedAt != null),
            "overdue" => query.Where(x => (x.ReturnedAt ?? today) > x.DueDate), _ => query
        };
    }

    private static ReportCellViewModel Cell(string value) => new(value);
    private static ReportCellViewModel BookCell(string value, int id) => new(value, "Books", "Details", id);
    private static ReportRowViewModel Row(params ReportCellViewModel[] cells) => new() { Cells = cells };
    private static bool Asc(ReportFilterViewModel filter) => filter.Direction == "asc";
    private static IQueryable<T> Page<T>(IQueryable<T> query, ReportPageViewModel report, bool export)
    {
        report.Filter.Page = Math.Clamp(report.Filter.Page, 1, report.TotalPages);
        return export ? query : query.Skip((report.Filter.Page - 1) * PageSize).Take(PageSize);
    }

    private async Task LoansAsync(ReportPageViewModel report, IQueryable<Loan> loans, bool export, CancellationToken ct)
    {
        var today = clock.Today;
        var month = report.Filter.GroupBy == "month";
        report.Title = "Кезеңдер бойынша кітап беру";
        report.Description = "Беру күні кезеңге түскен жазбалар. Белсенділік — бүгінгі мәртебе; кешігу — бүгінге дейінгі немесе нақты қайтарылған күнге дейінгі кешігу.";
        var totals = await loans.GroupBy(_ => 1).Select(g => new LoanCounts { Total = g.Count(), Active = g.Count(x => x.ReturnedAt == null), Returned = g.Count(x => x.ReturnedAt != null), Overdue = g.Count(x => (x.ReturnedAt ?? today) > x.DueDate) }).SingleOrDefaultAsync(ct) ?? new();
        report.Summary = [new("Барлық берулер", Number(totals.Total)), new("Белсенді", Number(totals.Active)), new("Қайтарылған", Number(totals.Returned)), new("Кешіккен", Number(totals.Overdue), "Қайтарылған кешіккен кітаптар да кіреді")];
        var grouped = loans.GroupBy(x => new { x.LoanDate.Year, x.LoanDate.Month, Day = month ? 1 : x.LoanDate.Day })
            .Select(g => new LoanPeriodRow { Year = g.Key.Year, Month = g.Key.Month, Day = g.Key.Day, Total = g.Count(), Active = g.Count(x => x.ReturnedAt == null), Returned = g.Count(x => x.ReturnedAt != null), Overdue = g.Count(x => (x.ReturnedAt ?? today) > x.DueDate) });
        report.TotalCount = await grouped.CountAsync(ct);
        var sort = report.Filter.Sort;
        IOrderedQueryable<LoanPeriodRow> ordered = sort switch
        {
            "total" => Asc(report.Filter) ? grouped.OrderBy(x => x.Total) : grouped.OrderByDescending(x => x.Total),
            "active" => Asc(report.Filter) ? grouped.OrderBy(x => x.Active) : grouped.OrderByDescending(x => x.Active),
            "returned" => Asc(report.Filter) ? grouped.OrderBy(x => x.Returned) : grouped.OrderByDescending(x => x.Returned),
            "overdue" => Asc(report.Filter) ? grouped.OrderBy(x => x.Overdue) : grouped.OrderByDescending(x => x.Overdue),
            _ => Asc(report.Filter) ? grouped.OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day) : grouped.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ThenByDescending(x => x.Day)
        };
        var rows = await Page(ordered.ThenBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day), report, export).ToListAsync(ct);
        report.Columns = [new(month ? "Ай" : "Күн", "period"), new("Барлығы", "total"), new("Белсенді", "active"), new("Қайтарылған", "returned"), new("Кешіккен", "overdue")];
        report.Rows = rows.Select(x => Row(Cell(new DateTime(x.Year, x.Month, x.Day).ToString(month ? "MM.yyyy" : "dd.MM.yyyy")), Cell(Number(x.Total)), Cell(Number(x.Active)), Cell(Number(x.Returned)), Cell(Number(x.Overdue)))).ToList();
    }

    private async Task PopularAsync(ReportPageViewModel report, IQueryable<Loan> loans, bool export, CancellationToken ct)
    {
        report.Title = "Танымал кітаптар";
        report.Description = "Таңдалған кезеңде берілген кітаптар. Орын беру саны бойынша есептеледі; саны тең кітаптар бір орынды бөліседі. Қолжетімділік — қазіргі қор.";
        var grouped = loans.GroupBy(x => new { x.BookCopy.BookId, x.BookCopy.Book.Title })
            .Select(g => new PopularRow { BookId = g.Key.BookId, Title = g.Key.Title, Total = g.Count(), Available = db.BookCopies.Count(c => c.BookId == g.Key.BookId && c.Status == Models.Enums.CopyStatus.Available && !c.Book.IsArchived) });
        report.TotalCount = await grouped.CountAsync(ct);
        report.Summary = [new("Кітап атаулары", Number(report.TotalCount)), new("Беру саны", Number(await loans.CountAsync(ct))), new("Қолжетімді даналар", Number(await grouped.SumAsync(x => (int?)x.Available, ct) ?? 0), "Есепке кірген кітаптардың қазіргі қоры")];
        var ranks = await grouped.Select(x => x.Total).Distinct().OrderByDescending(x => x).ToListAsync(ct);
        IOrderedQueryable<PopularRow> ordered = report.Filter.Sort switch
        {
            "book" => Asc(report.Filter) ? grouped.OrderBy(x => x.Title) : grouped.OrderByDescending(x => x.Title),
            "available" => Asc(report.Filter) ? grouped.OrderBy(x => x.Available) : grouped.OrderByDescending(x => x.Available),
            _ => Asc(report.Filter) ? grouped.OrderBy(x => x.Total) : grouped.OrderByDescending(x => x.Total)
        };
        var rows = await Page(ordered.ThenBy(x => x.BookId), report, export).ToListAsync(ct);
        var ids = rows.Select(x => x.BookId).ToList();
        var authors = await db.BookAuthors.AsNoTracking().Where(x => ids.Contains(x.BookId)).OrderBy(x => x.Author.LastName).Select(x => new { x.BookId, Name = x.Author.FirstName + " " + x.Author.LastName }).ToListAsync(ct);
        var names = authors.ToLookup(x => x.BookId, x => x.Name);
        report.Columns = [new("Орын", "total"), new("Кітап", "book"), new("Авторлар", ""), new("Беру саны", "total"), new("Қолжетімді дана", "available")];
        report.Rows = rows.Select(x => Row(Cell((ranks.IndexOf(x.Total) + 1).ToString()), BookCell(x.Title, x.BookId), Cell(string.Join(", ", names[x.BookId])), Cell(Number(x.Total)), Cell(Number(x.Available)))).ToList();
    }

    private async Task OverdueAsync(ReportPageViewModel report, IQueryable<Loan> loans, bool export, CancellationToken ct)
    {
        var today = clock.Today;
        var rate = options.Value.FinePerOverdueDay;
        report.Title = "Кешіктірулер";
        report.Description = "Беру күні кезеңге түскен кешіккен жазбалар. Қайтарылмаған кітапқа бүгінгі болжам, қайтарылған кітапқа бекітілген айыппұл көрсетіледі.";
        var query = loans.Where(x => (x.ReturnedAt ?? today) > x.DueDate).Select(x => new OverdueRow
        {
            Id = x.Id, Student = x.Student.FullName, StudentNumber = x.Student.StudentNumber, Book = x.BookCopy.Book.Title,
            LoanDate = x.LoanDate, DueDate = x.DueDate, ReturnedAt = x.ReturnedAt,
            Days = EF.Functions.DateDiffDay(x.DueDate, x.ReturnedAt ?? today),
            Fine = x.ReturnedAt == null ? EF.Functions.DateDiffDay(x.DueDate, today) * rate : x.Fines.Sum(f => (decimal?)f.Amount) ?? 0m
        });
        report.TotalCount = await query.CountAsync(ct);
        var sums = await query.GroupBy(_ => 1).Select(g => new { Days = g.Sum(x => x.Days), Amount = g.Sum(x => x.Fine), Open = g.Count(x => x.ReturnedAt == null) }).SingleOrDefaultAsync(ct);
        report.Summary = [new("Кешіктірілген жазбалар", Number(report.TotalCount)), new("Әлі қайтарылмаған", Number(sums?.Open ?? 0)), new("Жалпы кешігу", Number(sums?.Days ?? 0) + " күн"), new("Айыппұл есебі", Money(sums?.Amount ?? 0), "Болжамды және бекітілген сомалар қосындысы")];
        IOrderedQueryable<OverdueRow> ordered = report.Filter.Sort switch
        {
            "student" => Asc(report.Filter) ? query.OrderBy(x => x.Student) : query.OrderByDescending(x => x.Student),
            "book" => Asc(report.Filter) ? query.OrderBy(x => x.Book) : query.OrderByDescending(x => x.Book),
            "loan" => Asc(report.Filter) ? query.OrderBy(x => x.LoanDate) : query.OrderByDescending(x => x.LoanDate),
            "due" => Asc(report.Filter) ? query.OrderBy(x => x.DueDate) : query.OrderByDescending(x => x.DueDate),
            "fine" => Asc(report.Filter) ? query.OrderBy(x => x.Fine) : query.OrderByDescending(x => x.Fine),
            _ => Asc(report.Filter) ? query.OrderBy(x => x.Days) : query.OrderByDescending(x => x.Days)
        };
        var rows = await Page(ordered.ThenBy(x => x.Id), report, export).ToListAsync(ct);
        report.Columns = [new("Студент", "student"), new("Кітап", "book"), new("Берілген күні", "loan"), new("Мерзімі", "due"), new("Кешігу (күн)", "days"), new("Айыппұл", "fine"), new("Қайтару мәртебесі", "")];
        report.Rows = rows.Select(x => Row(Cell(x.Student + " · " + x.StudentNumber), new(x.Book, "Loans", "Details", x.Id), Cell(x.LoanDate.ToString("dd.MM.yyyy")), Cell(x.DueDate.ToString("dd.MM.yyyy")), Cell(Number(x.Days)), Cell(Money(x.Fine) + (x.ReturnedAt is null ? " (болжам)" : " (бекітілген)")), Cell(x.ReturnedAt?.ToString("dd.MM.yyyy") ?? "Қайтарылмаған"))).ToList();
    }

    private async Task ReadersAsync(ReportPageViewModel report, IQueryable<Loan> loans, bool export, CancellationToken ct)
    {
        var today = clock.Today;
        report.Title = "Оқырман белсенділігі";
        report.Description = "Беру күні кезеңге түскен жазбалар бойынша. Белсенділік ұпайы = 3 × беру + 2 × қайтару − 2 × кешігу (ең азы 0). Соңғы әрекет — осы жазбалардың беру/қайтару күні.";
        var grouped = loans.GroupBy(x => new { x.StudentId, x.Student.FullName, x.Student.StudentNumber, x.Student.Faculty }).Select(g => new ReaderRow
        {
            Id = g.Key.StudentId, Name = g.Key.FullName, Number = g.Key.StudentNumber, Faculty = g.Key.Faculty,
            Total = g.Count(), Returned = g.Count(x => x.ReturnedAt != null), Overdue = g.Count(x => (x.ReturnedAt ?? today) > x.DueDate),
            Latest = g.Max(x => x.ReturnedAt ?? x.LoanDate),
            Score = g.Count() * 3 + g.Count(x => x.ReturnedAt != null) * 2 - g.Count(x => (x.ReturnedAt ?? today) > x.DueDate) * 2
        });
        report.TotalCount = await grouped.CountAsync(ct);
        var sums = await grouped.GroupBy(_ => 1).Select(g => new { Loans = g.Sum(x => x.Total), Returns = g.Sum(x => x.Returned), Overdue = g.Sum(x => x.Overdue) }).SingleOrDefaultAsync(ct);
        report.Summary = [new("Белсенді оқырмандар", Number(report.TotalCount)), new("Беру саны", Number(sums?.Loans ?? 0)), new("Қайтару саны", Number(sums?.Returns ?? 0)), new("Кешіккен жазбалар", Number(sums?.Overdue ?? 0))];
        IOrderedQueryable<ReaderRow> ordered = report.Filter.Sort switch
        {
            "student" => Asc(report.Filter) ? grouped.OrderBy(x => x.Name) : grouped.OrderByDescending(x => x.Name),
            "total" => Asc(report.Filter) ? grouped.OrderBy(x => x.Total) : grouped.OrderByDescending(x => x.Total),
            "returned" => Asc(report.Filter) ? grouped.OrderBy(x => x.Returned) : grouped.OrderByDescending(x => x.Returned),
            "overdue" => Asc(report.Filter) ? grouped.OrderBy(x => x.Overdue) : grouped.OrderByDescending(x => x.Overdue),
            "recent" => Asc(report.Filter) ? grouped.OrderBy(x => x.Latest) : grouped.OrderByDescending(x => x.Latest),
            _ => Asc(report.Filter) ? grouped.OrderBy(x => x.Score) : grouped.OrderByDescending(x => x.Score)
        };
        var rows = await Page(ordered.ThenBy(x => x.Id), report, export).ToListAsync(ct);
        report.Columns = [new("Студент", "student"), new("Факультет", ""), new("Берілген", "total"), new("Қайтарылған", "returned"), new("Кешіккен", "overdue"), new("Соңғы әрекет", "recent"), new("Белсенділік ұпайы", "score")];
        report.Rows = rows.Select(x => Row(new(x.Name + " · " + x.Number, "Students", "Details", x.Id), Cell(x.Faculty), Cell(Number(x.Total)), Cell(Number(x.Returned)), Cell(Number(x.Overdue)), Cell(x.Latest.ToString("dd.MM.yyyy")), Cell(Number(Math.Max(0, x.Score))))).ToList();
    }

    private async Task RatingsAsync(ReportPageViewModel report, ReportingPeriod period, bool export, CancellationToken ct)
    {
        var filter = report.Filter;
        report.Title = "Әдебиеттер рейтингі";
        report.Description = "Кезеңде жарияланған, жасырылмаған және жойылмаған пікірлер. Бағалар тең болса, кітаптар бір орынды бөліседі. Берілу саны да осы кезеңмен шектеледі; пікірсіз кітаптар кірмейді.";
        var reviews = db.Reviews.AsNoTracking().Where(x => !x.IsDeleted && !x.IsHidden && x.CreatedAt >= period.UtcStart && x.CreatedAt < period.UtcEnd);
        if (!string.IsNullOrEmpty(filter.Search)) reviews = reviews.Where(x => x.Book.Title.Contains(filter.Search) || x.Book.ISBN.Contains(filter.Search) || x.Book.BookAuthors.Any(a => (a.Author.FirstName + " " + a.Author.LastName).Contains(filter.Search)));
        if (filter.CategoryId.HasValue) reviews = reviews.Where(x => x.Book.BookCategories.Any(c => c.CategoryId == filter.CategoryId));
        if (!string.IsNullOrEmpty(filter.Faculty)) reviews = reviews.Where(x => x.Student.Faculty == filter.Faculty);
        var query = reviews.GroupBy(x => new { x.BookId, x.Book.Title }).Select(g => new RatingRow
        {
            BookId = g.Key.BookId, Title = g.Key.Title, Average = g.Average(x => (double)x.Rating), Reviews = g.Count(),
            Loans = db.Loans.Count(l => l.BookCopy.BookId == g.Key.BookId && l.LoanDate >= period.Start && l.LoanDate < period.EndExclusive && (string.IsNullOrEmpty(filter.Faculty) || l.Student.Faculty == filter.Faculty))
        });
        report.TotalCount = await query.CountAsync(ct);
        var reviewCount = await reviews.CountAsync(ct);
        var average = await reviews.Select(x => (double?)x.Rating).AverageAsync(ct);
        report.Summary = [new("Бағаланған кітаптар", Number(report.TotalCount)), new("Ашық пікірлер", Number(reviewCount)), new("Орташа баға", Rating(average), "Барлық ашық пікірлер бойынша өлшенген орташа")];
        var ranks = await query.Select(x => x.Average).Distinct().OrderByDescending(x => x).ToListAsync(ct);
        IOrderedQueryable<RatingRow> ordered = filter.Sort switch
        {
            "book" => Asc(filter) ? query.OrderBy(x => x.Title) : query.OrderByDescending(x => x.Title),
            "reviews" => Asc(filter) ? query.OrderBy(x => x.Reviews) : query.OrderByDescending(x => x.Reviews),
            "total" => Asc(filter) ? query.OrderBy(x => x.Loans) : query.OrderByDescending(x => x.Loans),
            _ => Asc(filter) ? query.OrderBy(x => x.Average) : query.OrderByDescending(x => x.Average)
        };
        var rows = await Page(ordered.ThenBy(x => x.BookId), report, export).ToListAsync(ct);
        report.Columns = [new("Орын", "rating"), new("Кітап", "book"), new("Орташа рейтинг", "rating"), new("Пікірлер саны", "reviews"), new("Кезеңдегі беру", "total")];
        report.Rows = rows.Select(x => Row(Cell((ranks.IndexOf(x.Average) + 1).ToString()), BookCell(x.Title, x.BookId), Cell(Rating(x.Average)), Cell(Number(x.Reviews)), Cell(Number(x.Loans)))).ToList();
    }

    private class LoanCounts { public int Total { get; set; } public int Active { get; set; } public int Returned { get; set; } public int Overdue { get; set; } }
    private sealed class LoanPeriodRow : LoanCounts { public int Year { get; set; } public int Month { get; set; } public int Day { get; set; } }
    private sealed class PopularRow { public int BookId { get; set; } public string Title { get; set; } = ""; public int Total { get; set; } public int Available { get; set; } }
    private sealed class OverdueRow { public int Id { get; set; } public string Student { get; set; } = ""; public string StudentNumber { get; set; } = ""; public string Book { get; set; } = ""; public DateTime LoanDate { get; set; } public DateTime DueDate { get; set; } public DateTime? ReturnedAt { get; set; } public int Days { get; set; } public decimal Fine { get; set; } }
    private sealed class ReaderRow { public int Id { get; set; } public string Name { get; set; } = ""; public string Number { get; set; } = ""; public string Faculty { get; set; } = ""; public int Total { get; set; } public int Returned { get; set; } public int Overdue { get; set; } public DateTime Latest { get; set; } public int Score { get; set; } }
    private sealed class RatingRow { public int BookId { get; set; } public string Title { get; set; } = ""; public double Average { get; set; } public int Reviews { get; set; } public int Loans { get; set; } }
}
