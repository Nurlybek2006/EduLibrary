using EduLibrary.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduLibrary.Data.Configurations;

public class LibraryConfiguration : IEntityTypeConfiguration<Book>, IEntityTypeConfiguration<Author>,
    IEntityTypeConfiguration<Publisher>, IEntityTypeConfiguration<Category>, IEntityTypeConfiguration<BookCopy>,
    IEntityTypeConfiguration<Student>, IEntityTypeConfiguration<Loan>, IEntityTypeConfiguration<Return>,
    IEntityTypeConfiguration<Fine>, IEntityTypeConfiguration<FinePayment>, IEntityTypeConfiguration<Review>,
    IEntityTypeConfiguration<BookAuthor>, IEntityTypeConfiguration<BookCategory>, IEntityTypeConfiguration<Subject>,
    IEntityTypeConfiguration<StudentSubject>, IEntityTypeConfiguration<StudentInterest>, IEntityTypeConfiguration<ApplicationUser>,
    IEntityTypeConfiguration<SeedHistory>
{
    public void Configure(EntityTypeBuilder<Book> b)
    {
        b.Property(x=>x.ISBN).HasMaxLength(20).IsRequired();
        b.HasIndex(x=>x.ISBN).IsUnique();
        b.Property(x=>x.Title).HasMaxLength(300).IsRequired();
        b.HasIndex(x=>x.Title);
        b.Property(x=>x.Description).HasMaxLength(6000);
        b.Property(x=>x.Language).HasMaxLength(50).IsRequired();
        b.Property(x=>x.CoverImageUrl).HasMaxLength(1000);
        b.HasOne(x=>x.Publisher).WithMany(x=>x.Books).HasForeignKey(x=>x.PublisherId);
        b.ToTable(t=> { t.HasCheckConstraint("CK_Books_Year", "[PublicationYear] BETWEEN 1000 AND 2200"); t.HasCheckConstraint("CK_Books_Pages", "[PageCount] > 0"); });
    }
    public void Configure(EntityTypeBuilder<Author> b)
    {
        b.Property(x=>x.FirstName).HasMaxLength(100).IsRequired();
        b.Property(x=>x.LastName).HasMaxLength(100).IsRequired();
        b.Property(x=>x.Biography).HasMaxLength(4000);
        b.HasIndex(x=>new{x.FirstName,x.LastName}).IsUnique();
    }
    public void Configure(EntityTypeBuilder<Publisher> b)
    {
        b.Property(x=>x.Name).HasMaxLength(200).IsRequired(); b.HasIndex(x=>x.Name).IsUnique();
        b.Property(x=>x.Country).HasMaxLength(100); b.Property(x=>x.Website).HasMaxLength(1000);
    }
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.Property(x=>x.Name).HasMaxLength(100).IsRequired(); b.HasIndex(x=>x.Name).IsUnique();
        b.Property(x=>x.Description).HasMaxLength(2000);
    }
    public void Configure(EntityTypeBuilder<BookCopy> b)
    {
        b.Property(x=>x.InventoryNumber).HasMaxLength(80).IsRequired(); b.HasIndex(x=>x.InventoryNumber).IsUnique();
        b.Property(x=>x.Barcode).HasMaxLength(100).IsRequired(); b.HasIndex(x=>x.Barcode).IsUnique();
        b.Property(x=>x.ShelfLocation).HasMaxLength(100).IsRequired(); b.Property(x=>x.Condition).HasMaxLength(500);
        b.Property(x=>x.RowVersion).IsRowVersion(); b.HasIndex(x=>new{x.BookId,x.Status});
        b.HasOne(x=>x.Book).WithMany(x=>x.Copies).HasForeignKey(x=>x.BookId);
        b.ToTable(t=>t.HasCheckConstraint("CK_Copies_Status","[Status] BETWEEN 0 AND 4"));
    }
    public void Configure(EntityTypeBuilder<Student> b)
    {
        b.Property(x=>x.StudentNumber).HasMaxLength(50).IsRequired(); b.HasIndex(x=>x.StudentNumber).IsUnique();
        b.Property(x=>x.FullName).HasMaxLength(200).IsRequired(); b.Property(x=>x.Email).HasMaxLength(256).IsRequired();
        b.Property(x=>x.PhoneNumber).HasMaxLength(30); b.Property(x=>x.Faculty).HasMaxLength(150);
        b.Property(x=>x.GroupName).HasMaxLength(50); b.HasIndex(x=>new{x.Faculty,x.Course,x.GroupName});
        b.HasOne(x=>x.IdentityUser).WithOne().HasForeignKey<Student>(x=>x.IdentityUserId);
        b.ToTable(t=>t.HasCheckConstraint("CK_Students_Course","[Course] BETWEEN 1 AND 8"));
    }
    public void Configure(EntityTypeBuilder<Loan> b)
    {
        b.HasOne(x=>x.Student).WithMany(x=>x.Loans).HasForeignKey(x=>x.StudentId);
        b.HasOne(x=>x.BookCopy).WithMany(x=>x.Loans).HasForeignKey(x=>x.BookCopyId);
        b.HasOne(x=>x.IssuedByUser).WithMany().HasForeignKey(x=>x.IssuedByUserId);
        b.Property(x=>x.Notes).HasMaxLength(2000); b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasIndex(x=>x.BookCopyId).IsUnique().HasFilter("[ReturnedAt] IS NULL").HasDatabaseName("UX_Loans_ActiveCopy");
        b.HasIndex(x=>new{x.DueDate,x.ReturnedAt});
        b.HasAlternateKey(x=>new{x.Id,x.StudentId});
        b.ToTable(t=> { t.HasCheckConstraint("CK_Loans_Dates", "[DueDate] >= [LoanDate] AND ([ReturnedAt] IS NULL OR [ReturnedAt] >= [LoanDate])"); });
    }
    public void Configure(EntityTypeBuilder<Return> b)
    {
        b.HasOne(x=>x.Loan).WithOne(x=>x.Return).HasForeignKey<Return>(x=>x.LoanId);
        b.HasOne(x=>x.ReceivedByUser).WithMany().HasForeignKey(x=>x.ReceivedByUserId);
        b.Property(x=>x.ReturnCondition).HasMaxLength(500).IsRequired(); b.Property(x=>x.Notes).HasMaxLength(2000);
    }
    public void Configure(EntityTypeBuilder<Fine> b)
    {
        b.Property(x=>x.Amount).HasPrecision(18,2); b.Property(x=>x.PaidAmount).HasPrecision(18,2);
        b.Property(x=>x.Reason).HasMaxLength(1000); b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasIndex(x=>x.LoanId).IsUnique();
        b.HasOne(x=>x.Loan).WithMany(x=>x.Fines).HasForeignKey(x=>new{x.LoanId,x.StudentId}).HasPrincipalKey(x=>new{x.Id,x.StudentId});
        b.HasOne(x=>x.Student).WithMany(x=>x.Fines).HasForeignKey(x=>x.StudentId);
        b.ToTable(t=> { t.HasCheckConstraint("CK_Fines_Amount","[Amount] > 0 AND [PaidAmount] >= 0 AND [PaidAmount] <= [Amount]"); t.HasCheckConstraint("CK_Fines_Status","([Status] = 0 AND [PaidAmount] = 0 AND [PaidAt] IS NULL) OR ([Status] = 1 AND [PaidAmount] > 0 AND [PaidAmount] < [Amount] AND [PaidAt] IS NULL) OR ([Status] = 2 AND [PaidAmount] = [Amount] AND [PaidAt] IS NOT NULL)"); });
    }
    public void Configure(EntityTypeBuilder<FinePayment> b)
    {
        b.Property(x=>x.Amount).HasPrecision(18,2); b.HasOne(x=>x.Fine).WithMany(x=>x.Payments).HasForeignKey(x=>x.FineId);
        b.HasOne(x=>x.ReceivedByUser).WithMany().HasForeignKey(x=>x.ReceivedByUserId);
        b.ToTable(t=>t.HasCheckConstraint("CK_Payments_Amount","[Amount] > 0"));
    }
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.HasOne(x=>x.Book).WithMany(x=>x.Reviews).HasForeignKey(x=>x.BookId);
        b.HasOne(x=>x.Student).WithMany().HasForeignKey(x=>x.StudentId);
        b.Property(x=>x.Comment).HasMaxLength(3000); b.HasIndex(x=>new{x.BookId,x.StudentId}).IsUnique();
        b.ToTable(t=>t.HasCheckConstraint("CK_Reviews_Rating","[Rating] BETWEEN 1 AND 5"));
    }
    public void Configure(EntityTypeBuilder<BookAuthor> b) { b.HasKey(x=>new{x.BookId,x.AuthorId}); b.HasOne(x=>x.Book).WithMany(x=>x.BookAuthors).HasForeignKey(x=>x.BookId); b.HasOne(x=>x.Author).WithMany(x=>x.BookAuthors).HasForeignKey(x=>x.AuthorId); }
    public void Configure(EntityTypeBuilder<BookCategory> b) { b.HasKey(x=>new{x.BookId,x.CategoryId}); b.HasOne(x=>x.Book).WithMany(x=>x.BookCategories).HasForeignKey(x=>x.BookId); b.HasOne(x=>x.Category).WithMany(x=>x.BookCategories).HasForeignKey(x=>x.CategoryId); }
    public void Configure(EntityTypeBuilder<Subject> b) { b.Property(x=>x.Name).HasMaxLength(150).IsRequired(); b.HasIndex(x=>x.Name).IsUnique(); }
    public void Configure(EntityTypeBuilder<StudentSubject> b) { b.HasKey(x=>new{x.StudentId,x.SubjectId}); b.HasOne(x=>x.Student).WithMany(x=>x.StudentSubjects).HasForeignKey(x=>x.StudentId); b.HasOne(x=>x.Subject).WithMany(x=>x.StudentSubjects).HasForeignKey(x=>x.SubjectId); }
    public void Configure(EntityTypeBuilder<StudentInterest> b) { b.Property(x=>x.Name).HasMaxLength(100).IsRequired(); b.HasIndex(x=>new{x.StudentId,x.Name}).IsUnique(); b.HasOne(x=>x.Student).WithMany(x=>x.Interests).HasForeignKey(x=>x.StudentId); }
    public void Configure(EntityTypeBuilder<ApplicationUser> b) { b.Property(x=>x.FullName).HasMaxLength(200).IsRequired(); }
    public void Configure(EntityTypeBuilder<SeedHistory> b) { b.HasKey(x=>x.Key); b.Property(x=>x.Key).HasMaxLength(100); }
}
