using System.ComponentModel.DataAnnotations;

namespace EduLibrary.ViewModels;

public sealed class ReservationCodeLookupViewModel
{
    [StringLength(100, ErrorMessage = "Алып кету коды 100 таңбадан аспауы тиіс.")]
    [Display(Name = "Алып кету коды")]
    public string? Code { get; set; }
}
