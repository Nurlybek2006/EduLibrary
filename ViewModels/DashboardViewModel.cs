namespace EduLibrary.ViewModels;

public sealed class DashboardViewModel
{
    public int TotalBooks { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public int ActiveLoans { get; set; }
    public int OverdueLoans { get; set; }
    public int TotalStudents { get; set; }
    public int ActiveReaders { get; set; }
    public decimal OutstandingFines { get; set; }
    public DateTime Today { get; set; }
    public IReadOnlyList<LoanRowViewModel> RecentLoans { get; set; } = [];
    public IReadOnlyList<ReturnRowViewModel> RecentReturns { get; set; } = [];
    public IReadOnlyList<LoanRowViewModel> OverdueAlerts { get; set; } = [];
    public IReadOnlyList<PopularBookViewModel> PopularBooks { get; set; } = [];
}

public sealed class PopularBookViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int LoanCount { get; set; }
    public int AvailableCopies { get; set; }
}
