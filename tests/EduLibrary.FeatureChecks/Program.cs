using System.Xml.Linq;
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

// The caller may select the SQL server/authentication, never the test catalog.
// Only the random database created by this process can enter the cleanup path.
var source = Environment.GetEnvironmentVariable("EDULIBRARY_TEST_CONNECTION_STRING")
    ?? "Server=.;Trusted_Connection=True;TrustServerCertificate=True";
var ownDatabase = "EduLibraryFeatureChecks_" + Guid.NewGuid().ToString("N");
var connection = new SqlConnectionStringBuilder(source) { InitialCatalog = ownDatabase, Pooling = false, ConnectTimeout = 15, MultipleActiveResultSets = false };
var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options;
var clock = new FeatureClock();
var libraryOptions = Options.Create(new LibraryOptions { FinePerOverdueDay = 100m, DefaultLoanDays = 14 });
var recommendationOptions = Options.Create(new RecommendationOptions { ItemsPerSection = 12, CandidateLimit = 100, Llm = new() { Enabled = false } });
using var networkGuard = new NoNetworkHandler();
using var http = new HttpClient(networkGuard);
var explanations = new RecommendationExplanationService(new TemplateExplanationProvider(),
    new OpenAICompatibleExplanationProvider(http, recommendationOptions, NullLogger<OpenAICompatibleExplanationProvider>.Instance), recommendationOptions);
var checks = 0;
var created = false;
var failed = false;
ApplicationDbContext NewDb() => new(dbOptions);
CirculationService Circulation(ApplicationDbContext db) => new(db, libraryOptions, clock, NullLogger<CirculationService>.Instance);
RecommendationService Recommendations(ApplicationDbContext db) => new(db, recommendationOptions, explanations, NullLogger<RecommendationService>.Instance);
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + label);
    checks++; Console.WriteLine("PASS " + label);
}
async Task Reject(Func<Task> action, string label)
{
    try { await action(); }
    catch (BusinessException) { Check(true, label); return; }
    throw new InvalidOperationException("FAILED: expected business rejection: " + label);
}
async Task RejectConstraint(Func<Task> action, string label)
{
    try { await action(); }
    catch (DbUpdateException) { Check(true, label); return; }
    throw new InvalidOperationException("FAILED: expected SQL constraint rejection: " + label);
}

try
{
    var masterConnection = new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master" };
    await using (var master = new SqlConnection(masterConnection.ConnectionString))
    {
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{ownDatabase}]";
        await command.ExecuteNonQueryAsync(); created = true;
    }
    Console.WriteLine("Isolated feature database: " + ownDatabase);
    await using var db = NewDb();
    await db.Database.MigrateAsync();
    Check(!(await db.Database.GetPendingMigrationsAsync()).Any(), "additive shipped migrations apply to isolated SQL database");
    const string staff = "feature-staff";
    const string firstUser = "feature-reader-one";
    const string secondUser = "feature-reader-two";
    const string coldUser = "feature-reader-cold";
    db.Users.AddRange(new[] { staff, firstUser, secondUser, coldUser }.Select(id => new ApplicationUser
    { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), FullName = "Тексеру оқырманы " + id, SecurityStamp = Guid.NewGuid().ToString() }));
    var publisher = new Publisher { Name = "Feature Test Press", Country = "Қазақстан" };
    var author = new Author { FirstName = "Feature", LastName = "Author" };
    var category = new Category { Name = "Programming" };
    var physics = new Category { Name = "Physics" };
    var subject = new Subject { Name = "Programming Basics" };
    db.Publishers.Add(publisher); db.Authors.Add(author); db.Categories.AddRange(category, physics); db.Subjects.Add(subject);
    Book NewBook(int number, string title, int copies = 1, bool archived = false, bool science = false) => new()
    {
        ISBN = "978000200" + number.ToString("000") + "0", Title = title, Description = science ? "Physics and mathematics" : "Python programming algorithms",
        PublicationYear = 2024, PageCount = 200, Language = "English", Publisher = publisher, CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow, IsArchived = archived,
        BookAuthors = [new() { Author = author }], BookCategories = [new() { Category = science ? physics : category }],
        BookSubjects = science ? [] : [new() { Subject = subject }],
        Copies = Enumerable.Range(1, copies).Select(index => new BookCopy { InventoryNumber = $"FEATURE-{number}-{index}", Barcode = $"FEATUREQR{number:000}{index:000}",
            Status = CopyStatus.Available, ShelfLocation = "F-01", Condition = "Жақсы", AcquiredAt = clock.Today.AddDays(-30) }).ToList()
    };
    var reviewedBook = NewBook(1, "Python Review Book", 2);
    var dueOneBook = NewBook(2, "Programming Due Tomorrow");
    var archivedBook = NewBook(3, "Archived Test Book", archived: true);
    var activeBook = NewBook(4, "Active Reading Book");
    var previousBook = NewBook(5, "Previously Read Book");
    var scienceBook = NewBook(6, "Physics Discovery Book", science: true);
    var availableBook = NewBook(7, "Available Algorithms", 2);
    db.Books.AddRange(reviewedBook, dueOneBook, archivedBook, activeBook, previousBook, scienceBook, availableBook);
    Student NewStudent(string id, int number) => new() { StudentNumber = "FT-" + number, FullName = "Feature Reader " + number, Email = $"feature{number}@example.test",
        PhoneNumber = "", Faculty = number == 3 ? "" : "Computing", Course = 1, GroupName = "FT-101", IdentityUserId = id, CreatedAt = clock.UtcNow };
    var first = NewStudent(firstUser, 1); var second = NewStudent(secondUser, 2); var cold = NewStudent(coldUser, 3);
    first.Interests.Add(new StudentInterest { Name = "Python" }); first.StudentSubjects.Add(new StudentSubject { Subject = subject });
    db.Students.AddRange(first, second, cold); await db.SaveChangesAsync();
    Check(await db.Books.CountAsync() == 7 && await db.BookCopies.CountAsync() == 9, "real SQL fixture has linked books subjects students and distinct copies");

    var circulation = Circulation(db);
    IssueLoanViewModel Issue(Book book, Student student, int loanDaysAgo, int dueOffset) => new()
    { BookCopyId = book.Copies.First().Id, StudentId = student.Id, LoanDate = clock.Today.AddDays(-loanDaysAgo), DueDate = clock.Today.AddDays(dueOffset) };
    var activeLoanId = await circulation.IssueAsync(Issue(activeBook, first, 10, -2), staff);
    var previousLoanId = await circulation.IssueAsync(Issue(previousBook, first, 10, -5), staff);
    var previousReturn = await circulation.GetReturnFormAsync(previousLoanId) ?? throw new InvalidOperationException("Missing return fixture");
    await circulation.ReturnAsync(previousReturn, staff);
    var dueOneLoanId = await circulation.IssueAsync(Issue(dueOneBook, second, 0, 1), staff);
    var dueThreeLoanId = await circulation.IssueAsync(Issue(reviewedBook, second, 0, 3), staff);
    db.ChangeTracker.Clear();
    Check(await db.Notifications.CountAsync(n => n.Type == "Issued") == 4, "issue writes four persisted reader notifications inside circulation transactions");
    Check(await db.Notifications.CountAsync(n => n.Type == "Returned") == 1 && await db.Notifications.CountAsync(n => n.Type == "Fine") == 1,
        "overdue return writes returned and fine notifications atomically");
    var previousFine = await db.Fines.AsNoTracking().SingleAsync(f => f.LoanId == previousLoanId);
    Check(previousFine.Amount == 500m && await db.Notifications.AnyAsync(n => n.UserId == firstUser && n.Type == "Fine" && n.Link == "/Fines/Details/" + previousFine.Id),
        "fine notification links to the committed SQL fine record");

    var readingSpace = new ReadingSpaceService(db, clock, Recommendations(db));
    var firstShelf = await readingSpace.GetAsync(firstUser);
    Check(firstShelf.HasStudentProfile && firstShelf.StudentId == first.Id && firstShelf.ActiveLoanCount == 1
        && firstShelf.CurrentBooks.Single().LoanId == activeLoanId && firstShelf.CurrentBooks.Single().DaysUntilDue == -2,
        "reading space resolves current Identity reader and calculates real overdue shelf");
    Check(firstShelf.ReturnedBookCount == 1 && firstShelf.History.Single().LoanId == previousLoanId
        && firstShelf.OutstandingFines == previousFine.Amount && firstShelf.Interests.SequenceEqual(new[] { "Python" }),
        "reading space composes actual return history fine balance and interests from SQL");
    var secondShelf = await readingSpace.GetAsync(secondUser);
    Check(secondShelf.ActiveLoanCount == 2 && secondShelf.CurrentBooks.All(b => b.LoanId != activeLoanId)
        && secondShelf.OutstandingFines == 0 && secondShelf.RecentActivity.All(a => !a.Link.Contains("/Fines/")),
        "reading space isolates other reader loans fines and notifications");
    Check(!(await readingSpace.GetAsync("unlinked-identity")).HasStudentProfile,
        "unlinked reading identity receives empty state without another reader data");

    var reviews = new ReviewService(db, clock, NullLogger<ReviewService>.Instance);
    await reviews.SaveAsync(new ReviewEditViewModel { BookId = reviewedBook.Id, Rating = 5, Comment = "First review" }, firstUser);
    var initialReview = await db.Reviews.AsNoTracking().SingleAsync();
    var firstVersion = Convert.ToBase64String(initialReview.RowVersion);
    var publicReviews = (await reviews.GetBookReviewsAsync(reviewedBook.Id, null, false, 1))!;
    Check(publicReviews.ReviewCount == 1 && publicReviews.AverageRating == 5 && publicReviews.Reviews.Single().ReviewerLabel == "Оқырман", "public rating and review count aggregate persisted SQL without private reader names");
    await Reject(() => reviews.SaveAsync(new ReviewEditViewModel { BookId = reviewedBook.Id, Rating = 4 }, firstUser), "duplicate active student review rejected");
    await Reject(() => reviews.SaveAsync(new ReviewEditViewModel { BookId = reviewedBook.Id, Rating = 0 }, secondUser), "out-of-range rating rejected before SQL writes");
    Check(await reviews.GetEditFormAsync(initialReview.Id, secondUser) is null, "foreign review edit form is scoped out");
    await Reject(() => reviews.SaveAsync(new ReviewEditViewModel { Id = initialReview.Id, BookId = reviewedBook.Id, Rating = 1, RowVersion = firstVersion }, secondUser), "foreign review mutation rejected by service ownership");
    await Reject(() => reviews.DeleteAsync(initialReview.Id, firstVersion, secondUser), "foreign review deletion rejected by service ownership");
    var edit = (await reviews.GetEditFormAsync(initialReview.Id, firstUser))!; edit.Rating = 4; edit.Comment = "Revised review";
    await reviews.SaveAsync(edit, firstUser);
    await Reject(() => reviews.SaveAsync(new ReviewEditViewModel { Id = edit.Id, BookId = edit.BookId, Rating = 1, RowVersion = firstVersion }, firstUser), "stale review rowversion rejects lost update");
    Check((await reviews.GetBookReviewsAsync(reviewedBook.Id, null, false, 1))!.AverageRating == 4, "stale review mutation leaves current SQL rating intact");
    edit = (await reviews.GetEditFormAsync(initialReview.Id, firstUser))!;
    await reviews.ModerateAsync(edit.Id, true, edit.RowVersion!);
    publicReviews = (await reviews.GetBookReviewsAsync(reviewedBook.Id, null, false, 1))!;
    Check(publicReviews.ReviewCount == 0 && publicReviews.AverageRating == 0, "hidden review is absent from SQL public count and average");
    edit = (await reviews.GetEditFormAsync(initialReview.Id, firstUser))!; edit.Rating = 1; edit.Comment = "Moderated correction";
    await reviews.SaveAsync(edit, firstUser);
    Check((await reviews.GetEditFormAsync(edit.Id, firstUser))!.IsHidden && (await reviews.GetModerationAsync(new() { Status = "hidden" })).TotalCount == 1,
        "student editing cannot bypass admin moderation");
    await Reject(() => reviews.SaveAsync(new ReviewEditViewModel { BookId = reviewedBook.Id, Rating = 2 }, firstUser), "hidden review still occupies single active review slot");
    edit = (await reviews.GetEditFormAsync(edit.Id, firstUser))!;
    await reviews.DeleteAsync(edit.Id, edit.RowVersion!, firstUser);
    await reviews.SaveAsync(new ReviewEditViewModel { BookId = reviewedBook.Id, Rating = 2, Comment = "Replacement after deletion" }, firstUser);
    Check(await db.Reviews.CountAsync(r => r.BookId == reviewedBook.Id && r.StudentId == first.Id) == 2
        && await db.Reviews.CountAsync(r => r.BookId == reviewedBook.Id && r.StudentId == first.Id && !r.IsDeleted) == 1,
        "soft deletion preserves old row and allows one replacement review");
    await reviews.SaveAsync(new ReviewEditViewModel { BookId = reviewedBook.Id, Rating = 4 }, secondUser);
    publicReviews = (await reviews.GetBookReviewsAsync(reviewedBook.Id, null, false, 1))!;
    Check(publicReviews.ReviewCount == 2 && publicReviews.AverageRating == 3, "SQL averages only two current visible reviews and excludes deleted row");
    db.Reviews.Add(new Review { BookId = reviewedBook.Id, StudentId = first.Id, Rating = 5, CreatedAt = clock.UtcNow });
    await RejectConstraint(() => db.SaveChangesAsync(), "SQL filtered unique index independently blocks duplicate active review");
    db.ChangeTracker.Clear();
    Check(await reviews.GetCreateFormAsync(archivedBook.Id, firstUser) is null, "archived book cannot receive a new student review");

    var lookup = new CopyLookupService(db);
    var availableCopy = availableBook.Copies.First();
    Check(await lookup.FindByCodeAsync("  " + availableCopy.Barcode + "  ") == availableCopy.Id, "barcode lookup trims scanner whitespace and resolves exact SQL copy");
    Check(await lookup.FindByCodeAsync(availableCopy.InventoryNumber) == availableCopy.Id, "manual inventory number resolves the same physical copy");
    Check(await lookup.FindByCodeAsync(availableCopy.Barcode.ToLowerInvariant()) == availableCopy.Id, "manual barcode casing respects configured SQL collation");
    Check(await lookup.FindByCodeAsync("NO-SUCH-FEATURE-CODE") is null, "unknown physical code does not invent a copy");
    var svg = await lookup.GetQrSvgAsync(availableCopy.Id) ?? throw new InvalidOperationException("QR SVG missing");
    var svgXml = XDocument.Parse(svg);
    Check(svgXml.Root?.Name.LocalName == "svg" && !svgXml.Descendants().Any(x => x.Name.LocalName is "script" or "foreignObject"), "QR service produces a valid passive SVG from maintained renderer");
    Check(svg != await lookup.GetQrSvgAsync(availableBook.Copies.Last().Id) && !svg.Contains(firstUser) && !svg.Contains(first.FullName), "distinct copy barcodes produce distinct QR matrices with no reader data");
    var activeCopyDetails = (await lookup.GetDetailsAsync(activeBook.Copies.First().Id))!;
    Check(activeCopyDetails.ActiveLoanId == activeLoanId && activeCopyDetails.CanReturn && !activeCopyDetails.CanIssue, "lookup exposes correct quick-return state from active SQL loan");
    Check((await lookup.GetDetailsAsync(availableCopy.Id))!.CanIssue && !(await lookup.GetDetailsAsync(archivedBook.Copies.First().Id))!.CanIssue,
        "quick-issue eligibility requires available copy and unarchived book");

    var recommendations = Recommendations(db);
    var coldResult = (await recommendations.GetAsync(coldUser, false))!;
    var coldBooks = coldResult.Sections.SelectMany(s => s.Books).DistinctBy(b => b.BookId).ToList();
    Check(coldResult.IsColdStart && coldBooks.Count > 0 && coldResult.Sections.Count == 6 && coldResult.ActualMode == "Local Recommendation Engine", "cold-start recommendations query SQL and use actual local explanation provider");
    Check(coldBooks.All(b => b.RelevanceScore is >= 0 and <= 100 && b.Breakdown.All(f => f.Value is >= 0 and <= 1))
        && coldBooks.All(b => !string.IsNullOrWhiteSpace(b.Explanation)), "SQL-backed recommendations expose normalized scores and deterministic explanations");
    Check(coldBooks.All(b => b.BookId != archivedBook.Id) && coldBooks.Single(b => b.BookId == reviewedBook.Id).AverageRating == 3
        && coldBooks.Single(b => b.BookId == reviewedBook.Id).ReviewCount == 2,
        "recommendation candidates exclude archived books and use public SQL review average");
    Check(await recommendations.GetAsync(firstUser, false, second.Id) is null, "student cannot select another student's recommendation profile");
    var staffSelector = (await recommendations.GetAsync(staff, true))!;
    Check(staffSelector.StudentId is null && staffSelector.Students.Count == 3 && staffSelector.Sections.Count == 0, "staff recommendation selector starts without exposing an arbitrary student's profile");
    Check((await recommendations.GetAsync(staff, true, second.Id))!.StudentId == second.Id, "authorized staff can inspect selected student recommendations");
    var ownResult = (await recommendations.GetAsync(firstUser, false))!;
    var ownBooks = ownResult.Sections.SelectMany(s => s.Books).DistinctBy(b => b.BookId).ToList();
    Check(ownBooks.All(b => b.BookId != activeBook.Id && b.BookId != previousBook.Id && b.BookId != reviewedBook.Id), "default recommendations exclude active borrowed previously read and reviewed books");
    var repeatResult = (await recommendations.GetAsync(firstUser, false, includePreviouslyRead: true))!;
    var repeatBooks = repeatResult.Sections.SelectMany(s => s.Books).DistinctBy(b => b.BookId).ToList();
    Check(repeatBooks.Any(b => b.BookId == previousBook.Id) && repeatBooks.All(b => b.BookId != activeBook.Id), "explicit previous-read opt-in still cannot recommend active borrowed book");
    recommendationOptions.Value.Llm.Enabled = true;
    recommendationOptions.Value.Llm.Model = "test-key-missing";
    var missingKeyResult = (await recommendations.GetAsync(coldUser, false))!;
    Check(missingKeyResult.ActualMode == "Local Recommendation Engine"
        && missingKeyResult.Sections.SelectMany(s => s.Books).All(b => !string.IsNullOrWhiteSpace(b.Explanation)),
        "enabled explanation provider without API key falls back to deterministic local explanations");
    recommendationOptions.Value.Llm.Enabled = false;
    Check(networkGuard.Calls == 0, "SQL recommendation checks perform no paid or external LLM requests");

    var physicsSubject = new Subject { Name = "Physics Basics" };
    db.BookSubjects.Add(new BookSubject { BookId = scienceBook.Id, Subject = physicsSubject });
    await db.SaveChangesAsync();
    var preferenceAdd = new RecommendationPreferencesViewModel
    {
        StudentId = cold.Id, Interests = "Physics, physics; Mathematics\nPHYSICS",
        SelectedSubjectIds = [subject.Id, physicsSubject.Id, subject.Id]
    };
    Check(await recommendations.SavePreferencesAsync(coldUser, false, preferenceAdd), "own preference form saves academic interests and selected subjects");
    db.ChangeTracker.Clear();
    var savedInterests = await db.StudentInterests.AsNoTracking().Where(i => i.StudentId == cold.Id).OrderBy(i => i.Name).Select(i => i.Name).ToListAsync();
    var savedSubjects = await db.StudentSubjects.AsNoTracking().Where(s => s.StudentId == cold.Id).OrderBy(s => s.SubjectId).Select(s => s.SubjectId).ToListAsync();
    Check(savedInterests.Count == 2 && savedInterests.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["Physics", "Mathematics"])
        && savedSubjects.Count == 2 && savedSubjects.Contains(subject.Id) && savedSubjects.Contains(physicsSubject.Id),
        "SQL preferences deduplicate case-insensitive interest names and repeated subject IDs");
    var preferenceForm = (await recommendations.GetPreferencesAsync(coldUser, false))!;
    Check(preferenceForm.StudentId == cold.Id && preferenceForm.SelectedSubjectIds.Count == 2 && preferenceForm.Interests!.Contains("Physics"),
        "own preference editor reloads persisted SQL selections");
    var personalized = (await recommendations.GetAsync(coldUser, false))!;
    var physicsRecommendation = personalized.Sections.SelectMany(s => s.Books).First(b => b.BookId == scienceBook.Id);
    Check(!personalized.IsColdStart && physicsRecommendation.InterestScore > 0 && physicsRecommendation.SubjectScore > 0
        && physicsRecommendation.RelevanceScore > coldBooks.Single(b => b.BookId == scienceBook.Id).RelevanceScore
        && personalized.Interests.Contains("Physics") && personalized.Subjects.Contains("Physics Basics"),
        "recommendations recompute real interest subject and relevance signals from newly saved preferences");
    var forbiddenSave = await recommendations.SavePreferencesAsync(coldUser, false, new RecommendationPreferencesViewModel
    { StudentId = first.Id, Interests = "Foreign overwrite", SelectedSubjectIds = [physicsSubject.Id] });
    db.ChangeTracker.Clear();
    Check(!forbiddenSave && await db.StudentInterests.CountAsync(i => i.StudentId == first.Id) == 1
        && await db.StudentInterests.AnyAsync(i => i.StudentId == first.Id && i.Name == "Python")
        && await db.StudentSubjects.CountAsync(s => s.StudentId == first.Id) == 1
        && await db.StudentSubjects.AnyAsync(s => s.StudentId == first.Id && s.SubjectId == subject.Id),
        "foreign profile preference save returns false and leaves the target student's SQL preferences unchanged");
    await Reject(() => recommendations.SavePreferencesAsync(coldUser, false, new RecommendationPreferencesViewModel
    { StudentId = cold.Id, Interests = "Must not persist", SelectedSubjectIds = [physicsSubject.Id, int.MaxValue] }),
        "unknown selected subject rejects the entire preference update");
    db.ChangeTracker.Clear();
    var afterInvalidInterests = await db.StudentInterests.AsNoTracking().Where(i => i.StudentId == cold.Id).OrderBy(i => i.Name).Select(i => i.Name).ToListAsync();
    var afterInvalidSubjects = await db.StudentSubjects.AsNoTracking().Where(s => s.StudentId == cold.Id).OrderBy(s => s.SubjectId).Select(s => s.SubjectId).ToListAsync();
    Check(afterInvalidInterests.SequenceEqual(savedInterests) && afterInvalidSubjects.SequenceEqual(savedSubjects),
        "invalid subject rollback preserves all previous SQL interests and subject links");
    var removedPreference = await recommendations.SavePreferencesAsync(coldUser, false, new RecommendationPreferencesViewModel
    { StudentId = cold.Id, Interests = "Physics", SelectedSubjectIds = [physicsSubject.Id] });
    db.ChangeTracker.Clear();
    Check(removedPreference && await db.StudentInterests.CountAsync(i => i.StudentId == cold.Id) == 1
        && await db.StudentInterests.AnyAsync(i => i.StudentId == cold.Id && i.Name == "Physics")
        && await db.StudentSubjects.CountAsync(s => s.StudentId == cold.Id) == 1
        && await db.StudentSubjects.AnyAsync(s => s.StudentId == cold.Id && s.SubjectId == physicsSubject.Id),
        "own preference update removes deselected interest and subject while retaining the chosen values");
    var clearedPreferences = await recommendations.SavePreferencesAsync(coldUser, false, new RecommendationPreferencesViewModel
    { StudentId = cold.Id, Interests = "", SelectedSubjectIds = [] });
    db.ChangeTracker.Clear();
    Check(clearedPreferences && !await db.StudentInterests.AnyAsync(i => i.StudentId == cold.Id)
        && !await db.StudentSubjects.AnyAsync(s => s.StudentId == cold.Id), "own preference clear removes all SQL interest and subject rows");
    var coldAgain = (await recommendations.GetAsync(coldUser, false))!;
    Check(coldAgain.IsColdStart && coldAgain.Interests.Count == 0 && coldAgain.Subjects.Count == 0
        && coldAgain.Sections.Single(s => s.Key == "interests").Books.Count == 0
        && coldAgain.Sections.Single(s => s.Key == "subjects").Books.Count == 0,
        "cleared preferences recompute cold-start recommendations with empty personal-match sections");

    var notifications = new NotificationService(db, clock);
    var firstPage = await notifications.GetAsync(firstUser);
    var secondPage = await notifications.GetAsync(secondUser);
    Check(firstPage.TotalCount == 4 && secondPage.TotalCount == 2 && !firstPage.Items.Select(n => n.Id).Intersect(secondPage.Items.Select(n => n.Id)).Any(), "notification pages isolate reader identities in SQL");
    var secondNotification = secondPage.Items.First();
    Check(!await notifications.MarkReadAsync(firstUser, secondNotification.Id)
        && await db.Notifications.AnyAsync(n => n.Id == secondNotification.Id && n.ReadAt == null), "foreign notification mark-read is rejected without changing its state");
    var ownNotification = firstPage.Items.First();
    Check(await notifications.MarkReadAsync(firstUser, ownNotification.Id) && await notifications.MarkReadAsync(firstUser, ownNotification.Id), "own notification mark-read is idempotent");
    Check(await notifications.MarkAllReadAsync(firstUser) == 3 && (await notifications.GetBellAsync(firstUser)).UnreadCount == 0
        && (await notifications.GetBellAsync(secondUser)).UnreadCount == 2, "mark-all-read updates only current reader and bell counts remain scoped");
    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    async Task<int> ReminderRace()
    {
        await using var context = NewDb(); await ready.Task; return await new NotificationService(context, clock).GenerateRemindersAsync();
    }
    var raceOne = ReminderRace(); var raceTwo = ReminderRace(); ready.SetResult();
    var reminderCounts = await Task.WhenAll(raceOne, raceTwo);
    Check(reminderCounts.Sum() == 3, "parallel reminder workers generate each due-1 due-3 overdue event exactly once");
    Check(await db.Notifications.CountAsync(n => n.Type == "Due1") == 1 && await db.Notifications.CountAsync(n => n.Type == "Due3") == 1
        && await db.Notifications.CountAsync(n => n.Type == "Overdue") == 1 && await notifications.GenerateRemindersAsync() == 0,
        "persisted reminder deduplication survives subsequent worker run");
    Check(!await db.Notifications.AnyAsync(n => (n.Type == "Due1" || n.Type == "Due3" || n.Type == "Overdue") && n.Link == "/Loans/Details/" + previousLoanId),
        "returned loan never receives active due-date reminder");

    // Inject a duplicate notification for the next generated loan identity. The
    // ensuing uniqueness failure happens after core writes, testing full rollback.
    int nextLoanId;
    await using (var sql = new SqlConnection(connection.ConnectionString))
    {
        await sql.OpenAsync(); await using var command = sql.CreateCommand(); command.CommandText = "SELECT CONVERT(int, IDENT_CURRENT(N'Loans')) + 1";
        nextLoanId = Convert.ToInt32(await command.ExecuteScalarAsync());
    }
    var conflictNotification = new Notification { UserId = firstUser, DeduplicationKey = "Issued:" + nextLoanId, Type = "Fixture", Title = "Atomicity fixture", Message = "Rollback check", Link = "/Notifications", CreatedAt = clock.UtcNow };
    db.Notifications.Add(conflictNotification); await db.SaveChangesAsync();
    var loansBefore = await db.Loans.CountAsync(); var noticesBefore = await db.Notifications.CountAsync();
    await Reject(() => circulation.IssueAsync(new IssueLoanViewModel { BookCopyId = availableCopy.Id, StudentId = first.Id, LoanDate = clock.Today, DueDate = clock.Today.AddDays(14) }, staff),
        "notification constraint failure rejects entire issue transaction");
    db.ChangeTracker.Clear();
    Check(await db.Loans.CountAsync() == loansBefore && await db.Notifications.CountAsync() == noticesBefore
        && await db.BookCopies.AnyAsync(c => c.Id == availableCopy.Id && c.Status == CopyStatus.Available), "failed issue notification rolls back loan copy and all new notifications");
    await db.Notifications.Where(n => n.Id == conflictNotification.Id).ExecuteDeleteAsync();

    var returnConflict = new Notification { UserId = firstUser, DeduplicationKey = "Returned:" + activeLoanId, Type = "Fixture", Title = "Return rollback fixture", Message = "Rollback check", Link = "/Notifications", CreatedAt = clock.UtcNow };
    db.Notifications.Add(returnConflict); await db.SaveChangesAsync();
    var finesBefore = await db.Fines.CountAsync(); var returnsBefore = await db.Returns.CountAsync();
    var activeReturn = (await circulation.GetReturnFormAsync(activeLoanId))!;
    await Reject(() => circulation.ReturnAsync(activeReturn, staff), "notification constraint failure rejects entire overdue return transaction");
    db.ChangeTracker.Clear();
    Check(await db.Fines.CountAsync() == finesBefore && await db.Returns.CountAsync() == returnsBefore
        && await db.Loans.AnyAsync(l => l.Id == activeLoanId && l.ReturnedAt == null)
        && await db.BookCopies.AnyAsync(c => c.Id == activeBook.Copies.First().Id && c.Status == CopyStatus.OnLoan),
        "failed return notification rolls back return fine loan and copy status together");
    await db.Notifications.Where(n => n.Id == returnConflict.Id).ExecuteDeleteAsync();
    activeReturn = (await circulation.GetReturnFormAsync(activeLoanId))!;
    await circulation.ReturnAsync(activeReturn, staff);
    Check(await db.Notifications.CountAsync(n => n.UserId == firstUser && n.DeduplicationKey == "Returned:" + activeLoanId) == 1
        && await db.Fines.AnyAsync(f => f.LoanId == activeLoanId && f.Amount == 200m), "successful retry commits exactly one return notification and final overdue fine");
    Console.WriteLine($"SUCCESS: {checks} SQL-backed feature checks passed.");
}
catch (Exception error) { failed = true; Console.Error.WriteLine(error); }
finally
{
    if (created)
    {
        try
        {
            if (connection.InitialCatalog != ownDatabase || !ownDatabase.StartsWith("EduLibraryFeatureChecks_", StringComparison.Ordinal)
                || ownDatabase.Length != "EduLibraryFeatureChecks_".Length + 32)
                throw new InvalidOperationException("Feature test database cleanup safety check failed.");
            await using var cleanup = NewDb(); await cleanup.Database.EnsureDeletedAsync();
            Console.WriteLine("Removed own feature test database: " + ownDatabase);
        }
        catch (Exception error) { failed = true; Console.Error.WriteLine("Feature test cleanup failed: " + error.Message); }
    }
}
return failed ? 1 : 0;

sealed class FeatureClock : ILibraryClock
{
    public DateTime Today => new(2026, 10, 20);
    public DateTime UtcNow => new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
}

sealed class NoNetworkHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++; throw new InvalidOperationException("External explanation provider must remain disabled in SQL feature checks.");
    }
}
