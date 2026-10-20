using System.Globalization;
using System.Resources;

namespace EduLibrary.Localization;

/// <summary>Resource lookup for domain-facing messages where dependency injection is unavailable.</summary>
public static class UiText
{
    private static readonly ResourceManager Resources = new("EduLibrary.Resources.SharedResource", typeof(SharedResource).Assembly) { IgnoreCase = true };
    public static readonly string[] SupportedCultures = ["kk-KZ", "ru-RU", "en-US"];

    public static string Get(string text, params object?[] args)
    {
        var value = Resources.GetString(text, CultureInfo.CurrentUICulture) ?? text;
        return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
    }

    public static string Format(FormattableString text) => Get(text.Format, text.GetArguments());
    public static bool IsSupported(string? culture) => SupportedCultures.Contains(culture, StringComparer.Ordinal);
}
