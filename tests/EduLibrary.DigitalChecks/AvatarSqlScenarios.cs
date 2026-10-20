using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Services.Implementations;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

internal static class AvatarSqlScenarios
{
    public static async Task RunAsync(DbContextOptions<ApplicationDbContext> options, string contentRoot, ILibraryClock clock, Action<bool, string> check)
    {
        var prefix = Guid.NewGuid().ToString("N");
        var firstUser = "avatar-a-" + prefix;
        var secondUser = "avatar-b-" + prefix;
        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.AddRange(User(firstUser), User(secondUser));
            await db.SaveChangesAsync();
        }
        var storage = new AvatarStorage(new TestEnvironment(contentRoot), NullLogger<AvatarStorage>.Instance);
        AccountService Service(ApplicationDbContext db, IAvatarStorage? files = null) => new(db, null!, null!, clock, NullLogger<AccountService>.Instance, files ?? storage);
        async Task<string?> PathFor(string userId)
        {
            await using var db = new ApplicationDbContext(options);
            return await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.AvatarPath).SingleAsync();
        }
        bool Exists(string? url) => url is not null && storage.TryGetPhysicalPath(url[AvatarStorage.UrlPrefix.Length..], out _);

        await using (var db = new ApplicationDbContext(options))
            check((await Service(db).UpdateAvatarAsync(firstUser, Form())).Succeeded, "avatar: real account upload succeeds");
        var original = await PathFor(firstUser);
        check(AvatarStorage.IsSafeUrl(original) && Exists(original), "avatar: SQL stores an opaque route backed by a private generated file");
        check(await PathFor(secondUser) is null, "avatar: upload leaves unrelated account unchanged");
        using (var decoded = SKBitmap.Decode(File.ReadAllBytes(Physical(original!))))
            check(decoded.Width == 256 && decoded.Height == 256, "avatar: persisted file is normalized to 256 square pixels");

        await using (var db = new ApplicationDbContext(options))
        {
            var invalid = await Service(db).UpdateAvatarAsync(firstUser, new() { File = Upload("<svg><script>unsafe</script></svg>"u8.ToArray()) });
            check(!invalid.Succeeded, "avatar: disguised active content is rejected by account service");
        }
        check(await PathFor(firstUser) == original && Exists(original), "avatar: invalid replacement preserves prior SQL reference and image");
        await using (var db = new ApplicationDbContext(options))
            check(!(await Service(db).UpdateAvatarAsync(firstUser, new() { File = Upload(new byte[AvatarImageProcessor.MaxBytes + 1]) })).Succeeded, "avatar: oversized account upload is rejected");

        await using (var db = new ApplicationDbContext(options))
            check((await Service(db).UpdateAvatarAsync(firstUser, Form())).Succeeded, "avatar: replacement succeeds");
        var replacement = await PathFor(firstUser);
        check(replacement != original && Exists(replacement) && !Exists(original), "avatar: replacement commits new reference before removing old file");
        await using (var db = new ApplicationDbContext(options))
            check((await Service(db).RemoveAvatarAsync(secondUser)).Succeeded, "avatar: independent account can remove its own empty avatar");
        check(await PathFor(firstUser) == replacement && Exists(replacement), "avatar: other account removal cannot touch owner's photo");

        // Both requests capture the same old SQL stamp before either candidate
        // is returned by storage. Exactly one conditional update may succeed.
        var racingStorage = new RendezvousStorage(storage);
        async Task<AccountActionResult> Replace()
        {
            await using var db = new ApplicationDbContext(options);
            return await Service(db, racingStorage).UpdateAvatarAsync(firstUser, Form());
        }
        var attempts = await Task.WhenAll(Replace(), Replace());
        var winner = await PathFor(firstUser);
        check(attempts.Count(r => r.Succeeded) == 1, "avatar: two-context replacement race has exactly one SQL winner");
        check(Exists(winner) && !Exists(replacement), "avatar: winning image remains valid and former image is removed");
        check(racingStorage.Uploaded.Count(url => Exists(url)) == 1 && racingStorage.Uploaded.Contains(winner!), "avatar: losing concurrent upload is cleaned without deleting winning image");

        await using (var db = new ApplicationDbContext(options))
        {
            await db.Users.Where(u => u.Id == secondUser).ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnabled, true).SetProperty(u => u.LockoutEnd, clock.UtcNow.AddDays(1)));
            check(!(await Service(db).UpdateAvatarAsync(secondUser, Form())).Succeeded, "avatar: locked account cannot upload through service");
        }
        await using (var db = new ApplicationDbContext(options))
            check(!(await Service(db).UpdateAvatarAsync("missing-avatar-user-" + prefix, Form())).Succeeded, "avatar: missing account cannot create a stored upload");
        await using (var db = new ApplicationDbContext(options))
            check((await Service(db).RemoveAvatarAsync(firstUser)).Succeeded, "avatar: removal succeeds against real SQL account");
        check(await PathFor(firstUser) is null && !Exists(winner), "avatar: removal clears SQL path and deletes prior file");
        await using (var db = new ApplicationDbContext(options))
            check((await Service(db).RemoveAvatarAsync(firstUser)).Succeeded, "avatar: repeated removal remains harmless");

        string Physical(string url)
        {
            if (!storage.TryGetPhysicalPath(url[AvatarStorage.UrlPrefix.Length..], out var path)) throw new InvalidOperationException("Expected test avatar is missing.");
            return path;
        }
    }

    private static ApplicationUser User(string id) => new()
    {
        Id = id, UserName = id + "@example.invalid", Email = id + "@example.invalid", NormalizedUserName = (id + "@example.invalid").ToUpperInvariant(),
        NormalizedEmail = (id + "@example.invalid").ToUpperInvariant(), FullName = "Avatar Test Reader", SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
    };

    private static AvatarUploadViewModel Form()
    {
        using var surface = SKSurface.Create(new SKImageInfo(40, 20));
        surface.Canvas.Clear(SKColors.Teal);
        using var image = surface.Snapshot();
        using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
        return new() { File = Upload(bytes.ToArray()) };
    }

    private static IFormFile Upload(byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "File", "../../original.png")
        { Headers = new HeaderDictionary(), ContentType = "image/png" };

    private sealed class RendezvousStorage(IAvatarStorage inner) : IAvatarStorage
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int count;
        public List<string> Uploaded { get; } = [];
        public async Task<string> SaveAsync(AvatarUploadViewModel model, CancellationToken ct = default)
        {
            var url = await inner.SaveAsync(model, ct);
            lock (Uploaded) Uploaded.Add(url);
            if (Interlocked.Increment(ref count) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            return url;
        }
        public void Delete(string? path) => inner.Delete(path);
        public bool TryGetPhysicalPath(string? id, out string path) => inner.TryGetPhysicalPath(id, out path);
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "EduLibrary.DigitalChecks";
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
