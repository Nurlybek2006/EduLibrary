using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Implementations;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

// Always replace the database name. A supplied connection can select the server and
// authentication, but this harness never creates, mutates, or drops that database.
var sourceConnection = Environment.GetEnvironmentVariable("EDULIBRARY_TEST_CONNECTION_STRING")
    ?? "Server=.;Trusted_Connection=True;TrustServerCertificate=True";
var testDatabaseName = "EduLibraryChecks_" + Guid.NewGuid().ToString("N");
var builder = new SqlConnectionStringBuilder(sourceConnection)
{
    InitialCatalog = testDatabaseName,
    MultipleActiveResultSets = false,
    ConnectTimeout = 15,
    Pooling = false
};
var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(builder.ConnectionString).Options;
var libraryOptions = Options.Create(new LibraryOptions { DefaultLoanDays = 14, FinePerOverdueDay = 100m });
var clock = new CheckClock();
var checks = 0;
var ownsDatabase = false;
var failure = false;

ApplicationDbContext NewDb() => new(dbOptions);
CirculationService Service(ApplicationDbContext context) => new(context, libraryOptions, clock, NullLogger<CirculationService>.Instance);
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    checks++;
    Console.WriteLine("PASS " + name);
}
async Task Reject(Func<Task> action, string name)
{
    try { await action(); }
    catch (BusinessException) { Check(true, name); return; }
    throw new InvalidOperationException("FAILED: expected business rejection: " + name);
}
IssueLoanViewModel Issue(int copyId, int studentId, int loanDaysAgo = 10, int dueDaysAgo = 5) => new()
{
    StudentId = studentId,
    BookCopyId = copyId,
    LoanDate = clock.Today.AddDays(-loanDaysAgo),
    DueDate = clock.Today.AddDays(-dueDaysAgo)
};

Console.WriteLine("SQL integration database: " + testDatabaseName);
try
{
    await using var db = NewDb();
    // Claim ownership immediately after CREATE DATABASE succeeds, so even a later
    // schema-creation failure reaches cleanup. The identifier contains only our
    // constant prefix and hexadecimal GUID; caller input never becomes SQL text.
    var masterConnection = new SqlConnectionStringBuilder(builder.ConnectionString) { InitialCatalog = "master" };
    await using (var master = new SqlConnection(masterConnection.ConnectionString))
    {
        await master.OpenAsync();
        await using var create = master.CreateCommand();
        create.CommandText = $"CREATE DATABASE [{testDatabaseName}]";
        await create.ExecuteNonQueryAsync();
        ownsDatabase = true;
    }
    // Exercise the shipped migrations, including SQL Server rowversions, filtered
    // indexes, check constraints, and transactions, in our isolated database.
    await db.Database.MigrateAsync();
    var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    Check(appliedMigrations.Length > 0 && !(await db.Database.GetPendingMigrationsAsync()).Any(), "shipped EF migrations create current SQL schema");
    const string staffUserId = "checks-staff";
    const string firstUserId = "checks-student-one";
    const string secondUserId = "checks-student-two";
    db.Users.AddRange(
        new ApplicationUser { Id = staffUserId, UserName = "checks-staff", NormalizedUserName = "CHECKS-STAFF", FullName = "Тексеру қызметкері", SecurityStamp = Guid.NewGuid().ToString() },
        new ApplicationUser { Id = firstUserId, UserName = "checks-one", NormalizedUserName = "CHECKS-ONE", FullName = "Бірінші оқырман", SecurityStamp = Guid.NewGuid().ToString() },
        new ApplicationUser { Id = secondUserId, UserName = "checks-two", NormalizedUserName = "CHECKS-TWO", FullName = "Екінші оқырман", SecurityStamp = Guid.NewGuid().ToString() });
    var firstStudent = new Student { StudentNumber = "CHECK-001", FullName = "Бірінші оқырман", Email = "one@example.test", IdentityUserId = firstUserId, Faculty = "Ақпараттық технологиялар", Course = 2, GroupName = "CS-21", IsActive = true, CreatedAt = clock.UtcNow };
    var secondStudent = new Student { StudentNumber = "CHECK-002", FullName = "Екінші оқырман", Email = "two@example.test", IdentityUserId = secondUserId, Faculty = "Ақпараттық технологиялар", Course = 2, GroupName = "CS-22", IsActive = true, CreatedAt = clock.UtcNow };
    var inactiveStudent = new Student { StudentNumber = "CHECK-003", FullName = "Мұрағатталған оқырман", Email = "inactive@example.test", Faculty = "Тексеру", Course = 1, IsActive = false, CreatedAt = clock.UtcNow };
    var publisher = new Publisher { Name = "Тексеру баспасы", Country = "Қазақстан" };
    var book = new Book { ISBN = "9780132350884", Title = "Тексеру кітабы", Language = "Қазақша", PublicationYear = 2020, PageCount = 100, Publisher = publisher, CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow };
    var copies = Enumerable.Range(1, 4).Select(index => new BookCopy { Book = book, InventoryNumber = $"CHECK-{index:000}", Barcode = $"CHECK-BARCODE-{index:000}", ShelfLocation = "A-01", Condition = "Жақсы", AcquiredAt = clock.Today.AddYears(-1), Status = CopyStatus.Available }).ToList();
    db.Students.AddRange(firstStudent, secondStudent, inactiveStudent);
    db.BookCopies.AddRange(copies);
    await db.SaveChangesAsync();
    var firstStudentId = firstStudent.Id;
    var secondStudentId = secondStudent.Id;
    var inactiveStudentId = inactiveStudent.Id;
    var copyIds = copies.Select(x => x.Id).ToArray();
    var service = Service(db);

    await Reject(() => service.IssueAsync(Issue(copyIds[0], inactiveStudentId), staffUserId), "inactive students cannot borrow");
    await Reject(() => service.IssueAsync(Issue(copyIds[0], firstStudentId, -1, -14), staffUserId), "future issue date rejected");
    await Reject(() => service.IssueAsync(Issue(copyIds[0], firstStudentId, 5, 10), staffUserId), "due date before loan date rejected");
    Check(await db.Loans.CountAsync() == 0, "invalid issue leaves no loan records");

    var loanId = await service.IssueAsync(Issue(copyIds[0], firstStudentId), staffUserId);
    Check(await db.BookCopies.AsNoTracking().Where(x => x.Id == copyIds[0]).Select(x => x.Status).SingleAsync() == CopyStatus.OnLoan, "issue marks physical copy on loan");
    await Reject(() => service.IssueAsync(Issue(copyIds[0], secondStudentId), staffUserId), "a copy cannot be issued twice");
    var ownLoans = await service.GetLoansAsync(new LoanFilterViewModel { Status = "all" }, firstUserId, false);
    Check(ownLoans.TotalCount == 1 && ownLoans.Items.Single().EstimatedFine == 500m, "overdue forecast uses configured daily rate");
    Check((await service.GetLoansAsync(new LoanFilterViewModel { Status = "all" }, secondUserId, false)).TotalCount == 0, "student loan list is identity scoped");
    Check(await service.GetLoanAsync(loanId, secondUserId, false) is null, "another student's loan detail is hidden");
    Check((await service.GetLoansAsync(new LoanFilterViewModel { Status = "all" }, null, false)).TotalCount == 0, "missing identity grants no loan visibility");

    var staleReturn = await service.GetReturnFormAsync(loanId) ?? throw new InvalidOperationException("Return form missing");
    var changedLoan = await db.Loans.SingleAsync(x => x.Id == loanId);
    changedLoan.Notes = "Басқа қызметкер енгізген өзгеріс";
    await db.SaveChangesAsync();
    await Reject(() => service.ReturnAsync(staleReturn, staffUserId), "stale return token rejected");
    db.ChangeTracker.Clear();
    Check(!await db.Returns.AnyAsync() && !await db.Fines.AnyAsync() && await db.Loans.AnyAsync(x => x.Id == loanId && x.ReturnedAt == null)
        && await db.BookCopies.AnyAsync(x => x.Id == copyIds[0] && x.Status == CopyStatus.OnLoan), "failed return rolls back loan copy return and fine together");

    var returnForm = await service.GetReturnFormAsync(loanId) ?? throw new InvalidOperationException("Return form missing");
    returnForm.ReturnDate = clock.Today.AddDays(1);
    await Reject(() => service.ReturnAsync(returnForm, staffUserId), "future return date rejected");
    returnForm.ReturnDate = clock.Today.AddDays(-11);
    await Reject(() => service.ReturnAsync(returnForm, staffUserId), "return before loan date rejected");
    returnForm.ReturnDate = clock.Today;
    await service.ReturnAsync(returnForm, staffUserId);
    db.ChangeTracker.Clear();
    var fine = await db.Fines.AsNoTracking().SingleAsync(x => x.LoanId == loanId);
    Check(fine.Amount == 500m && fine.StudentId == firstStudentId && fine.Status == FineStatus.Unpaid, "return fixes five-day overdue fine at 500 KZT");
    Check(await db.BookCopies.AnyAsync(x => x.Id == copyIds[0] && x.Status == CopyStatus.Available), "good returned copy becomes available");
    await Reject(() => service.ReturnAsync(returnForm, staffUserId), "duplicate return rejected");
    Check(await db.Returns.CountAsync(x => x.LoanId == loanId) == 1 && await db.Fines.CountAsync(x => x.LoanId == loanId) == 1, "duplicate return creates neither duplicate return nor fine");
    Check(await service.GetFineAsync(fine.Id, secondUserId, false) is null, "another student's fine detail is hidden");
    Check((await service.GetFinesAsync(null, "all", 1, secondUserId, false)).OutstandingTotal == 0m, "student cannot see another student's outstanding balance");
    Check((await service.GetFinesAsync(null, "all", 1, firstUserId, false)).OutstandingTotal == 500m, "student sees own outstanding balance");

    await Reject(() => service.PayFineAsync(new FinePaymentViewModel { FineId = fine.Id, Amount = -1m }, staffUserId), "negative fine payment rejected");
    await Reject(() => service.PayFineAsync(new FinePaymentViewModel { FineId = fine.Id, Amount = 0.001m }, staffUserId), "payment with excess decimal precision rejected");
    var initialVersion = Convert.ToBase64String(fine.RowVersion);
    await service.PayFineAsync(new FinePaymentViewModel { FineId = fine.Id, Amount = 200m, RowVersion = initialVersion }, staffUserId);
    db.ChangeTracker.Clear();
    var partial = await db.Fines.AsNoTracking().SingleAsync(x => x.Id == fine.Id);
    Check(partial.PaidAmount == 200m && partial.Status == FineStatus.PartiallyPaid && partial.PaidAt is null, "partial payment updates balance and status");
    await Reject(() => service.PayFineAsync(new FinePaymentViewModel { FineId = fine.Id, Amount = 50m, RowVersion = initialVersion }, staffUserId), "stale payment token rejected");
    await Reject(() => service.PayFineAsync(new FinePaymentViewModel { FineId = fine.Id, Amount = 301m }, staffUserId), "overpayment rejected");
    Check(await db.FinePayments.CountAsync(x => x.FineId == fine.Id) == 1 && await db.FinePayments.SumAsync(x => x.Amount) == 200m, "rejected payments create no audit entries");
    await service.PayFineAsync(new FinePaymentViewModel { FineId = fine.Id, Amount = 300m, RowVersion = Convert.ToBase64String(partial.RowVersion) }, staffUserId);
    db.ChangeTracker.Clear();
    var paid = await db.Fines.AsNoTracking().SingleAsync(x => x.Id == fine.Id);
    Check(paid.PaidAmount == paid.Amount && paid.Status == FineStatus.Paid && paid.PaidAt.HasValue, "full payment closes fine with paid timestamp");
    Check(await db.FinePayments.Where(x => x.FineId == fine.Id).SumAsync(x => x.Amount) == paid.Amount
        && await db.FinePayments.AllAsync(x => x.ReceivedByUserId == staffUserId), "payment audit amounts reconcile and record receiving staff");

    var damagedLoanId = await service.IssueAsync(Issue(copyIds[1], secondStudentId, 0, -14), staffUserId);
    var damagedReturn = await service.GetReturnFormAsync(damagedLoanId) ?? throw new InvalidOperationException("Damaged return form missing");
    damagedReturn.ReturnCondition = "Damaged";
    await service.ReturnAsync(damagedReturn, staffUserId);
    Check(await db.BookCopies.AnyAsync(x => x.Id == copyIds[1] && x.Status == CopyStatus.Damaged)
        && !await db.Fines.AnyAsync(x => x.LoanId == damagedLoanId), "on-time damaged return marks copy damaged without overdue fine");

    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    async Task<bool> Race(int studentId)
    {
        await using var raceDb = NewDb();
        await ready.Task;
        try { await Service(raceDb).IssueAsync(Issue(copyIds[2], studentId, 0, -14), staffUserId); return true; }
        catch (BusinessException) { return false; }
    }
    var firstRace = Race(firstStudentId);
    var secondRace = Race(secondStudentId);
    ready.SetResult();
    var raceResults = await Task.WhenAll(firstRace, secondRace);
    Check(raceResults.Count(x => x) == 1, "parallel librarians issuing the same copy have exactly one winner");
    Check(await db.Loans.CountAsync(x => x.BookCopyId == copyIds[2] && x.ReturnedAt == null) == 1
        && await db.BookCopies.AnyAsync(x => x.Id == copyIds[2] && x.Status == CopyStatus.OnLoan), "parallel issue preserves one active loan and consistent copy status");

    var dashboard = await new DashboardService(db, clock, libraryOptions).GetAsync();
    Check(dashboard.TotalBooks == 1 && dashboard.TotalCopies == 4 && dashboard.AvailableCopies == 2
        && dashboard.ActiveLoans == 1 && dashboard.OverdueLoans == 0 && dashboard.TotalStudents == 3
        && dashboard.ActiveReaders == 1 && dashboard.OutstandingFines == 0m, "dashboard statistics reflect committed SQL records");
    Check(dashboard.RecentLoans.Count == 3 && dashboard.RecentReturns.Count == 2 && dashboard.PopularBooks.Single().LoanCount == 3, "dashboard previews query real circulation history");

    await using var catalogDb = NewDb();
    var catalog = new CatalogService(catalogDb, clock, NullLogger<CatalogService>.Instance);
    await catalog.SaveAuthorAsync(new AuthorEditViewModel { FirstName = "Айша", LastName = "КаталогБір", Biography = "Каталог тексеруінің бірінші авторы" });
    await catalog.SaveAuthorAsync(new AuthorEditViewModel { FirstName = "Әли", LastName = "КаталогЕкі", Biography = "Каталог тексеруінің екінші авторы" });
    await catalog.SavePublisherAsync(new PublisherEditViewModel { Name = "Каталог тексеру баспасы", Country = "Қазақстан", Website = "https://example.test" });
    await catalog.SaveCategoryAsync(new CategoryEditViewModel { Name = "Каталог тексеру: бірінші", Description = "Алғашқы санат" });
    await catalog.SaveCategoryAsync(new CategoryEditViewModel { Name = "Каталог тексеру: екінші", Description = "Жаңартылған санат" });
    var firstAuthorId = (await catalog.GetAuthorsAsync("КаталогБір")).Items.Single().Id;
    var secondAuthorId = (await catalog.GetAuthorsAsync("КаталогЕкі")).Items.Single().Id;
    var catalogPublisherId = (await catalog.GetPublishersAsync("Каталог тексеру баспасы")).Items.Single().Id;
    var firstCategoryId = (await catalog.GetCategoriesAsync("Каталог тексеру: бірінші")).Items.Single().Id;
    var secondCategoryId = (await catalog.GetCategoriesAsync("Каталог тексеру: екінші")).Items.Single().Id;
    Check(await catalogDb.Authors.CountAsync() == 2 && await catalogDb.Categories.CountAsync() == 2
        && await catalogDb.Publishers.CountAsync() == 2, "catalog lookup services persist authors categories and publishers");

    var newBook = new BookEditViewModel
    {
        ISBN = "978-0-596-52068-7", Title = "Каталогты сақтау тексеруі", Description = "Кітап сипаттамасы",
        PublicationYear = 2024, Language = "Қазақша", PageCount = 240, PublisherId = catalogPublisherId,
        AuthorIds = [firstAuthorId, secondAuthorId, firstAuthorId], CategoryIds = [firstCategoryId, firstCategoryId]
    };
    var catalogBookId = await catalog.SaveBookAsync(newBook);
    Check(await catalogDb.Books.AsNoTracking().AnyAsync(x => x.Id == catalogBookId && x.ISBN == "9780596520687" && x.Title == newBook.Title)
        && await catalogDb.BookAuthors.CountAsync(x => x.BookId == catalogBookId) == 2
        && await catalogDb.BookCategories.CountAsync(x => x.BookId == catalogBookId) == 1,
        "book create normalizes ISBN and persists distinct author category joins");
    var createdBook = await catalog.GetBookAsync(catalogBookId, false) ?? throw new InvalidOperationException("Created catalog book missing");
    Check(createdBook.Book.Authors.Contains("КаталогБір") && createdBook.Book.Authors.Contains("КаталогЕкі")
        && createdBook.Book.Publisher == "Каталог тексеру баспасы" && createdBook.Categories.Single() == "Каталог тексеру: бірінші",
        "book details materialize saved lookup relationships");

    var bookEdit = await catalog.GetBookForEditAsync(catalogBookId) ?? throw new InvalidOperationException("Book edit model missing");
    bookEdit.Title = "Каталогтың жаңартылған кітабы";
    bookEdit.AuthorIds = [secondAuthorId];
    bookEdit.CategoryIds = [secondCategoryId];
    await catalog.SaveBookAsync(bookEdit);
    catalogDb.ChangeTracker.Clear();
    Check(await catalogDb.Books.AnyAsync(x => x.Id == catalogBookId && x.Title == bookEdit.Title)
        && await catalogDb.BookAuthors.CountAsync(x => x.BookId == catalogBookId) == 1
        && await catalogDb.BookAuthors.AnyAsync(x => x.BookId == catalogBookId && x.AuthorId == secondAuthorId)
        && await catalogDb.BookCategories.CountAsync(x => x.BookId == catalogBookId) == 1
        && await catalogDb.BookCategories.AnyAsync(x => x.BookId == catalogBookId && x.CategoryId == secondCategoryId),
        "book edit replaces author and category links in SQL");
    await Reject(() => catalog.SaveBookAsync(new BookEditViewModel
    {
        ISBN = "978 0 596 52068 7", Title = "Қайталанған ISBN", PublicationYear = 2024, Language = "Қазақша", PageCount = 100,
        PublisherId = catalogPublisherId, AuthorIds = [secondAuthorId], CategoryIds = [secondCategoryId]
    }), "equivalent ISBN with different separators cannot create duplicate book");
    Check(await catalogDb.Books.CountAsync(x => x.ISBN == "9780596520687") == 1, "duplicate ISBN rejection leaves one persisted book");

    CopyEditViewModel NewCopy(string inventory) => new()
    {
        BookId = catalogBookId, InventoryNumber = inventory, Barcode = null, ShelfLocation = "B-02",
        Condition = "Жақсы", AcquiredAt = clock.Today, Status = CopyStatus.Available
    };
    var generatedCopyOneId = await catalog.SaveCopyAsync(NewCopy("CATALOG-001"));
    var generatedCopyTwoId = await catalog.SaveCopyAsync(NewCopy("CATALOG-002"));
    var generatedCopies = await catalogDb.BookCopies.AsNoTracking().Where(x => x.BookId == catalogBookId).ToListAsync();
    Check(generatedCopies.Count == 2 && generatedCopies.All(x => !string.IsNullOrWhiteSpace(x.Barcode))
        && generatedCopies.Select(x => x.Barcode).Distinct().Count() == 2, "copy create generates distinct nonempty persisted barcodes");
    var staleCopy = await catalog.GetCopyAsync(generatedCopyOneId) ?? throw new InvalidOperationException("Copy edit model missing");
    var currentCopy = await catalog.GetCopyAsync(generatedCopyOneId) ?? throw new InvalidOperationException("Copy edit model missing");
    currentCopy.ShelfLocation = "B-03";
    await catalog.SaveCopyAsync(currentCopy);
    staleCopy.ShelfLocation = "STALE-SHELF";
    await Reject(() => catalog.SaveCopyAsync(staleCopy), "copy edit rejects stale SQL rowversion");
    catalogDb.ChangeTracker.Clear();
    Check(await catalogDb.BookCopies.AnyAsync(x => x.Id == generatedCopyOneId && x.ShelfLocation == "B-03"), "stale copy edit preserves latest persisted shelf");

    var filteredBooks = await catalog.GetBooksAsync(new BookFilterViewModel
    {
        Search = "Каталогтың жаңартылған", AuthorId = secondAuthorId, PublisherId = catalogPublisherId,
        CategoryId = secondCategoryId, Language = "Қазақша", Sort = "year"
    }, false);
    Check(filteredBooks.TotalCount == 1 && filteredBooks.Books.Single().Id == catalogBookId
        && filteredBooks.Books.Single().AvailableCopies == 2, "catalog search and combined relationship filters query saved SQL data");
    await catalog.SetBookArchivedAsync(catalogBookId, true);
    Check(await catalog.GetBookAsync(catalogBookId, false) is null
        && (await catalog.GetBooksAsync(new BookFilterViewModel { Search = "9780596520687", IncludeArchived = true }, false)).TotalCount == 0,
        "archive hides book from public details and catalog even with archived query flag");
    var staffArchivedBook = await catalog.GetBookAsync(catalogBookId, true);
    Check(staffArchivedBook is not null && staffArchivedBook.Book.IsArchived && staffArchivedBook.Book.AvailableCopies == 0
        && staffArchivedBook.Copies.Count == 2, "staff retains archived book and physical copy history");
    await catalog.SetBookArchivedAsync(catalogBookId, false);
    var restoredBook = await catalog.GetBookAsync(catalogBookId, false);
    Check(restoredBook is not null && !restoredBook.Book.IsArchived && restoredBook.Book.AvailableCopies == 2,
        "restore returns archived book to public catalog with saved copies");

    await Reject(() => catalog.DeleteAuthorAsync(secondAuthorId), "referenced author deletion blocked");
    await Reject(() => catalog.DeletePublisherAsync(catalogPublisherId), "referenced publisher deletion blocked");
    await Reject(() => catalog.DeleteCategoryAsync(secondCategoryId), "referenced category deletion blocked");
    Check(await catalogDb.Authors.AnyAsync(x => x.Id == secondAuthorId) && await catalogDb.Publishers.AnyAsync(x => x.Id == catalogPublisherId)
        && await catalogDb.Categories.AnyAsync(x => x.Id == secondCategoryId), "blocked lookup deletion preserves referenced records");

    // Deliberately include low hidden/deleted ratings. They must not affect either
    // public report ranking or analytics, and no main application data is touched.
    catalogDb.Reviews.AddRange(
        new Review { BookId = book.Id, StudentId = firstStudentId, Rating = 5, Comment = "Ашық пікір", CreatedAt = clock.UtcNow },
        new Review { BookId = book.Id, StudentId = secondStudentId, Rating = 1, Comment = "Жасырылған пікір", IsHidden = true, CreatedAt = clock.UtcNow },
        new Review { BookId = book.Id, StudentId = inactiveStudentId, Rating = 1, Comment = "Жойылған пікір", IsDeleted = true, CreatedAt = clock.UtcNow },
        new Review { BookId = catalogBookId, StudentId = secondStudentId, Rating = 4, Comment = "Екінші ашық пікір", CreatedAt = clock.UtcNow.AddDays(-10) });
    await catalogDb.SaveChangesAsync();
    catalogDb.ChangeTracker.Clear();
    var reporting = new ReportingService(catalogDb, clock, libraryOptions);
    foreach (var reportType in new[] { "loans", "popular", "overdue", "readers", "ratings" })
    {
        var report = await reporting.GetAsync(new ReportFilterViewModel { Type = reportType, Range = "30" });
        Check(report.Rows.Count > 0 && report.Columns.Count > 0 && report.Summary.Count > 0
            && report.Rows.All(row => row.Cells.Count == report.Columns.Count), reportType + " report generates complete SQL-backed rows and summaries");
        var csv = await reporting.ExportCsvAsync(new ReportFilterViewModel { Type = reportType, Range = "30" });
        Check(csv.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })
            && Encoding.UTF8.GetString(csv).Contains(report.Title), reportType + " report exports UTF8 BOM CSV with actual report content");
        var printable = await reporting.GetPrintAsync(new ReportFilterViewModel { Type = reportType, Range = "30", Page = 1000 });
        Check(printable.Rows.Count == printable.TotalCount, reportType + " print report contains every filtered row");
    }
    var loanReport = await reporting.GetAsync(new ReportFilterViewModel { Type = "loans", Range = "30" });
    Check(loanReport.Summary[0].Value == "3" && loanReport.Summary[1].Value == "1" && loanReport.Summary[2].Value == "2"
        && loanReport.Summary[3].Value == "1", "loan report SQL summaries match three issues two returns and one historical delay");
    var monthlyReport = await reporting.GetAsync(new ReportFilterViewModel { Type = "loans", Range = "30", GroupBy = "month" });
    Check(monthlyReport.TotalCount == 1 && monthlyReport.Rows.Single().Cells[1].Value == "3", "monthly loan grouping aggregates the same source records");
    var overdueReport = await reporting.GetAsync(new ReportFilterViewModel { Type = "overdue", Range = "30" });
    Check(overdueReport.TotalCount == 1 && overdueReport.Rows.Single().Cells[4].Value == "5"
        && overdueReport.Rows.Single().Cells[5].Value.Contains("500"), "overdue report preserves actual return delay and fixed fine");
    var ratingReport = await reporting.GetAsync(new ReportFilterViewModel { Type = "ratings", Range = "30" });
    Check(ratingReport.TotalCount == 2 && ratingReport.Rows[0].Cells[1].Id == book.Id
        && ratingReport.Rows.All(row => row.Cells[3].Value == "1"), "ratings report excludes hidden and deleted reviews and orders real averages");
    var filteredRating = await reporting.GetAsync(new ReportFilterViewModel { Type = "ratings", Range = "30", CategoryId = secondCategoryId, Search = "Каталогтың жаңартылған" });
    Check(filteredRating.TotalCount == 1 && filteredRating.Rows.Single().Cells[1].Id == catalogBookId, "rating report search and category filters apply in SQL");
    var todayLoans = await reporting.GetAsync(new ReportFilterViewModel { Type = "loans", Range = "custom", StartDate = clock.Today, EndDate = clock.Today });
    Check(todayLoans.Summary[0].Value == "2", "custom report date range excludes older issues");

    var analyticsService = new AnalyticsService(catalogDb, clock);
    var analytics = await analyticsService.GetAsync(new ReportFilterViewModel { Range = "30" });
    Check(analytics.Statistics.Count == 10 && analytics.Charts.Count == 7, "analytics supplies ten labeled metrics and seven real chart series");
    Check(analytics.Charts.Single(x => x.Id == "loans").Values.Sum() == 3
        && analytics.Charts.Single(x => x.Id == "returns").Values.Sum() == 2
        && analytics.Charts.Single(x => x.Id == "popular").Values.Single() == 3, "analytics loan return and popularity charts reconcile with SQL history");
    Check(analytics.Charts.Single(x => x.Id == "overdue").Values.Sum() == 1
        && analytics.Charts.Single(x => x.Id == "readers").Values.Sum() >= 2,
        "analytics overdue and reader activity are calculated from actual dates and identities");
    Check(analytics.Charts.Single(x => x.Id == "ratings").Values.SequenceEqual(new[] { 5d, 4d })
        && analytics.Statistics.Single(x => x.Label == "Әдебиеттердің орташа бағасы").Value.Contains("4"),
        "analytics rating charts exclude hidden and deleted fixtures");
    var todayAnalytics = await analyticsService.GetAsync(new ReportFilterViewModel { Range = "custom", StartDate = clock.Today, EndDate = clock.Today });
    Check(todayAnalytics.Charts.Single(x => x.Id == "loans").Values.Sum() == 2
        && todayAnalytics.Charts.Single(x => x.Id == "ratings").Values.SequenceEqual(new[] { 5d })
        && todayAnalytics.Charts.Single(x => x.Id == "loans").Labels.Length == 1,
        "analytics custom range updates both calendar loan events and UTC review events");
    var emptyAnalytics = await analyticsService.GetAsync(new ReportFilterViewModel { Range = "custom", StartDate = clock.Today.AddDays(-100), EndDate = clock.Today.AddDays(-95) });
    Check(emptyAnalytics.Charts.All(x => !x.HasData), "empty analytics range uses honest zero or empty series without fabricated statistics");
    Console.WriteLine($"SUCCESS: {checks} SQL-backed integration checks passed.");
}
catch (Exception exception)
{
    failure = true;
    Console.Error.WriteLine(exception);
}
finally
{
    if (ownsDatabase)
    {
        try
        {
            // The connection is constructed above from an unguessable name generated
            // in this process. Never delete a caller-supplied catalog.
            if (!builder.InitialCatalog.Equals(testDatabaseName, StringComparison.Ordinal)
                || !testDatabaseName.StartsWith("EduLibraryChecks_", StringComparison.Ordinal))
                throw new InvalidOperationException("Test database cleanup safety check failed.");
            await using var cleanup = NewDb();
            await cleanup.Database.EnsureDeletedAsync();
            Console.WriteLine("Removed own test database: " + testDatabaseName);
        }
        catch (Exception exception)
        {
            failure = true;
            Console.Error.WriteLine("Cleanup failed for " + testDatabaseName + ": " + exception.Message);
        }
    }
}
return failure ? 1 : 0;

sealed class CheckClock : ILibraryClock
{
    public DateTime Today => new(2026, 10, 19);
    public DateTime UtcNow => new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
}
