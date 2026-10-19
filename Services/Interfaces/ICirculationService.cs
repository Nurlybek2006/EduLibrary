using EduLibrary.ViewModels;

namespace EduLibrary.Services.Interfaces;

public interface ICirculationService
{
    Task<LoanListViewModel> GetLoansAsync(LoanFilterViewModel filter, string? userId, bool isStaff, CancellationToken ct = default);
    Task<LoanDetailsViewModel?> GetLoanAsync(int id, string? userId, bool isStaff, CancellationToken ct = default);
    Task<IssueLoanViewModel> GetIssueFormAsync(int? copyId = null, int? studentId = null, CancellationToken ct = default);
    Task PopulateIssueFormAsync(IssueLoanViewModel model, CancellationToken ct = default);
    Task<int> IssueAsync(IssueLoanViewModel model, string issuedByUserId, CancellationToken ct = default);
    Task<ReturnLoanViewModel?> GetReturnFormAsync(int loanId, CancellationToken ct = default);
    Task ReturnAsync(ReturnLoanViewModel model, string receivedByUserId, CancellationToken ct = default);
    Task<ReturnListViewModel> GetReturnsAsync(string? search, int page = 1, CancellationToken ct = default);
    Task<FineListViewModel> GetFinesAsync(string? search, string? status, int page, string? userId, bool isStaff, CancellationToken ct = default);
    Task<FineDetailsViewModel?> GetFineAsync(int id, string? userId, bool isStaff, CancellationToken ct = default);
    Task PayFineAsync(FinePaymentViewModel model, string receivedByUserId, CancellationToken ct = default);
}
