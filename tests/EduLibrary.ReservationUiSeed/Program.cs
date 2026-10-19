using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// This utility can only seed a caller-owned, already existing reservation UI
// database. It deliberately has no database creation, migration or cleanup path.
if (args.Length != 1 || !Regex.IsMatch(args[0], @"\AEduLibraryReservationUi_[a-f0-9]{32}\z", RegexOptions.CultureInvariant))
{
    Console.Error.WriteLine("Expected one isolated database name: EduLibraryReservationUi_<32 lowercase hex characters>.");
    return 2;
}

var databaseName = args[0];
var root = FindWorkspace();
if (root is null || !File.ReadAllLines(Path.Combine(root, ".gitignore")).Any(line => line.Trim() == "artifacts/"))
{
    Console.Error.WriteLine("The workspace must ignore artifacts/ before fixture credentials can be written.");
    return 2;
}
var outputPath = Path.Combine(root, "artifacts", "reservation-ui-fixture.json");
var source = Environment.GetEnvironmentVariable("EDULIBRARY_TEST_CONNECTION_STRING")
    ?? "Server=.;Trusted_Connection=True;TrustServerCertificate=True";
var connection = new SqlConnectionStringBuilder(source)
{
    InitialCatalog = databaseName, Pooling = false, ConnectTimeout = 15, MultipleActiveResultSets = false
};
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options;
var stage = "open isolated database";
try
{
    await using var db = new ApplicationDbContext(options);
    await db.Database.OpenConnectionAsync();
    await using (var verify = db.Database.GetDbConnection().CreateCommand())
    {
        verify.CommandText = "SELECT DB_NAME()";
        var actualDatabase = (string?)await verify.ExecuteScalarAsync();
        if (!string.Equals(actualDatabase, databaseName, StringComparison.Ordinal))
            throw new InvalidOperationException("The connected database is not the requested isolated target.");
    }
    stage = "verify existing schema and seeded lookup rows";
    if ((await db.Database.GetPendingMigrationsAsync()).Any())
        throw new InvalidOperationException("The isolated database needs the shipped migrations before seeding.");
    var studentRole = await db.Roles.AsNoTracking().SingleAsync(role => role.Name == RoleNames.Student);
    var publisherId = await db.Publishers.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).FirstAsync();
    var authorId = await db.Authors.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).FirstAsync();
    var categoryId = await db.Categories.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).FirstAsync();
    var now = DateTime.UtcNow;
    var fixtureKey = Guid.NewGuid().ToString("N");
    var hasher = new PasswordHasher<ApplicationUser>();
    var students = new List<StudentFixture>();
    stage = "seed two fresh students and one available copy";
    await using var transaction = await db.Database.BeginTransactionAsync();
    for (var index = 1; index <= 2; index++)
    {
        var email = $"reservation-ui-{fixtureKey}-{index}@example.test";
        var password = "EdU!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant() + "9a";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true,
            FullName = $"Бронь сынақ оқырманы {index}", SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true, LockoutEnd = null,
            AccessFailedCount = 0, TwoFactorEnabled = false
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        var student = new Student
        {
            StudentNumber = $"RUI-{fixtureKey}-{index}", FullName = user.FullName, Email = email,
            PhoneNumber = "", Faculty = "Ақпараттық технологиялар", Course = 1,
            GroupName = "RUI-101", IdentityUserId = user.Id, CreatedAt = now, IsActive = true
        };
        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = studentRole.Id });
        db.Students.Add(student);
        await db.SaveChangesAsync();
        students.Add(new StudentFixture(email, password, student.Id, user.Id));
    }
    string isbn;
    do { isbn = NewIsbn(); } while (await db.Books.AnyAsync(book => book.ISBN == isbn));
    var copy = new BookCopy
    {
        InventoryNumber = "RUI-" + fixtureKey, Barcode = "RUIQR" + fixtureKey,
        Status = CopyStatus.Available, ShelfLocation = "RUI-01", Condition = "Жақсы", AcquiredAt = now.Date
    };
    var book = new Book
    {
        ISBN = isbn, Title = "Алып кету сынағы · " + fixtureKey[..8],
        Description = "Бронь, кезек және кітапханадан алып кету ағынын оқшауланған ортада тексеруге арналған кітап.",
        PublicationYear = 2026, Language = "Қазақша", PageCount = 240,
        PublisherId = publisherId, CreatedAt = now, UpdatedAt = now, IsArchived = false,
        BookAuthors = [new BookAuthor { AuthorId = authorId }],
        BookCategories = [new BookCategory { CategoryId = categoryId }], Copies = [copy]
    };
    db.Books.Add(book);
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    stage = "write ignored fixture JSON";
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    var json = JsonSerializer.Serialize(new { database = databaseName, bookId = book.Id, bookCopyId = copy.Id, students, createdAtUtc = now },
        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    var temporaryPath = outputPath + ".tmp";
    await File.WriteAllTextAsync(temporaryPath, json);
    File.Move(temporaryPath, outputPath, overwrite: true);
    Console.WriteLine("Reservation UI fixture written to ignored artifacts/reservation-ui-fixture.json; credentials were not printed.");
    return 0;
}
catch (Exception error)
{
    // Do not print exception data, SQL parameters, user credentials or hashes.
    Console.Error.WriteLine($"Reservation UI fixture failed during '{stage}' ({error.GetType().Name}). Credentials were not printed.");
    return 1;
}

static string? FindWorkspace()
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "EduLibrary.csproj")) && File.Exists(Path.Combine(directory.FullName, ".gitignore")))
            return directory.FullName;
    return null;
}

static string NewIsbn()
{
    var prefix = "978" + string.Concat(Enumerable.Range(0, 9).Select(_ => RandomNumberGenerator.GetInt32(10).ToString()));
    var sum = prefix.Select((digit, index) => (digit - '0') * (index % 2 == 0 ? 1 : 3)).Sum();
    return prefix + ((10 - sum % 10) % 10);
}

internal sealed record StudentFixture(string Email, string Password, int StudentId, string UserId);
