namespace EduLibrary.Models.Enums;

public enum CopyStatus { Available, OnLoan, Damaged, Lost, Maintenance, Reserved }
public enum FineStatus { Unpaid, PartiallyPaid, Paid }
public enum ReservationStatus { Pending, ReadyForPickup, Queued, Fulfilled, Cancelled, Expired }
