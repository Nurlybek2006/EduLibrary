using EduLibrary.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var ownDatabase = "EduLibraryDigitalChecks_" + Guid.NewGuid().ToString("N");
var source = Environment.GetEnvironmentVariable("EDULIBRARY_TEST_CONNECTION_STRING") ?? "Server=.;Trusted_Connection=True;TrustServerCertificate=True";
var connection = new SqlConnectionStringBuilder(source) { InitialCatalog = ownDatabase, Pooling = false, ConnectTimeout = 15 };
var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options;
var ownFolder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "EduLibraryDigitalChecks_" + Guid.NewGuid().ToString("N")));
var created = false; var failed = false;
try
{
    await using (var master = new SqlConnection(new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master" }.ConnectionString))
    {
        await master.OpenAsync(); await using var command = master.CreateCommand(); command.CommandText = $"CREATE DATABASE [{ownDatabase}]";
        await command.ExecuteNonQueryAsync(); created = true;
    }
    Directory.CreateDirectory(ownFolder);
    Console.WriteLine("Isolated digital database: " + ownDatabase);
    await using (var db = new ApplicationDbContext(dbOptions)) await db.Database.MigrateAsync();
    var fixture = new DigitalFixture(dbOptions, ownFolder);
    await fixture.SeedAsync();
    await DigitalScenarios.RunAsync(fixture);
    await DigitalDemoSeedScenarios.RunAsync(fixture);
    await AvatarSqlScenarios.RunAsync(dbOptions, ownFolder, fixture.Clock, fixture.Check);
    await NotificationLocalizationScenarios.RunAsync(fixture);
    Console.WriteLine($"SUCCESS: {fixture.CheckCount} SQL-backed digital checks passed.");
}
catch (Exception error) { failed = true; Console.Error.WriteLine(error); }
finally
{
    if (created)
    {
        try
        {
            if (connection.InitialCatalog != ownDatabase || !System.Text.RegularExpressions.Regex.IsMatch(ownDatabase, "^EduLibraryDigitalChecks_[a-f0-9]{32}$")) throw new InvalidOperationException("Database cleanup guard failed.");
            await using var db = new ApplicationDbContext(dbOptions); await db.Database.EnsureDeletedAsync();
            Console.WriteLine("Removed own digital database: " + ownDatabase);
        }
        catch (Exception error) { failed = true; Console.Error.WriteLine("Own database cleanup failed: " + error.Message); }
    }
    if (Directory.Exists(ownFolder))
    {
        var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!ownFolder.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(ownFolder), "^EduLibraryDigitalChecks_[a-f0-9]{32}$")) throw new InvalidOperationException("Storage cleanup guard failed.");
        Directory.Delete(ownFolder, recursive: true);
    }
}
return failed ? 1 : 0;
