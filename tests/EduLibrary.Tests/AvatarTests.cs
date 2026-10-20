using System.Buffers.Binary;
using System.Text;
using EduLibrary.Helpers;
using EduLibrary.Services.Implementations;
using EduLibrary.ViewComponents;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace EduLibrary.Tests;

public sealed class AvatarTests
{
    private static byte[] Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int width = 40, int height = 20)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Red);
        using var blue = new SKPaint { Color = SKColors.Blue };
        surface.Canvas.DrawRect(width / 2f, 0, width / 2f, height, blue);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(format, 95);
        return encoded.ToArray();
    }

    private static IFormFile Upload(byte[] bytes, string name = "photo.png", string mime = "image/png") => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "File", name)
        { Headers = new HeaderDictionary(), ContentType = mime };

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, "photo.jpg", "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Jpeg, "photo.jpeg", "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Png, "photo.png", "image/png")]
    [InlineData(SKEncodedImageFormat.Webp, "photo.webp", "image/webp")]
    public async Task Supported_rasters_are_decoded_and_reencoded_as_single_square_webp(SKEncodedImageFormat format, string name, string mime)
    {
        var output = await AvatarImageProcessor.ProcessAsync(new() { File = Upload(Image(format), name, mime) });
        using var data = SKData.CreateCopy(output);
        using var codec = SKCodec.Create(data);
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        Assert.Equal(256, codec.Info.Width);
        Assert.Equal(256, codec.Info.Height);
        Assert.InRange(output.Length, 1, AvatarImageProcessor.MaxBytes);
    }

    [Fact]
    public async Task Crop_uses_server_validated_coordinates_instead_of_storing_original_bytes()
    {
        var output = await AvatarImageProcessor.ProcessAsync(new() { File = Upload(Image()), CropX = .5, CropY = 0, CropSize = 1 });
        using var image = SKBitmap.Decode(output);
        var pixel = image.GetPixel(128, 128);
        Assert.True(pixel.Blue > 200 && pixel.Red < 50);
        await Assert.ThrowsAsync<BusinessException>(() => AvatarImageProcessor.ProcessAsync(new() { File = Upload(Image()), CropX = .9, CropSize = 1 }));
    }

    [Fact]
    public async Task Exif_orientation_is_applied_before_crop_and_is_not_copied_to_output()
    {
        var jpeg = Image(SKEncodedImageFormat.Jpeg);
        byte[] exif = [
            0xFF, 0xE1, 0, 34, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 1, 0,
            0x12, 0x01, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0
        ];
        var oriented = jpeg[..2].Concat(exif).Concat(jpeg[2..]).ToArray();
        var output = await AvatarImageProcessor.ProcessAsync(new() { File = Upload(oriented, "portrait.jpg", "image/jpeg"), CropY = .5, CropSize = 1 });
        using var image = SKBitmap.Decode(output);
        var pixel = image.GetPixel(128, 128);
        Assert.True(pixel.Blue > 180 && pixel.Red < 70);
        Assert.DoesNotContain("Exif", Encoding.Latin1.GetString(output));
    }

    [Theory]
    [InlineData("photo.svg", "image/svg+xml")]
    [InlineData("photo.exe", "image/png")]
    [InlineData("photo.png", "text/html")]
    [InlineData("photo.jpg", "image/jpeg")]
    public async Task Extension_mime_and_actual_codec_must_agree(string name, string mime)
    {
        await Assert.ThrowsAsync<BusinessException>(() => AvatarImageProcessor.ProcessAsync(new() { File = Upload(Image(), name, mime) }));
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("MZ executable payload")]
    public async Task Disguised_active_content_is_rejected_even_with_png_name_and_mime(string payload)
    {
        await Assert.ThrowsAsync<BusinessException>(() => AvatarImageProcessor.ProcessAsync(new() { File = Upload(Encoding.UTF8.GetBytes(payload)) }));
    }

    [Fact]
    public async Task Oversized_empty_truncated_and_excessive_dimensions_are_rejected()
    {
        await Assert.ThrowsAsync<BusinessException>(() => AvatarImageProcessor.ProcessAsync(new() { File = Upload(new byte[AvatarImageProcessor.MaxBytes + 1]) }));
        await Assert.ThrowsAsync<BusinessException>(() => AvatarImageProcessor.ProcessAsync(new() { File = Upload([]) }));
        await Assert.ThrowsAsync<BusinessException>(() => AvatarImageProcessor.ProcessAsync(new() { File = Upload(Image()[..12]) }));
        await Assert.ThrowsAsync<BusinessException>(() => AvatarImageProcessor.ProcessAsync(new() { File = Upload(Image(width: 4097, height: 1)) }));
    }

    [Fact]
    public async Task Real_stream_length_is_bounded_when_upload_metadata_lies()
    {
        var file = new MisreportedFile(new byte[AvatarImageProcessor.MaxBytes + 1]);
        await Assert.ThrowsAsync<BusinessException>(() => AvatarImageProcessor.ProcessAsync(new() { File = file }));
    }

    [Fact]
    public async Task Reencoding_strips_untrusted_text_metadata()
    {
        const string marker = "private-location-metadata-should-not-survive";
        var png = Image();
        var payload = Encoding.ASCII.GetBytes("Comment\0" + marker);
        var chunk = new byte[12 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, payload.Length);
        "tEXt"u8.CopyTo(chunk.AsSpan(4));
        payload.CopyTo(chunk, 8);
        var crc = uint.MaxValue;
        foreach (var value in chunk.AsSpan(4, payload.Length + 4))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc);
        var tagged = png[..^12].Concat(chunk).Concat(png[^12..]).ToArray();
        Assert.Contains(marker, Encoding.Latin1.GetString(tagged));

        var result = await AvatarImageProcessor.ProcessAsync(new() { File = Upload(tagged) });

        Assert.DoesNotContain(marker, Encoding.Latin1.GetString(result));
    }

    [Theory]
    [InlineData(double.NaN, 0, 1)]
    [InlineData(0, double.PositiveInfinity, 1)]
    [InlineData(-.1, 0, 1)]
    [InlineData(0, 0, 1.1)]
    [InlineData(.1, 0, 0)]
    public void Invalid_crop_numbers_are_rejected(double x, double y, double size) => Assert.False(AvatarImageProcessor.ValidCrop(new() { CropX = x, CropY = y, CropSize = size }));

    [Theory]
    [InlineData("../../appsettings.json")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.svg")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.webp")]
    [InlineData("/Account/Avatar/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.webp?file=secret")]
    [InlineData("https://example.com/avatar.webp")]
    public void File_and_url_validation_rejects_paths_extensions_queries_and_external_urls(string value)
    {
        Assert.False(AvatarStorage.IsSafeFileName(value));
        Assert.False(AvatarStorage.IsSafeUrl(value));
    }

    [Fact]
    public async Task Storage_uses_opaque_names_and_only_removes_its_own_generated_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "EduLibraryAvatarTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var storage = new AvatarStorage(new TestEnvironment(root), NullLogger<AvatarStorage>.Instance);
        string? first = null, second = null;
        var sentinel = Path.Combine(root, "keep.txt");
        try
        {
            await File.WriteAllTextAsync(sentinel, "keep");
            first = await storage.SaveAsync(new() { File = Upload(Image(), "../../untrusted.png") });
            second = await storage.SaveAsync(new() { File = Upload(Image()) });
            Assert.NotEqual(first, second);
            Assert.True(AvatarStorage.IsSafeUrl(first));
            Assert.DoesNotContain("untrusted", first);
            Assert.True(storage.TryGetPhysicalPath(first[AvatarStorage.UrlPrefix.Length..], out var path));
            Assert.StartsWith(Path.GetFullPath(Path.Combine(root, "App_Data", "avatars")), path);
            storage.Delete(first);
            Assert.False(File.Exists(path));
            storage.Delete("/Account/Avatar/../../keep.txt");
            Assert.True(File.Exists(sentinel));
            Assert.True(storage.TryGetPhysicalPath(second[AvatarStorage.UrlPrefix.Length..], out _));
        }
        finally
        {
            storage.Delete(first); storage.Delete(second);
            File.Delete(sentinel);
            var avatars = Path.Combine(root, "App_Data", "avatars");
            if (Directory.Exists(avatars)) Directory.Delete(avatars);
            var data = Path.Combine(root, "App_Data");
            if (Directory.Exists(data)) Directory.Delete(data);
            Directory.Delete(root);
        }
    }

    [Theory]
    [InlineData("Әли Нұрлан", "ӘН")]
    [InlineData("Мария Иванова", "МИ")]
    [InlineData("Ada Lovelace", "AL")]
    [InlineData("Solo", "S")]
    [InlineData("", "EL")]
    public void Initials_fallback_handles_kazakh_russian_and_english_names(string name, string expected) => Assert.Equal(expected, AvatarViewComponent.Initials(name));

    [Fact]
    public void Profile_preferences_only_accept_supported_cultures_and_theme_values()
    {
        Assert.All(new[] { "kk-KZ", "ru-RU", "en-US" }, value => Assert.True(ProfilePreferences.IsLanguage(value)));
        Assert.All(new[] { "system", "light", "dark" }, value => Assert.True(ProfilePreferences.IsTheme(value)));
        Assert.False(ProfilePreferences.IsLanguage("en-GB"));
        Assert.False(ProfilePreferences.IsLanguage("../../file"));
        Assert.False(ProfilePreferences.IsTheme("<script>"));
    }

    private sealed class MisreportedFile(byte[] content) : IFormFile
    {
        public string ContentType => "image/png";
        public string ContentDisposition => "";
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public long Length => 1;
        public string Name => "File";
        public string FileName => "photo.png";
        public Stream OpenReadStream() => new MemoryStream(content);
        public void CopyTo(Stream target) => target.Write(content);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => target.WriteAsync(content, cancellationToken).AsTask();
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "EduLibrary.Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
