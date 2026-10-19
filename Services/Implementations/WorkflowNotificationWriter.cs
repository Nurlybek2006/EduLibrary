using EduLibrary.Data;
using EduLibrary.Models.Entities;
using EduLibrary.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Services.Implementations;

// Runs after generated IDs are available, before the circulation transaction
// commits. A notification can never outlive a rolled-back issue/return/fine.
public static class WorkflowNotificationWriter
{
    public static async Task AppendAsync(ApplicationDbContext db,ILibraryClock clock,IReadOnlyList<Loan> issued,IReadOnlyList<Return> returned,IReadOnlyList<Fine> fines,CancellationToken ct)
    {
        var ids=issued.Select(x=>x.Id).Concat(returned.Select(x=>x.LoanId)).Concat(fines.Select(x=>x.LoanId)).Distinct().ToArray();
        if(ids.Length==0)return;
        var loans=await db.Loans.AsNoTracking().Where(l=>ids.Contains(l.Id)&&l.Student.IdentityUserId!=null)
            .Select(l=>new{l.Id,UserId=l.Student.IdentityUserId!,Title=l.BookCopy.Book.Title,l.DueDate}).ToDictionaryAsync(l=>l.Id,ct);
        void Add(int loanId,string type,string title,string message,string? link=null)
        {
            if(!loans.TryGetValue(loanId,out var loan))return;
            db.Notifications.Add(new(){UserId=loan.UserId,DeduplicationKey=$"{type}:{loanId}",Type=type,Title=title,Message=$"«{loan.Title}»: {message}",Link=link??$"/Loans/Details/{loanId}",CreatedAt=clock.UtcNow});
        }
        foreach(var loan in issued)Add(loan.Id,"Issued","Кітап сәтті берілді",$"қайтару мерзімі — {loan.DueDate:dd.MM.yyyy}.");
        foreach(var item in returned)Add(item.LoanId,"Returned","Кітап сәтті қайтарылды","кітапхана дананы қабылдады.");
        foreach(var fine in fines)Add(fine.LoanId,"Fine","Айыппұл есептелді",$"бекітілген сома — {fine.Amount:N2} ₸.",$"/Fines/Details/{fine.Id}");
    }
}
