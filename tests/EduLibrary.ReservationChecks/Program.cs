using EduLibrary.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Caller configuration may select a server/authentication method, never the test
// catalog. Ownership is recorded immediately after CREATE DATABASE succeeds.
var source = Environment.GetEnvironmentVariable("EDULIBRARY_TEST_CONNECTION_STRING")
    ?? "Server=.;Trusted_Connection=True;TrustServerCertificate=True";
var ownDatabase = "EduLibraryReservationChecks_" + Guid.NewGuid().ToString("N");
var connection = new SqlConnectionStringBuilder(source)
{
    InitialCatalog = ownDatabase, Pooling = false, ConnectTimeout = 15, MultipleActiveResultSets = false
};
var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options;
var ownsDatabase = false;
var failed = false;
try
{
    var masterConnection = new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master" };
    await using (var master = new SqlConnection(masterConnection.ConnectionString))
    {
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{ownDatabase}]";
        await command.ExecuteNonQueryAsync();
        ownsDatabase = true;
    }
    Console.WriteLine("Isolated reservation database: " + ownDatabase);
    await using (var db = new ApplicationDbContext(dbOptions))
    {
        await db.Database.MigrateAsync();
        if ((await db.Database.GetPendingMigrationsAsync()).Any()) throw new InvalidOperationException("Pending reservation migrations remain.");
    }
    var fixture = new ReservationFixture(dbOptions);
    await fixture.SeedAsync();
    await ReservationScenarios.RunAsync(fixture);
    Console.WriteLine($"SUCCESS: {fixture.CheckCount} SQL-backed reservation checks passed.");
}
catch (Exception error)
{
    failed = true;
    Console.Error.WriteLine(error);
}
finally
{
    if (ownsDatabase)
    {
        try
        {
            if (connection.InitialCatalog != ownDatabase
                || !ownDatabase.StartsWith("EduLibraryReservationChecks_", StringComparison.Ordinal)
                || ownDatabase.Length != "EduLibraryReservationChecks_".Length + 32)
                throw new InvalidOperationException("Reservation database cleanup safety check failed.");
            await using var cleanup = new ApplicationDbContext(dbOptions);
            await cleanup.Database.EnsureDeletedAsync();
            Console.WriteLine("Removed own reservation database: " + ownDatabase);
        }
        catch (Exception error)
        {
            failed = true;
            Console.Error.WriteLine("Reservation database cleanup failed: " + error.Message);
        }
    }
}
return failed ? 1 : 0;
