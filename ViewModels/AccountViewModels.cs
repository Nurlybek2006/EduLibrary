using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using EduLibrary.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.ViewModels;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Электрондық поштаны енгізіңіз."), EmailAddress(ErrorMessage = "Электрондық пошта дұрыс емес."), Display(Name = "Электрондық пошта")]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "Құпиясөзді енгізіңіз."), DataType(DataType.Password), Display(Name = "Құпиясөз")]
    public string Password { get; set; } = "";
    [Display(Name = "Мені есте сақтау")]
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Аты-жөніңізді енгізіңіз."), StringLength(200, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Аты-жөні")]
    public string FullName { get; set; } = "";
    [Required(ErrorMessage = "Студент нөмірін енгізіңіз."), StringLength(30, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Студент нөмірі")]
    public string StudentNumber { get; set; } = "";
    [Required(ErrorMessage = "Электрондық поштаны енгізіңіз."), EmailAddress(ErrorMessage = "Электрондық пошта дұрыс емес."), StringLength(256, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Электрондық пошта")]
    public string Email { get; set; } = "";
    [Phone(ErrorMessage = "Телефон нөмірі дұрыс емес."), StringLength(30, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Телефон")]
    public string? PhoneNumber { get; set; }
    [Required(ErrorMessage = "Факультетті енгізіңіз."), StringLength(150, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Факультет")]
    public string Faculty { get; set; } = "";
    [Range(1, 8, ErrorMessage = "Курс 1 мен 8 аралығында болуы тиіс."), Display(Name = "Курс")]
    public int Course { get; set; } = 1;
    [Required(ErrorMessage = "Топты енгізіңіз."), StringLength(50, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Топ")]
    public string GroupName { get; set; } = "";
    [Required(ErrorMessage = "Құпиясөзді енгізіңіз."), StringLength(100, MinimumLength = 10, ErrorMessage = "Құпиясөз 10–100 таңбадан тұруы тиіс."), DataType(DataType.Password), Display(Name = "Құпиясөз")]
    public string Password { get; set; } = "";
    [Required(ErrorMessage = "Құпиясөзді қайталаңыз."), DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "Құпиясөздер сәйкес емес."), Display(Name = "Құпиясөзді қайталау")]
    public string ConfirmPassword { get; set; } = "";
}

public sealed class ProfileViewModel
{
    public string? AvatarPath { get; set; }
    [Required(ErrorMessage = "Choose a supported language.")]
    public string PreferredLanguage { get; set; } = "kk-KZ";
    [Required(ErrorMessage = "Choose a supported theme.")]
    public string PreferredTheme { get; set; } = "system";
    public int ActiveLoanCount { get; set; }
    public int ReturnedLoanCount { get; set; }
    public int DigitalBookCount { get; set; }
    public int CompletedDigitalBookCount { get; set; }
    public List<DigitalAssetCardViewModel> DigitalBooks { get; set; } = [];
    public List<ProfileLoanViewModel> RecentLoans { get; set; } = [];
    [Required(ErrorMessage = "Аты-жөніңізді енгізіңіз."), StringLength(200, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Аты-жөні")]
    public string FullName { get; set; } = "";
    [Required(ErrorMessage = "Электрондық поштаны енгізіңіз."), EmailAddress(ErrorMessage = "Электрондық пошта дұрыс емес."), StringLength(256, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Электрондық пошта")]
    public string Email { get; set; } = "";
    [Phone(ErrorMessage = "Телефон нөмірі дұрыс емес."), StringLength(30, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Телефон")]
    public string? PhoneNumber { get; set; }
    public int? StudentId { get; set; }
    public string? StudentNumber { get; set; }
    public string? Faculty { get; set; }
    public int? Course { get; set; }
    public string? GroupName { get; set; }
    [StringLength(1000, ErrorMessage = "Қызығушылықтар тізімі 1000 таңбадан аспасын."), Display(Name = "Қызығушылықтар")]
    public string? Interests { get; set; }
    [Display(Name = "Оқитын пәндер")]
    public List<int> SelectedSubjectIds { get; set; } = [];
    public List<SelectListItem> Subjects { get; set; } = [];
}

public sealed class AvatarUploadViewModel
{
    [Required(ErrorMessage = "Choose a photo to upload.")]
    public IFormFile? File { get; set; }
    [Range(0d, 1d, ErrorMessage = "The photo crop is invalid.")]
    [ModelBinder(BinderType = typeof(DoubleModelBinder))]
    public double CropX { get; set; }
    [Range(0d, 1d, ErrorMessage = "The photo crop is invalid.")]
    [ModelBinder(BinderType = typeof(DoubleModelBinder))]
    public double CropY { get; set; }
    [Range(0d, 1d, ErrorMessage = "The photo crop is invalid.")]
    [ModelBinder(BinderType = typeof(DoubleModelBinder))]
    public double CropSize { get; set; }
}

public sealed class ProfileLoanViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime LoanDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnedAt { get; set; }
}

public sealed class AvatarViewModel
{
    public string Initials { get; set; } = "EL";
    public string? ImageUrl { get; set; }
    public string Size { get; set; } = "small";
    public string Name { get; set; } = "";
}

public sealed record AccountActionResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static AccountActionResult Success() => new(true, []);
    public static AccountActionResult Failure(params string[] errors) => new(false, errors.Select(error => EduLibrary.Localization.UiText.Get(error)).ToArray());
}

