using Microsoft.AspNetCore.Identity;
using EduLibrary.Models.Enums;

namespace EduLibrary.Models.Entities;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = "";
}

public class Book
{
    public int Id { get; set; }
    public string ISBN { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int PublicationYear { get; set; }
    public string Language { get; set; } = "";
    public int PageCount { get; set; }
    public string? CoverImageUrl { get; set; }
    public int PublisherId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsArchived { get; set; }
    public Publisher Publisher { get; set; } = null!;
    public ICollection<BookAuthor> BookAuthors { get; set; } = new List<BookAuthor>();
    public ICollection<BookCategory> BookCategories { get; set; } = new List<BookCategory>();
    public ICollection<BookCopy> Copies { get; set; } = new List<BookCopy>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<BookSubject> BookSubjects { get; set; } = new List<BookSubject>();
}

public class Author
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Biography { get; set; } = "";
    public ICollection<BookAuthor> BookAuthors { get; set; } = new List<BookAuthor>();
}

public class Publisher
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public string? Website { get; set; }
    public ICollection<Book> Books { get; set; } = new List<Book>();
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ICollection<BookCategory> BookCategories { get; set; } = new List<BookCategory>();
}

public class BookAuthor
{
    public int BookId { get; set; }
    public int AuthorId { get; set; }
    public Book Book { get; set; } = null!;
    public Author Author { get; set; } = null!;
}

public class BookCategory
{
    public int BookId { get; set; }
    public int CategoryId { get; set; }
    public Book Book { get; set; } = null!;
    public Category Category { get; set; } = null!;
}

public class BookCopy
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string InventoryNumber { get; set; } = "";
    public string Barcode { get; set; } = "";
    public CopyStatus Status { get; set; }
    public string ShelfLocation { get; set; } = "";
    public string Condition { get; set; } = "";
    public DateTime AcquiredAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Book Book { get; set; } = null!;
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
}

public class Student
{
    public int Id { get; set; }
    public string StudentNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string Faculty { get; set; } = "";
    public int Course { get; set; }
    public string GroupName { get; set; } = "";
    public string? IdentityUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public ApplicationUser? IdentityUser { get; set; }
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
    public ICollection<Fine> Fines { get; set; } = new List<Fine>();
    public ICollection<StudentSubject> StudentSubjects { get; set; } = new List<StudentSubject>();
    public ICollection<StudentInterest> Interests { get; set; } = new List<StudentInterest>();
}

public class Loan
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public int BookCopyId { get; set; }
    public DateTime LoanDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public string IssuedByUserId { get; set; } = "";
    public string? Notes { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Student Student { get; set; } = null!;
    public BookCopy BookCopy { get; set; } = null!;
    public ApplicationUser IssuedByUser { get; set; } = null!;
    public Return? Return { get; set; }
    public ICollection<Fine> Fines { get; set; } = new List<Fine>();
}

public class Return
{
    public int Id { get; set; }
    public int LoanId { get; set; }
    public DateTime ReturnDate { get; set; }
    public string ReceivedByUserId { get; set; } = "";
    public string ReturnCondition { get; set; } = "";
    public string? Notes { get; set; }
    public Loan Loan { get; set; } = null!;
    public ApplicationUser ReceivedByUser { get; set; } = null!;
}

public class Fine
{
    public int Id { get; set; }
    public int LoanId { get; set; }
    public int StudentId { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public FineStatus Status { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Loan Loan { get; set; } = null!;
    public Student Student { get; set; } = null!;
    public ICollection<FinePayment> Payments { get; set; } = new List<FinePayment>();
}

public class FinePayment
{
    public int Id { get; set; }
    public int FineId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
    public string ReceivedByUserId { get; set; } = "";
    public Fine Fine { get; set; } = null!;
    public ApplicationUser ReceivedByUser { get; set; } = null!;
}

public class Review
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int StudentId { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Book Book { get; set; } = null!;
    public Student Student { get; set; } = null!;
}

public class Subject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ICollection<StudentSubject> StudentSubjects { get; set; } = new List<StudentSubject>();
}

public class StudentSubject
{
    public int StudentId { get; set; }
    public int SubjectId { get; set; }
    public Student Student { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
}

public class StudentInterest
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string Name { get; set; } = "";
    public Student Student { get; set; } = null!;
}

public class SeedHistory
{
    public string Key { get; set; } = "";
    public DateTime AppliedAt { get; set; }
}
