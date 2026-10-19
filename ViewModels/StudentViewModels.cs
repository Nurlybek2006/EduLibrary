using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduLibrary.ViewModels;

public sealed class StudentListViewModel
{
    public string? Search { get; set; }
    public string? Faculty { get; set; }
    public int? Course { get; set; }
    public string? GroupName { get; set; }
    public bool IncludeInactive { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public List<string> Faculties { get; set; } = [];
    public List<StudentListItem> Students { get; set; } = [];
}

public sealed class StudentListItem
{
    public int Id { get; set; }
    public string StudentNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Faculty { get; set; } = "";
    public int Course { get; set; }
    public string GroupName { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class StudentEditViewModel
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Студент нөмірін енгізіңіз."), StringLength(30, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Студент нөмірі")]
    public string StudentNumber { get; set; } = "";
    [Required(ErrorMessage = "Аты-жөнін енгізіңіз."), StringLength(200, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Аты-жөні")]
    public string FullName { get; set; } = "";
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
    [StringLength(1000, ErrorMessage = "Ең көбі {1} таңба енгізіңіз."), Display(Name = "Қызығушылықтар")]
    public string? Interests { get; set; }
    [Display(Name = "Оқитын пәндер")]
    public List<int> SelectedSubjectIds { get; set; } = [];
    public List<SelectListItem> Subjects { get; set; } = [];
}

public sealed class StudentDetailsViewModel
{
    public int Id { get; set; }
    public string StudentNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? PhoneNumber { get; set; }
    public string Faculty { get; set; } = "";
    public int Course { get; set; }
    public string GroupName { get; set; } = "";
    public bool IsActive { get; set; }
    public bool CanManage { get; set; }
    public decimal OutstandingFines { get; set; }
    public List<string> Interests { get; set; } = [];
    public List<string> Subjects { get; set; } = [];
    public List<StudentLoanItem> ActiveLoans { get; set; } = [];
    public List<StudentLoanItem> History { get; set; } = [];
    public List<StudentFineItem> Fines { get; set; } = [];
}

public sealed class StudentLoanItem
{
    public int Id { get; set; }
    public string BookTitle { get; set; } = "";
    public int BookId { get; set; }
    public string? CoverImageUrl { get; set; }
    public int PublicationYear { get; set; }
    public List<string> BookAuthors { get; set; } = [];
    public string Authors => string.Join(", ", BookAuthors);
    public string InventoryNumber { get; set; } = "";
    public DateTime LoanDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public int OverdueDays { get; set; }
}

public sealed class StudentFineItem
{
    public string BookTitle { get; set; } = "";
    public string Reason { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal Balance => Amount - PaidAmount;
    public DateTime CreatedAt { get; set; }
}

