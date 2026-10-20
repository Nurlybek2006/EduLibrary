using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Implementations;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal sealed class DigitalFixture(DbContextOptions<ApplicationDbContext> dbOptions, string ownFolder)
{
    public const string Staff = "digital-check-staff", Reader = "digital-check-reader", OtherReader = "digital-check-other-reader";
    public DbContextOptions<ApplicationDbContext> DbOptions => dbOptions;
    public string OwnFolder => ownFolder;
    public string Workspace { get; } = FindWorkspace();
    public DigitalClock Clock { get; } = new();
    public IOptions<DigitalLibraryOptions> Settings { get; } = Options.Create(new DigitalLibraryOptions { StorageRoot = ownFolder, MaxBookmarksPerAsset = 4 });
    public DigitalAssetStorage Storage { get; private set; } = null!;
    public int[] Books { get; private set; } = [];
    public int CheckCount { get; private set; }
    public ApplicationDbContext NewDb() => new(dbOptions);
    public DigitalLibraryService Library(ApplicationDbContext db) => new(db, Storage, Settings, Clock, NullLogger<DigitalLibraryService>.Instance);
    public ReadingProgressService Progress(ApplicationDbContext db) => new(db, Library(db), Settings, Clock);
    public void Check(bool value, string label) { if (!value) throw new InvalidOperationException("FAILED: " + label); CheckCount++; Console.WriteLine("PASS " + label); }
    public async Task Reject(Func<Task> action, string label) { try { await action(); } catch (BusinessException) { Check(true, label); return; } throw new InvalidOperationException("Expected business rejection: " + label); }
    public async Task SeedAsync()
    {
        Storage = new(new DigitalEnvironment { ContentRootPath = Workspace, WebRootPath = Path.Combine(Workspace, "wwwroot") }, Settings);
        await using var db = NewDb();
        db.Roles.AddRange(new IdentityRole { Id = "digital-role-staff", Name = RoleNames.Librarian, NormalizedName = "LIBRARIAN" }, new IdentityRole { Id = "digital-role-reader", Name = RoleNames.Student, NormalizedName = "STUDENT" });
        foreach (var id in new[] { Staff, Reader, OtherReader })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), FullName = id, SecurityStamp = Guid.NewGuid().ToString() });
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = id == Staff ? "digital-role-staff" : "digital-role-reader" });
            if (id != Staff) db.Students.Add(new Student { StudentNumber = id, FullName = id, Email = id + "@example.test", Faculty = "Test", Course = 1, GroupName = "D-01", IdentityUserId = id, IsActive = true, CreatedAt = Clock.UtcNow });
        }
        var publisher = new Publisher { Name = "Digital Checks Press", Country = "Test" };
        var books = Enumerable.Range(1, 3).Select(i => new Book { Title = "Original Digital Lesson " + i, ISBN = "9780014001" + i.ToString("000"), Description = "Own isolated digital fixture", Language = "English", PublicationYear = 2026,
            PageCount = 8, Publisher = publisher, CreatedAt = Clock.UtcNow, UpdatedAt = Clock.UtcNow, Copies = [new BookCopy { InventoryNumber = "DIG-" + i, Barcode = "DIGCODE" + i, Status = CopyStatus.Available, ShelfLocation = "D-01", Condition = "Good", AcquiredAt = Clock.Today }] }).ToList();
        db.Books.AddRange(books); await db.SaveChangesAsync(); Books = books.Select(b => b.Id).ToArray();
        Check(!db.Database.HasPendingModelChanges(), "isolated SQL database has current model migrations");
    }
    public DigitalAssetEditViewModel Metadata(int bookIndex = 0, DigitalFileFormat format = DigitalFileFormat.Pdf) => new()
    {
        BookId = Books[bookIndex], FileFormat = format, Language = "Қазақша", LicenseType = DigitalLicenseType.Original,
        LicenseName = "Creative Commons Attribution 4.0", LicenseUrl = "https://creativecommons.org/licenses/by/4.0/", SourceUrl = "/digital-demo/provenance.html",
        RightsHolder = "EduLibrary Demo Content", RightsConfirmed = true, IsPublished = true, AllowDownload = true, AccessLevel = DigitalAccessLevel.Authenticated
    };
    public async Task<int> UploadAsync(DigitalAssetEditViewModel model)
    {
        var path = Path.Combine(Workspace, "Data", "DigitalDemo", "csharp-basics" + DigitalFileRules.Extension(model.FileFormat));
        await using var stream = File.OpenRead(path);
        model.File = new FormFile(stream, 0, stream.Length, "File", Path.GetFileName(path)) { Headers = new HeaderDictionary(), ContentType = DigitalFileRules.MimeType(model.FileFormat) };
        await using var db = NewDb(); return await Library(db).SaveAsync(model, Staff);
    }
    private static string FindWorkspace()
    {
        for (var path = new DirectoryInfo(Directory.GetCurrentDirectory()); path is not null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "EduLibrary.csproj"))) return path.FullName;
        throw new InvalidOperationException("Run the digital checks from the workspace.");
    }
}
internal sealed class DigitalClock : ILibraryClock
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 2, 4, 0, 0, DateTimeKind.Utc);
    public DateTime Today => UtcNow.AddHours(5).Date;
}
internal sealed class DigitalEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "EduLibrary";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
