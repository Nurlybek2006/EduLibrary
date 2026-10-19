using System.ComponentModel.DataAnnotations;
using EduLibrary.Models.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduLibrary.ViewModels;

public sealed class ReservationFilterViewModel
{
    [StringLength(200, ErrorMessage = "Іздеу мәтіні 200 таңбадан аспасын.")]
    public string? Search { get; set; }
    public string Status { get; set; } = "active";
    public int Page { get; set; } = 1;
    public int? BookId { get; set; }
    public int? StudentId { get; set; }
}

public sealed class ReservationListViewModel
{
    public ReservationFilterViewModel Filter { get; set; } = new();
    public bool IsStaff { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;
    public int PendingCount { get; set; }
    public int ReadyCount { get; set; }
    public int QueuedCount { get; set; }
    public List<ReservationRowViewModel> Items { get; set; } = [];
}

public class ReservationRowViewModel
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = "";
    public string Authors { get; set; } = "";
    public string? CoverImageUrl { get; set; }
    public int PublicationYear { get; set; }
    public string StudentName { get; set; } = "";
    public string StudentNumber { get; set; } = "";
    public ReservationStatus Status { get; set; }
    public string StatusLabel => Status switch { ReservationStatus.Pending => "Дайындалуын күтуде", ReservationStatus.ReadyForPickup => "Алып кетуге дайын", ReservationStatus.Queued => "Кезекте", ReservationStatus.Fulfilled => "Кітап берілді", ReservationStatus.Cancelled => "Тоқтатылған", ReservationStatus.Expired => "Мерзімі өткен", _ => "Белгісіз" };
    public DateTime CreatedAt { get; set; }
    public DateTime? PickupAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? QueuePosition { get; set; }
    public string PickupLocationName { get; set; } = "";
}

public sealed class ReservationDetailsViewModel : ReservationRowViewModel
{
    public bool IsStaff { get; set; }
    public bool CanCancel { get; set; }
    public bool CanPrepare { get; set; }
    public bool CanFulfill { get; set; }
    public string RowVersion { get; set; } = "";
    public string PickupCode { get; set; } = "";
    public string PickupLocationAddress { get; set; } = "";
    public string? InventoryNumber { get; set; }
    public string? ShelfLocation { get; set; }
    public int? LoanId { get; set; }
    public DateTime? ReadyAt { get; set; }
    public ReservationFulfillViewModel FulfillForm { get; set; } = new();
}

public sealed class ReservationCreateViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Кітапты дұрыс таңдаңыз.")]
    public int BookId { get; set; }
    public string BookTitle { get; set; } = "";
    public string Authors { get; set; } = "";
    public string? CoverImageUrl { get; set; }
    public int PublicationYear { get; set; }
    public int AvailableCopies { get; set; }
    public bool JoinQueue { get; set; }
    [Required(ErrorMessage = "Алып кету орнын таңдаңыз."), StringLength(40, ErrorMessage = "Алып кету орны 40 таңбадан аспасын."), Display(Name = "Алып кету орны")]
    public string PickupLocationId { get; set; } = "main";
    [DataType(DataType.Date), Display(Name = "Алып кету күні")]
    public DateTime? PickupDate { get; set; }
    [StringLength(5, ErrorMessage = "Уақытты СС:ММ пішімінде таңдаңыз."), Display(Name = "Алып кету уақыты")]
    public string? PickupTime { get; set; }
    public List<SelectListItem> Locations { get; set; } = [];
    public List<SelectListItem> PickupDates { get; set; } = [];
    public List<PickupSlotViewModel> Slots { get; set; } = [];
}

public sealed class PickupSlotViewModel
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public int AvailableCapacity { get; set; }
}

public sealed class ReservationReadyViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Броньді дұрыс таңдаңыз.")]
    public int Id { get; set; }
    public string BookTitle { get; set; } = "";
    public string StudentName { get; set; } = "";
    [Range(1, int.MaxValue, ErrorMessage = "Кітап данасын таңдаңыз."), Display(Name = "Кітап данасы")]
    public int BookCopyId { get; set; }
    [Required(ErrorMessage = "Бетті жаңартып, қайта көріңіз.")]
    public string RowVersion { get; set; } = "";
    public List<SelectListItem> CopyOptions { get; set; } = [];
}

public sealed class ReservationFulfillViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Броньді дұрыс таңдаңыз.")]
    public int Id { get; set; }
    [Required(ErrorMessage = "Студент көрсеткен бронь кодын енгізіңіз."), StringLength(100, ErrorMessage = "Бронь коды 100 таңбадан аспасын."), Display(Name = "Бронь коды")]
    public string PickupCode { get; set; } = "";
    [Required(ErrorMessage = "Бетті жаңартып, қайта көріңіз.")]
    public string RowVersion { get; set; } = "";
    [DataType(DataType.Date), Display(Name = "Қайтару мерзімі")]
    public DateTime DueDate { get; set; }
}
