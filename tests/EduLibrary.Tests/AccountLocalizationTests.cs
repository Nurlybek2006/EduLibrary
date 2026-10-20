using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Resources;
using EduLibrary.Helpers;
using EduLibrary.ViewModels;

namespace EduLibrary.Tests;

public sealed class AccountLocalizationTests
{
    [Theory]
    [InlineData("kk-KZ", "Пошта немесе құпиясөз дұрыс емес, не аккаунт уақытша бұғатталған.")]
    [InlineData("ru-RU", "Неверная почта или пароль, либо аккаунт временно заблокирован.")]
    [InlineData("en-US", "The email or password is incorrect, or the account is temporarily locked.")]
    public void Negative_login_result_uses_selected_language(string culture, string expected)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var result = AccountActionResult.Failure("Пошта немесе құпиясөз дұрыс емес, не аккаунт уақытша бұғатталған.");
            Assert.False(result.Succeeded);
            Assert.Equal(expected, Assert.Single(result.Errors));
        }
        finally { CultureInfo.CurrentCulture = originalCulture; CultureInfo.CurrentUICulture = originalUiCulture; }
    }

    [Theory]
    [InlineData("kk-KZ")]
    [InlineData("ru-RU")]
    [InlineData("en-US")]
    public void Account_display_and_explicit_validation_keys_have_all_three_resource_values(string culture)
    {
        var resources = new ResourceManager("EduLibrary.Resources.SharedResource", typeof(EduLibrary.SharedResource).Assembly);
        foreach (var model in new[] { typeof(LoginViewModel), typeof(RegisterViewModel), typeof(ProfileViewModel), typeof(AvatarUploadViewModel) })
        {
            foreach (var property in model.GetProperties())
            {
                foreach (var validation in property.GetCustomAttributes<ValidationAttribute>())
                    if (validation.ErrorMessage is string key)
                        Assert.False(string.IsNullOrWhiteSpace(resources.GetString(key, CultureInfo.GetCultureInfo(culture))), $"Missing {culture} validation resource: {model.Name}.{property.Name}: {key}");
                if (property.GetCustomAttribute<DisplayAttribute>()?.Name is string label)
                    Assert.False(string.IsNullOrWhiteSpace(resources.GetString(label, CultureInfo.GetCultureInfo(culture))), $"Missing {culture} display resource: {label}");
            }
        }
    }

    [Theory]
    [InlineData("ru-RU", "Пароль должен содержать не менее")]
    [InlineData("en-US", "Password must contain at least")]
    public void Identity_password_validation_is_translated_and_formats_required_length(string culture, string phrase)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var message = new KazakhIdentityErrorDescriber().PasswordTooShort(14).Description;
            Assert.Contains("14", message);
            Assert.Contains(phrase, message);
            Assert.DoesNotContain("{0}", message);
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }
}
