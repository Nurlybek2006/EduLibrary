using EduLibrary.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduLibrary.Data.Configurations;

public class BookSubjectConfiguration : IEntityTypeConfiguration<BookSubject>
{
    public void Configure(EntityTypeBuilder<BookSubject> b)
    {
        b.HasKey(x=>new{x.BookId,x.SubjectId});
        b.HasOne(x=>x.Book).WithMany(x=>x.BookSubjects).HasForeignKey(x=>x.BookId);
        b.HasOne(x=>x.Subject).WithMany().HasForeignKey(x=>x.SubjectId);
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(x=>x.UserId).HasMaxLength(450).IsRequired();
        b.Property(x=>x.DeduplicationKey).HasMaxLength(160).IsRequired();
        b.Property(x=>x.Type).HasMaxLength(40).IsRequired();
        b.Property(x=>x.Title).HasMaxLength(200).IsRequired();
        b.Property(x=>x.Message).HasMaxLength(1000).IsRequired();
        b.Property(x=>x.Link).HasMaxLength(300).IsRequired();
        b.HasIndex(x=>new{x.UserId,x.DeduplicationKey}).IsUnique();
        b.HasIndex(x=>new{x.UserId,x.ReadAt,x.CreatedAt});
        b.HasIndex(x=>x.DeduplicationKey);
        b.HasOne(x=>x.User).WithMany().HasForeignKey(x=>x.UserId);
        b.ToTable(t=>t.HasCheckConstraint("CK_Notifications_ReadAt", "[ReadAt] IS NULL OR [ReadAt] >= [CreatedAt]"));
    }
}
