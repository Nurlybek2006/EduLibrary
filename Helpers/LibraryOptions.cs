namespace EduLibrary.Helpers;

public class LibraryOptions
{
    public decimal FinePerOverdueDay { get; set; } = 100m;
    public int DefaultLoanDays { get; set; } = 14;
}

public class BusinessException(string message) : Exception(message);

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Librarian = "Librarian";
    public const string Student = "Student";
    public const string Staff = Admin + "," + Librarian;
}
