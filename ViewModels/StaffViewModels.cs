using System.ComponentModel.DataAnnotations;

namespace EduLibrary.ViewModels;

public sealed class StaffListItem
{
    public string Id { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsInactive { get; set; }
    public bool CanDeactivate { get; set; }
}

public sealed class CreateLibrarianViewModel
{
    [Required(ErrorMessage = "Аты-жөнін енгізіңіз."), StringLength(200, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Аты-жөні")]
    public string FullName { get; set; } = "";
    [Required(ErrorMessage = "Электрондық поштаны енгізіңіз."), EmailAddress(ErrorMessage = "Электрондық пошта дұрыс емес."), StringLength(256, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Электрондық пошта")]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "Құпиясөзді енгізіңіз."), StringLength(100, MinimumLength = 10, ErrorMessage = "Құпиясөз 10–100 таңбадан тұруы тиіс."), DataType(DataType.Password), Display(Name = "Бастапқы құпиясөз")]
    public string Password { get; set; } = "";
    [Required(ErrorMessage = "Құпиясөзді қайталаңыз."), DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "Құпиясөздер сәйкес емес."), Display(Name = "Құпиясөзді қайталау")]
    public string ConfirmPassword { get; set; } = "";
}

