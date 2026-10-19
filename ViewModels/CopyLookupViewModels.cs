using System.ComponentModel.DataAnnotations;
using EduLibrary.Models.Enums;

namespace EduLibrary.ViewModels;

public sealed class CopyLookupViewModel
{
    [StringLength(100, ErrorMessage = "Код 100 таңбадан аспауы тиіс.")]
    [Display(Name = "Штрихкод немесе түгендеу нөмірі")]
    public string? Code { get; set; }
    public bool Searched { get; set; }
}

public sealed class CopyDetailsViewModel
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = "";
    public string ISBN { get; set; } = "";
    public string InventoryNumber { get; set; } = "";
    public string Barcode { get; set; } = "";
    public CopyStatus Status { get; set; }
    public string StatusLabel => CatalogLabels.CopyStatusLabel(Status);
    public string ShelfLocation { get; set; } = "";
    public string Condition { get; set; } = "";
    public DateTime AcquiredAt { get; set; }
    public bool BookArchived { get; set; }
    public int? ActiveLoanId { get; set; }
    public DateTime? LoanDate { get; set; }
    public DateTime? DueDate { get; set; }
    public bool CanIssue => !BookArchived && Status == CopyStatus.Available && !ActiveLoanId.HasValue;
    public bool CanReturn => Status == CopyStatus.OnLoan && ActiveLoanId.HasValue;
}
