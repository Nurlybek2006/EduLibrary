namespace EduLibrary.Helpers;

public static class ProfilePreferences
{
    public static readonly string[] Languages = ["kk-KZ", "ru-RU", "en-US"];
    public static readonly string[] Themes = ["system", "light", "dark"];
    public static bool IsLanguage(string? value) => Languages.Contains(value, StringComparer.Ordinal);
    public static bool IsTheme(string? value) => Themes.Contains(value, StringComparer.Ordinal);
}
