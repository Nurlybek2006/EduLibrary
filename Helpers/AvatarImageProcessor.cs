using EduLibrary.ViewModels;
using SkiaSharp;

namespace EduLibrary.Helpers;

/// <summary>Accepts a bounded raster upload and exports only fresh 256-pixel image data.</summary>
public static class AvatarImageProcessor
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxDimension = 4096;
    public const long MaxPixels = 16_000_000;

    public static async Task<byte[]> ProcessAsync(AvatarUploadViewModel model, CancellationToken ct = default)
    {
        var file = model.File ?? throw new BusinessException("Choose a photo to upload.");
        if (file.Length <= 0 || file.Length > MaxBytes) throw new BusinessException("The photo must be no larger than 2 MB.");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var expected = extension switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".png" => SKEncodedImageFormat.Png,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => throw new BusinessException("Use a JPG, PNG, or WebP photo.")
        };
        var mime = expected switch { SKEncodedImageFormat.Jpeg => "image/jpeg", SKEncodedImageFormat.Png => "image/png", _ => "image/webp" };
        if (!string.Equals(file.ContentType, mime, StringComparison.OrdinalIgnoreCase)) throw new BusinessException("The photo format does not match its content type.");
        if (!ValidCrop(model)) throw new BusinessException("The photo crop is invalid.");
        await using var input = file.OpenReadStream();
        using var bytes = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        {
            if (bytes.Length + read > MaxBytes) throw new BusinessException("The photo must be no larger than 2 MB.");
            bytes.Write(buffer, 0, read);
        }
        var encoded = bytes.ToArray();
        if (!HasSignature(encoded, expected)) throw new BusinessException("The photo signature is invalid.");
        try
        {
            using var data = SKData.CreateCopy(encoded);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.EncodedFormat != expected) throw new BusinessException("The photo cannot be decoded safely.");
            var info = codec.Info;
            if (info.Width < 1 || info.Height < 1 || info.Width > MaxDimension || info.Height > MaxDimension || (long)info.Width * info.Height > MaxPixels)
                throw new BusinessException("The photo dimensions must not exceed 4096 pixels or 16 megapixels.");
            if (codec.FrameCount > 1) throw new BusinessException("Use a still photo instead of an animated image.");
            using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new BusinessException("The photo cannot be decoded safely.");
            var rotated = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var width = rotated ? info.Height : info.Width;
            var height = rotated ? info.Width : info.Height;
            var size = model.CropSize > 0 ? model.CropSize * Math.Min(width, height) : Math.Min(width, height);
            var x = model.CropSize > 0 ? model.CropX * width : (width - size) / 2;
            var y = model.CropSize > 0 ? model.CropY * height : (height - size) / 2;
            if (size < 1 || x + size > width + 0.01 || y + size > height + 0.01) throw new BusinessException("The photo crop is invalid.");
            using var surface = SKSurface.Create(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)(256 / size));
            canvas.Translate((float)-x, (float)-y);
            Orient(canvas, codec.EncodedOrigin, info.Width, info.Height);
            using var source = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            using var normalized = surface.Snapshot();
            using var output = normalized.Encode(SKEncodedImageFormat.Webp, 85);
            return output?.ToArray() ?? throw new BusinessException("The photo cannot be decoded safely.");
        }
        catch (BusinessException) { throw; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new BusinessException("The photo cannot be decoded safely.");
        }
    }

    public static bool ValidCrop(AvatarUploadViewModel model) => double.IsFinite(model.CropX) && double.IsFinite(model.CropY) && double.IsFinite(model.CropSize)
        && model.CropX is >= 0 and <= 1 && model.CropY is >= 0 and <= 1 && model.CropSize is >= 0 and <= 1
        && (model.CropSize > 0 || (model.CropX == 0 && model.CropY == 0));

    private static bool HasSignature(ReadOnlySpan<byte> data, SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF,
        SKEncodedImageFormat.Png => data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        SKEncodedImageFormat.Webp => data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8),
        _ => false
    };

    private static void Orient(SKCanvas canvas, SKEncodedOrigin origin, int width, int height)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(width, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(width, height); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, height); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(height, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(height, width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, width); canvas.RotateDegrees(270); break;
        }
    }
}
