using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduLibrary.ViewModels;

public sealed class LoanFilterViewModel
{
    public string? Search { get; set; }
    public string Status { get; set; } = "active";
    public int Page { get; set; } = 1;
}

public sealed class LoanListViewModel
{
    public LoanFilterViewModel Filter { get; set; } = new();
    public IReadOnlyList<LoanRowViewModel> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageSize { get; set; } = 15;
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool IsStaff { get; set; }
}

public sealed class LoanRowViewModel
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string StudentNumber { get; set; } = "";
    public string BookTitle { get; set; } = "";
    public string InventoryNumber { get; set; } = "";
    public DateTime LoanDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public int OverdueDays { get; set; }
    public decimal EstimatedFine { get; set; }
    public string StatusLabel => ReturnedAt.HasValue ? "Қайтарылды" : OverdueDays > 0 ? "Мерзімі өткен" : "Оқырманда";
    public string StatusClass => ReturnedAt.HasValue ? "success" : OverdueDays > 0 ? "danger" : "primary";
}

public sealed class LoanDetailsViewModel
{
    public LoanRowViewModel Loan { get; set; } = new();
    public string? Notes { get; set; }
    public string? ReturnCondition { get; set; }
    public string? ReturnNotes { get; set; }
    public decimal FinalFine { get; set; }
    public bool IsStaff { get; set; }
}

public sealed class IssueLoanViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Студентті таңдаңыз.")]
    [Display(Name = "Студент")]
    public int StudentId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Кітап данасын таңдаңыз.")]
    [Display(Name = "Қолжетімді дана")]
    public int BookCopyId { get; set; }
    [Required(ErrorMessage = "Берілген күнді енгізіңіз."), DataType(DataType.Date)]
    [Display(Name = "Берілген күні")]
    public DateTime LoanDate { get; set; }
    [Required(ErrorMessage = "Қайтару мерзімін енгізіңіз."), DataType(DataType.Date)]
    [Display(Name = "Қайтару мерзімі")]
    public DateTime DueDate { get; set; }
    [StringLength(2000, ErrorMessage = "Ескертпе 2000 таңбадан аспауы тиіс.")]
    [Display(Name = "Ескертпе")]
    public string? Notes { get; set; }
    public List<SelectListItem> Students { get; set; } = [];
    public List<SelectListItem> Copies { get; set; } = [];
    public int DefaultLoanDays { get; set; }
}

public sealed class ReturnLoanViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Кітап беру жазбасы таңдалмады.")]
    public int LoanId { get; set; }
    [Required(ErrorMessage = "Қайтарылған күнді енгізіңіз."), DataType(DataType.Date)]
    [Display(Name = "Қайтарылған күні")]
    public DateTime ReturnDate { get; set; }
    [Required(ErrorMessage = "Дананың жағдайын таңдаңыз.")]
    [RegularExpression("^(Good|Damaged)$", ErrorMessage = "Дананың жағдайын дұрыс таңдаңыз.")]
    [Display(Name = "Дананың жағдайы")]
    public string ReturnCondition { get; set; } = "Good";
    [StringLength(2000, ErrorMessage = "Ескертпе 2000 таңбадан аспауы тиіс.")]
    [Display(Name = "Ескертпе")]
    public string? Notes { get; set; }
    public string? RowVersion { get; set; }
    public string BookTitle { get; set; } = "";
    public string InventoryNumber { get; set; } = "";
    public string StudentName { get; set; } = "";
    public DateTime LoanDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal FinePerOverdueDay { get; set; }
    public int OverdueDays { get; set; }
    public decimal EstimatedFine { get; set; }
}

public sealed class ReturnRowViewModel
{
    public int LoanId { get; set; }
    public string BookTitle { get; set; } = "";
    public string InventoryNumber { get; set; } = "";
    public string StudentName { get; set; } = "";
    public DateTime ReturnDate { get; set; }
    public string ReturnCondition { get; set; } = "";
    public decimal FineAmount { get; set; }
}

public sealed class ReturnListViewModel
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int TotalCount { get; set; }
    public int PageSize { get; set; } = 15;
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public IReadOnlyList<ReturnRowViewModel> Items { get; set; } = [];
}

public sealed class FineRowViewModel
{
    public int Id { get; set; }
    public int LoanId { get; set; }
    public string StudentName { get; set; } = "";
    public string StudentNumber { get; set; } = "";
    public string BookTitle { get; set; } = "";
    public string Reason { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal Balance => Amount - PaidAmount;
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string StatusLabel => Balance == 0 ? "Төленді" : PaidAmount > 0 ? "Ішінара төленді" : "Төленбеген";
    public string StatusClass => Balance == 0 ? "success" : PaidAmount > 0 ? "warning" : "danger";
}

public sealed class FineListViewModel
{
    public string? Search { get; set; }
    public string Status { get; set; } = "outstanding";
    public int Page { get; set; } = 1;
    public int TotalCount { get; set; }
    public int PageSize { get; set; } = 15;
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public decimal OutstandingTotal { get; set; }
    public bool IsStaff { get; set; }
    public IReadOnlyList<FineRowViewModel> Items { get; set; } = [];
}

public sealed class FineDetailsViewModel
{
    public FineRowViewModel Fine { get; set; } = new();
    public bool IsStaff { get; set; }
    public string RowVersion { get; set; } = "";
    public IReadOnlyList<FinePaymentRowViewModel> Payments { get; set; } = [];
}

public sealed class FinePaymentRowViewModel
{
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
}

public sealed class FinePaymentViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Айыппұл таңдалмады.")]
    public int FineId { get; set; }
    [Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true, ErrorMessage = "Төлем сомасы 0,01 теңгеден кем болмауы тиіс.")]
    [Display(Name = "Төлем сомасы (₸)")]
    public decimal Amount { get; set; }
    public string? RowVersion { get; set; }
}
