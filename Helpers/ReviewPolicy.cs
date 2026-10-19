namespace EduLibrary.Helpers;

public static class ReviewPolicy
{
    public static void ValidateContent(int rating, string? comment)
    {
        if (rating is < 1 or > 5) throw new BusinessException("Баға 1–5 жұлдыз аралығында болуы тиіс.");
        if (comment?.Length > 3000) throw new BusinessException("Пікір 3000 таңбадан аспауы тиіс.");
    }

    public static byte[] ParseVersion(string? value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value ?? "");
            if (bytes.Length != 8) throw new FormatException();
            return bytes;
        }
        catch (FormatException)
        {
            throw new BusinessException("Жазба нұсқасы жарамсыз. Бетті жаңартып, қайта көріңіз.");
        }
    }
}
