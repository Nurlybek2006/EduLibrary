using QRCoder;

namespace EduLibrary.Helpers;

public static class CopyCode
{
    public static string Normalize(string? value)
    {
        var code = value?.Trim() ?? "";
        if (code.Length is < 1 or > 100 || code.Any(char.IsControl))
            throw new BusinessException("Штрихкодты немесе түгендеу нөмірін енгізіңіз (1–100 таңба).");
        return code;
    }

    // Only the copy's unique opaque code is encoded. No book-loan or reader data.
    public static string CreateQrSvg(string barcode)
    {
        var code = Normalize(barcode);
        using var data = QRCodeGenerator.GenerateQrCode(code, QRCodeGenerator.ECCLevel.Q);
        using var renderer = new SvgQRCode(data);
        return renderer.GetGraphic(8);
    }
}
