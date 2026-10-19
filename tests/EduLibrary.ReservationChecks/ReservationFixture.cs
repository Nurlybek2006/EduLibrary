using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Implementations;
using EduLibrary.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal sealed class ReservationFixture(DbContextOptions<ApplicationDbContext> dbOptions)
{
    public const string StaffUserId = "reservation-check-staff";
    public const string OtherStaffUserId = "reservation-check-staff-two";
    public ReservationClock Clock { get; } = new();
    public IOptions<LibraryOptions> LibraryOptions { get; } = Options.Create(new LibraryOptions { DefaultLoanDays = 14, FinePerOverdueDay = 100m });
    public Dictionary<string, int> Books { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int[]> Copies { get; } = new(StringComparer.Ordinal);
    public int[] Students { get; private set; } = [];
    public int CheckCount { get; private set; }
    public ApplicationDbContext NewDb() => new(dbOptions);
    public string Reader(int index) => "reservation-check-reader-" + index;
    public CirculationService Circulation(ApplicationDbContext db) => new(db, LibraryOptions, Clock, NullLogger<CirculationService>.Instance);
    public ReservationOptions Settings { get; } = new() { SlotCapacity = 100 };
    public ReservationService Service(ApplicationDbContext db, ReservationOptions? settings = null) => new(db, Options.Create(settings ?? Settings), LibraryOptions, Clock, NullLogger<ReservationService>.Instance);

    public void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + label);
        CheckCount++;
        Console.WriteLine("PASS " + label);
    }

    public async Task Reject(Func<Task> action, string label)
    {
        try { await action(); }
        catch (BusinessException) { Check(true, label); return; }
        throw new InvalidOperationException("FAILED: expected business rejection: " + label);
    }

    public async Task SeedAsync()
    {
        await using var db = NewDb();
        db.Roles.AddRange(new IdentityRole { Id = "reservation-role-staff", Name = "Librarian", NormalizedName = "LIBRARIAN" },
            new IdentityRole { Id = "reservation-role-reader", Name = "Student", NormalizedName = "STUDENT" });
        foreach (var staffId in new[] { StaffUserId, OtherStaffUserId })
        {
            db.Users.Add(new ApplicationUser { Id = staffId, UserName = staffId, NormalizedUserName = staffId.ToUpperInvariant(), FullName = "Reservation Test Librarian", SecurityStamp = Guid.NewGuid().ToString() });
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = staffId, RoleId = "reservation-role-staff" });
        }
        var students = new List<Student>();
        for (var index = 0; index < 14; index++)
        {
            var userId = Reader(index);
            db.Users.Add(new ApplicationUser { Id = userId, UserName = userId, NormalizedUserName = userId.ToUpperInvariant(), FullName = "Reservation Reader " + index, SecurityStamp = Guid.NewGuid().ToString() });
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = "reservation-role-reader" });
            students.Add(new Student { StudentNumber = $"RS-{index:000}", FullName = "Reservation Reader " + index,
                Email = $"reservation{index}@example.test", Faculty = "Тексеру факультеті", Course = 2, GroupName = "RS-26",
                IdentityUserId = userId, IsActive = index != 13, CreatedAt = Clock.UtcNow });
        }
        db.Students.AddRange(students);
        var publisher = new Publisher { Name = "Reservation Test Press", Country = "Қазақстан" };
        var specs = new[]
        {
            ("available", 2), ("unavailable", 1), ("concurrent", 1), ("fifo", 1), ("cancel", 1), ("expiry", 1),
            ("pickup", 1), ("double-pickup", 1), ("stale", 1), ("archived", 1), ("borrowed", 1), ("slots", 4),
            ("create-rollback", 1), ("ready-rollback", 1), ("expiry-rollback", 1), ("return-promotion", 1)
        };
        var books = new Dictionary<string, Book>();
        for (var index = 0; index < specs.Length; index++)
        {
            var (key, copyCount) = specs[index];
            var book = new Book { ISBN = "9780014" + index.ToString("00000") + "0", Title = "Reservation " + key,
                Description = "Isolated reservation checks", Publisher = publisher, PublicationYear = 2026, PageCount = 200,
                Language = "Қазақша", CreatedAt = Clock.UtcNow, UpdatedAt = Clock.UtcNow, IsArchived = key == "archived",
                Copies = Enumerable.Range(1, copyCount).Select(copy => new BookCopy { InventoryNumber = $"RES-{key}-{copy}", Barcode = $"RESCODE-{key}-{copy}",
                    ShelfLocation = "R-01", Condition = "Жақсы", AcquiredAt = Clock.Today.AddDays(-30), Status = CopyStatus.Available }).ToList() };
            db.Books.Add(book); books.Add(key, book);
        }
        await db.SaveChangesAsync();
        Students = students.Select(x => x.Id).ToArray();
        foreach (var pair in books) { Books.Add(pair.Key, pair.Value.Id); Copies.Add(pair.Key, pair.Value.Copies.Select(x => x.Id).ToArray()); }
        Check(Books.Count == 16 && Students.Length == 14, "isolated reservation fixtures persist with current migrations");
    }

    public async Task<int> SeedActiveLoanAsync(string book, int student)
    {
        await using var db = NewDb();
        var copy = await db.BookCopies.SingleAsync(x => x.Id == Copies[book][0]);
        copy.Status = CopyStatus.OnLoan;
        var loan = new Loan { StudentId = Students[student], BookCopyId = copy.Id, LoanDate = Clock.Today.AddDays(-1), DueDate = Clock.Today.AddDays(14), IssuedByUserId = StaffUserId };
        db.Loans.Add(loan); await db.SaveChangesAsync();
        return loan.Id;
    }
}

internal sealed class ReservationClock : ILibraryClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 26, 4, 0, 0, DateTimeKind.Utc);
    public DateTime Today => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, ReportingRules.LibraryTimeZone).Date;
    public void Advance(TimeSpan time) => UtcNow = UtcNow.Add(time);
}
