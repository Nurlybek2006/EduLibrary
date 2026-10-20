using System.IO.Compression;
using EduLibrary.Data;
using EduLibrary.Data.Seed;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class DigitalDemoSeedScenarios
{
    public static async Task RunAsync(DigitalFixture f)
    {
        int booksBefore, assetsBefore, copiesBefore, loansBefore;
        await using (var db = f.NewDb())
        {
            booksBefore = await db.Books.CountAsync(); assetsBefore = await db.DigitalBookAssets.CountAsync();
            copiesBefore = await db.BookCopies.CountAsync(); loansBefore = await db.Loans.CountAsync();
        }
        var filesBefore = Directory.EnumerateFiles(f.OwnFolder).Count();
        await using var provider = new ServiceCollection()
            .AddScoped<ApplicationDbContext>(_ => f.NewDb())
            .AddSingleton<IWebHostEnvironment>(new DigitalEnvironment { ContentRootPath = f.Workspace, WebRootPath = Path.Combine(f.Workspace, "wwwroot") })
            .AddSingleton<IDigitalAssetStorage>(f.Storage).AddSingleton<ILibraryClock>(f.Clock).BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope()) await DigitalDemoSeeder.SeedAsync(scope.ServiceProvider);
        await using (var db = f.NewDb())
        {
            f.Check(await db.Books.CountAsync() == booksBefore + 5 && await db.DigitalBookAssets.CountAsync() == assetsBefore + 10,
                "original demo seeder registers exactly five real books and ten PDF/EPUB assets");
            f.Check(await db.BookCopies.CountAsync() == copiesBefore && await db.Loans.CountAsync() == loansBefore,
                "original digital seeding preserves physical copies and circulation records");
            var assets = await db.DigitalBookAssets.AsNoTracking().Where(a => a.Book.Publisher.Name == "EduLibrary Demo").ToListAsync();
            f.Check(assets.All(a => a.IsPublished && a.RightsConfirmed && a.LicenseType == DigitalLicenseType.Original
                && a.SourceUrl == "/digital-demo/provenance.html" && a.LicenseUrl == "https://creativecommons.org/licenses/by/4.0/" && a.RightsHolder == "EduLibrary Demo Content"),
                "every seeded digital edition records genuine original-content provenance and confirmed publication rights");
            f.Check(assets.Count(a => a.FileFormat == DigitalFileFormat.Pdf && a.PageCount == 8) == 5
                && assets.GroupBy(a => a.BookId).All(g => g.Count() == 2 && g.Select(a => a.FileFormat).Distinct().Count() == 2),
                "each original demo book has a validated eight-page PDF and a matching EPUB");
            foreach (var asset in assets)
            {
                await using var stream = await f.Storage.OpenAsync(asset.StoragePath);
                if (stream is null || stream.Length != asset.FileSize) throw new InvalidOperationException("Seeded asset has no matching protected file.");
                if (asset.FileFormat == DigitalFileFormat.Epub)
                {
                    using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                    if (zip.GetEntry("mimetype") is null || zip.GetEntry("META-INF/container.xml") is null) throw new InvalidOperationException("Seeded EPUB is not a valid passive package.");
                }
            }
            f.Check(Directory.EnumerateFiles(f.OwnFolder).Count() == filesBefore + 10, "all ten seeded editions have actual protected files and no staged-file leaks");
        }
        await using (var scope = provider.CreateAsyncScope()) await DigitalDemoSeeder.SeedAsync(scope.ServiceProvider);
        await using (var db = f.NewDb())
        {
            f.Check(await db.Books.CountAsync() == booksBefore + 5 && await db.DigitalBookAssets.CountAsync() == assetsBefore + 10
                && Directory.EnumerateFiles(f.OwnFolder).Count() == filesBefore + 10
                && await db.SeedHistory.CountAsync(s => s.Key == "original-digital-textbooks-v1") == 1,
                "a second independent seeder run creates no duplicate books assets files or seed markers");
        }
    }
}
