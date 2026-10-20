using System.Text.Json;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Data.Seed;

/// <summary>Registers only original, locally shipped demo content; never attaches files to commercial books.</summary>
public static class DigitalDemoSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var storage = services.GetRequiredService<IDigitalAssetStorage>();
        var clock = services.GetRequiredService<ILibraryClock>();
        var sourceRoot = Path.Combine(environment.ContentRootPath, "Data", "DigitalDemo");
        var manifestPath = Path.Combine(sourceRoot, "manifest.json");
        if (!File.Exists(manifestPath)) throw new InvalidOperationException("The original digital demo manifest is missing.");
        var manifest = JsonSerializer.Deserialize<List<DemoBook>>(await File.ReadAllTextAsync(manifestPath, ct),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("The digital demo manifest is invalid.");
        var staffId = await (from user in db.Users
            join membership in db.UserRoles on user.Id equals membership.UserId
            join role in db.Roles on membership.RoleId equals role.Id
            where role.Name == RoleNames.Admin || role.Name == RoleNames.Librarian
            orderby role.Name, user.Id
            select user.Id).FirstOrDefaultAsync(ct);
        if (staffId is null) return;
        const string seedKey = "original-digital-textbooks-v1";
        if (await db.SeedHistory.AnyAsync(row => row.Key == seedKey, ct)) return;
        var storedFiles = new List<string>();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var author = await db.Authors.SingleOrDefaultAsync(row => row.FirstName == "EduLibrary" && row.LastName == "Demo Content", ct);
            if (author is null) { author = new Author { FirstName="EduLibrary", LastName="Demo Content", Biography="Original short educational materials created for the EduLibrary demonstration." }; db.Authors.Add(author); }
            var publisher = await db.Publishers.SingleOrDefaultAsync(row => row.Name == "EduLibrary Demo", ct);
            if (publisher is null) { publisher = new Publisher { Name="EduLibrary Demo", Country="Қазақстан" }; db.Publishers.Add(publisher); }
            foreach (var item in manifest)
            {
                var book = await db.Books.SingleOrDefaultAsync(row => row.ISBN == item.Isbn, ct);
                if (book is null)
                {
                    var category = db.Categories.Local.FirstOrDefault(row => row.Name == item.Category)
                        ?? await db.Categories.SingleOrDefaultAsync(row => row.Name == item.Category, ct);
                    if (category is null) { category = new Category { Name=item.Category, Description="Түпнұсқа оқу материалдары" }; db.Categories.Add(category); }
                    var subject = db.Subjects.Local.FirstOrDefault(row => row.Name == item.Subject)
                        ?? await db.Subjects.SingleOrDefaultAsync(row => row.Name == item.Subject, ct);
                    if (subject is null) { subject = new Subject { Name=item.Subject }; db.Subjects.Add(subject); }
                    book = new Book
                    {
                        ISBN=item.Isbn,Title=item.Title,Description=item.Description,Language="Қазақша",PublicationYear=2026,
                        PageCount=item.PageCount,CoverImageUrl=item.CoverImageUrl,Publisher=publisher,CreatedAt=clock.UtcNow,UpdatedAt=clock.UtcNow,
                        BookAuthors=[new BookAuthor {Author=author}],BookCategories=[new BookCategory {Category=category}],BookSubjects=[new BookSubject {Subject=subject}]
                    };
                    db.Books.Add(book);
                    await db.SaveChangesAsync(ct);
                }
                foreach (var file in item.Files)
                {
                    var format = file.Format == "PDF" ? DigitalFileFormat.Pdf : DigitalFileFormat.Epub;
                    if (await db.DigitalBookAssets.AnyAsync(asset => asset.BookId == book.Id && asset.FileFormat == format, ct)) continue;
                    if (Path.GetFileName(file.FileName) != file.FileName) throw new InvalidOperationException("Unsafe original-demo manifest filename.");
                    var path = Path.Combine(sourceRoot, file.FileName);
                    await using var input = File.OpenRead(path);
                    var digest = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input, ct)).ToLowerInvariant();
                    if (!string.Equals(digest, file.Sha256, StringComparison.Ordinal)) throw new InvalidOperationException("Original demo checksum mismatch.");
                    input.Position = 0;
                    var upload = new FormFile(input,0,input.Length,"File",file.FileName)
                    { Headers=new HeaderDictionary(), ContentType=format == DigitalFileFormat.Pdf ? "application/pdf" : "application/epub+zip" };
                    var saved = await storage.SaveAsync(upload,format,ct);
                    storedFiles.Add(saved.StoragePath);
                    db.DigitalBookAssets.Add(new DigitalBookAsset
                    {
                        Book=book,FileName=Path.GetFileName(saved.StoragePath),OriginalFileName=saved.OriginalFileName,StoragePath=saved.StoragePath,
                        FileFormat=format,FileSize=saved.FileSize,MimeType=saved.MimeType,Checksum=saved.Checksum,PageCount=saved.PageCount,Language="Қазақша",
                        LicenseType=DigitalLicenseType.Original,LicenseName="Creative Commons Attribution 4.0 International",LicenseUrl="https://creativecommons.org/licenses/by/4.0/",
                        SourceUrl="/digital-demo/provenance.html",RightsHolder="EduLibrary Demo Content",RightsConfirmed=true,AccessLevel=DigitalAccessLevel.Authenticated,
                        AllowDownload=true,IsPublished=true,UploadedByUserId=staffId,CreatedAt=clock.UtcNow,UpdatedAt=clock.UtcNow
                    });
                    await db.SaveChangesAsync(ct);
                }
            }
            db.SeedHistory.Add(new SeedHistory { Key=seedKey,AppliedAt=clock.UtcNow });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            foreach (var file in storedFiles) await storage.DeleteAsync(file, CancellationToken.None);
            throw;
        }
    }

    private sealed class DemoBook
    {
        public DemoBook() { }
        public string Title {get;set;}=""; public string Isbn {get;set;}=""; public string Description {get;set;}="";
        public string Category {get;set;}=""; public string Subject {get;set;}=""; public string CoverImageUrl {get;set;}="";
        public int PageCount {get;set;} public List<DemoFile> Files {get;set;}=[];
    }
    private sealed class DemoFile { public DemoFile() { } public string Format {get;set;}=""; public string FileName {get;set;}=""; public string Sha256 {get;set;}=""; }
}
