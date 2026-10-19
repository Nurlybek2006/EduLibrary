using EduLibrary.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduLibrary.Data.Configurations;

public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> b)
    {
        b.Property(r => r.PickupCode).HasMaxLength(36).IsRequired();
        b.Property(r => r.PickupLocationId).HasMaxLength(40).IsRequired();
        b.Property(r => r.RowVersion).IsRowVersion();
        b.HasOne(r => r.Book).WithMany().HasForeignKey(r => r.BookId);
        b.HasOne(r => r.Student).WithMany().HasForeignKey(r => r.StudentId);
        b.HasOne(r => r.BookCopy).WithMany().HasForeignKey(r => new { r.BookCopyId, r.BookId }).HasPrincipalKey(c => new { c.Id, c.BookId });
        b.HasOne(r => r.Loan).WithMany().HasForeignKey(r => new { r.LoanId, r.StudentId }).HasPrincipalKey(l => new { l.Id, l.StudentId });
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.PreparedByUserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.CancelledByUserId);
        b.HasIndex(r => r.PickupCode).IsUnique();
        b.HasIndex(r => new { r.StudentId, r.BookId }).IsUnique().HasFilter("[Status] IN (0, 1, 2)").HasDatabaseName("UX_Reservations_ActiveStudentBook");
        b.HasIndex(r => r.BookCopyId).IsUnique().HasFilter("[BookCopyId] IS NOT NULL AND [Status] IN (0, 1)").HasDatabaseName("UX_Reservations_ActiveCopy");
        b.HasIndex(r => r.LoanId).IsUnique().HasFilter("[LoanId] IS NOT NULL");
        b.HasIndex(r => new { r.BookId, r.Status, r.CreatedAtUtc, r.Id });
        b.HasIndex(r => new { r.PickupLocationId, r.PickupAtUtc, r.Status });
        b.HasIndex(r => new { r.Status, r.ExpiresAtUtc });
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Reservations_Status", "[Status] BETWEEN 0 AND 5");
            t.HasCheckConstraint("CK_Reservations_Allocation", "([Status] IN (0,1,3) AND [BookCopyId] IS NOT NULL AND [PickupAtUtc] IS NOT NULL AND [ExpiresAtUtc] IS NOT NULL) OR ([Status] = 2 AND [BookCopyId] IS NULL AND [PickupAtUtc] IS NULL AND [ExpiresAtUtc] IS NULL) OR [Status] IN (4,5)");
            t.HasCheckConstraint("CK_Reservations_Fulfillment", "([Status] = 3 AND [LoanId] IS NOT NULL AND [FulfilledAtUtc] IS NOT NULL AND [ReadyAtUtc] IS NOT NULL) OR ([Status] <> 3 AND [LoanId] IS NULL AND [FulfilledAtUtc] IS NULL)");
            t.HasCheckConstraint("CK_Reservations_Dates", "([ReadyAtUtc] IS NULL OR [ReadyAtUtc] >= [CreatedAtUtc]) AND ([ExpiresAtUtc] IS NULL OR [ExpiresAtUtc] >= [CreatedAtUtc]) AND ([PickupAtUtc] IS NULL OR [ExpiresAtUtc] IS NULL OR [ExpiresAtUtc] >= [PickupAtUtc]) AND ([FulfilledAtUtc] IS NULL OR [FulfilledAtUtc] >= [CreatedAtUtc]) AND ([CancelledAtUtc] IS NULL OR [CancelledAtUtc] >= [CreatedAtUtc]) AND ([ExpiredAtUtc] IS NULL OR [ExpiredAtUtc] >= [CreatedAtUtc])");
            t.HasCheckConstraint("CK_Reservations_Ready", "[Status] <> 1 OR [ReadyAtUtc] IS NOT NULL");
            t.HasCheckConstraint("CK_Reservations_Completed", "([Status] <> 4 OR [CancelledAtUtc] IS NOT NULL) AND ([Status] <> 5 OR [ExpiredAtUtc] IS NOT NULL)");
            t.HasCheckConstraint("CK_Reservations_Code", "LEN([PickupCode]) = 36 AND LEFT([PickupCode],4) = 'ELR-' AND SUBSTRING([PickupCode],5,32) COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%'");
        });
    }
}
