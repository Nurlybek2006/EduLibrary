using EduLibrary.Models.Enums;

namespace EduLibrary.Models.Entities;

public sealed class Reservation
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int StudentId { get; set; }
    public int? BookCopyId { get; set; }
    public int? LoanId { get; set; }
    public ReservationStatus Status { get; set; }
    public string PickupLocationId { get; set; } = "main";
    public DateTime? RequestedPickupAtUtc { get; set; }
    public DateTime? PickupAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadyAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? ExpiredAtUtc { get; set; }
    public string PickupCode { get; set; } = "";
    public string? PreparedByUserId { get; set; }
    public string? CancelledByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Book Book { get; set; } = null!;
    public Student Student { get; set; } = null!;
    public BookCopy? BookCopy { get; set; }
    public Loan? Loan { get; set; }
}
